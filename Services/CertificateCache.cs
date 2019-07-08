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
    public interface ICertificateCache
    {
        Task<X509Certificate2> GetOrCreateCertificateAsync();
        void ReleaseCertificate(X509Certificate2 cert);
    }

    public class CertificateCache : ICertificateCache
    {
        private static readonly TimeSpan interval = TimeSpan.FromDays(7.0); // reload cert every 7 days

        private readonly string sslCertificateFile;
        private readonly string sslCertificatePrivateKeyFile;
        private readonly SecureString sslCertificatePassword;
        private readonly Timer timer;

        private List<X509Certificate2> certCache = new List<X509Certificate2>();

        private void TimerCallback(object state)
        {
            lock (certCache)
            {
                foreach (X509Certificate2 cert in certCache)
                {
                    cert.Dispose();
                }
                certCache.Clear();
            }
        }

        private void TestSslCertificate()
        {
            MailDemonLog.Info("Testing ssl certificate file {0}, private key file {1}", sslCertificateFile, sslCertificatePrivateKeyFile);
            X509Certificate2 sslCert = GetOrCreateCertificateAsync().Sync();
            if (sslCert == null)
            {
                MailDemonLog.Error("SSL certificate failed to load or is not setup in config!");
            }
            else
            {
                ReleaseCertificate(sslCert);
                MailDemonLog.Info("SSL certificate loaded succesfully!");
                HasCertificate = true;
            }
        }

        public CertificateCache(IConfiguration config)
        {
            IConfigurationSection rootSection = config.GetSection("mailDemon");
            sslCertificateFile = rootSection["sslCertificateFile"];
            sslCertificatePrivateKeyFile = rootSection["sslCertificatePrivateKeyFile"];
            if (!string.IsNullOrWhiteSpace(sslCertificateFile))
            {
                sslCertificatePassword = (rootSection["sslCertificatePassword"] ?? string.Empty).ToSecureString();
            }
            Instance = this;
            TestSslCertificate();
            timer = new Timer(TimerCallback, null, interval, interval);
        }

        public Task<X509Certificate2> GetOrCreateCertificateAsync()
        {
            if (certCache.Count == 0)
            {
                return MailDemonExtensionMethods.LoadSslCertificate(sslCertificateFile, sslCertificatePrivateKeyFile, sslCertificatePassword);
            }
            lock (certCache)
            {
                int index = certCache.Count - 1;
                X509Certificate2 cert = certCache[index];
                certCache.RemoveAt(index);
                return Task.FromResult(cert);
            }
        }

        public void ReleaseCertificate(X509Certificate2 cert)
        {
            if (cert == null)
            {
                return;
            }

            lock (certCache)
            {
                if (!certCache.Contains(cert))
                {
                    certCache.Add(cert);
                }
            }
        }

        public bool HasCertificate { get; private set; }

        public static CertificateCache Instance { get; private set; }
    }
}
