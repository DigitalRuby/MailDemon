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
        /// <param name="toDomain">The domain to send to</param>
        /// <param name="messages">Messages to send - to address in each should be in toDomain</param>
        /// <returns>Task</returns>
        Task SendMailAsync(string toDomain, IEnumerable<MimeMessage> messages);
    }
}
