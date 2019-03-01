using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

//using RazorLight;
//using RazorLight.Razor;

namespace MailDemon
{
    /*
    public class MailDemonRazorLightDatabaseProject : RazorLightProject
    {
        private readonly string rootPath;

        private class MailDemonRazorProjectItem : RazorLightProjectItem
        {
            private readonly byte[] content;

            public MailDemonRazorProjectItem(string key)
            {
                Key = key;
                using (MailDemonDatabase db = new MailDemonDatabase())
                {
                    MailTemplate template = db.Select<MailTemplate>(t => t.Name == key).FirstOrDefault();
                    if (template != null)
                    {
                        content = System.Text.Encoding.UTF8.GetBytes(template.Text);
                    }
                }
                ExpirationToken = new MailDemonDatabaseChangeToken(key);
            }

            public override string Key { get; }
            public override bool Exists => content != null;
            public override System.IO.Stream Read() => new MemoryStream(content);
        }

        public MailDemonRazorLightDatabaseProject(string rootPath)
        {
            this.rootPath = rootPath;
        }

        private RazorLightProjectItem TryFileProject(string templateKey)
        {
            if (!templateKey.EndsWith(".cshtml"))
            {
                templateKey += ".cshtml";
            }
            if (File.Exists(templateKey) || File.Exists(Path.Combine(rootPath, templateKey)))
            {
                return new MailDemonFileProjectItem(rootPath, templateKey);
            }
            string alternate = "Views/Shared/" + templateKey;
            if (File.Exists(alternate) || File.Exists(Path.Combine(rootPath, alternate)))
            {
                return new MailDemonFileProjectItem(rootPath, alternate);
            }
            alternate = "Views/Templates/" + templateKey;
            if (File.Exists(alternate) || File.Exists(Path.Combine(rootPath, alternate)))
            {
                return new MailDemonFileProjectItem(rootPath, alternate);
            }
            return null;
        }

        public override Task<IEnumerable<RazorLightProjectItem>> GetImportsAsync(string templateKey)
        {
            return Task.FromResult(Enumerable.Empty<RazorLightProjectItem>());
        }

        public override Task<RazorLightProjectItem> GetItemAsync(string templateKey)
        {
            RazorLightProjectItem project = TryFileProject(templateKey);
            if (project != null)
            {
                return Task.FromResult(project);
            }
            return Task.FromResult<RazorLightProjectItem>(new MailDemonRazorProjectItem(templateKey));
        }
    }
    */
}
