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
        private readonly string _rootPath;
        private readonly string _viewPath;
        private byte[] _viewContent;
        private DateTimeOffset _lastModified;
        private bool _exists;

        public MailDemonDatabaseFileInfo(string rootPath, string viewPath)
        {
            _rootPath = rootPath;
            _viewPath = viewPath;
            GetView(viewPath);
        }
        public bool Exists => _exists;

        public bool IsDirectory => false;

        public DateTimeOffset LastModified => _lastModified;

        public long Length
        {
            get { return _viewContent == null ? 0 : _viewContent.Length; }
        }

        public string Name => Path.GetFileName(_viewPath);

        public string PhysicalPath => null;

        public Stream CreateReadStream()
        {
            return new MemoryStream(_viewContent);
        }

        private void GetView(string viewPath)
        {
            // db files must not start with '_'
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
                    _exists = true;
                    _viewContent = template.Template;
                    _lastModified = template.LastModified;
                }
                else
                {
                    string fullPath = Path.Combine(_rootPath, "Templates", viewPath);
                    if (File.Exists(fullPath))
                    {
                        _viewContent = File.ReadAllBytes(fullPath);
                        _lastModified = File.GetLastWriteTimeUtc(fullPath);
                    }
                }
            }
        }
    }
}
