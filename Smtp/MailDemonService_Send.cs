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
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using DnsClient;

using MailKit.Net.Smtp;

using MimeKit;

namespace MailDemon
{
    public partial class MailDemonService
    {
        private static readonly HeaderId[] headersToSign = new HeaderId[] { HeaderId.From, HeaderId.Subject, HeaderId.Date };

        /// <summary>
        /// Send mail and kicks off the send in a new task
        /// </summary>
        /// <param name="foundUser">Found user</param>
        /// <param name="reader">Reader</param>
        /// <param name="writer">Writer</param>
        /// <param name="line">Line</param>
        /// <param name="endPoint">End point</param>
        /// <param name="validateSpf">Whether to validate spf</param>
        /// <returns>Task</returns>
        private async Task SendMail(MailDemonUser foundUser, Stream reader, StreamWriter writer, string line, IPEndPoint endPoint)
        {
            MailFromResult result = await ParseMailFrom(foundUser, reader, writer, line, endPoint);
            SendMail(writer, result, endPoint, true).GetAwaiter();
            await writer.WriteLineAsync($"250 2.1.0 OK");
        }

        /// <summary>
        /// Send mail and awaits until all messages are sent
        /// </summary>
        /// <param name="writer">Writer</param>
        /// <param name="result">Mail result to send</param>
        /// <param name="endPoint">End point</param>
        /// <param name="dispose">Whether to dispose result when done</param>
        /// <returns>Task</returns>
        private async Task SendMail(StreamWriter writer, MailFromResult result, IPEndPoint endPoint, bool dispose)
        {
            try
            {
                // send all emails in one shot for each domain in order to batch
                List<Task> tasks = new List<Task>();
                foreach (var group in result.ToAddresses)
                {
                    tasks.Add(SendMessage(writer, result.Message, result.From, group.Key, group.Value, endPoint));
                }
                await Task.WhenAll(tasks);
            }
            finally
            {
                if (dispose)
                {
                    result.Dispose();
                }
            }
        }

        private async Task SendMessage(StreamWriter writer, MimeMessage msg, MailboxAddress from, string toDomain, IEnumerable<MailboxAddress> toAddresses, IPEndPoint endPoint)
        {
            using (SmtpClient client = new SmtpClient()
            {
                ServerCertificateValidationCallback = (object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) =>
                {
                    return (sslPolicyErrors == SslPolicyErrors.None ||
                        (ignoreCertificateErrorsRegex.TryGetValue(toDomain, out Regex re) && re.IsMatch(certificate.Subject)));
                }
            })
            {
                IPHostEntry ip = null;
                LookupClient lookup = new LookupClient();
                MailDemonLog.Write(LogLevel.Info, "QueryAsync mx for domain {0}", toDomain);
                IDnsQueryResponse result = await lookup.QueryAsync(toDomain, QueryType.MX, cancellationToken: cancelToken);
                MimeMessage clone = new MimeMessage(from, toAddresses, msg.Subject, msg.Body);
                clone.Cc.AddRange(msg.Cc);
                clone.Date = msg.Date;
                if (dkimSigner != null)
                {
                    clone.Prepare(EncodingConstraint.SevenBit);
                    clone.Sign(dkimSigner, headersToSign);
                }
                foreach (DnsClient.Protocol.MxRecord record in result.AllRecords)
                {
                    // attempt to send, if fail, try next address
                    try
                    {
                        MailDemonLog.Write(LogLevel.Debug, "GetHostEntryAsync for exchange {0}", record.Exchange);
                        ip = await Dns.GetHostEntryAsync(record.Exchange);
                        foreach (IPAddress ipAddress in ip.AddressList)
                        {
                            string host = ip.HostName;
                            try
                            {
                                MailDemonLog.Write(LogLevel.Debug, "Sending message to host {0}, from {1}, to {2}", host, msg.From, msg.To);
                                await client.ConnectAsync(host, options: MailKit.Security.SecureSocketOptions.StartTlsWhenAvailable, cancellationToken: cancelToken).TimeoutAfter(30000);
                                await client.SendAsync(clone, cancelToken).TimeoutAfter(30000);
                                return;
                            }
                            catch (Exception ex)
                            {
                                MailDemonLog.Error(ex);
                            }
                            finally
                            {
                                try
                                {
                                    await client.DisconnectAsync(true, cancelToken);
                                }
                                catch
                                {
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MailDemonLog.Error(ex);
                    }
                }
            }
        }
    }
}
