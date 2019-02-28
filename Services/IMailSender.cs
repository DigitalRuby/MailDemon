using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using MimeKit;

namespace MailDemon
{
    /// <summary>
    /// Interface to sent mail
    /// </summary>
    public interface IMailSender
    {
        /// <summary>
        /// Send some email
        /// </summary>
        /// <param name="message">Message to send</param>
        /// <param name="from">Who the message is from</param>
        /// <param name="toDomain">The domain to send to</param>
        /// <param name="toAddresses">The addresses to send to - each address must be in toDomain</param>
        /// <param name="onPrepare">Callback for additional message preparation</param>
        /// <returns>Task</returns>
        Task SendMailAsync(MimeMessage message, MailboxAddress from, string toDomain, IEnumerable<MailboxAddress> toAddresses, Action<MimeMessage> onPrepare = null);
    }
}
