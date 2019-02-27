using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace MailDemon
{
    public partial class MailDemonService
    {
        private async Task ProcessConnection()
        {
            using (TcpClient tcpClient = await server.AcceptTcpClientAsync())
            {
                await HandleClientConnectionAsync(tcpClient);
            }
        }

        private async Task<string> ReadLineAsync(Stream reader)
        {
            byte[] buf = new byte[1];
            byte b;
            string result = string.Empty;
            MemoryStream ms = new MemoryStream();
            while (reader != null && await reader.ReadAsync(buf, 0, 1) == 1)
            {
                b = buf[0];
                switch (b)
                {
                    case (byte)'\n':
                        reader = null;
                        break;

                    case (byte)'\r':
                    case 0:
                        break;

                    default:
                        ms.WriteByte(b);
                        break;
                }
                if (ms.Length > maxLineSize)
                {
                    throw new InvalidOperationException("Line too large");
                }
            }
            result = MailDemonExtensionMethods.Utf8EncodingNoByteMarker.GetString(ms.GetBuffer().AsSpan(0, (int)ms.Length));
            MailDemonLog.Debug("CLIENT: " + result);
            return result;
        }

        private async Task ReadWriteAsync(Stream reader, Stream writer, int count)
        {
            byte[] buffer = new byte[8192];
            int readCount;
            while (!cancelToken.IsCancellationRequested && count > 0)
            {
                readCount = await reader.ReadAsync(buffer, cancelToken);
                if (readCount < 1)
                {
                    throw new InvalidDataException("No data received from client");
                }
                count -= readCount;
                await writer.WriteAsync(buffer, 0, readCount, cancelToken);
            }
            await writer.FlushAsync();
        }

        private async Task<string> ValidateGreeting(string command, string line, IPEndPoint endPoint)
        {
            string clientDomain = line.Substring(4).Trim(' ', '[', ']', '(', ')', '<', '>');

            if (requireEhloIpHostMatch)
            {
                bool localHost = false;
                if ((clientDomain == "::1" || clientDomain == "127.0.0.1" || clientDomain == "localhost"))
                {
                    switch (endPoint.Address.ToString())
                    {
                        case "127.0.0.1":
                        case "::1":
                            localHost = true;
                            break;
                    }
                }

                if (!localHost)
                {
                    // client may not send ip in helo, must be fqdn
                    if (IPAddress.TryParse(clientDomain, out IPAddress clientDomainIp))
                    {
                        throw new ArgumentException($"Client HELO with just ip '{clientDomainIp}' not allowed");
                    }

                    // client fqdn ip addresses must contain the connection ip address
                    IPHostEntry entry = await Dns.GetHostEntryAsync(clientDomain);
                    bool foundOne = false;
                    foreach (IPAddress ip in entry.AddressList)
                    {
                        if (ip.Equals(endPoint.Address))
                        {
                            foundOne = true;
                            break;
                        }
                    }
                    if (!foundOne)
                    {
                        // reverse ip to host, if host matches client is OK, otherwise fail
                        entry = await Dns.GetHostEntryAsync(endPoint.Address);
                        if (!entry.HostName.Equals(clientDomain, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new ArgumentException($"Client {command} ip '{endPoint.Address}' does not match host '{clientDomain}' ip addresses");
                        }
                    }
                }
            }

            return clientDomain;
        }

        private async Task HandleEhlo(StreamWriter writer, string line, SslStream sslStream, X509Certificate2 sslCertificate, IPEndPoint endPoint)
        {
            await ValidateGreeting("EHLO", line, endPoint);
            await writer.WriteLineAsync($"250-SIZE {maxMessageSize}");
            await writer.WriteLineAsync($"250-8BITMIME");
            await writer.WriteLineAsync($"250-AUTH PLAIN");
            await writer.WriteLineAsync($"250-PIPELINING");
            await writer.WriteLineAsync($"250-ENHANCEDSTATUSCODES");
            await writer.WriteLineAsync($"250-BINARYMIME");
            await writer.WriteLineAsync($"250-CHUNKING");
            if (!string.IsNullOrWhiteSpace(sslCertificateFile) && sslStream == null && port != 465 && port != 587 && File.Exists(sslCertificateFile))
            {
                await writer.WriteLineAsync($"250-STARTTLS");
            }
            await writer.WriteLineAsync($"250 SMTPUTF8");
        }

        private async Task HandleHelo(StreamWriter writer, string line, IPEndPoint endPoint)
        {
            string clientDomain = await ValidateGreeting("HELO", line, endPoint);
            await writer.WriteLineAsync($"220 {Domain} Hello {clientDomain}");
        }

        private async Task<MailDemonUser> Authenticate(Stream reader, StreamWriter writer, string line)
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
                MailDemonLog.Info("User {0} authenticated", foundUser.Name);
                await writer.WriteLineAsync($"235 2.7.0 Accepted");
                return foundUser;
            }

            // fail
            MailDemonLog.Warn("Authentication failed: {0}", sentAuth);
            await writer.WriteLineAsync($"535 authentication failed");
            string userName = null;
            for (int i = 0; i < sentAuth.Length; i++)
            {
                if (sentAuth[i] == '\0')
                {
                    userName = sentAuth.Substring(0, i);
                    break;
                }
            }
            return new MailDemonUser(userName, userName, null, null, null, false);
        }

        private async Task HandleClientConnectionAsync(TcpClient tcpClient)
        {
            using (tcpClient)
            {
                string ipAddress = (tcpClient.Client.RemoteEndPoint as IPEndPoint).Address.ToString();
                MailDemonUser authenticatedUser = null;
                X509Certificate2 sslCert = null;
                bool helo = false;

                try
                {
                    tcpClient.ReceiveTimeout = tcpClient.SendTimeout = streamTimeoutMilliseconds;

                    MailDemonLog.Info("Connection from {0}", ipAddress);

                    // immediately drop if client is blocked
                    if (CheckBlocked(ipAddress))
                    {
                        MailDemonLog.Warn("Blocking {0}", ipAddress);
                        return;
                    }

                    using (NetworkStream clientStream = tcpClient.GetStream())
                    {
                        // create comm streams
                        SslStream sslStream = null;
                        clientStream.ReadTimeout = clientStream.WriteTimeout = streamTimeoutMilliseconds;
                        Stream reader = clientStream;
                        StreamWriter writer = new StreamWriter(clientStream, MailDemonExtensionMethods.Utf8EncodingNoByteMarker) { AutoFlush = true, NewLine = "\r\n" };

                        if (port == 465 || port == 587)
                        {
                            sslCert = (sslCert ?? LoadSslCertificate());
                            Tuple<SslStream, Stream, StreamWriter> tls = await StartTls(tcpClient, ipAddress, reader, writer, false, sslCert);
                            if (tls == null)
                            {
                                throw new IOException("Failed to start TLS, ssl certificate failed to load");
                            }
                            sslStream = tls.Item1;
                            reader = tls.Item2;
                            writer = tls.Item3;
                        }

                        MailDemonLog.Info("Connection accepted from {0}", ipAddress);

                        // send greeting
                        await writer.WriteLineAsync($"220 {Domain} {greeting}");
                        IPEndPoint endPoint = tcpClient.Client.RemoteEndPoint as IPEndPoint;

                        while (true)
                        {
                            string line = await ReadLineAsync(reader);

                            // these commands are allowed before HELO
                            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                            {
                                break;
                            }
                            else if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase))
                            {
                                await HandleEhlo(writer, line, sslStream, sslCert, endPoint);
                                helo = true;
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
                                    Tuple<SslStream, Stream, StreamWriter> tls = await StartTls(tcpClient, ipAddress, reader, writer, true, sslCert);
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
                            else if (line.StartsWith("HELO", StringComparison.OrdinalIgnoreCase))
                            {
                                await HandleHelo(writer, line, endPoint);
                                helo = true;
                            }
                            else if (!helo)
                            {
                                throw new InvalidOperationException("Client did not send greeting before line " + line);
                            }

                            // these commands may only appear after HELO/EHLO
                            else if (line.StartsWith("RSET", StringComparison.OrdinalIgnoreCase))
                            {
                                await writer.WriteLineAsync($"250 2.0.0 Resetting");
                                authenticatedUser = null;
                            }
                            else if (line.StartsWith("AUTH PLAIN", StringComparison.OrdinalIgnoreCase))
                            {
                                authenticatedUser = await Authenticate(reader, writer, line);
                                if (!authenticatedUser.Authenticated)
                                {
                                    throw new InvalidOperationException("Authentication failed");
                                }
                            }

                            // if authenticated, only valid line is MAIL FROM
                            // TODO: consider changing this
                            else if (authenticatedUser != null)
                            {
                                if (line.StartsWith("MAIL FROM:<", StringComparison.OrdinalIgnoreCase))
                                {
                                    await SendMail(authenticatedUser, reader, writer, line, endPoint);
                                }
                                else
                                {
                                    MailDemonLog.Warn("Ignoring client command: " + line);
                                }
                            }
                            else
                            {
                                if (line.StartsWith("MAIL FROM:<", StringComparison.OrdinalIgnoreCase))
                                {
                                    // non-authenticated user, forward message on if possible, check settings
                                    await ReceiveMail(reader, writer, line, endPoint);
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
                    IncrementFailure(ipAddress, authenticatedUser?.Name);
                    MailDemonLog.Error(ex);
                }
                finally
                {
                    sslCert?.Dispose();
                    MailDemonLog.Info("{0} disconnected", ipAddress);
                }
            }
        }
    }
}
