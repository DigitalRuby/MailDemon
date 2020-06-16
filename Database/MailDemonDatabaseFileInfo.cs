using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace MailDemon
{
    public class MailDemonDatabaseFileInfo : IFileInfo
    {
        private readonly IMailDemonDatabaseProvider dbProvider;
        private readonly string rootPath;
        private readonly string fileName;
        private readonly string fileNameNoExtension;
        private readonly string fullPath;
        private readonly string name;
        private byte[] contents;

        public MailDemonDatabaseFileInfo(IMailDemonDatabaseProvider dbProvider, string rootPath, string viewPath)
        {
            this.dbProvider = dbProvider;

            // work-around bug in .NET core pathing with view start for custom rendered razor views
            if (viewPath.Equals("/_ViewStart.cshtml") || viewPath.Equals("_ViewStart.cshtml", StringComparison.OrdinalIgnoreCase))
            {
                viewPath = "Views/_ViewStart.cshtml";
            }
            this.rootPath = rootPath;
            this.fileName = viewPath;
            this.fullPath = Path.Combine(rootPath, viewPath);
            if (!File.Exists(this.fullPath))
            {
                this.fullPath = viewPath;
            }
            this.fullPath = this.fullPath.Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            this.fileNameNoExtension = Path.GetFileNameWithoutExtension(viewPath);
            this.name = Path.GetFileName(viewPath);
            GetContent();
        }
        public bool Exists { get { return contents != null; } }

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
                    using var db = dbProvider.GetDatabase();
                    MailTemplate template = db.Templates.FirstOrDefault(t => t.Name == fileNameNoExtension);
                    if (template == null)
                    {
                        return default;
                    }
                    return template.LastModified;
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
                contents = File.ReadAllBytes(fullPath);
                return;
            }
            using var db = dbProvider.GetDatabase();
            MailTemplate template = db.Templates.FirstOrDefault(t => t.Name == fileNameNoExtension);

            // views from db get layout default forced if no layout specified
            if (template != null && template.Text != null)
            {
                contents = System.Text.Encoding.UTF8.GetBytes(template.Text);
            }
        }
    }
}
