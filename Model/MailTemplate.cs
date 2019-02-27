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
        /// Confirmation template name
        /// </summary>
        public const string NameConfirmation = "Confirmation";

        /// <summary>
        /// Welcome subscription template name
        /// </summary>
        public const string NameWelcome = "Welcome";

        /// <summary>
        /// Confirm subscription var name
        /// </summary>
        public const string VarConfirmUrl = "confirm-url";

        /// <summary>
        /// Unsubscribe var name
        /// </summary>
        public const string VarUnsubscribeUrl = "unsubscribe-url";

        /// <summary>
        /// Get a full template name from a list name and template name
        /// </summary>
        /// <param name="listName">List name</param>
        /// <param name="templateName">Template name</param>
        /// <returns>Full name</returns>
        public static string GetFullName(string listName, string templateName)
        {
            return listName + "|" + templateName;
        }

        /// <summary>
        /// Id
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// Name, format is [listname]|[templatename]
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Subject
        /// </summary>
        public string Subject { get; set; }

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
