using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace MailDemon
{
    public class MailDemonDatabaseFileProvider : IFileProvider
    {
        private class PhysicalDirectoryContents : IDirectoryContents
        {
            private readonly IMailDemonDatabaseProvider dbProvider;
            private readonly string dir;

            public PhysicalDirectoryContents(IMailDemonDatabaseProvider dbProvider, string dir)
            {
                this.dbProvider = dbProvider;
                this.dir = dir;
            }

            public bool Exists => Directory.Exists(dir);

            public IEnumerator<IFileInfo> GetEnumerator()
            {
                foreach (string file in Directory.EnumerateFiles(dir, "*.cshtml"))
                {
                    yield return new MailDemonDatabaseFileInfo(dbProvider, dir, Path.GetFileName(file));
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                foreach (string file in Directory.EnumerateFiles(dir, "*.cshtml"))
                {
                    yield return new MailDemonDatabaseFileInfo(dbProvider, dir, Path.GetFileName(file));
                }
            }
        }

        private readonly IMailDemonDatabaseProvider dbProvider;
        private readonly IMemoryCache memoryCache;
        private readonly string rootPath;

        public MailDemonDatabaseFileProvider(IMailDemonDatabaseProvider dbProvider,
            IMemoryCache memoryCache, string rootPath)
        {
            this.dbProvider = dbProvider;
            this.memoryCache = memoryCache;
            this.rootPath = rootPath;
        }

        public IDirectoryContents GetDirectoryContents(string subPath)
        {
            return new PhysicalDirectoryContents(dbProvider, rootPath);
        }

        public IFileInfo GetFileInfo(string subPath)
        {
            var result = new MailDemonDatabaseFileInfo(dbProvider, rootPath, subPath);
            return result.Exists ? result as IFileInfo : new NotFoundFileInfo(subPath);
        }

        public IChangeToken Watch(string filter)
        {
            filter = filter.Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (File.Exists(filter))
            {
                return new MailDemonFileChangeToken(filter);
            }
            return new MailDemonDatabaseChangeToken(dbProvider, memoryCache, filter);
        }
    }
}
