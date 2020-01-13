using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace MailDemon
{
    public class MailDemonDatabaseFileProvider : IFileProvider
    {
        private class PhysicalDirectoryContents : IDirectoryContents
        {
            private readonly IServiceProvider serviceProvider;
            private readonly string dir;

            public PhysicalDirectoryContents(IServiceProvider serviceProvider, string dir)
            {
                this.serviceProvider = serviceProvider;
                this.dir = dir;
            }

            public bool Exists => Directory.Exists(dir);

            public IEnumerator<IFileInfo> GetEnumerator()
            {
                foreach (string file in Directory.EnumerateFiles(dir, "*.cshtml"))
                {
                    yield return new MailDemonDatabaseFileInfo(serviceProvider, dir, Path.GetFileName(file));
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                foreach (string file in Directory.EnumerateFiles(dir, "*.cshtml"))
                {
                    yield return new MailDemonDatabaseFileInfo(serviceProvider, dir, Path.GetFileName(file));
                }
            }
        }

        private readonly IServiceProvider serviceProvider;
        private readonly string rootPath;

        public MailDemonDatabaseFileProvider(IServiceProvider serviceProvider, string rootPath)
        {
            this.serviceProvider = serviceProvider;
            this.rootPath = rootPath;
        }

        public IDirectoryContents GetDirectoryContents(string subPath)
        {
            return new PhysicalDirectoryContents(serviceProvider, rootPath);
        }

        public IFileInfo GetFileInfo(string subPath)
        {
            var result = new MailDemonDatabaseFileInfo(serviceProvider, rootPath, subPath);
            return result.Exists ? result as IFileInfo : new NotFoundFileInfo(subPath);
        }

        public IChangeToken Watch(string filter)
        {
            filter = filter.Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (File.Exists(filter))
            {
                return new MailDemonFileChangeToken(filter);
            }
            return new MailDemonDatabaseChangeToken(serviceProvider, filter);
        }
    }
}
