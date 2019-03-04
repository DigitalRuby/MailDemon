#region Imports

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

using DnsClient;

using MailKit;
using MailKit.Net;
using MailKit.Net.Smtp;

using MimeKit;
using MimeKit.Utils;

using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using MimeKit.Cryptography;

#endregion Imports

namespace MailDemon
{
    public partial class MailDemonService : IDisposable
    {
        private class TcpListenerActive : TcpListener, IDisposable
        {
            public TcpListenerActive(IPEndPoint localEP) : base(localEP) { }
            public TcpListenerActive(IPAddress localaddr, int port) : base(localaddr, port) { }
            public void Dispose() { Stop(); }
            public new bool Active => base.Active;
        }

        private class CacheEntry
        {
            public int Count;
        }

        private TcpListenerActive server;
        private CancellationToken cancelToken;

        private readonly int streamTimeoutMilliseconds = 5000;
        private readonly int maxMessageSize = 16777216;
        private readonly int maxLineSize = 1024;
        private readonly List<MailDemonUser> users = new List<MailDemonUser>();
        private readonly MemoryCache cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = (1024 * 1024 * 16), CompactionPercentage = 0.9 });
        private readonly int maxConnectionCount = 128;
        private readonly MailboxAddress globalForwardAddress;
        private readonly int maxFailuresPerIPAddress = 3;
        private readonly TimeSpan failureLockoutTimespan = TimeSpan.FromDays(1.0);
        private readonly IPAddress ip;
        private readonly int port = 25;
        private readonly string greeting = "ESMTP & MailDemon &";
        private readonly bool requireEhloIpHostMatch;
        private readonly bool requireSpfMatch = true;
        private readonly DkimSigner dkimSigner;

        public string Domain { get; private set; }
        public IReadOnlyList<MailDemonUser> Users { get { return users; } }

        public MailDemonService(string[] args, IConfiguration configuration)
        {
            IConfigurationSection rootSection = configuration.GetSection("mailDemon");
            Domain = (rootSection["domain"] ?? Domain);
            ip = (string.IsNullOrWhiteSpace(rootSection["ip"]) ? IPAddress.Any : IPAddress.Parse(rootSection["ip"]));
            port = rootSection.GetValue("port", port);
            maxFailuresPerIPAddress = rootSection.GetValue("maxFailuresPerIPAddress", maxFailuresPerIPAddress);
            maxConnectionCount = rootSection.GetValue("maxConnectionCount", maxConnectionCount);
            maxMessageSize = rootSection.GetValue("maxMessageSize", maxMessageSize);
            globalForwardAddress = rootSection.GetValue("globalForwardAddress", globalForwardAddress);
            greeting = (rootSection["greeting"] ?? greeting).Replace("\r", string.Empty).Replace("\n", string.Empty);
            if (TimeSpan.TryParse(rootSection["failureLockoutTimespan"], out TimeSpan _failureLockoutTimespan))
            {
                failureLockoutTimespan = _failureLockoutTimespan;
            }
            failureLockoutTimespan = _failureLockoutTimespan;
            IConfigurationSection userSection = rootSection.GetSection("users");
            foreach (var child in userSection.GetChildren())
            {
                MailDemonUser user = new MailDemonUser(child["name"], child["displayName"], child["password"], child["address"], child["forwardAddress"], true);
                users.Add(user);
                MailDemonLog.Debug("Loaded user {0}", user);
            }
            requireEhloIpHostMatch = rootSection.GetValue<bool>("requireEhloIpHostMatch", requireEhloIpHostMatch);
            requireSpfMatch = rootSection.GetValue<bool>("requireSpfMatch", requireSpfMatch);
            string dkimFile = rootSection.GetValue<string>("dkimPemFile", null);
            string dkimSelector = rootSection.GetValue<string>("dkimSelector", null);
            if (File.Exists(dkimFile) && !string.IsNullOrWhiteSpace(dkimSelector))
            {
                try
                {
                    using (StringReader stringReader = new StringReader(File.ReadAllText(dkimFile)))
                    {
                        PemReader pemReader = new PemReader(stringReader);
                        object pemObject = pemReader.ReadObject();
                        AsymmetricKeyParameter privateKey = ((AsymmetricCipherKeyPair)pemObject).Private;
                        dkimSigner = new DkimSigner(privateKey, Domain, dkimSelector);
                        MailDemonLog.Info("Loaded dkim file at {0}", dkimFile);
                    }
                }
                catch (Exception ex)
                {
                    MailDemonLog.Error(ex);
                }
            }
            sslCertificateFile = rootSection["sslCertificateFile"];
            sslCertificatePrivateKeyFile = rootSection["sslCertificatePrivateKeyFile"];
            if (!string.IsNullOrWhiteSpace(sslCertificateFile))
            {
                sslCertificatePassword = (rootSection["sslCertificatePassword"] ?? string.Empty).ToSecureString();
            }
            IConfigurationSection ignoreRegexSection = rootSection.GetSection("ignoreCertificateErrorsRegex");
            if (ignoreRegexSection != null)
            {
                foreach (var child in ignoreRegexSection.GetChildren())
                {
                    Regex re = new Regex(child["regex"].ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
                    foreach (var domain in child.GetSection("domains").GetChildren())
                    {
                        ignoreCertificateErrorsRegex[domain.Value] = re;
                    }
                }
            }
        }

        public async Task StartAsync(CancellationToken cancelToken)
        {
            Dispose();
            this.cancelToken = cancelToken;
            TestSslCertificate();
            server = new TcpListenerActive(IPAddress.Any, port);
            server.Start(maxConnectionCount);
            cancelToken.Register(Dispose);
            while (server.Active)
            {
                try
                {
                    await ProcessConnection();
                }
                catch (Exception ex)
                {
                    MailDemonLog.Error(ex);
                }
            }
        }

        public void Dispose()
        {
            try
            {
                server?.Dispose();
            }
            catch
            {
            }
            server = null;
        }

        private bool CheckBlocked(string ipAddress)
        {
            string key = "RateLimit_" + ipAddress;
            return (cache.TryGetValue(key, out CacheEntry count) && count.Count >= maxFailuresPerIPAddress);
        }

        private void IncrementFailure(string ipAddress, string userName)
        {
            string key = "RateLimit_" + ipAddress;
            CacheEntry entry = cache.GetOrCreate(key, (i) =>
            {
                i.AbsoluteExpirationRelativeToNow = failureLockoutTimespan;
                i.Size = (key.Length * 2) + 16; // 12 bytes for C# object plus 4 bytes int
                return new CacheEntry();
            });
            Interlocked.Increment(ref entry.Count);
            if (userName != null && Directory.Exists(@"/var/log"))
            {
                // write auth log failure for ipban integration
                try
                {
                    File.AppendAllText("/var/log/ipbancustom_maildemon.log", $"{DateTime.UtcNow.ToString("u")}, ipban failed login, ip address: {ipAddress}, source: SMTP, user: {userName}");
                }
                catch (Exception ex)
                {
                    MailDemonLog.Error("Failed to write ipban auth log", ex);
                }
            }
        }
    }
}
