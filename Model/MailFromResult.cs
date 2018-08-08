using System;
using System.Collections.Generic;
using System.Text;

using MimeKit;

namespace MailDemon
{
    /// <summary>
    /// Result of a mail from smtp message
    /// </summary>
    public class MailFromResult
    {
        /// <summary>
        /// Full message
        /// </summary>
        public MimeMessage Message { get; set; }

        /// <summary>
        /// Message from address
        /// </summary>
        public string From { get; set; }

        /// <summary>
        /// Group domain and to addresses for domain
        /// </summary>
        public Dictionary<string, List<string>> ToAddresses { get; set; }
    }
}
