using System;
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
        private readonly IServiceProvider serviceProvider;
        private readonly string rootPath;

        public MailDemonDatabaseFileProvider(IServiceProvider serviceProvider, string rootPath)
        {
            this.serviceProvider = serviceProvider;
            this.rootPath = rootPath;
        }

        public IDirectoryContents GetDirectoryContents(string subPath)
        {
            return null;
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
