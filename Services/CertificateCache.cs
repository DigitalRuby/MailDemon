using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace MailDemon
{
    public class CertificateCache
    {
        private Dictionary<string, X509Certificate2> certCache = new Dictionary<string, X509Certificate2>();

        public CertificateCache(IConfiguration config)
        {
            Instance = this;
        }

        public async Task<X509Certificate2> LoadSslCertificateAsync(string publicKeyFile, string privateKeyFile, SecureString password)
        {
            string hash = (publicKeyFile ?? string.Empty) + "_" + (privateKeyFile ?? string.Empty);
            X509Certificate2 cert;
            lock (certCache)
            {
                certCache.TryGetValue(hash, out cert);
            }
            if (cert != null)
            {
                if (cert.NotAfter <= DateTime.Now.Add(TimeSpan.FromDays(1.0)))
                {
                    lock (certCache)
                    {
                        certCache.Remove(hash);
                        X509Certificate2 toDispose = cert;
                        Task.Run(async () =>
                        {
                            // cleanup the old cert after one hour, allowing anyone holding on to it to still use it for a while
                            await Task.Delay(TimeSpan.FromHours(1.0));
                            try
                            {
                                toDispose.Dispose();
                            }
                            catch
                            {
                            }
                        });
                        cert = null;
                    }
                }
            }
            if (cert == null)
            {
                cert = await MailDemonExtensionMethods.LoadSslCertificateAsync(publicKeyFile, privateKeyFile, password);
                lock (certCache)
                {
                    certCache[hash] = cert;
                }
            }
            return cert;
        }

        public static CertificateCache Instance { get; private set; }
    }
}
