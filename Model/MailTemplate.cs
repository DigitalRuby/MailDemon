using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using System.ComponentModel.DataAnnotations.Schema;

namespace MailDemon
{
    public class MailTemplateBase
    {
        /// <summary>
        /// Id
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// Template list name
        /// </summary>
        public string ListName { get; set; }

        /// <summary>
        /// Template name
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Last modified
        /// </summary>
        public DateTime LastModified { get; set; }
    }

    /// <summary>
    /// Mail template
    /// </summary>
    public class MailTemplate : MailTemplateBase
    {
        /// <summary>
        /// Template text (razor format)
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// Whether the template is dirty (modified)
        /// </summary>
        public bool Dirty { get; set; } = true;
    }

    public class MailTemplateModel : BaseModel<MailTemplate> { }
}
