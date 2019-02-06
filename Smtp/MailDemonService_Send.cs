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
        private async Task SendMail(MailDemonUser foundUser, Stream reader, StreamWriter writer, string line)
        {
            MailFromResult result = await ParseMailFrom(foundUser, reader, writer, line);
            await SendMail(result);
            await writer.WriteLineAsync($"250 2.1.0 OK");
        }

        private async Task SendMail(MailFromResult result)
        {
            // send all emails in one shot for each domain in order to batch
            foreach (var group in result.ToAddresses)
            {
                await SendMessage(result.Message, result.From, group.Key);
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
                IDnsQueryResponse result = await lookup.QueryAsync(domain, QueryType.MX);
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
                                await client.ConnectAsync(host, options: MailKit.Security.SecureSocketOptions.StartTlsWhenAvailable).TimeoutAfter(10000);
                                await client.SendAsync(msg).TimeoutAfter(10000);
                                sent = true;
                                break;
                            }
                            catch (Exception ex)
                            {
                                MailDemonLog.Error(ex);
                            }
                            finally
                            {
                                await client.DisconnectAsync(true);
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
