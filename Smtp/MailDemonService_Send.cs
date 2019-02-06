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
        /// <summary>
        /// Send mail and kicks off the send in a new task
        /// </summary>
        /// <param name="foundUser">Found user</param>
        /// <param name="reader">Reader</param>
        /// <param name="writer">Writer</param>
        /// <param name="line">Line</param>
        /// <returns>Task</returns>
        private async Task SendMail(MailDemonUser foundUser, Stream reader, StreamWriter writer, string line)
        {
            MailFromResult result = await ParseMailFrom(foundUser, reader, writer, line);
            SendMail(result).GetAwaiter();
            await writer.WriteLineAsync($"250 2.1.0 OK");
        }

        /// <summary>
        /// Send mail and awaits until all messages are sent
        /// </summary>
        /// <param name="result">Mail result to send</param>
        /// <returns>Task</returns>
        private async Task SendMail(MailFromResult result)
        {
            try
            {
                // send all emails in one shot for each domain in order to batch
                foreach (var group in result.ToAddresses)
                {
                    await SendMessage(result.Message, result.From, group.Key);
                }
            }
            finally
            {
                result.Dispose();
            }
        }

        private async Task SendMessage(MimeMessage msg, InternetAddress from, string domain)
        {
            MailDemonLog.Write(LogLevel.Info, "Sending from {0}, to: {1}", from, msg.To.ToString());
            using (SmtpClient client = new SmtpClient()
            {
                ServerCertificateValidationCallback = (object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) =>
                {
                    return (sslPolicyErrors == SslPolicyErrors.None ||
                        (ignoreCertificateErrorsRegex.TryGetValue(domain, out Regex re) && re.IsMatch(certificate.Subject)));
                }
            })
            {
                IPHostEntry ip = null;
                bool sent = false;
                LookupClient lookup = new LookupClient();
                MailDemonLog.Write(LogLevel.Info, "QueryAsync mx for domain {0}", domain);
                IDnsQueryResponse result = await lookup.QueryAsync(domain, QueryType.MX, cancellationToken: cancelToken);
                foreach (DnsClient.Protocol.MxRecord record in result.AllRecords)
                {
                    // attempt to send, if fail, try next address
                    try
                    {
                        MailDemonLog.Write(LogLevel.Info, "GetHostEntryAsync for exchange {0}", record.Exchange);
                        ip = await Dns.GetHostEntryAsync(record.Exchange);
                        foreach (IPAddress ipAddress in ip.AddressList)
                        {
                            string host = ip.HostName;
                            try
                            {
                                msg.From.Clear();
                                msg.From.Add(from);
                                MailDemonLog.Write(LogLevel.Info, "Sending message to host {0}", host);
                                await client.ConnectAsync(host, options: MailKit.Security.SecureSocketOptions.StartTlsWhenAvailable, cancellationToken: cancelToken).TimeoutAfter(30000);
                                await client.SendAsync(msg, cancelToken).TimeoutAfter(30000);
                                sent = true;
                                break;
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

                    if (sent)
                    {
                        break;
                    }
                }
            }
        }
    }
}
