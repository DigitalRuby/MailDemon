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
    public partial class MailDemonService : IMailSender
    {
        /// <summary>
        /// Set to true to turn off actual sending of smtp messages, useful for performance testing
        /// </summary>
        public bool DisableSending { get; set; }

        /// <inheritdoc />
        public async Task SendMailAsync(string toDomain, IAsyncEnumerable<MailToSend> messages)
        {
            using SmtpClient client = new SmtpClient()
            {
                ServerCertificateValidationCallback = (object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) =>
                {
                    return (sslPolicyErrors == SslPolicyErrors.None ||
                        ignoreCertificateErrorsRegex.TryGetValue("*", out _) ||
                        (ignoreCertificateErrorsRegex.TryGetValue(toDomain, out Regex regex)) && regex.IsMatch(certificate.Subject));
                }
            };
            IPHostEntry ip = null;
            LookupClient lookup = new LookupClient();
            MailDemonLog.Debug("QueryAsync mx for domain {0}", toDomain);
            IDnsQueryResponse result = await lookup.QueryAsync(toDomain, QueryType.MX, cancellationToken: cancelToken);
            bool connected = false;
            foreach (DnsClient.Protocol.MxRecord record in result.AllRecords)
            {
                // attempt to send, if fail, try next address
                try
                {
                    MailDemonLog.Debug("GetHostEntryAsync for exchange {0}", record.Exchange);
                    ip = await Dns.GetHostEntryAsync(record.Exchange);
                    foreach (IPAddress ipAddress in ip.AddressList)
                    {
                        string host = ip.HostName;
                        try
                        {
                            if (!DisableSending)
                            {
                                await client.ConnectAsync(host, options: MailKit.Security.SecureSocketOptions.Auto, cancellationToken: cancelToken).TimeoutAfter(30000);
                            }
                            connected = true;
                            await foreach (MailToSend message in messages)
                            {
                                if (dkimSigner != null)
                                {
                                    message.Message.Prepare(EncodingConstraint.SevenBit);
                                    dkimSigner.Sign(message.Message, headersToSign);
                                }
                                try
                                {
                                    if (!DisableSending)
                                    {
                                        MailDemonLog.Debug("Sending message to host {0}, from {1}, to {2}", host, message.Message.From, message.Message.To);
                                        await client.SendAsync(message.Message, cancelToken).TimeoutAfter(30000);
                                        MailDemonLog.Debug("Success message to host {0}, from {1}, to {2}", host, message.Message.From, message.Message.To);
                                    }

                                    // callback success
                                    message.Callback?.Invoke(message.Subscription, string.Empty);
                                }
                                catch (Exception exInner)
                                {
                                    MailDemonLog.Debug("Fail message to host {0}, from {1}, to {2} {3}", host, message.Message.From, message.Message.To, exInner);

                                    // TODO: Handle SmtpCommandException: Greylisted, please try again in 180 seconds
                                    // callback error
                                    message.Callback?.Invoke(message.Subscription, exInner.Message);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // all messages fail for this domain
                            MailDemonLog.Error(host + " (" + toDomain + ")", ex);
                            await foreach (MailToSend message in messages)
                            {
                                message.Callback?.Invoke(message.Subscription, ex.Message);
                            }
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
                        if (connected)
                        {
                            // we successfuly got a mail server, don't loop more ips
                            break;
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // dns error, move on to next mail server
                    MailDemonLog.Error(toDomain, ex);
                }
                if (connected)
                {
                    // we successfuly got a mail server, don't loop more dns entries
                    break;
                }
            }
        }

        // h=Subject:To:Cc:References:From:Message-ID:Date:MIME-Version:In-Reply-To:Content-Type
        private static readonly HeaderId[] headersToSign = new HeaderId[] { HeaderId.From, HeaderId.Cc, HeaderId.Subject, HeaderId.Date, HeaderId.MessageId, HeaderId.References,
            HeaderId.MimeVersion, HeaderId.InReplyTo, HeaderId.ContentType, HeaderId.ContentLanguage, HeaderId.Body };

        /// <summary>
        /// Send mail and kicks off the send in a new task
        /// </summary>
        /// <param name="foundUser">Found user</param>
        /// <param name="reader">Reader</param>
        /// <param name="writer">Writer</param>
        /// <param name="line">Line</param>
        /// <param name="endPoint">End point</param>
        /// <param name="prepMessage">Allow changing mime message right before it is sent</param>
        /// <returns>Task</returns>
        private async Task SendMail(MailDemonUser foundUser, Stream reader, StreamWriter writer, string line, IPEndPoint endPoint, Action<MimeMessage> prepMessage)
        {
            MailFromResult result = await ParseMailFrom(foundUser, reader, writer, line, endPoint);
            SendMail(writer, result, endPoint, true, prepMessage).GetAwaiter();
            await writer.WriteLineAsync($"250 2.1.0 OK");
            await writer.FlushAsync();
        }

        /// <summary>
        /// Send mail and awaits until all messages are sent
        /// </summary>
        /// <param name="writer">Writer</param>
        /// <param name="result">Mail result to send</param>
        /// <param name="endPoint">End point</param>
        /// <param name="dispose">Whether to dispose result when done</param>
        /// <param name="prepMessage">Allow changing mime message right before it is sent</param>
        /// <returns>Task</returns>
        private async Task SendMail(StreamWriter writer, MailFromResult result, IPEndPoint endPoint, bool dispose, Action<MimeMessage> prepMessage)
        {
            try
            {
                // send all emails in one shot for each domain in order to batch
                DateTime start = DateTime.UtcNow;
                int count = 0;
                List<Task> tasks = new List<Task>();
                foreach (var group in result.ToAddresses)
                {
                    tasks.Add(SendMailInternal(writer, result.BackingFile, result.From, group.Key, group.Value, endPoint, null));
                    count++;
                }
                await Task.WhenAll(tasks);
                MailDemonLog.Info("Sent {0} batches of messages in {1:0.00} seconds", count, (DateTime.UtcNow - start).TotalSeconds);
            }
            catch (Exception ex)
            {
                MailDemonLog.Error(ex);
            }
            finally
            {
                if (dispose)
                {
                    result.Dispose();
                }
            }
        }

        private async IAsyncEnumerable<MailToSend> EnumerateMessages(MimeMessage message, string toDomain,
            MailboxAddress from, IEnumerable<MailboxAddress> toAddresses, Action<MimeMessage> prepMessage)
        {
            foreach (MailboxAddress toAddress in toAddresses)
            {
                message.From.Clear();
                message.To.Clear();
                message.From.Add(from);
                message.To.Add(toAddress);
                prepMessage?.Invoke(message);
                yield return new MailToSend { Message = message };
            }
            await Task.Yield();
        }

        private async Task SendMailInternal(StreamWriter writer, string fileName, MailboxAddress from, string toDomain,
            IEnumerable<MailboxAddress> toAddresses, IPEndPoint endPoint, Action<MimeMessage> prepMessage)
        {
            try
            {
                using Stream fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
                MimeMessage message = await MimeMessage.LoadAsync(fs, true, cancelToken);
                IAsyncEnumerable<MailToSend> toSend = EnumerateMessages(message, toDomain, from, toAddresses, prepMessage);
                await SendMailAsync(toDomain, toSend);
            }
            catch (Exception ex)
            {
                MailDemonLog.Error(ex);
            }
        }
    }
}
