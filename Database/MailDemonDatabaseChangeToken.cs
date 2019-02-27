using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.Primitives;

namespace MailDemon
{
    public class MailDemonDatabaseChangeToken : IChangeToken
    {
        private readonly string viewPath;
        private readonly string fileNameNoExtension;
        private bool firstChange = true;

        public MailDemonDatabaseChangeToken(string viewPath)
        {
            this.viewPath = viewPath.Trim('/', '\\', '~');
            this.fileNameNoExtension = Path.GetFileNameWithoutExtension(viewPath);
        }

        public bool ActiveChangeCallbacks => false;

        public bool HasChanged
        {
            get
            {
                if (firstChange)
                {
                    firstChange = false;
                    return true;
                }

                using (var db = new MailDemonDatabase())
                {
                    bool changed = false;
                    db.Select<MailTemplate>(l => l.Name == fileNameNoExtension, (t) =>
                    {
                        if (t.Dirty)
                        {
                            changed = true;
                            t.Dirty = false;
                            return true;
                        }
                        return false;
                    });
                    return changed;
                }
            }
        }

        public IDisposable RegisterChangeCallback(Action<object> callback, object state) => EmptyDisposable.Instance;

        internal class EmptyDisposable : IDisposable
        {
            public static EmptyDisposable Instance { get; } = new EmptyDisposable();
            private EmptyDisposable() { }
            public void Dispose() { }
        }
    }
}
