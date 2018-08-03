#region Imports

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
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

using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

#endregion Imports

namespace MailDemon
{
    public class MailDemonService : IDisposable
    {
        private class TcpListenerActive : TcpListener, IDisposable
        {
            public TcpListenerActive(IPEndPoint localEP) : base(localEP) { }
            public TcpListenerActive(IPAddress localaddr, int port) : base(localaddr, port) { }
            public void Dispose() { Stop(); }
            public new bool Active => base.Active;
        }

        private class SmtpMimeMessageStream : Stream
        {
            private Stream baseStream;
            private int state; // 0 = none, 1 = has \r, 2 = has \n, 3 = has ., 4 = has \r 5 = has \n done!

            public SmtpMimeMessageStream(Stream baseStream)
            {
                this.baseStream = baseStream;
            }

            public override bool CanRead => baseStream.CanRead;

            public override bool CanSeek => baseStream.CanSeek;

            public override bool CanWrite => baseStream.CanWrite;

            public override long Length => baseStream.Length;

            public override long Position { get => baseStream.Position; set => baseStream.Position = value; }

            public override void Flush()
            {
                baseStream.Flush();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (state == 5)
                {
                    state = 0;
                    return 0;
                }

                int read = baseStream.Read(buffer, offset, count);
                if (read > 0)
                {
                    int end = offset + read;
                    for (int i = offset; i < end; i++)
                    {
                        switch (state)
                        {
                            case 0:
                                if (buffer[i] == '\r')
                                {
                                    state = 1;
                                }
                                else
                                {
                                    state = 0;
                                }
                                break;

                            case 1:
                                if (buffer[i] == '\n')
                                {
                                    state = 2;
                                }
                                else
                                {
                                    state = 0;
                                }
                                break;

                            case 2:
                                if (buffer[i] == '.')
                                {
                                    state = 3;
                                }
                                else if (buffer[i] == '\r')
                                {
                                    state = 1;
                                }
                                else
                                {
                                    state = 0;
                                }
                                break;

                            case 3:
                                if (buffer[i] == '\r')
                                {
                                    state = 4;
                                }
                                else
                                {
                                    state = 0;
                                }
                                break;

                            case 4:
                                if (buffer[i] == '\n')
                                {
                                    state = 5;

                                    // don't return the ending \r\n.\r\n, that is part of the protocol
                                    // there is a rare chance that a read of this terminator can split over
                                    // two Read(...) method calls, in which case the \r\n.\r\n will be
                                    // part of the message... this edge case is not handled yet
                                    return (read >= 5 ? read - 5 : read);
                                }
                                else
                                {
                                    state = 0;
                                }
                                break;
                        }
                    }
                }
                return read;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                return baseStream.Seek(offset, origin);
            }

            public override void SetLength(long value)
            {
                baseStream.SetLength(value);
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                baseStream.Write(buffer, offset, count);
            }
        }

        private class CacheEntry
        {
            public int Count;
        }

        public class MailDemonUser
        {
            public MailDemonUser(string name, string password)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new ArgumentException("Name must not be null or empty", nameof(name));
                }
                if (string.IsNullOrWhiteSpace(password))
                {
                    throw new ArgumentException("Password must not be null or empty", nameof(password));
                }
                Name = name;
                Password = new SecureString();
                foreach (char c in password)
                {
                    Password.AppendChar(c);
                }
                Plain = string.Format("\0{0}\0{1}", name, password);
            }

            public string Name { get; private set; }
            public SecureString Password { get; private set; }
            public string Plain { get; private set; }
        }

        private TcpListenerActive server;

        private readonly List<MailDemonUser> users = new List<MailDemonUser>();
        private readonly MemoryCache cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = (1024 * 1024 * 16), CompactionPercentage = 0.9 });
        private readonly Dictionary<string, Regex> ignoreCertificateErrorsRegex = new Dictionary<string, Regex>(StringComparer.OrdinalIgnoreCase); // domain,regex
        private readonly int maxConnectionCount = 128;
        private readonly int maxFailuresPerIPAddress = 3;
        private readonly TimeSpan failureLockoutTimespan = TimeSpan.FromDays(1.0);
        private readonly IPAddress ip;
        private readonly int port = 25;
        private readonly string greeting = "ESMTP & MailDemon &";
        private readonly string sslCertificateFile;
        private readonly string sslCertificatePrivateKeyFile;
        private readonly SecureString sslCertificatePassword;
        private readonly Timer sslCertificateTimer;

        private X509Certificate2 sslCertificate;

        public string Domain { get; private set; }
        public IReadOnlyList<MailDemonUser> Users { get { return users; } }

        public MailDemonService(string[] args, IConfiguration configuration)
        {
            IConfigurationSection rootSection = configuration.GetSection("mailDemon");
            Domain = (rootSection["domain"] ?? Domain);
            ip = (string.IsNullOrWhiteSpace(rootSection["ip"]) ? IPAddress.Any : IPAddress.Parse(rootSection["ip"]));
            if (int.TryParse(rootSection["port"].ToString(CultureInfo.InvariantCulture), out int _port))
            {
                port = _port;
            }
            if (int.TryParse(rootSection["maxFailuresPerIPAddress"].ToString(CultureInfo.InvariantCulture), out int _maxFailuresPerIPAddress))
            {
                maxFailuresPerIPAddress = _maxFailuresPerIPAddress;
            }
            if (int.TryParse(rootSection["maxConnectionCount"].ToString(CultureInfo.InvariantCulture), out int _maxConnectionCount))
            {
                maxConnectionCount = _maxConnectionCount;
            }
            greeting = (rootSection["greeting"] ?? greeting).Replace("\r", string.Empty).Replace("\n", string.Empty);
            if (TimeSpan.TryParse(rootSection["failureLockoutTimespan"], out TimeSpan _failureLockoutTimespan))
            {
                failureLockoutTimespan = _failureLockoutTimespan;
            }
            failureLockoutTimespan = _failureLockoutTimespan;
            IConfigurationSection userSection = rootSection.GetSection("users");
            foreach (var child in userSection.GetChildren())
            {
                users.Add(new MailDemonUser(child["name"], child["password"]));
            }
            sslCertificateFile = rootSection["sslCertificateFile"];
            sslCertificatePrivateKeyFile = rootSection["sslCertificatePrivateKeyFile"];
            if (!string.IsNullOrWhiteSpace(sslCertificateFile))
            {
                sslCertificatePassword = new SecureString();
                foreach (char c in rootSection["sslCertificatePassword"] ?? string.Empty)
                {
                    sslCertificatePassword.AppendChar(c);
                }
                sslCertificateTimer = new Timer(SslCertificateTimerCallback, null, TimeSpan.FromDays(1.0), TimeSpan.FromDays(1.0));
                LoadSslCertificate();
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

        public async Task StartAsync(CancellationToken token)
        {
            Dispose();
            server = new TcpListenerActive(IPAddress.Any, port);
            server.Start(maxConnectionCount);
            token.Register(Dispose);
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

        private async Task ProcessConnection()
        {
            using (TcpClient client = await server.AcceptTcpClientAsync())
            {
                string ipAddress = (client.Client.RemoteEndPoint as IPEndPoint).Address.ToString();
                MailDemonUser authenticatedUser = null;
                client.ReceiveTimeout = 5000;
                client.SendTimeout = 5000;

                try
                {
                    MailDemonLog.Write(LogLevel.Info, "Connection from {0}", ipAddress);

                    // immediately drop if client is blocked
                    if (CheckBlocked(ipAddress))
                    {
                        client.Close();
                        MailDemonLog.Write(LogLevel.Warn, "Blocking {0}", ipAddress);
                        return;
                    }

                    using (NetworkStream stream = client.GetStream())
                    {
                        // create comm streams
                        SslStream sslStream = null;
                        StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                        StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" };
                        if (port == 465 || port == 587)
                        {
                            Tuple<SslStream, StreamReader, StreamWriter> tls = await StartTls(reader, writer, false);
                            if (tls == null)
                            {
                                throw new IOException("Failed to start TLS, ssl certificate failed to load");
                            }
                            sslStream = tls.Item1;
                            reader = tls.Item2;
                            writer = tls.Item3;
                        }

                        MailDemonLog.Write(LogLevel.Info, "Connection accepted from {0}", ipAddress);

                        // send greeting
                        await writer.WriteLineAsync($"220 {Domain} {greeting}");

                        while (true)
                        {
                            // read initial client string
                            string line = await ReadLineAsync(reader);
                            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("QUIT"))
                            {
                                // empty line or QUIT terminates session
                                break;
                            }
                            else if (line.StartsWith("RSET"))
                            {
                                await writer.WriteLineAsync($"250 2.0.0 Resetting");
                                authenticatedUser = null;
                            }
                            else if (line.StartsWith("EHLO"))
                            {
                                await HandleEhlo(writer, sslStream);
                            }
                            else if (line.StartsWith("HELO"))
                            {
                                await writer.WriteLineAsync($"220 {Domain} Hello {line.Substring(4).Trim()}");
                            }
                            else if (line.StartsWith("AUTH PLAIN"))
                            {
                                authenticatedUser = await Authenticate(reader, writer, line);
                            }
                            else if (line.StartsWith("STARTTLS"))
                            {
                                if (sslStream != null)
                                {
                                    await writer.WriteLineAsync("503 TLS already initiated");
                                }
                                else
                                {
                                    Tuple<SslStream, StreamReader, StreamWriter> tls = await StartTls(reader, writer, true);
                                    if (tls == null)
                                    {
                                        await writer.WriteLineAsync("503 Failed to start TLS");
                                    }
                                    else
                                    {
                                        sslStream = tls.Item1;
                                        reader = tls.Item2;
                                        writer = tls.Item3;
                                    }
                                }
                            }

                            // if authenticated, only valid line is MAIL FROM
                            // TODO: consider changing this
                            else if (authenticatedUser != null)
                            {
                                if (line.StartsWith("MAIL FROM:<"))
                                {
                                    await SendMail(authenticatedUser, reader, writer, line);
                                }
                                else
                                {
                                    MailDemonLog.Write(LogLevel.Warn, "Ignoring client command: " + line);
                                }
                            }
                            else
                            {
                                throw new InvalidOperationException("Invalid message: " + line + ", not authenticated");
                            }
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    IncrementFailure(ipAddress);
                    throw;
                }
                finally
                {
                    MailDemonLog.Write(LogLevel.Info, "{0} disconnected", ipAddress);
                }
            }
        }

        private async Task<string> ReadLineAsync(StreamReader reader)
        {
            string line = await reader.ReadLineAsync();
            MailDemonLog.Write(LogLevel.Debug, "CLIENT: " + line);
            return line;
        }

        private async Task HandleEhlo(StreamWriter writer, SslStream sslStream)
        {
            await writer.WriteLineAsync($"250-SIZE 65536");
            await writer.WriteLineAsync($"250-8BITMIME");
            await writer.WriteLineAsync($"250-AUTH PLAIN");
            await writer.WriteLineAsync($"250-PIPELINING");
            await writer.WriteLineAsync($"250-ENHANCEDSTATUSCODES");
            //await writer.WriteLineAsync($"250-BINARYMIME");
            //await writer.WriteLineAsync($"250-CHUNKING");
            if (sslCertificate != null && sslStream == null && port != 465 && port != 587)
            {
                await writer.WriteLineAsync($"250-STARTTLS");
            }
            await writer.WriteLineAsync($"250 SMTPUTF8");
        }

        private async Task<MailDemonUser> Authenticate(StreamReader reader, StreamWriter writer, string line)
        {
            MailDemonUser foundUser = null;
            if (line == "AUTH PLAIN")
            {
                await writer.WriteLineAsync($"334");
                line = await ReadLineAsync(reader) ?? string.Empty;
            }
            else
            {
                line = line.Substring(11);
            }
            string sentAuth = Encoding.UTF8.GetString(Convert.FromBase64String(line));
            foreach (MailDemonUser user in users)
            {
                if (user.Plain == sentAuth)
                {
                    foundUser = user;
                    break;
                }
            }
            if (foundUser != null)
            {
                MailDemonLog.Write(LogLevel.Info, "User {0} authenticated", foundUser.Name);
                await writer.WriteLineAsync($"235 2.7.0 Accepted");
                return foundUser;
            }

            // fail
            MailDemonLog.Write(LogLevel.Warn, "Authentication failed: {0}", sentAuth);
            await writer.WriteLineAsync($"535 authentication failed");
            throw new InvalidOperationException("Authentication failed");
        }

        private async Task<Tuple<SslStream, StreamReader, StreamWriter>> StartTls(StreamReader reader, StreamWriter writer, bool sendReadyCommand)
        {
            if (sslCertificate == null)
            {
                await writer.WriteLineAsync("501 Syntax error (no parameters allowed)");
                return null;
            }
            else if (sendReadyCommand)
            {
                // upgrade to ssl
                await writer.WriteLineAsync($"220 Ready to start TLS");
            }

            // create ssl stream and ensure encryption is required
            SslStream sslStream = new SslStream(reader.BaseStream, false, null, null, EncryptionPolicy.RequireEncryption);

            try
            {
                // this can hang if the client does not authenticate ssl properly, so we kill it after 5 seconds
                if (!sslStream.AuthenticateAsServerAsync(sslCertificate, false, System.Security.Authentication.SslProtocols.Tls12, true).Wait(5000))
                {
                    // forces the authenticate as server to fail and throw exception
                    sslStream.Dispose();
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Unable to negotiate ssl: " + ex.Message, ex);
            }

            // create comm streams on top of ssl stream
            StreamReader sslReader = new StreamReader(sslStream, Encoding.UTF8);
            StreamWriter sslWriter = new StreamWriter(sslStream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" };

            return new Tuple<SslStream, StreamReader, StreamWriter>(sslStream, sslReader, sslWriter);
        }

        private async Task SendMail(MailDemonUser foundUser, StreamReader reader, StreamWriter writer, string line)
        {
            string fromAddress = line.Substring(11).Trim('>');
            if (!MailboxAddress.TryParse(fromAddress, out _))
            {
                throw new ArgumentException("Invalid from address: " + fromAddress);
            }

            // denote success
            await writer.WriteLineAsync($"250 2.1.0 OK");

            // read to addresses
            line = await ReadLineAsync(reader);
            Dictionary<string, List<string>> addressGroups = new Dictionary<string, List<string>>();
            while (line.StartsWith("RCPT TO:<"))
            {
                string toAddress = line.Substring(9).Trim('>');

                if (!MailboxAddress.TryParse(toAddress, out _))
                {
                    throw new ArgumentException("Invalid to address: " + toAddress);
                }

                // group addresses by domain
                int pos = toAddress.LastIndexOf('@');
                if (pos > 0)
                {
                    string addressDomain = toAddress.Substring(++pos);
                    if (!addressGroups.TryGetValue(addressDomain, out List<string> addressList))
                    {
                        addressGroups[addressDomain] = addressList = new List<string>();
                    }
                    addressList.Add(toAddress);
                }

                // denote success
                await writer.WriteLineAsync($"250 2.1.0 OK");
                line = await ReadLineAsync(reader);
            }

            // if no to addresses, fail
            if (addressGroups.Count == 0)
            {
                throw new InvalidOperationException("Invalid message: " + line);
            }

            if (line.StartsWith("DATA"))
            {
                await writer.WriteLineAsync($"354");
                SmtpMimeMessageStream mimeStream = new SmtpMimeMessageStream(reader.BaseStream);
                MimeMessage mimeMessage = await MimeMessage.LoadAsync(mimeStream, true);
                await writer.WriteLineAsync($"250 2.0.0 OK");

                // send all emails in one shot for each domain in order to batch
                foreach (var group in addressGroups)
                {
                    await SendMessage(mimeMessage, foundUser.Name + "@" + Domain, group.Key, group.Value);
                }
            }
            else if (line.Length > 0)
            {
                throw new InvalidOperationException("Invalid message: " + line);
            }
        }

        private bool CheckBlocked(string ipAddress)
        {
            string key = "RateLimit_" + ipAddress;
            return (cache.TryGetValue(key, out CacheEntry count) && count.Count >= maxFailuresPerIPAddress);
        }

        private void IncrementFailure(string ipAddress)
        {
            string key = "RateLimit_" + ipAddress;
            CacheEntry entry = cache.GetOrCreate(key, (i) =>
            {
                i.AbsoluteExpirationRelativeToNow = failureLockoutTimespan;
                i.Size = (key.Length * 2) + 16; // 12 bytes for C# object plus 4 bytes int
                return new CacheEntry();
            });
            Interlocked.Increment(ref entry.Count);
        }

        private void SslCertificateTimerCallback(object state)
        {
            LoadSslCertificate();
        }

        private RSACryptoServiceProvider GetRSAProviderForPrivateKey(string pemPrivateKey)
        {
            RSACryptoServiceProvider rsaKey = new RSACryptoServiceProvider();
            PemReader reader = new PemReader(new StringReader(pemPrivateKey));
            RsaPrivateCrtKeyParameters rkp = reader.ReadObject() as RsaPrivateCrtKeyParameters;
            RSAParameters rsaParameters = DotNetUtilities.ToRSAParameters(rkp);
            rsaKey.ImportParameters(rsaParameters);
            return rsaKey;
        }

        private void LoadSslCertificate()
        {
            try
            {
                sslCertificate = new X509Certificate2(File.ReadAllBytes(sslCertificateFile), sslCertificatePassword);
                if (!sslCertificate.HasPrivateKey && !string.IsNullOrWhiteSpace(sslCertificatePrivateKeyFile))
                {
                    sslCertificate = sslCertificate.CopyWithPrivateKey(GetRSAProviderForPrivateKey(File.ReadAllText(sslCertificatePrivateKeyFile)));
                }
                MailDemonLog.Write(LogLevel.Warn, "Loaded ssl certificate {0}", sslCertificate);
            }
            catch (Exception ex)
            {
                MailDemonLog.Write(LogLevel.Error, "Error loading ssl certificate: {0}", ex);
            }
        }

        private async Task SendMessage(MimeMessage msg, string from, string domain, IEnumerable<string> addresses)
        {
            MailDemonLog.Write(LogLevel.Info, "Sending from {0}", from);
            using (SmtpClient client = new SmtpClient()
            {
                ServerCertificateValidationCallback = (object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) =>
                {
                    return (sslPolicyErrors == SslPolicyErrors.None ||
                        (ignoreCertificateErrorsRegex.TryGetValue(domain, out Regex re) && re.IsMatch(certificate.Subject)));
                }
            })
            {
                IPHostEntry ip = null;
                bool sent = false;
                LookupClient lookup = new LookupClient();
                IDnsQueryResponse result = await lookup.QueryAsync(domain, QueryType.MX);
                foreach (DnsClient.Protocol.MxRecord record in result.AllRecords)
                {
                    // attempt to send, if fail, try next address
                    try
                    {
                        ip = await Dns.GetHostEntryAsync(record.Exchange);
                        foreach (IPAddress ipAddress in ip.AddressList)
                        {
                            string host = ip.HostName;
                            try
                            {
                                await client.ConnectAsync(host, options: MailKit.Security.SecureSocketOptions.StartTlsWhenAvailable);
                                await client.SendAsync(msg);
                                sent = true;
                                break;
                            }
                            catch (Exception ex)
                            {
                                MailDemonLog.Error(ex);
                            }
                            finally
                            {
                                await client.DisconnectAsync(true);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MailDemonLog.Error(ex);
                    }

                    if (sent)
                    {
                        break;
                    }
                }
            }
        }
    }
}
