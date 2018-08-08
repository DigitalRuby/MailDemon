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
using System.Runtime.InteropServices;

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

        private class CacheEntry
        {
            public int Count;
        }

        private TcpListenerActive server;

        private readonly List<MailDemonUser> users = new List<MailDemonUser>();
        private readonly MemoryCache cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = (1024 * 1024 * 16), CompactionPercentage = 0.9 });
        private readonly Dictionary<string, Regex> ignoreCertificateErrorsRegex = new Dictionary<string, Regex>(StringComparer.OrdinalIgnoreCase); // domain,regex
        private readonly int maxConnectionCount = 128;
        private readonly string globalForwardAddress;
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
            port = GetConfig(rootSection, "port", port);
            maxFailuresPerIPAddress = GetConfig(rootSection, "maxFailuresPerIPAddress", maxFailuresPerIPAddress);
            maxConnectionCount = GetConfig(rootSection, "maxConnectionCount", maxConnectionCount);
            globalForwardAddress = GetConfig(rootSection, "globalForwardAddress", globalForwardAddress);
            greeting = (rootSection["greeting"] ?? greeting).Replace("\r", string.Empty).Replace("\n", string.Empty);
            if (TimeSpan.TryParse(rootSection["failureLockoutTimespan"], out TimeSpan _failureLockoutTimespan))
            {
                failureLockoutTimespan = _failureLockoutTimespan;
            }
            failureLockoutTimespan = _failureLockoutTimespan;
            IConfigurationSection userSection = rootSection.GetSection("users");
            foreach (var child in userSection.GetChildren())
            {
                users.Add(new MailDemonUser(child["name"], child["password"], child["address"], child["forwardAddress"]));
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

        private static string ToUnsecureString(SecureString securePassword)
        {
            IntPtr unmanagedString = IntPtr.Zero;
            try
            {
                unmanagedString = Marshal.SecureStringToGlobalAllocUnicode(securePassword);
                return Marshal.PtrToStringUni(unmanagedString);
            }
            finally
            {
                if (unmanagedString != IntPtr.Zero)
                {
                    Marshal.ZeroFreeGlobalAllocUnicode(unmanagedString);
                }
            }
        }

        private T GetConfig<T>(IConfiguration config, string key, T defaultValue)
        {
            string value = config["key"];
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }
            return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
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
                            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                            {
                                // empty line or QUIT terminates session
                                break;
                            }
                            else if (line.StartsWith("RSET", StringComparison.OrdinalIgnoreCase))
                            {
                                await writer.WriteLineAsync($"250 2.0.0 Resetting");
                                authenticatedUser = null;
                            }
                            else if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase))
                            {
                                await HandleEhlo(writer, sslStream);
                            }
                            else if (line.StartsWith("HELO", StringComparison.OrdinalIgnoreCase))
                            {
                                await writer.WriteLineAsync($"220 {Domain} Hello {line.Substring(4).Trim()}");
                            }
                            else if (line.StartsWith("AUTH PLAIN", StringComparison.OrdinalIgnoreCase))
                            {
                                authenticatedUser = await Authenticate(reader, writer, line);
                            }
                            else if (line.StartsWith("STARTTLS", StringComparison.OrdinalIgnoreCase))
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
                                if (line.StartsWith("MAIL FROM:<", StringComparison.OrdinalIgnoreCase))
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
                                if (line.StartsWith("MAIL FROM:<", StringComparison.OrdinalIgnoreCase))
                                {
                                    // non-authenticated user, forward message on if possible, check settings
                                    await ReceiveMail(reader, writer, line);
                                }
                                else
                                {
                                    throw new InvalidOperationException("Invalid message: " + line + ", not authenticated");
                                }
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
                if (ToUnsecureString(user.PasswordPlain) == sentAuth)
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

        private async Task<MailFromResult> ParseMailFrom(MailDemonUser fromUser, StreamReader reader, StreamWriter writer, string line)
        {
            string fromAddress = line.Substring(11);
            int pos = fromAddress.IndexOf('>');
            if (pos >= 0)
            {
                fromAddress = fromAddress.Substring(0, pos);
            }
            if (!MailboxAddress.TryParse(fromAddress, out _))
            {
                await writer.WriteLineAsync($"500 invalid command - bad from address format");
                await writer.FlushAsync();
                throw new ArgumentException("Invalid format for from address: " + fromAddress);
            }

            if (fromUser != null && fromUser.Address != fromAddress)
            {
                await writer.WriteLineAsync($"500 invalid command");
                await writer.FlushAsync();
                throw new InvalidOperationException("Invalid from address - bad from address");
            }

            // denote success
            await writer.WriteLineAsync($"250 2.1.0 OK");

            // read to addresses
            line = await ReadLineAsync(reader);
            Dictionary<string, List<string>> toAddressesByDomain = new Dictionary<string, List<string>>();
            while (line.StartsWith("RCPT TO:<", StringComparison.OrdinalIgnoreCase))
            {
                string toAddress = line.Substring(9).Trim('>');

                if (!MailboxAddress.TryParse(toAddress, out _))
                {
                    await writer.WriteLineAsync($"500 invalid command - bad to address format");
                    await writer.FlushAsync();
                    throw new ArgumentException("Invalid to address: " + toAddress);
                }

                // if no authenticated user, the to address must match an existing user address
                else if (fromUser == null && users.FirstOrDefault(u => u.Address == toAddress) == null)
                {
                    await writer.WriteLineAsync($"500 invalid command - bad to address");
                    await writer.FlushAsync();
                    throw new InvalidOperationException("Invalid to address: " + toAddress);
                }
                // else user is authenticated, can send email to anyone

                // group addresses by domain
                pos = toAddress.LastIndexOf('@');
                if (pos > 0)
                {
                    string addressDomain = toAddress.Substring(++pos);
                    if (!toAddressesByDomain.TryGetValue(addressDomain, out List<string> addressList))
                    {
                        toAddressesByDomain[addressDomain] = addressList = new List<string>();
                    }
                    addressList.Add(toAddress);
                }

                // denote success
                await writer.WriteLineAsync($"250 2.1.0 OK");
                line = await ReadLineAsync(reader);
            }

            // if no to addresses, fail
            if (toAddressesByDomain.Count == 0)
            {
                await writer.WriteLineAsync($"500 invalid command");
                await writer.FlushAsync();
                throw new InvalidOperationException("Invalid message: " + line);
            }

            if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync($"354");
                SmtpMimeMessageStream mimeStream = new SmtpMimeMessageStream(reader.BaseStream);
                MimeMessage mimeMessage = await MimeMessage.LoadAsync(mimeStream, true);
                await writer.WriteLineAsync($"250 2.0.0 OK");

                return new MailFromResult
                {
                    From = fromAddress,
                    ToAddresses = toAddressesByDomain,
                    Message = mimeMessage
                };
            }
            else
            {
                await writer.WriteLineAsync($"500 invalid command");
                await writer.FlushAsync();
                throw new InvalidOperationException("Invalid line in mail from: " + line);
            }
        }

        private async Task SendMail(MailDemonUser foundUser, StreamReader reader, StreamWriter writer, string line)
        {
            MailFromResult result = await ParseMailFrom(foundUser, reader, writer, line);
            await SendMail(result);
            await writer.WriteLineAsync($"250 2.1.0 OK");
        }

        private async Task SendMail(MailFromResult result)
        {
            // send all emails in one shot for each domain in order to batch
            foreach (var group in result.ToAddresses)
            {
                await SendMessage(result.Message, result.From, group.Key);
            }
        }

        private async Task ReceiveMail(StreamReader reader, StreamWriter writer, string line)
        {
            MailFromResult result = await ParseMailFrom(null, reader, writer, line);

            // mail demon doesn't have an inbox, only forwarding, so see if any of the to addresses can be forwarded
            foreach (var kv in result.ToAddresses)
            {
                foreach (string address in kv.Value)
                {
                    MailDemonUser user = users.FirstOrDefault(u => u.Address == address);

                    // if no user or the forward address points to a user, fail
                    if (user == null || users.FirstOrDefault(u => u.Address == user.ForwardAddress) != null)
                    {
                        await writer.WriteLineAsync($"500 invalid command");
                        await writer.FlushAsync();
                    }

                    // setup forward headers
                    string forwardToAddress = (string.IsNullOrWhiteSpace(user.ForwardAddress) ? globalForwardAddress : user.ForwardAddress);
                    if (!string.IsNullOrWhiteSpace(forwardToAddress))
                    {
                        MailboxAddress forwardFrom = new MailboxAddress(user.Address);
                        MailboxAddress forwardTo = new MailboxAddress(forwardToAddress);
                        result.Message.ResentFrom.Add(forwardFrom);
                        result.Message.ResentTo.Add(forwardTo);
                        result.Message.ResentDate = DateTime.UtcNow;
                        string toDomain = user.ForwardAddress.Substring(user.ForwardAddress.IndexOf('@') + 1);

                        // make a new result to forward
                        MailFromResult newResult = new MailFromResult
                        {
                            From = result.From,
                            Message = result.Message,
                            ToAddresses = new Dictionary<string, List<string>> { { toDomain, new List<string> { user.ForwardAddress } } }
                        };

                        // forward the message on and clear the forward headers
                        MailDemonLog.Write(LogLevel.Info, "Forwarding message, from: {0}, to: {1}, forward: {2}", newResult.From, address, forwardToAddress);
                        await SendMail(newResult);
                        result.Message.ResentFrom.Remove(forwardFrom);
                        result.Message.ResentTo.Remove(forwardTo);
                    }
                }
            }

            await writer.WriteLineAsync($"250 2.1.0 OK");
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

        private async Task SendMessage(MimeMessage msg, string from, string domain)
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
