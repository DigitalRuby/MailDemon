using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using RazorLight;
using RazorLight.Razor;

namespace MailDemon
{
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
                        content = template.Template;
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

        public override Task<IEnumerable<RazorLightProjectItem>> GetImportsAsync(string templateKey)
        {
            return Task.FromResult(Enumerable.Empty<RazorLightProjectItem>());
        }

        public override Task<RazorLightProjectItem> GetItemAsync(string templateKey)
        {
            if (File.Exists(templateKey) || File.Exists(Path.Combine(rootPath, templateKey)))
            {
                return Task.FromResult<RazorLightProjectItem>(new MailDemonFileProjectItem(rootPath, templateKey));
            }
            return Task.FromResult<RazorLightProjectItem>(new MailDemonRazorProjectItem(templateKey));
        }
    }
}
