using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace MailDemon
{
    public class MailDemonDatabaseFileProvider : IFileProvider
    {
        private readonly string rootPath;

        public MailDemonDatabaseFileProvider(string rootPath)
        {
            this.rootPath = rootPath;
        }

        public IDirectoryContents GetDirectoryContents(string subPath)
        {
            return null;
        }

        public IFileInfo GetFileInfo(string subPath)
        {
            var result = new MailDemonDatabaseFileInfo(rootPath, subPath);
            return result.Exists ? result as IFileInfo : new NotFoundFileInfo(subPath);
        }

        public IChangeToken Watch(string filter)
        {
            return new MailDemonDatabaseChangeToken(filter);
        }
    }
}
