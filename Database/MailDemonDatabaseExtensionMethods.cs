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
            string token = string.Empty;
            MailListSubscription final = reg;
            db.Select<MailListSubscription>(r => r.EmailAddress == final.EmailAddress && r.ListName == final.ListName, (foundReg) =>
            {
                foundReg.Fields.Clear();
                foreach (var kv in final.Fields)
                {
                    foundReg.SetField(kv.Key, kv.Value);
                }
                foundReg.Error = final.Error;
                foundReg.Message = final.Message;
                foundReg.IPAddress = final.IPAddress;
                foundReg.MailList = final.MailList;
                foundReg.TemplateName = final.TemplateName;
                final = foundReg;
                return false;
            });
            reg = final;
            if (reg.SubscribeToken == null)
            {
                reg.SubscribeToken = Guid.NewGuid().ToString("N");
                reg.Expires = DateTime.UtcNow.AddHours(1.0);
                db.Upsert(reg);
                return true;
            }
            return false;
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
            db.Select<MailListSubscription>(r => r.SubscribeToken == token, (foundReg) =>
            {
                if (foundReg.ListName == listName && foundReg.SubscribedDate == default && foundReg.SubscribeToken == token)
                {
                    reg = foundReg;
                    foundReg.Expires = DateTime.MaxValue;
                    foundReg.SubscribedDate = DateTime.UtcNow;
                    foundReg.UnsubscribedDate = default;
                    foundReg.UnsubscribeToken = Guid.NewGuid().ToString("N");
                    foundReg.MailList = db.Select<MailList>(l => l.Name == listName).FirstOrDefault();
                    return true;
                }
                return false;
            });
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
            bool foundOne = false;
            db.Select<MailListSubscription>(r => r.UnsubscribeToken == token, (foundReg) =>
            {
                if (foundReg.ListName == listName && foundReg.UnsubscribedDate == default)
                {
                    foundOne = true;
                    foundReg.UnsubscribedDate = DateTime.UtcNow;
                    return true;
                }
                return false;
            });
            return foundOne;
        }
    }
}
