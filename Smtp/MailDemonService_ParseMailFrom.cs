using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using MimeKit;

namespace MailDemon
{
    public partial class MailDemonService
    {
        private async Task<MailFromResult> ParseMailFrom(MailDemonUser fromUser, Stream reader, StreamWriter writer, string line)
        {
            string fromAddress = line.Substring(11);
            bool binaryMime = (line.Contains("BODY=BINARYMIME", StringComparison.OrdinalIgnoreCase));
            int pos = fromAddress.IndexOf('>');
            if (pos >= 0)
            {
                fromAddress = fromAddress.Substring(0, pos);
            }
            if (!MailboxAddress.TryParse(fromAddress, out _))
            {
                await writer.WriteLineAsync($"500 invalid command - bad from address format");
                await writer.FlushAsync();
                throw new ArgumentException("Invalid format for from address: " + fromAddress);
            }

            if (fromUser != null && fromUser.Address != fromAddress)
            {
                await writer.WriteLineAsync($"500 invalid command");
                await writer.FlushAsync();
                throw new InvalidOperationException("Invalid from address - bad from address");
            }

            // denote success for sender and binarymime
            string binaryMimeOk = (binaryMime ? " and BINARYMIME" : string.Empty);
            await writer.WriteLineAsync($"250 2.1.0 sender {fromUser.Name}{binaryMimeOk} OK");

            // read to addresses
            line = await ReadLineAsync(reader);
            Dictionary<string, List<string>> toAddressesByDomain = new Dictionary<string, List<string>>();
            while (line.StartsWith("RCPT TO:<", StringComparison.OrdinalIgnoreCase))
            {
                string toAddress = line.Substring(9).Trim('>');

                if (!MailboxAddress.TryParse(toAddress, out _))
                {
                    await writer.WriteLineAsync($"500 invalid command - bad to address format");
                    await writer.FlushAsync();
                    throw new ArgumentException("Invalid to address: " + toAddress);
                }

                // if no authenticated user, the to address must match an existing user address
                else if (fromUser == null && users.FirstOrDefault(u => u.Address == toAddress) == null)
                {
                    await writer.WriteLineAsync($"500 invalid command - bad to address");
                    await writer.FlushAsync();
                    throw new InvalidOperationException("Invalid to address: " + toAddress);
                }
                // else user is authenticated, can send email to anyone

                // group addresses by domain
                pos = toAddress.LastIndexOf('@');
                if (pos > 0)
                {
                    string addressDomain = toAddress.Substring(++pos);
                    if (!toAddressesByDomain.TryGetValue(addressDomain, out List<string> addressList))
                    {
                        toAddressesByDomain[addressDomain] = addressList = new List<string>();
                    }
                    addressList.Add(toAddress);
                }

                // denote success for recipient
                await writer.WriteLineAsync($"250 2.1.0 recipient {toAddress} OK");
                line = await ReadLineAsync(reader);
            }

            // if no to addresses, fail
            if (toAddressesByDomain.Count == 0)
            {
                await writer.WriteLineAsync($"500 invalid command");
                await writer.FlushAsync();
                throw new InvalidOperationException("Invalid message: " + line);
            }

            if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
            {
                if (binaryMime)
                {
                    await writer.WriteLineAsync("503 5.5.1 Bad sequence of commands, BODY=BINARYMIME requires BDAT, not DATA");
                    await writer.FlushAsync();
                    throw new InvalidOperationException("Invalid message: " + line);
                }
                string tempFile = Path.GetTempFileName();
                try
                {
                    int totalCount = 0;
                    using (Stream tempFileWriter = File.Create(tempFile))
                    {
                        await writer.WriteLineAsync($"354");
                        int b;
                        int state = 0;
                        while (state != 3 && (b = reader.ReadByte()) >= 0)
                        {
                            if (b == (byte)'.')
                            {
                                if (state == 0)
                                {
                                    state = 1;
                                }
                                else
                                {
                                    // reset
                                    state = 0;
                                }
                            }
                            else if (b == (byte)'\r')
                            {
                                if (state == 1)
                                {
                                    state = 2;
                                }
                                else
                                {
                                    // reset
                                    state = 0;
                                }
                            }
                            else if (b == (byte)'\n')
                            {
                                if (state == 2)
                                {
                                    // end of message
                                    state = 3;
                                }
                                else
                                {
                                    // reset
                                    state = 0;
                                }
                            }
                            else
                            {
                                // reset
                                state = 0;
                            }
                            totalCount++;
                            if (totalCount > maxMessageSize)
                            {
                                await writer.WriteLineAsync("552 message too large");
                                throw new InvalidOperationException("Invalid message: " + line);
                            }
                            tempFileWriter.WriteByte((byte)b);
                        }
                    }

                    // strip off the \r\n.\r\n, that is part of the protocol
                    using (FileStream tempFileStream = File.Open(tempFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        if (tempFileStream.Length >= 3)
                        {
                            tempFileStream.SetLength(tempFileStream.Length - 3);
                        }
                    }
                    await writer.WriteLineAsync($"250 2.5.0 OK");
                    return new MailFromResult
                    {
                        From = (fromUser == null ? new MailboxAddress(fromAddress) : fromUser.MailAddress),
                        ToAddresses = toAddressesByDomain,
                        Message = await MimeMessage.LoadAsync(tempFile, cancelToken)
                    };
                }
                finally
                {
                    File.Delete(tempFile);
                }
            }
            else if (line.StartsWith("BDAT", StringComparison.OrdinalIgnoreCase))
            {
                // https://tools.ietf.org/html/rfc1830
                string tempFile = Path.GetTempFileName();
                bool last = false;
                int totalBytes = 0;

                try
                {
                    // send bdat to temp file to avoid memory issues
                    using (Stream stream = File.OpenWrite(tempFile))
                    {
                        do
                        {
                            int space = line.IndexOf(' ');
                            int space2 = line.IndexOf(' ', space + 1);
                            if (space2 < 0)
                            {
                                space2 = line.Length;
                            }
                            if (space < 0 || !int.TryParse(line.AsSpan(space, space2 - space),
                                NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out int size))
                            {
                                await writer.WriteLineAsync($"500 invalid command");
                                throw new InvalidOperationException("Invalid message: " + line);
                            }
                            last = line.Contains("LAST", StringComparison.OrdinalIgnoreCase);
                            totalBytes += size;
                            if (totalBytes > maxMessageSize)
                            {
                                await writer.WriteLineAsync("552 message too large");
                                throw new InvalidOperationException("Invalid message: " + line);
                            }
                            await ReadWriteAsync(reader, stream, size);
                            if (last)
                            {
                                await writer.WriteLineAsync($"250 2.5.0 total {totalBytes} bytes received message OK");
                            }
                            else
                            {
                                await writer.WriteLineAsync($"250 2.0.0 {size} bytes received OK");
                            }
                        }
                        while (!last && !cancelToken.IsCancellationRequested && (line = await ReadLineAsync(reader)) != null);
                    }
                    MimeMessage msg = await MimeMessage.LoadAsync(tempFile, cancelToken);
                    return new MailFromResult
                    {
                        From = (fromUser == null ? new MailboxAddress(fromAddress) : fromUser.MailAddress),
                        ToAddresses = toAddressesByDomain,
                        Message = msg
                    };
                }
                finally
                {
                    File.Delete(tempFile);
                }
            }
            else
            {
                await writer.WriteLineAsync($"500 invalid command");
                await writer.FlushAsync();
                throw new InvalidOperationException("Invalid line in mail from: " + line);
            }
        }
    }
}
