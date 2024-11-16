using System;
using System.Collections.Generic;
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

using MimeKit;

using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;

namespace MailDemon
{
    public partial class MailDemonService
    {
        private readonly string sslCertificateFile;
        private readonly string sslCertificatePrivateKeyFile;
        private readonly SecureString sslCertificatePassword;
        private readonly Dictionary<string, Regex> ignoreCertificateErrorsRegex = new Dictionary<string, Regex>(StringComparer.OrdinalIgnoreCase); // domain,regex

        private void TestSslCertificate()
        {
            MailDemonLog.Info("Testing ssl certificate file {0}, private key file {1}", sslCertificateFile, sslCertificatePrivateKeyFile);
            X509Certificate2 sslCert = CertificateCache.Instance.LoadSslCertificateAsync(sslCertificateFile, sslCertificatePrivateKeyFile, sslCertificatePassword).Sync();
            if (sslCert == null)
            {
                MailDemonLog.Error("SSL certificate failed to load or is not setup in config!");
            }
            else
            {
                MailDemonLog.Warn("SSL certificate loaded succesfully!");
            }
        }

        private async Task<Tuple<SslStream, Stream, StreamWriter>> StartTls
        (
            TcpClient tcpClient,
            string clientIPAddress,
            Stream reader,
            StreamWriter writer,
            bool sendReadyCommand,
            X509Certificate2 sslCertificate)
        {
            if (sslCertificate == null)
            {
                await writer.WriteLineAsync("501 Syntax error (no parameters allowed)");
                await writer.FlushAsync();
                return null;
            }
            else if (sendReadyCommand)
            {
                // upgrade to ssl
                await writer.WriteLineAsync($"220 Ready to start TLS");
                await writer.FlushAsync();
            }

            // create ssl stream and ensure encryption is required
            SslStream sslStream = new SslStream(reader, false, null, null, EncryptionPolicy.RequireEncryption);

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
                MailDemonLog.Info($"Starting ssl connection from client {clientIPAddress}");
                await sslStream.AuthenticateAsServerAsync(sslCertificate, false, System.Security.Authentication.SslProtocols.Tls12 |
                    System.Security.Authentication.SslProtocols.Tls13, true);
                sslServerEnabled = true;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Unable to negotiate ssl from client {clientIPAddress}, error: {ex}");
            }

            // create comm streams on top of ssl stream
            Stream sslReader = sslStream;
            StreamWriter sslWriter = new StreamWriter(sslStream, MailDemonExtensionMethods.Utf8EncodingNoByteMarker) { AutoFlush = true, NewLine = "\r\n" };

            return new Tuple<SslStream, Stream, StreamWriter>(sslStream, sslReader, sslWriter);
        }
    }
}
