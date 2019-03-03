using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using MailKit;
using MailKit.Net.Smtp;

using MimeKit;

namespace MailDemon
{
    /// <summary>
    /// Creates mail messages
    /// </summary>
    public interface IMailCreator
    {
        /// <summary>
        /// Create mail
        /// </summary>
        /// <param name="templateName">Full template name</param>
        /// <param name="model">Model</param>
        /// <param name="extraInfo">Extra info (view bag)</param>
        /// <returns>MimeMessage with body and subject populated, you will need to set to and from addresses, etc.</returns>
        Task<MimeMessage> CreateMailAsync(string templateName, object model, ExpandoObject extraInfo);
    }

    /// <summary>
    /// Mail creator implementation with razor light engine
    /// For the mail subject, use <!-- Subject: ... --> inside the body of the template
    /// </summary>
    public class MailCreator : IMailCreator
    {
        private readonly IViewRenderService templateEngine;

        private async Task<MimeMessage> CreateMailInternalAsync(string templateName, object model, ExpandoObject extraInfo, bool allowDefault)
        {
            string html = await templateEngine.RenderViewToStringAsync(templateName, model, extraInfo);

            if (html != null)
            {
                Match subject = Regex.Match(html, @"\<!-- ?Subject: (?<subject>.*?) ?--\>", RegexOptions.IgnoreCase);
                if (subject.Success)
                {
                    string subjectText = subject.Groups["subject"].Value.Trim();
                    return new MimeMessage
                    {
                        Body = (new BodyBuilder
                        {
                            HtmlBody = html
                        }).ToMessageBody(),
                        Subject = subjectText
                    };
                }
                else
                {
                    throw new InvalidOperationException(Resources.MissingSubjectInTemplate);
                }
            }
            else if (allowDefault)
            {
                templateName = MailTemplate.GetTemplateName(templateName);
                return await CreateMailInternalAsync(templateName + "Default", model, extraInfo, false);
            }

            throw new ArgumentException("No view found for name " + templateName);
        }

        /// <summary>
        /// Consructor
        /// </summary>
        /// <param name="templateEngine">View render service</param>
        public MailCreator(IViewRenderService templateEngine)
        {
            this.templateEngine = templateEngine ?? throw new ArgumentNullException(nameof(templateEngine));
        }

        /// <inheritdoc />
        public Task<MimeMessage> CreateMailAsync(string templateName, object model, ExpandoObject extraInfo)
        {
            return CreateMailInternalAsync(templateName, model, extraInfo, true);
        }
    }
}
