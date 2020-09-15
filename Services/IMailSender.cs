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
        /// Send many emails. All emails must be from the same domain.
        /// </summary>
        /// <param name="messages">Messages to send, should all be in the same domain</param>
        /// <param name="synchronous">Whether to send synchronously, in which case exceptions will throw out</param>
        /// <returns>Task</returns>
        Task SendMailAsync(IReadOnlyCollection<MailToSend> messages, bool synchronous);
    }
}
