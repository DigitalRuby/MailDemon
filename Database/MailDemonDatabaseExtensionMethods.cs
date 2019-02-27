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
        /// <param name="fields">Fields</param>
        /// <param name="emailAddress">Email address</param>
        /// <param name="listName">List name</param>
        /// <param name="ipAddress">IP address</param>
        /// <returns>Registration</returns>
        public static MailListRegistration PreSubscribeToMailingList(this MailDemonDatabase db, IDictionary<string, object> fields, string emailAddress, string listName, string ipAddress)
        {
            // make sure we have a list
            MailList list = db.Select<MailList>(l => l.Name == listName).FirstOrDefault();
            if (list == null)
            {
                throw new ArgumentException("No list with name " + listName);
            }
            string token = string.Empty;
            MailListRegistration reg = null;
            db.Select<MailListRegistration>(r => r.EmailAddress == emailAddress && r.ListName == listName, (foundReg) =>
            {
                reg = foundReg;
                return false;
            });
            if (reg == null)
            {
                // new subscribe confirm
                reg = new MailListRegistration
                {
                    EmailAddress = emailAddress,
                    ListName = listName,
                    Fields = fields,
                    Expires = DateTime.UtcNow.AddHours(1.0),
                    IPAddress = ipAddress,
                    SubscribeToken = token = Guid.NewGuid().ToString("N")
                };
                db.Insert(reg);
            }
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
