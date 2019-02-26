using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MailDemon
{
    /// <summary>
    /// Mail template
    /// </summary>
    public class MailTemplate
    {
        /// <summary>
        /// Id
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// Template name
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Razor cshtml
        /// </summary>
        public byte[] Template { get; set; }

        /// <summary>
        /// Last modified
        /// </summary>
        public DateTime LastModified { get; set; }

        /// <summary>
        /// Whether the template is dirty (modified)
        /// </summary>
        public bool Dirty { get; set; } = true;
    }
}
