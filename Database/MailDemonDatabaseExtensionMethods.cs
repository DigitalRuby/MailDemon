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
        /// <returns>Subscribe token or null if already subscribed</returns>
        public static string PreSubscribeToMailingList(this MailDemonDatabase db, IDictionary<string, object> fields, string emailAddress, string listName, string ipAddress)
        {
            string token = string.Empty;
            db.Select<MailListRegistration>(r => r.EmailAddress == emailAddress && r.ListName == listName, (foundReg) =>
            {
                if (foundReg.SubscribedDate == default)
                {
                    token = foundReg.SubscribeToken;
                }
                else
                {
                    // already subscribed
                    token = null;
                }
                return false;
            });
            if (token != null && token.Length == 0)
            {
                // new subscribe confirm
                MailListRegistration reg = new MailListRegistration
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
            return token;
        }

        /// <summary>
        /// Confirm subscribe to a mailing list
        /// </summary>
        /// <param name="db">DB</param>
        /// <param name="listName">List name</param>
        /// <param name="token">Subscribe token</param>
        /// <returns>True if subscribed, false if not</returns>
        public static bool ConfirmSubscribeToMailingList(this MailDemonDatabase db, string listName, string token)
        {
            bool foundOne = false;
            db.Select<MailListRegistration>(r => r.SubscribeToken == token, (foundReg) =>
            {
                if (foundReg.ListName == listName && foundReg.SubscribedDate == default && foundReg.SubscribeToken == token)
                {
                    foundOne = true;
                    foundReg.SubscribedDate = DateTime.UtcNow;
                    foundReg.UnsubscribeToken = Guid.NewGuid().ToString("N");
                    return true;
                }
                return false;
            });
            return foundOne;
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
