using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MailDemon
{
    public static class MailDemonDatabaseExtensionMethods
    {
        /// <summary>
        /// Pre subscribe to a mailing list
        /// </summary>
        /// <param name="db">DB</param>
        /// <param name="reg">Registration, receives new registration if success</param>
        /// <returns>True if success, false if already subscribed</returns>
        public static bool PreSubscribeToMailingList(this MailDemonDatabase db, ref MailListSubscription reg)
        {
            bool result = false;
            string token = string.Empty;
            MailListSubscription final = reg;
            MailListSubscription dbReg = db.Subscriptions.FirstOrDefault(r => r.EmailAddress == final.EmailAddress && r.ListName == final.ListName);
            if (dbReg != null)
            {
                dbReg.Fields.Clear();
                foreach (var kv in final.Fields)
                {
                    dbReg.SetField(kv.Key, kv.Value);
                }
                dbReg.Error = final.Error;
                dbReg.Message = final.Message;
                dbReg.IPAddress = final.IPAddress;
                dbReg.MailList = final.MailList;
                dbReg.TemplateName = final.TemplateName;
                final = dbReg;
            }
            reg = final;
            if (reg.SubscribeToken == null)
            {
                reg.SubscribeToken = Guid.NewGuid().ToString("N");
                reg.Expires = DateTime.UtcNow.AddHours(1.0);
                if (reg.Id == 0)
                {
                    db.Subscriptions.Add(reg);
                }
                else
                {
                    db.Update(reg);
                }
                result = true;
            }
            db.SaveChanges();
            return result;
        }

        /// <summary>
        /// Confirm subscribe to a mailing list
        /// </summary>
        /// <param name="db">DB</param>
        /// <param name="listName">List name</param>
        /// <param name="token">Subscribe token</param>
        /// <returns>Registration or null if not found</returns>
        public static MailListSubscription ConfirmSubscribeToMailingList(this MailDemonDatabase db, string listName, string token)
        {
            MailListSubscription reg = null;
            MailListSubscription foundReg = db.Subscriptions.FirstOrDefault(r => r.SubscribeToken == token);
            if (foundReg != null && foundReg.ListName == listName && foundReg.SubscribedDate == default && foundReg.SubscribeToken == token)
            {
                reg = foundReg;
                foundReg.Expires = DateTime.MaxValue;
                foundReg.SubscribedDate = DateTime.UtcNow;
                foundReg.UnsubscribedDate = default;
                foundReg.UnsubscribeToken = Guid.NewGuid().ToString("N");
                foundReg.MailList = db.Lists.FirstOrDefault(l => l.Name == listName);
                db.SaveChanges();
            }
            return reg;
        }

        /// <summary>
        /// Unsubscribe from a mailing list
        /// </summary>
        /// <param name="db">DB</param>
        /// <param name="listName">List name</param>
        /// <param name="token">Unsubscribe token</param>
        /// <returns>True if unsubscribed, false if not</returns>
        public static bool UnsubscribeFromMailingList(this MailDemonDatabase db, string listName, string token)
        {
            MailListSubscription foundReg = db.Subscriptions.FirstOrDefault(r => r.UnsubscribeToken == token && r.ListName == listName && r.UnsubscribedDate == default);
            if (foundReg != null)
            {
                foundReg.UnsubscribedDate = DateTime.UtcNow;
                foundReg.SubscribeToken = null;
                db.SaveChanges();
                return true;
            };
            return false;
        }
    }
}
