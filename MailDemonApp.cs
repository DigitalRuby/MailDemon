using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using DnsClient;
using DnsClient.Internal;

using MailKit;
using MailKit.Net;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using MimeKit;

namespace MailDemon
{
    public class MailDemonApp
    {
        private MailDemonService mailService;
        private MailDemonWebApp webApp;
        private CancellationTokenSource cancel = new CancellationTokenSource();

        private void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            cancel.Cancel();
        }

        private static async Task TestClientConnectionAsync(MailDemonService demon, string server, string to, string file)
        {
            SmtpClient client = new SmtpClient()
            {
                SslProtocols = System.Security.Authentication.SslProtocols.None,
                Timeout = 60000 // 60 secs
            };
            await client.ConnectAsync(server, 25, MailKit.Security.SecureSocketOptions.StartTlsWhenAvailable);

            try
            {
                await client.AuthenticateAsync(new NetworkCredential("no", "no"));
            }
            catch
            {
                // expected
            }

            await client.DisconnectAsync(false);
            await client.ConnectAsync(server, 25, MailKit.Security.SecureSocketOptions.StartTlsWhenAvailable);
            await client.AuthenticateAsync(new NetworkCredential(demon.Users.First().UserName, demon.Users.First().Password));

            MimeMessage msg = new MimeMessage();
            msg.From.Add(demon.Users.First().MailAddress);
            foreach (string toAddress in to.Split(',', ';'))
            {
                msg.To.Add(MailboxAddress.Parse(toAddress));
            }
            msg.Subject = "Test Subject";
            BodyBuilder bodyBuilder = new BodyBuilder();
            Multipart multipart = new Multipart("mixed");
            bodyBuilder.HtmlBody = "<html><body><b>Test Email Html Body Which is Bold 12345</b></body></html>";
            multipart.Add(bodyBuilder.ToMessageBody());
            if (file != null && File.Exists(file))
            {
                byte[] bytes = System.IO.File.ReadAllBytes(file);
                var attachment = new MimePart("binary", "bin")
                {
                    Content = new MimeContent(new MemoryStream(bytes), ContentEncoding.Binary),
                    ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                    ContentTransferEncoding = ContentEncoding.Binary, // Base64 for DATA test, Binary for BINARYMIME test
                    FileName = Path.GetFileName(file)
                };
                multipart.Add(attachment);
            }
            msg.Body = multipart;
            await client.SendAsync(msg);
            await client.DisconnectAsync(true);
            Console.WriteLine("Test message sent");
        }

        private Task Run(string[] args)
        {
            Console.CancelKeyPress += Console_CancelKeyPress;

            // read config
            string rootDir = Directory.GetCurrentDirectory();
            IConfigurationBuilder configBuilder = new ConfigurationBuilder().SetBasePath(rootDir);
            if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development" &&
                File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "appsettings.Development.json")))
            {
                configBuilder.AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), "appsettings.Development.json"));
            }
            else
            {
                configBuilder.AddJsonFile("appsettings.json");
            }
            IConfigurationRoot config = configBuilder.Build();
            CertificateCache cache = new CertificateCache(config); // singleton

            // start web server
            webApp = new MailDemonWebApp(args, rootDir, config);
            Task webTask = webApp.StartAsync(cancel.Token);

            // start mail server
            var mailLogger = webApp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MailDemonService>>();
            webApp.MailService = mailService = new MailDemonService(args, config, mailLogger);
            Task mailTask = mailService.StartAsync(cancel.Token);
                        
            var logger = webApp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MailDemonApp>>();
            logger.LogInformation("Mail demon running");

            // test sending with the server:
            // test localhost toaddress@domain.com,toaddress@otherdomain.com [full path to file to attach]
            if (args.Length > 0 && args[0].StartsWith("test", StringComparison.OrdinalIgnoreCase))
            {
                mailService.DisableSending = true;
                string file = args.Length > 3 ? args[3] : null;
                TestClientConnectionAsync(mailService, args[1], args[2], file).ConfigureAwait(false).GetAwaiter().GetResult();
                TestClientConnectionAsync(mailService, args[1], args[2], file).ConfigureAwait(false).GetAwaiter().GetResult();
                args = [];
            }

            return Task.WhenAll(mailTask, webTask);
        }

        public static Task Main(string[] args)
        {
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            MailDemonApp app = new();
            return app.Run(args);
        }
    }
}
