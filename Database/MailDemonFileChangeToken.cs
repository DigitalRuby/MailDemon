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
}
