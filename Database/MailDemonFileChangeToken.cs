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
            lastWriteTime = fileInfo.LastWriteTimeUtc;
        }

        public bool ActiveChangeCallbacks => false;

        public bool HasChanged
        {
            get
            {
                fileInfo.Refresh();
                if (fileInfo.LastWriteTimeUtc != lastWriteTime)
                {
                    hasChanged = true;
                    lastWriteTime = fileInfo.LastWriteTimeUtc;
                }
                return hasChanged;
            }
        }

        public IDisposable RegisterChangeCallback(Action<object> callback, object state) => MailDemonDatabaseChangeToken.EmptyDisposable.Instance;
        internal bool hasChanged;
    }

    public class MailDemonFileProjectItem : RazorLight.Razor.RazorLightProjectItem
    {
        private readonly string templateKey;
        private readonly string fullPath;

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
            ExpirationToken = new MailDemonFileChangeToken(fullPath);
        }

        public override string Key => templateKey;
        public override bool Exists => File.Exists(fullPath);
        public override Stream Read()
        {
            byte[] bytes = File.ReadAllBytes(fullPath);
            (ExpirationToken as MailDemonFileChangeToken).hasChanged = false;
            return new MemoryStream(bytes);
        }
    }
}
