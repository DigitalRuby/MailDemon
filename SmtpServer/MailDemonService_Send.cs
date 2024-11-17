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

using Microsoft.Extensions.Logging;

using MimeKit;

namespace MailDemon
{
    public partial class MailDemonService : IMailSender
    {
        // h=Subject:To:Cc:References:From:Message-ID:Date:MIME-Version:In-Reply-To:Content-Type
        private static readonly HeaderId[] headersToSign = new HeaderId[] { HeaderId.From, HeaderId.Cc, HeaderId.Subject, HeaderId.Date, HeaderId.MessageId, HeaderId.References,
            HeaderId.MimeVersion, HeaderId.InReplyTo, HeaderId.ContentType, HeaderId.ContentLanguage, HeaderId.Body };

        /// <summary>
        /// Set to true to turn off actual sending of smtp messages, useful for performance testing
        /// </summary>
        public bool DisableSending { get; set; }

        /// <inheritdoc />
        public async Task SendMailAsync(IReadOnlyCollection<MailToSend> messages, bool synchronous)
        {
            if (messages.Count == 0)
            {
                return;
            }

            string toDomain = messages.First().Message.To.First().GetDomain();
            using SmtpClient client = CreateClient(toDomain);
            try
            {
                await PerformSmtpClientOperation(client, toDomain, async () =>
                {
                    foreach (MailToSend message in messages)
                    {
                        await SendOneMessage(client, message, synchronous);
                    }
                }, synchronous);
            }
            catch (Exception ex)
            {
                // all messages fail for this domain
                logger.LogError(ex, "Unable to send email messages to {addresses}", string.Join(';', messages.Select(m => m.Message.To.First().ToString())));
                foreach (MailToSend message in messages)
                {
                    message.Callback?.Invoke(message.Subscription, "Error: " + ex.Message);
                }
                throw;
            }
        }

        private SmtpClient CreateClient(string sendToDomain)
        {
            return new SmtpClient
            {
                LocalDomain = Domain,
                Timeout = 30000,
                ServerCertificateValidationCallback = (object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) =>
                {
                    return (sslPolicyErrors == SslPolicyErrors.None ||
                        ignoreCertificateErrorsRegex.TryGetValue("*", out _) ||
                        (ignoreCertificateErrorsRegex.TryGetValue(sendToDomain, out Regex regex)) && regex.IsMatch(certificate.Subject));
                }
            };
        }

        private async Task PerformSmtpClientOperation(SmtpClient client, string domain, Func<Task> clientAction, bool synchronous)
        {
            LookupClient lookup = new LookupClient();
            logger.LogDebug("QueryAsync mx for domain {domain}", domain);
            IDnsQueryResponse result = await lookup.QueryAsync(domain, QueryType.MX, cancellationToken: cancelToken);
            Exception lastError = null;

            foreach (var potentialMxRecord in result.AllRecords)
            {
                if (potentialMxRecord is not DnsClient.Protocol.MxRecord record)
                {
                    continue;
                }

                // exit out if synchronous and we have an error
                else if (lastError != null && synchronous)
                {
                    break;
                }

                try
                {
                    logger.LogDebug("GetHostEntryAsync for {exchange}", record.Exchange);
                    IPHostEntry ip = await Dns.GetHostEntryAsync(record.Exchange);
                    foreach (IPAddress ipAddress in ip.AddressList)
                    {
                        string host = ip.HostName;
                        try
                        {
                            if (!DisableSending)
                            {
                                await client.ConnectAsync(host, options: MailKit.Security.SecureSocketOptions.Auto, cancellationToken: cancelToken).TimeoutAfter(30000);
                            }
                            await clientAction();
                            return; // all done!
                        }
                        catch (Exception ex)
                        {
                            if (synchronous)
                            {
                                lastError = ex;
                                break;
                            }
                            logger.LogError(ex, "Error performing action for {domain}", domain);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // try next dns
                    lastError = ex;
                }
            }

            if (lastError != null)
            {
                throw lastError;
            }
        }

        private async Task SendOneMessage(SmtpClient client, MailToSend message, bool synchronous)
        {
            // if dkim sign fails, all messages fail
            if (dkimSigner != null)
            {
                message.Message.Prepare(EncodingConstraint.SevenBit);
                dkimSigner.Sign(message.Message, headersToSign);
            }

            try
            {
                if (!DisableSending)
                {
                    logger.LogDebug("Sending message from {from}, to {to}", message.Message.From, message.Message.To);
                    await client.SendAsync(message.Message, cancelToken).TimeoutAfter(30000);
                    logger.LogDebug("Success message from {from}, to {to}", message.Message.From, message.Message.To);
                }

                // callback success
                message.Callback?.Invoke(message.Subscription, string.Empty);
            }
            catch (Exception exInner)
            {
                logger.LogDebug("Fail message {from}, {to}, {innerError}", message.Message.From, message.Message.To, exInner.Message);

                // TODO: Handle SmtpCommandException: Greylisted, please try again in 180 seconds
                message.Callback?.Invoke(message.Subscription, exInner.Message);

                if (synchronous)
                {
                    throw;
                }
            }
        }

        /// <summary>
        /// Send mail to one person
        /// </summary>
        /// <param name="foundUser">Found user</param>
        /// <param name="reader">Reader</param>
        /// <param name="writer">Writer</param>
        /// <param name="line">Line</param>
        /// <param name="endPoint">End point</param>
        /// <param name="prepMessage">Allow changing mime message right before it is sent</param>
        /// or run in the background (false)</param>
        /// <param name="synchronous">Whether the send is synchronous, in which case exceptions will throw out</param>
        /// <returns>Task</returns>
        private async Task SendMail(MailDemonUser foundUser, Stream reader, StreamWriter writer,
            string line, IPEndPoint endPoint, bool synchronous)
        {
            MailFromResult result = await ParseMailFrom(foundUser, reader, writer, line, endPoint);
            if (result is null)
            {
                return;
            }
            string origSuccessLine = result.SuccessLine;
            int maxCount = (synchronous ? 2 : 1);

            try
            {
                for (int i = 0; i < maxCount; i++)
                {
                    try
                    {
                        // wait for call to complete, exception will be propagated to the caller if synchronous
                        result.SuccessLine = origSuccessLine;
                        await SendMail(result, false, null, synchronous);
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (ex is AggregateException aggEx)
                        {
                            ex = aggEx.InnerExceptions.FirstOrDefault();
                        }

                        // denote failure to the caller
                        if (ex is SmtpCommandException smtpEx)
                        {
                            result.SuccessLine = (int)smtpEx.StatusCode + " " + ex.Message;
                        }
                        else
                        {
                            result.SuccessLine = "455 Internal Error: " + ex.Message;
                        }
                        if (i == 0 && maxCount > 1)
                        {
                            // wait a bit and retry
                            await Task.Delay(10000);
                        }
                    }
                }
            }
            finally
            {
                result.Dispose();
            }

            // denote to caller that we have sent the message successfully
            result.SuccessLine ??= $"250 2.1.0 OK";
            await writer.WriteLineAsync(result.SuccessLine);
            await writer.FlushAsync();
        }

        /// <summary>
        /// Send mail with message prep action
        /// </summary>
        /// <param name="result">Mail result to send</param>
        /// <param name="dispose">Whether to dispose result when done</param>
        /// <param name="prepMessage">Action to prep message for send</param>
        /// <param name="synchronous">Whether to send synchronously, in which case exceptions witll throw out</param>
        /// <returns>Task</returns>
        private async Task SendMail(MailFromResult result, bool dispose, Action<MimeMessage> prepMessage, bool synchronous)
        {
            try
            {
                // send all emails in one shot for each domain in order to batch
                DateTime start = DateTime.UtcNow;
                int count = 0;
                List<Task> tasks = new List<Task>();
                foreach (var group in result.ToAddresses)
                {
                    Task task = SendMailInternal(result.BackingFile, result.From, group.Value, prepMessage, synchronous);
                    tasks.Add(task);
                    count++;
                }
                if (tasks.Count == 1)
                {
                    await tasks.First();
                }
                else
                {
                    await Task.WhenAll(tasks);
                }
                logger.LogInformation("Sent {count} batches of messages in {seconds}", count, (DateTime.UtcNow - start).TotalSeconds);
            }
            finally
            {
                if (dispose)
                {
                    result.Dispose();
                }
            }
        }

        private IReadOnlyCollection<MailToSend> EnumerateMessages(MimeMessage message,
            MailboxAddress from, IEnumerable<MailboxAddress> toAddresses, Action<MimeMessage> prepMessage)
        {
            List<MailToSend> messages = new List<MailToSend>();
            foreach (MailboxAddress toAddress in toAddresses)
            {
                message.From.Clear();
                message.To.Clear();
                message.From.Add(from);
                message.To.Add(toAddress);
                prepMessage?.Invoke(message);
                messages.Add(new MailToSend { Message = message });
            }
            return messages;
        }

        private async Task SendMailInternal(string fileName, MailboxAddress from,
            IEnumerable<MailboxAddress> toAddresses, Action<MimeMessage> prepMessage, bool synchronous)
        {
            if (!File.Exists(fileName))
            {
                throw new InvalidOperationException("Cannot send email, backing file does not exist");
            }
            using Stream fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            MimeMessage message = await MimeMessage.LoadAsync(fs, true, cancelToken);
            IReadOnlyCollection<MailToSend> toSend = EnumerateMessages(message, from, toAddresses, prepMessage);
            await SendMailAsync(toSend, synchronous);
        }
    }
}
