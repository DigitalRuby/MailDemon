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
        /// Message from address
        /// </summary>
        public MailboxAddress From { get; set; }

        /// <summary>
        /// Group domain and to addresses for domain
        /// </summary>
        public IEnumerable<KeyValuePair<string, IEnumerable<MailboxAddress>>> ToAddresses { get; set; }

        /// <summary>
        /// Backing file of the message
        /// </summary>
        public string BackingFile { get; set; }

        /// <summary>
        /// Cleanup all resources, delete backing file if Stream is FileStream.
        /// </summary>
        public void Dispose()
        {
            try
            {
                if (File.Exists(BackingFile))
                {
                    File.Delete(BackingFile);
                }
                BackingFile = null;
            }
            catch
            {
            }
        }
    }
}
