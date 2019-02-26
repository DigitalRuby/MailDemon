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
            private readonly byte[] _content;

            public MailDemonRazorProjectItem(string key, byte[] content)
            {
                Key = key;
                _content = content;
                ExpirationToken = new MailDemonDatabaseChangeToken(key);
            }

            public override string Key { get; }
            public override bool Exists => _content != null;
            public override System.IO.Stream Read() => new MemoryStream(_content);
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
            using (MailDemonDatabase db = new MailDemonDatabase())
            {
                MailTemplate template = db.Select<MailTemplate>(t => t.Name == templateKey).FirstOrDefault();
                return Task.FromResult<RazorLightProjectItem>(new MailDemonRazorProjectItem(templateKey, template?.Template));
            }
        }
    }
}
