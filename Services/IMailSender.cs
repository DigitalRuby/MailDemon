using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using MimeKit;

namespace MailDemon
{
    /// <summary>
    /// Mail to be sent
    /// </summary>
    public class MailToSend
    {
        /// <summary>
        /// Subscription
        /// </summary>
        public MailListSubscription Subscription { get; set; }

        /// <summary>
        /// Message
        /// </summary>
        public MimeMessage Message { get; set; }

        /// <summary>
        /// Callback, string will be empty if success, otherwise an error string
        /// </summary>
        public Action<MailListSubscription, string> Callback { get; set; }
    }

    /// <summary>
    /// Interface to sent mail
    /// </summary>
    public interface IMailSender
    {
        /// <summary>
        /// Send some email
        /// </summary>
        /// <param name="toDomain">The domain to send to</param>
        /// <param name="messages">Messages to send - to address in each should be in toDomain</param>
        /// <returns>Task</returns>
        Task SendMailAsync(string toDomain, IAsyncEnumerable<MailToSend> messages);
    }
}
