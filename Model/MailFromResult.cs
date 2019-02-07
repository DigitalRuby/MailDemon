using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using MimeKit;

namespace MailDemon
{
    /// <summary>
    /// Result of a mail from smtp message
    /// </summary>
    public class MailFromResult : IDisposable
    {
        /// <summary>
        /// Full message
        /// </summary>
        public MimeMessage Message { get; set; }

        /// <summary>
        /// Message from address
        /// </summary>
        public MailboxAddress From { get; set; }

        /// <summary>
        /// Group domain and to addresses for domain
        /// </summary>
        public Dictionary<string, List<string>> ToAddresses { get; set; }

        /// <summary>
        /// Backing stream of the message
        /// </summary>
        public Stream Stream { get; set; }

        /// <summary>
        /// Cleanup all resources
        /// </summary>
        public void Dispose()
        {
            try
            {
                string toDelete = null;
                if (Stream is FileStream fs)
                {
                    toDelete = fs.Name;
                }
                Stream.Dispose();
                if (toDelete != null)
                {
                    File.Delete(toDelete);
                }
            }
            catch
            {
            }
        }
    }
}
