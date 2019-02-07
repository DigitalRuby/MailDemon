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
        private async Task ReceiveMail(Stream reader, StreamWriter writer, string line, IPEndPoint endPoint)
        {
            IPHostEntry entry = await Dns.GetHostEntryAsync(endPoint.Address);
            using (MailFromResult result = await ParseMailFrom(null, reader, writer, line))
            {
                // protect agains spoofing, only accept mail where the host matches the connection ip address
                int pos = result.From.Name.IndexOf('@');
                string host = result.From.Name.Substring(++pos);
                if (!host.Equals(entry.HostName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Host name in mail address '{result.From.Name}' does not match connection host of '{endPoint.Address}'");
                }

                // mail demon doesn't have an inbox, only forwarding, so see if any of the to addresses can be forwarded
                foreach (var kv in result.ToAddresses)
                {
                    foreach (string address in kv.Value)
                    {
                        MailDemonUser user = users.FirstOrDefault(u => u.Address == address);

                        // if no user or the forward address points to a user, fail
                        if (user == null || users.FirstOrDefault(u => u.Address == user.ForwardAddress) != null)
                        {
                            await writer.WriteLineAsync($"500 invalid command - cannot forward");
                            await writer.FlushAsync();
                        }

                        // setup forward headers
                        string forwardToAddress = (string.IsNullOrWhiteSpace(user.ForwardAddress) ? globalForwardAddress : user.ForwardAddress);
                        if (string.IsNullOrWhiteSpace(forwardToAddress))
                        {
                            await writer.WriteLineAsync($"500 invalid command - cannot forward 2");
                            await writer.FlushAsync();
                        }
                        else
                        {
                            // create brand new message to forward
                            MimeMessage message = new MimeMessage();
                            message.From.Add(user.MailAddress);
                            message.ReplyTo.Add(user.MailAddress);
                            message.To.Add(new MailboxAddress(forwardToAddress));
                            message.Subject = "FW: " + result.Message.Subject;

                            // now to create our body...
                            BodyBuilder builder = new BodyBuilder
                            {
                                TextBody = result.Message.TextBody,
                                HtmlBody = result.Message.HtmlBody
                            };
                            foreach (var attachment in result.Message.Attachments)
                            {
                                builder.Attachments.Add(attachment);
                            }
                            message.Body = builder.ToMessageBody();
                            string toDomain = user.ForwardAddress.Substring(user.ForwardAddress.IndexOf('@') + 1);

                            // create new object to forward on
                            MailFromResult newResult = new MailFromResult
                            {
                                From = user.MailAddress,
                                Message = message,
                                ToAddresses = new Dictionary<string, List<string>> { { toDomain, new List<string> { forwardToAddress } } }
                            };

                            // forward the message on and clear the forward headers
                            MailDemonLog.Write(LogLevel.Info, "Forwarding message, from: {0}, to: {1}, forward: {2}", result.From, address, forwardToAddress);
                            SendMail(newResult).GetAwaiter();
                        }
                    }
                }
            }
        }
    }
}
