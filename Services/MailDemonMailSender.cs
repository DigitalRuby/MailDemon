using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Threading.Tasks;

using RazorLight;

namespace MailDemon
{
    public class MailDemonMailSender : IMailSendService
    {
        private readonly MailDemonService service;
        private readonly IServiceProvider provider;

        public MailDemonMailSender(MailDemonService service, IServiceProvider provider)
        {
            this.service = service;
            this.provider = provider;
        }

        public Task SendMail(string to, string listName, string templateName, object model, ExpandoObject extraInfo)
        {
            if (provider.GetService(typeof(IRazorLightEngine)) is IRazorLightEngine engine)
            {
                string path = MailTemplate.GetFullName(listName, templateName);
                string html;
                var found = engine.TemplateCache.RetrieveTemplate(path);
                if (!found.Success)
                {
                    html = engine.CompileRenderAsync(path, model, extraInfo).Sync();
                }
            }
            return Task.CompletedTask;
        }
    }
}
