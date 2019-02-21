using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.FileProviders;

namespace MailDemon
{
    public class MailDemonDatabaseFileInfo : IFileInfo
    {
        private readonly string rootPath;
        private readonly string viewPath;
        private readonly string name;
        private byte[] contents;

        public MailDemonDatabaseFileInfo(string rootPath, string viewPath)
        {
            this.rootPath = rootPath;
            this.viewPath = viewPath;
            this.name = Path.GetFileName(viewPath);
            GetView(viewPath);
        }
        public bool Exists { get; private set; }

        public bool IsDirectory => false;

        public DateTimeOffset LastModified { get; private set; }

        public long Length
        {
            get { return contents == null ? 0 : contents.Length; }
        }

        public string Name => Path.GetFileName(viewPath);

        public string PhysicalPath => null;

        public Stream CreateReadStream()
        {
            return new MemoryStream(contents);
        }

        private void GetView(string viewPath)
        {
            string fileName = Path.GetFileNameWithoutExtension(viewPath);
            using (var db = new MailDemonDatabase())
            {
                MailTemplate template = null;
                db.Select<MailTemplate>(t => t.Name == fileName, (foundTemplate) =>
                {
                    template = foundTemplate;
                    foundTemplate.LastRequested = DateTime.UtcNow;
                    return true;
                });
                if (template != null && template.Template != null)
                {
                    Exists = true;
                    contents = template.Template;
                    LastModified = template.LastModified;
                }
                else
                {
                    string fullPath = Path.Combine(rootPath, viewPath);
                    if (File.Exists(fullPath))
                    {
                        contents = File.ReadAllBytes(fullPath);
                        LastModified = File.GetLastWriteTimeUtc(fullPath);
                    }
                }
            }
        }
    }
}
