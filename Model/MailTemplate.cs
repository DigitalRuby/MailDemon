using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using System.ComponentModel.DataAnnotations.Schema;
using System.Text.RegularExpressions;

namespace MailDemon
{
    public class MailTemplateBase
    {
        /// <summary>
        /// Separator in full template names for the list name and template name
        /// </summary>
        public const string FullNameSeparator = "@";

        /// <summary>
        /// Initial subscribe page. The template with the form fields to start a subscription request.
        /// </summary>
        public const string NameSubscribeInitial = "SubscribeInitial";

        /// <summary>
        /// Subscribe confirm template name. The template that requests the user to confirm their subscription.
        /// </summary>
        public const string NameSubscribeConfirm = "SubscribeConfirm";

        /// <summary>
        /// Welcome subscription template name. The template that informs the user of their active subscription.
        /// </summary>
        public const string NameSubscribeWelcome = "SubscribeWelcome";

        /// <summary>
        /// Get a full template name from a list name and template name
        /// </summary>
        /// <param name="listName">List name</param>
        /// <param name="templateName">Template name</param>
        /// <returns>Full name</returns>
        public static string GetFullTemplateName(string listName, string templateName)
        {
            return listName + "/" + templateName;
        }

        /// <summary>
        /// Get a list and template name from a full template name
        /// </summary>
        /// <param name="listName">List name</param>
        /// <param name="templateName">Template name</param>
        /// <returns>True if success, false if invalid full template name</returns>
        public bool GetListNameAndTemplateName(out string listName, out string templateName)
        {
            int pos = Name.IndexOf(FullNameSeparator);
            if (pos < 0)
            {
                listName = templateName = null;
                return false;
            }
            listName = Name.Substring(0, pos);
            templateName = Name.Substring(++pos);
            return true;
        }

        /// <summary>
        /// Validate that a name has no bad chars in it
        /// </summary>
        /// <param name="name">Name</param>
        /// <returns>True if valid, false otherwise</returns>
        public static bool ValidateName(string name)
        {
            return (!string.IsNullOrWhiteSpace(name) && Regex.IsMatch(name, "^[A-Za-z0-9_\\-\\. ]+$"));
        }

        /// <summary>
        /// Id
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// Name, format is [listname]@[templatename]
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Title
        /// </summary>
        public string Title { get; set; }

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
