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
        private readonly string fileName;
        private readonly string fileNameNoExtension;
        private readonly string fullPath;
        private readonly string name;
        private byte[] contents;

        public MailDemonDatabaseFileInfo(string rootPath, string viewPath)
        {
            this.rootPath = rootPath;
            this.fileName = viewPath;
            this.fullPath = (Path.IsPathFullyQualified(viewPath) ? viewPath : Path.Combine(rootPath, viewPath)).Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            this.fileNameNoExtension = Path.GetFileNameWithoutExtension(viewPath);
            this.name = Path.GetFileName(viewPath);
            GetContent();
        }
        public bool Exists { get; private set; }

        public bool IsDirectory => false;

        public DateTimeOffset LastModified
        {
            get
            {
                if (File.Exists(fullPath))
                {
                    return new FileInfo(fullPath).LastWriteTimeUtc;
                }
                else
                {
                    using (var db = new MailDemonDatabase())
                    {
                        MailTemplate template = db.Select<MailTemplate>(t => t.Name == fileNameNoExtension).FirstOrDefault();
                        if (template == null)
                        {
                            return default;
                        }
                        return template.LastModified;
                    }
                }
            }
        }

        public long Length
        {
            get { return contents == null ? 0 : contents.Length; }
        }

        public string Name => fileName;

        public string PhysicalPath => fullPath;

        public Stream CreateReadStream()
        {
            return new MemoryStream(contents);
        }

        private void GetContent()
        {
            if (File.Exists(fullPath))
            {
                Exists = true;
                contents = File.ReadAllBytes(fullPath);
                return;
            }
            using (var db = new MailDemonDatabase())
            {
                MailTemplate template = null;
                db.Select<MailTemplate>(t => t.Name == fileNameNoExtension, (foundTemplate) =>
                {
                    template = foundTemplate;
                    return true;
                });
                if (template != null && template.Text != null)
                {
                    Exists = true;
                    contents = System.Text.Encoding.UTF8.GetBytes(template.Text);
                }
            }
        }
    }
}
