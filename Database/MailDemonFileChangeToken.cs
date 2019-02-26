using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.Primitives;

namespace MailDemon
{
    public class MailDemonFileChangeToken : IChangeToken
    {
        private readonly FileInfo fileInfo;
        private DateTime lastWriteTime;

        public MailDemonFileChangeToken(string viewPath)
        {
            fileInfo = new FileInfo(viewPath);
        }

        public bool ActiveChangeCallbacks => false;

        public bool HasChanged
        {
            get
            {
                fileInfo.Refresh();
                bool changed = (fileInfo.LastWriteTimeUtc != lastWriteTime);
                lastWriteTime = fileInfo.LastWriteTimeUtc;
                return changed;
            }
        }

        public IDisposable RegisterChangeCallback(Action<object> callback, object state) => MailDemonDatabaseChangeToken.EmptyDisposable.Instance;
    }

    public class MailDemonFileProjectItem : RazorLight.Razor.RazorLightProjectItem
    {
        private readonly string templateKey;
        private readonly string fullPath;
        private readonly byte[] content;

        public MailDemonFileProjectItem(string rootDirectory, string templateKey)
        {
            this.templateKey = templateKey;
            if (Path.IsPathFullyQualified(templateKey))
            {
                fullPath = templateKey;
            }
            else
            {
                fullPath = Path.Combine(rootDirectory, templateKey);
            }
            if (File.Exists(fullPath))
            {
                content = File.ReadAllBytes(fullPath);
            }
            ExpirationToken = new MailDemonFileChangeToken(fullPath);
        }

        public override string Key => templateKey;
        public override bool Exists => content != null;
        public override Stream Read() => new MemoryStream(content);
    }
}
