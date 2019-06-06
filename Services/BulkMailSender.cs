using MimeKit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

namespace MailDemon
{
    public interface IBulkMailSender
    {
        Task SendBulkMail(MailList list, IMailCreator mailCreator, IMailSender mailSender, string fullTemplateName, string unsubscribeUrl);
    }

    public class BulkMailSender : IBulkMailSender
    {
        private readonly IServiceProvider serviceProvider;

        // TODO: Use async enumerator
        private IEnumerable<MimeMessage> GetMessages(IEnumerable<MailListSubscription> subs, IMailCreator mailCreator, MailList list, string fullTemplateName)
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
                yield return message;
            }
        }

        public BulkMailSender(IServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider;
        }

        public async Task SendBulkMail(MailList list, IMailCreator mailCreator, IMailSender mailSender, string fullTemplateName, string unsubscribeUrl)
        {
            List<MailListSubscription> subs = new List<MailListSubscription>();
            string toDomain = null;
            string addressDomain;

            using (var db = serviceProvider.GetService<IMailDemonDatabase>())
            {
                foreach (MailListSubscription sub in db.Select<MailListSubscription>(s => s.ListName == list.Name && s.SubscribedDate != default && s.UnsubscribedDate == default).OrderBy(s => s.EmailAddressDomain))
                {
                    addressDomain = sub.EmailAddressDomain;
                    if (toDomain != null && addressDomain != toDomain)
                    {
                        try
                        {
                            await mailSender.SendMailAsync(toDomain, GetMessages(subs, mailCreator, list, fullTemplateName));
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
                }
            }
            if (subs.Count != 0)
            {
                try
                {
                    await mailSender.SendMailAsync(toDomain, GetMessages(subs, mailCreator, list, fullTemplateName));
                }
                catch (Exception ex)
                {
                    MailDemonLog.Error(ex);
                }
            }
        }
    }
}
