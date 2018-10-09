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
using MimeKit.Utils;

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

        private readonly Encoding utf8Encoding = new UTF8Encoding(false);
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
                MailDemonUser user = new MailDemonUser(child["name"], child["displayName"], child["password"], child["address"], child["forwardAddress"]);
                users.Add(user);
                MailDemonLog.Write(LogLevel.Debug, "Loaded user {0}", user);
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
            TestSslCertificate();
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

        private void TestSslCertificate()
        {
            MailDemonLog.Write(LogLevel.Info, "Testing ssl certificate file {0}, private key file {1}", sslCertificateFile, sslCertificatePrivateKeyFile);
            X509Certificate sslCert = LoadSslCertificate();
            if (sslCert == null)
            {
                MailDemonLog.Error("SSL certificate failed to load or is not setup in config!");
            }
            else
            {
                MailDemonLog.Write(LogLevel.Info, "SSL certificate loaded succesfully!");
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

        private async Task HandleClientConnectionAsync(TcpClient tcpClient)
        {
            string ipAddress = (tcpClient.Client.RemoteEndPoint as IPEndPoint).Address.ToString();
            MailDemonUser authenticatedUser = null;
            X509Certificate2 sslCert = null;
            try
            {
                tcpClient.ReceiveTimeout = 5000;
                tcpClient.SendTimeout = 5000;

                MailDemonLog.Write(LogLevel.Info, "Connection from {0}", ipAddress);

                // immediately drop if client is blocked
                if (CheckBlocked(ipAddress))
                {
                    tcpClient.Close();
                    MailDemonLog.Write(LogLevel.Warn, "Blocking {0}", ipAddress);
                    return;
                }

                using (NetworkStream clientStream = tcpClient.GetStream())
                {
                    // create comm streams
                    SslStream sslStream = null;
                    StreamReader reader = new StreamReader(clientStream, Encoding.UTF8);
                    StreamWriter writer = new StreamWriter(clientStream, utf8Encoding) { AutoFlush = true, NewLine = "\r\n" };

                    if (port == 465 || port == 587)
                    {
                        sslCert = (sslCert ?? LoadSslCertificate());
                        Tuple<SslStream, StreamReader, StreamWriter> tls = await StartTls(tcpClient, ipAddress, reader, writer, false, sslCert);
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
                            await HandleEhlo(writer, sslStream, sslCert);
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
                                sslCert = (sslCert ?? LoadSslCertificate());
                                Tuple<SslStream, StreamReader, StreamWriter> tls = await StartTls(tcpClient, ipAddress, reader, writer, true, sslCert);
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
            catch (Exception ex)
            {
                IncrementFailure(ipAddress);
                MailDemonLog.Error(ex);
            }
            finally
            {
                sslCert?.Dispose();
                MailDemonLog.Write(LogLevel.Info, "{0} disconnected", ipAddress);
            }
        }

        private async Task ProcessConnection()
        {
            using (TcpClient tcpClient = await server.AcceptTcpClientAsync())
            {
                await HandleClientConnectionAsync(tcpClient);
            }
        }

        private async Task<string> ReadLineAsync(StreamReader reader)
        {
            Task<string> readLineTask = reader.ReadLineAsync();
            if (!(await readLineTask.TryAwait(5000)))
            {
                throw new TimeoutException("Client read timed out");
            }
            MailDemonLog.Write(LogLevel.Debug, "CLIENT: " + readLineTask.Result);
            return readLineTask.Result;
        }

        private async Task HandleEhlo(StreamWriter writer, SslStream sslStream, X509Certificate2 sslCertificate)
        {
            await writer.WriteLineAsync($"250-SIZE 65536");
            await writer.WriteLineAsync($"250-8BITMIME");
            await writer.WriteLineAsync($"250-AUTH PLAIN");
            await writer.WriteLineAsync($"250-PIPELINING");
            await writer.WriteLineAsync($"250-ENHANCEDSTATUSCODES");
            //await writer.WriteLineAsync($"250-BINARYMIME");
            //await writer.WriteLineAsync($"250-CHUNKING");
            if (!string.IsNullOrWhiteSpace(sslCertificateFile) && sslStream == null && port != 465 && port != 587)
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
            string sentAuth = Encoding.UTF8.GetString(Convert.FromBase64String(line)).Replace("\0", "(null)");
            foreach (MailDemonUser user in users)
            {
                if (user.Authenticate(sentAuth))
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

        private async Task<Tuple<SslStream, StreamReader, StreamWriter>> StartTls(
            TcpClient tcpClient,
            string clientIPAddress,
            StreamReader reader,
            StreamWriter writer,
            bool sendReadyCommand,
            X509Certificate2 sslCertificate)
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
                bool sslServerEnabled = false;

                // shut everything down after 5 seconds if not success
                Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    if (!sslServerEnabled)
                    {
                        try
                        {
                            sslStream.Close();
                            tcpClient.Close();
                        }
                        catch
                        {
                        }
                    }
                }).ConfigureAwait(false).GetAwaiter();
                MailDemonLog.Write(LogLevel.Info, $"Starting ssl connection from client {clientIPAddress}");
                await sslStream.AuthenticateAsServerAsync(sslCertificate, false, System.Security.Authentication.SslProtocols.Tls12, true);
                sslServerEnabled = true;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Unable to negotiate ssl from client {clientIPAddress}, error: {ex}");
            }

            // create comm streams on top of ssl stream
            StreamReader sslReader = new StreamReader(sslStream, Encoding.UTF8);
            StreamWriter sslWriter = new StreamWriter(sslStream, utf8Encoding) { AutoFlush = true, NewLine = "\r\n" };

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
                CancellationTokenSource token = new CancellationTokenSource();
                Task<MimeMessage> mimeTask = MimeMessage.LoadAsync(mimeStream, true, token.Token);
                if (!(await mimeTask.TryAwait(30000)))
                {
                    token.Cancel();
                    throw new TimeoutException("Failed to read mime data - timeout");
                }
                await writer.WriteLineAsync($"250 2.0.0 OK");
                return new MailFromResult
                {
                    From = (fromUser == null ? new MailboxAddress(fromAddress) : fromUser.MailAddress),
                    ToAddresses = toAddressesByDomain,
                    Message = mimeTask.Result
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
                        await writer.WriteLineAsync($"500 invalid command - cannot forward");
                        await writer.FlushAsync();
                    }

                    // setup forward headers
                    string forwardToAddress = (string.IsNullOrWhiteSpace(user.ForwardAddress) ? globalForwardAddress : user.ForwardAddress);
                    if (string.IsNullOrWhiteSpace(forwardToAddress))
                    {
                        await writer.WriteLineAsync($"500 invalid command - cannot forward 2");
                        await writer.FlushAsync();
                    }
                    else
                    {
                        // create brand new message to forward
                        MimeMessage message = new MimeMessage();
                        message.From.Add(user.MailAddress);
                        message.ReplyTo.Add(user.MailAddress);
                        message.To.Add(new MailboxAddress(forwardToAddress));
                        message.Subject = "FW: " + result.Message.Subject;

                        // now to create our body...
                        BodyBuilder builder = new BodyBuilder
                        {
                            TextBody = result.Message.TextBody,
                            HtmlBody = result.Message.HtmlBody
                        };
                        foreach (var attachment in result.Message.Attachments)
                        {
                            builder.Attachments.Add(attachment);
                        }
                        message.Body = builder.ToMessageBody();
                        string toDomain = user.ForwardAddress.Substring(user.ForwardAddress.IndexOf('@') + 1);

                        // create new object to forward on
                        MailFromResult newResult = new MailFromResult
                        {
                            From = user.MailAddress,
                            Message = message,
                            ToAddresses = new Dictionary<string, List<string>> { { toDomain, new List<string> { forwardToAddress } } }
                        };

                        // forward the message on and clear the forward headers
                        MailDemonLog.Write(LogLevel.Info, "Forwarding message, from: {0}, to: {1}, forward: {2}", result.From, address, forwardToAddress);
                        await SendMail(newResult);
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

        private RSACryptoServiceProvider GetRSAProviderForPrivateKey(string pemPrivateKey)
        {
            RSACryptoServiceProvider rsaKey = new RSACryptoServiceProvider();
            PemReader reader = new PemReader(new StringReader(pemPrivateKey));
            RsaPrivateCrtKeyParameters rkp = reader.ReadObject() as RsaPrivateCrtKeyParameters;
            RSAParameters rsaParameters = DotNetUtilities.ToRSAParameters(rkp);
            rsaKey.ImportParameters(rsaParameters);
            return rsaKey;
        }

        private X509Certificate2 LoadSslCertificate()
        {
            Exception error = null;
            if (!string.IsNullOrWhiteSpace(sslCertificateFile))
            {
                for (int i = 0; i < 2; i++)
                {
                    try
                    {
                        byte[] bytes = File.ReadAllBytes(sslCertificateFile);
                        X509Certificate2 newSslCertificate = (sslCertificatePassword == null ? new X509Certificate2(bytes) : new X509Certificate2(bytes, sslCertificatePassword));
                        if (!newSslCertificate.HasPrivateKey && !string.IsNullOrWhiteSpace(sslCertificatePrivateKeyFile))
                        {
                            newSslCertificate = newSslCertificate.CopyWithPrivateKey(GetRSAProviderForPrivateKey(File.ReadAllText(sslCertificatePrivateKeyFile)));
                        }
                        MailDemonLog.Write(LogLevel.Info, "Loaded ssl certificate {0}", newSslCertificate);
                        return newSslCertificate;
                    }
                    catch (Exception ex)
                    {
                        error = ex;

                        // in case something is copying a new certificate, give it a second and try one more time
                        Thread.Sleep(1000);
                    }
                }
            }
            if (error != null)
            {
                MailDemonLog.Write(LogLevel.Error, "Error loading ssl certificate: {0}", error);
            }
            return null;
        }

        private async Task SendMessage(MimeMessage msg, InternetAddress from, string domain)
        {
            MailDemonLog.Write(LogLevel.Info, "Sending from {0}, to: {1}", from, msg.To.ToString());
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
                MailDemonLog.Write(LogLevel.Info, "QueryAsync mx for domain {0}", domain);
                IDnsQueryResponse result = await lookup.QueryAsync(domain, QueryType.MX);
                foreach (DnsClient.Protocol.MxRecord record in result.AllRecords)
                {
                    // attempt to send, if fail, try next address
                    try
                    {
                        MailDemonLog.Write(LogLevel.Info, "GetHostEntryAsync for exchange {0}", record.Exchange);
                        ip = await Dns.GetHostEntryAsync(record.Exchange);
                        foreach (IPAddress ipAddress in ip.AddressList)
                        {
                            string host = ip.HostName;
                            try
                            {
                                msg.From.Clear();
                                msg.From.Add(from);
                                MailDemonLog.Write(LogLevel.Info, "Sending message to host {0}", host);
                                Task sendTask = client.ConnectAsync(host, options: MailKit.Security.SecureSocketOptions.StartTlsWhenAvailable).ContinueWith(async (task) => await client.SendAsync(msg));
                                await sendTask.TryAwait(10000);
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
