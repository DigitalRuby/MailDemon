﻿using Microsoft.Extensions.Logging;

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
        private async Task ProcessConnection(TcpClient tcpClient)
        {
            try
            {
                await HandleClientConnectionAsync(tcpClient);
            }
            catch (ObjectDisposedException)
            {
                // ignore, happens on shutdown
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error from client connection {endPoint}", tcpClient.Client.RemoteEndPoint);
            }
            finally
            {
                try
                {
                    tcpClient.Dispose();
                }
                catch
                {
                }
            }
        }

        private async Task<string> ReadLineAsync(Stream reader)
        {
            byte[] buf = new byte[1];
            byte b;
            string result;
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
            logger.LogDebug("CLIENT: {result}", result);
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
            await writer.WriteLineAsync($"250-AUTH LOGIN PLAIN");
            await writer.WriteLineAsync($"250-PIPELINING");
            await writer.WriteLineAsync($"250-ENHANCEDSTATUSCODES");
            await writer.WriteLineAsync($"250-BINARYMIME");
            await writer.WriteLineAsync($"250-CHUNKING");
            var cert = await CertificateCache.Instance.LoadSslCertificateAsync(sslCertificateFile, sslCertificatePrivateKeyFile,
                sslCertificatePassword, logger);
            if (cert != null && sslStream == null && port != 465 && port != 587)
            {
                await writer.WriteLineAsync($"250-STARTTLS");
            }
            await writer.WriteLineAsync($"250 SMTPUTF8");
            await writer.FlushAsync();
        }

        private async Task HandleHelo(StreamWriter writer, string line, IPEndPoint endPoint)
        {
            string clientDomain = await ValidateGreeting("HELO", line, endPoint);
            await writer.WriteLineAsync($"220 {Domain} Hello {clientDomain}");
            await writer.FlushAsync();
        }

        private async Task<MailDemonUser> AuthenticatePlain(Stream reader, StreamWriter writer, string line)
        {
            if (line == "AUTH PLAIN")
            {
                await writer.WriteLineAsync($"334 ok");
                await writer.FlushAsync();
                line = await ReadLineAsync(reader) ?? string.Empty;
            }
            else
            {
                line = line.Substring(11);
            }
            string sentAuth = Encoding.UTF8.GetString(Convert.FromBase64String(line)).Replace("\0", ":").Trim(':');
            string userName = sentAuth;
            string password = null;
            int pos = sentAuth.IndexOf(':');
            if (pos >= 0)
            {
                userName = sentAuth.Substring(0, pos).Trim();
                password = sentAuth.Substring(++pos);
            }
            foreach (MailDemonUser user in users)
            {
                if (user.Authenticate(userName, password, logger))
                {
                    logger.LogInformation("User {user} authenticated", user.UserName);
                    await writer.WriteLineAsync($"235 2.7.0 Accepted");
                    await writer.FlushAsync();
                    return user;
                }
            }

            // fail
            logger.LogWarning("Authentication failed: {auth}", sentAuth);
            await writer.WriteLineAsync($"535 authentication failed");
            await writer.FlushAsync();
            return new MailDemonUser(userName, userName, password, userName, null, false, false);
        }

        private async Task<MailDemonUser> AuthenticateLogin(Stream reader, StreamWriter writer, string line)
        {
            MailDemonUser foundUser = null;
            await writer.WriteLineAsync("334 VXNlcm5hbWU6"); // user
            await writer.FlushAsync();
            string userName = await ReadLineAsync(reader) ?? string.Empty;
            await writer.WriteLineAsync("334 UGFzc3dvcmQ6"); // pwd
            await writer.FlushAsync();
            string password = await ReadLineAsync(reader) ?? string.Empty;
            userName = Encoding.UTF8.GetString(Convert.FromBase64String(userName)).Trim();
            password = Encoding.UTF8.GetString(Convert.FromBase64String(password));
            string sentAuth = userName + ":" + password;
            foreach (MailDemonUser user in users)
            {
                if (user.Authenticate(userName, password, logger))
                {
                    foundUser = user;
                    break;
                }
            }
            if (foundUser != null)
            {
                logger.LogInformation("User {user} authenticated", foundUser.UserName);
                await writer.WriteLineAsync($"235 2.7.0 Accepted");
                await writer.FlushAsync();
                return foundUser;
            }

            // fail
            logger.LogWarning("Authentication failed: {auth}", sentAuth);
            await writer.WriteLineAsync($"535 authentication failed");
            await writer.FlushAsync();
            return new MailDemonUser(userName, userName, password, userName, null, false, false);
        }

        private static readonly TimeSpan sessionTimeout = TimeSpan.FromMinutes(5.0);

        private async Task HandleClientConnectionAsync(TcpClient tcpClient)
        {
            if (tcpClient is null || tcpClient.Client is null || !tcpClient.Client.Connected)
            {
                return;
            }

            DateTime start = DateTime.UtcNow;
            string ipAddress = (tcpClient.Client.RemoteEndPoint as IPEndPoint).Address.ToString();
            MailDemonUser authenticatedUser = null;
            NetworkStream clientStream = null;
            X509Certificate2 sslCert = null;
            SslStream sslStream = null;
            bool helo = false;
            try
            {
                tcpClient.ReceiveTimeout = tcpClient.SendTimeout = streamTimeoutMilliseconds;

                logger.LogInformation("Connection from {ip}", ipAddress);

                // immediately drop if client is blocked
                if (CheckBlocked(ipAddress))
                {
                    logger.LogWarning("Blocked {ip}", ipAddress);
                    return;
                }

                clientStream = tcpClient.GetStream();

                // create comm streams
                clientStream.ReadTimeout = clientStream.WriteTimeout = streamTimeoutMilliseconds;
                Stream reader = clientStream;
                StreamWriter writer = new StreamWriter(clientStream, MailDemonExtensionMethods.Utf8EncodingNoByteMarker) { AutoFlush = true, NewLine = "\r\n" };

                async Task StartSSL()
                {
                    sslCert = await CertificateCache.Instance.LoadSslCertificateAsync(sslCertificateFile, sslCertificatePrivateKeyFile,
                        sslCertificatePassword, logger);
                    Tuple <SslStream, Stream, StreamWriter> tls = await StartTls(tcpClient, ipAddress, reader, writer, true, sslCert);
                    if (tls == null)
                    {
                        await writer.WriteLineAsync("503 Failed to start TLS");
                        await writer.FlushAsync();
                        throw new IOException("Failed to start TLS, ssl certificate failed to load");
                    }
                    else
                    {
                        sslStream = tls.Item1;
                        reader = tls.Item2;
                        writer = tls.Item3;
                    }
                }

                if (port == 465 || port == 587)
                {
                    await StartSSL();
                }

                logger.LogInformation("Connection accepted from {ip}", ipAddress);

                // send greeting
                await writer.WriteLineAsync($"220 {Domain} {greeting}");
                await writer.FlushAsync();
                IPEndPoint endPoint = tcpClient.Client.RemoteEndPoint as IPEndPoint;

                while (true)
                {
                    if ((DateTime.UtcNow - start) > sessionTimeout)
                    {
                        throw new TimeoutException($"Session expired after {sessionTimeout.TotalMinutes:0.00} minutes");
                    }

                    string line = await ReadLineAsync(reader);

                    // these commands are allowed before HELO
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("221 session terminated");
                        await writer.FlushAsync();
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
                            await writer.FlushAsync();
                        }
                        else
                        {
                            await StartSSL();
                        }
                    }
                    else if (line.StartsWith("HELO", StringComparison.OrdinalIgnoreCase))
                    {
                        await HandleHelo(writer, line, endPoint);
                        helo = true;
                    }
                    else if (line.StartsWith("NOOP", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("220 OK");
                        await writer.FlushAsync();
                    }
                    else if (line.StartsWith("HELP", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("220 OK Please use EHLO command");
                        await writer.FlushAsync();
                    }
                    else if (!helo)
                    {
                        throw new InvalidOperationException("Client did not send greeting before line " + line);
                    }

                    // these commands may only appear after HELO/EHLO
                    else if (line.StartsWith("RSET", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync($"250 2.0.0 Resetting");
                        await writer.FlushAsync();
                        authenticatedUser = null;
                    }
                    else if (line.StartsWith("AUTH PLAIN", StringComparison.OrdinalIgnoreCase))
                    {
                        authenticatedUser = await AuthenticatePlain(reader, writer, line);
                        if (authenticatedUser.Authenticated && tcpClient.Client.RemoteEndPoint is IPEndPoint remoteEndPoint)
                        {
                            IPBan.IPBanPlugin.IPBanLoginSucceeded("SMTP", authenticatedUser.UserName, remoteEndPoint.Address.ToString());
                        }
                        else
                        {
                            throw new InvalidOperationException("Authentication failed");
                        }
                    }
                    else if (line.StartsWith("AUTH LOGIN", StringComparison.OrdinalIgnoreCase))
                    {
                        authenticatedUser = await AuthenticateLogin(reader, writer, line);
                        if (authenticatedUser.Authenticated && tcpClient.Client.RemoteEndPoint is IPEndPoint remoteEndPoint)
                        {
                            IPBan.IPBanPlugin.IPBanLoginSucceeded("SMTP", authenticatedUser.UserName, remoteEndPoint.Address.ToString());
                        }
                        else
                        {
                            throw new InvalidOperationException("Authentication failed");
                        }
                    }

                    // if authenticated, only valid line is MAIL FROM
                    // TODO: The mail from address is ignored, we assume only can send as yourself who you authenticated as
                    // consider error if from address doesn't match
                    // TODO: consider changing this
                    else if (authenticatedUser != null)
                    {
                        if (line.StartsWith("MAIL FROM:<", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                await SendMail(authenticatedUser, reader, writer, line, endPoint, true);
                            }
                            catch (Exception ex)
                            {
                                throw new ApplicationException("Error sending mail from " + endPoint, ex);
                            }
                        }
                        else
                        {
                            logger.LogWarning("Ignoring client command: {line}", line);
                        }
                    }
                    else
                    {
                        if (line.StartsWith("MAIL FROM:<", StringComparison.OrdinalIgnoreCase))
                        {
                            // non-authenticated user, forward message on if possible, check settings
                            try
                            {
                                bool result = await ReceiveMail(reader, writer, line, endPoint);
                                if (!result)
                                {
                                    await writer.WriteLineAsync("221 session terminated");
                                    await writer.FlushAsync();
                                    break;
                                }
                            }
                            catch (Exception ex)
                            {
                                throw new ApplicationException("Error receiving mail from " + endPoint, ex);
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException("Invalid message: " + line + ", not authenticated");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!(ex is SocketException))
                {
                    IncrementFailure(ipAddress, authenticatedUser?.UserName);
                    logger.LogError(ex, "{ip} error", ipAddress);
                }
            }
            finally
            {
                sslStream?.Dispose();
                clientStream?.Dispose();
                logger.LogInformation("{ip} disconnected", ipAddress);
            }
        }
    }
}
