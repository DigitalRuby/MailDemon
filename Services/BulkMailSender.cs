using MimeKit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

namespace MailDemon
{
    /// <summary>
    /// Handles sending of bulk email
    /// </summary>
    public interface IBulkMailSender
    {
        /// <summary>
        /// Send bulk email
        /// </summary>
        /// <param name="list">List to send email from</param>
        /// <param name="mailCreator">Creates the email message</param>
        /// <param name="mailSender">Sends the email message</param>
        /// <param name="all">True to send to all subscribers, false to only send to subscribers with a non-empty result (error state)</param>
        /// <param name="fullTemplateName">The template to create, i.e. List@TemplateName</param>
        /// <param name="unsubscribeUrl">The unsubscribe url to put in the message, {0} is the unsubscribe token</param>
        /// <returns>Task</returns>
        Task SendBulkMail(MailList list, IMailCreator mailCreator, IMailSender mailSender, bool all,
            string fullTemplateName, string unsubscribeUrl);
    }

    public class BulkMailSender : IBulkMailSender
    {
        private readonly IServiceProvider serviceProvider;

        // TODO: Use async enumerator
        private IEnumerable<MailToSend> GetMessages(IEnumerable<MailListSubscription> subs, IMailCreator mailCreator, MailList list,
            string fullTemplateName, Action<MailListSubscription, string> callback)
        {
            foreach (MailListSubscription sub in subs)
            {
                MimeMessage message = mailCreator.CreateMailAsync(fullTemplateName, sub, null, null).Sync();
                message.From.Clear();
                message.To.Clear();
                if (string.IsNullOrWhiteSpace(list.FromEmailName))
                {
                    message.From.Add(new MailboxAddress(list.FromEmailAddress));
                }
                else
                {
                    message.From.Add(new MailboxAddress(list.FromEmailName, list.FromEmailAddress));
                }
                message.To.Add(new MailboxAddress(sub.EmailAddress));
                yield return new MailToSend { Subscription = sub, Message = message, Callback = callback };
            }
        }

        public BulkMailSender(IServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider;
        }

        public async Task SendBulkMail(MailList list, IMailCreator mailCreator, IMailSender mailSender, bool all,
            string fullTemplateName, string unsubscribeUrl)
        {
            MailDemonLog.Warn("Started bulk send for {0}", fullTemplateName);

            List<MailListSubscription> subs = new List<MailListSubscription>();
            string toDomain = null;
            string addressDomain;
            DateTime now = DateTime.UtcNow;
            int count = 0;

            using (var db = serviceProvider.GetService<IMailDemonDatabase>())
            {
                void callbackHandler(MailListSubscription _sub, string error)
                {
                    _sub.Result = error;
                    _sub.ResultTimestamp = DateTime.UtcNow;
                    db.Update(_sub);
                }

                // mark subs as pending
                IEnumerable<MailListSubscription> updates = db.Select<MailListSubscription>(s =>
                    s.ListName == list.Name && s.SubscribedDate != default && s.UnsubscribedDate == default && (all || !string.IsNullOrWhiteSpace(s.Result)),
                    (sub) =>
                    {
                        sub.MakePending(now, all);
                        return true;
                    });
                foreach (MailListSubscription sub in db.Select<MailListSubscription>(s => s.ListName == list.Name && s.Result == "Pending"))
                {
                    addressDomain = sub.EmailAddressDomain;
                    if (toDomain != null && addressDomain != toDomain)
                    {
                        now = DateTime.UtcNow;
                        try
                        {
                            await mailSender.SendMailAsync(toDomain, GetMessages(subs, mailCreator, list, fullTemplateName, callbackHandler));
                        }
                        catch (Exception ex)
                        {
                            MailDemonLog.Error(ex);
                        }
                        subs.Clear();
                    }
                    toDomain = addressDomain;
                    sub.MailList = list;
                    sub.UnsubscribeUrl = string.Format(unsubscribeUrl, sub.UnsubscribeToken);
                    subs.Add(sub);
                    count++;
                }

                // handle any left-over subs
                if (subs.Count != 0)
                {
                    count += subs.Count;
                    try
                    {
                        await mailSender.SendMailAsync(toDomain, GetMessages(subs, mailCreator, list, fullTemplateName, callbackHandler));
                    }
                    catch (Exception ex)
                    {
                        MailDemonLog.Error(ex);
                    }
                }

                MailDemonLog.Warn("Finished bulk send {0} messages for {1}", count, fullTemplateName);
            }
        }
    }
}
