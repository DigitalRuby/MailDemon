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
        private string _viewPath;

        public MailDemonDatabaseChangeToken(string viewPath)
        {
            _viewPath = viewPath;
        }

        public bool ActiveChangeCallbacks => false;

        public bool HasChanged
        {
            get
            {
                string fileName = Path.GetFileNameWithoutExtension(_viewPath);
                using (var db = new MailDemonDatabase())
                {
                    MailTemplate template = db.Select<MailTemplate>(l => l.Name == fileName).FirstOrDefault();
                    if (template == null)
                    {
                        return false;
                    }
                    return (template.LastModified > template.LastRequested);
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
