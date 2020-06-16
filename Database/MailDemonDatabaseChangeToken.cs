using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace MailDemon
{
    public class MailDemonDatabaseChangeToken : IChangeToken
    {
        private readonly IMailDemonDatabaseProvider dbProvider;
        private readonly string viewPath;
        private readonly string fileNameNoExtension;

        public MailDemonDatabaseChangeToken(IMailDemonDatabaseProvider dbProvider, string viewPath)
        {
            this.dbProvider = dbProvider;
            this.viewPath = viewPath.Trim('/', '\\', '~');
            this.fileNameNoExtension = Path.GetFileNameWithoutExtension(viewPath);
        }

        public bool ActiveChangeCallbacks => false;

        public bool HasChanged
        {
            get
            {
                using var db = dbProvider.GetDatabase();
                bool changed = false;
                MailTemplate template = db.Templates.FirstOrDefault(t => t.Name == fileNameNoExtension);
                if (template != null && template.Dirty)
                {
                    changed = true;
                    template.Dirty = false;
                    db.Update(template);
                    db.SaveChanges();
                }
                return changed;
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
