using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using MailKit;
using MailKit.Net.Smtp;

using MimeKit;

using RazorLight;

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
        private readonly IRazorLightEngine templateEngine;

        private async Task<MimeMessage> CreateMailInternalAsync(string templateName, object model, ExpandoObject extraInfo, bool allowDefault, Exception ex)
        {
            string html = null;
            var found = templateEngine.TemplateCache.RetrieveTemplate(templateName);
            if (found.Success)
            {
                html = await templateEngine.RenderTemplateAsync(found.Template.TemplatePageFactory(), model, extraInfo);
            }
            else
            {
                try
                {
                    html = await templateEngine.CompileRenderAsync(templateName, model, extraInfo);
                }
                catch (Exception _ex)
                {
                    if (allowDefault)
                    {
                        // try a default render...
                        await CreateMailInternalAsync(templateName + "Default", model, extraInfo, false, _ex);
                    }
                    else
                    {
                        MailDemonLog.Error(ex ?? _ex);
                    }
                }
            }

            if (html != null)
            {
                Match subject = Regex.Match(html, @"\<-- ?Subject: (?<subject>.*?) ?--\>", RegexOptions.IgnoreCase);
                if (subject.Success)
                {
                    string subjectText = subject.Groups["subject"].Value;
                    return new MimeMessage
                    {
                        Body = (new BodyBuilder
                        {
                            HtmlBody = "<html><body><b>Test Email Html Body Which is Bold 12345</b></body></html>"
                        }).ToMessageBody(),
                        Subject = subjectText
                    };
                }
            }

            throw new ArgumentException("Unable to find template '" + templateName + "'");
        }

        /// <summary>
        /// Consructor
        /// </summary>
        /// <param name="templateEngine">Razor light engine</param>
        public MailCreator(IRazorLightEngine templateEngine)
        {
            this.templateEngine = templateEngine ?? throw new ArgumentNullException(nameof(templateEngine));
        }

        /// <inheritdoc />
        public Task<MimeMessage> CreateMailAsync(string templateName, object model, ExpandoObject extraInfo)
        {
            return CreateMailInternalAsync(templateName, model, extraInfo, true, null);
        }
    }
}
