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
        /// <param name="reg">Registration</param>
        /// <returns>Registration</returns>
        public static MailListRegistration PreSubscribeToMailingList(this MailDemonDatabase db, MailListRegistration reg)
        {
            string token = string.Empty;
            db.Select<MailListRegistration>(r => r.EmailAddress == reg.EmailAddress && r.ListName == reg.ListName, (foundReg) =>
            {
                foundReg.Fields.Clear();
                foreach (var kv in reg.Fields)
                {
                    foundReg.SetField(kv.Key, kv.Value);
                }
                foundReg.Error = reg.Error;
                foundReg.Message = reg.Message;
                foundReg.IPAddress = reg.IPAddress;
                foundReg.MailList = reg.MailList;
                foundReg.TemplateName = reg.TemplateName;
                reg = foundReg;
                return false;
            });
            if (reg.SubscribeToken == null)
            {
                reg.SubscribeToken = Guid.NewGuid().ToString("N");
                reg.Expires = DateTime.UtcNow.AddHours(1.0);
            }
            db.Upsert(reg);
            return reg;
        }

        /// <summary>
        /// Confirm subscribe to a mailing list
        /// </summary>
        /// <param name="db">DB</param>
        /// <param name="listName">List name</param>
        /// <param name="token">Subscribe token</param>
        /// <returns>Registration or null if not found</returns>
        public static MailListRegistration ConfirmSubscribeToMailingList(this MailDemonDatabase db, string listName, string token)
        {
            MailListRegistration reg = null;
            db.Select<MailListRegistration>(r => r.SubscribeToken == token, (foundReg) =>
            {
                if (foundReg.ListName == listName && foundReg.SubscribedDate == default && foundReg.SubscribeToken == token)
                {
                    reg = foundReg;
                    foundReg.SubscribedDate = DateTime.UtcNow;
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
            db.Select<MailListRegistration>(r => r.UnsubscribeToken == token, (foundReg) =>
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
