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

using MailKit;
using MailKit.Net;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Configuration;
using MimeKit;

namespace MailDemon
{
    public class MailDemonApp
    {
        private static MailDemonService demon;
        private static CancellationTokenSource cancel = new CancellationTokenSource();

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            cancel.Cancel();
        }

        private static async Task TestClientConnectionAsync(MailDemonService demon, string to, string file)
        {
            SmtpClient client = new SmtpClient()
            {
                SslProtocols = System.Security.Authentication.SslProtocols.None,
                Timeout = 60000 // 60 secs
            };
            await client.ConnectAsync("localhost", 25, MailKit.Security.SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(new NetworkCredential(demon.Users.First().Name, demon.Users.First().Password));

            MimeMessage msg = new MimeMessage();
            msg.From.Add(new MailboxAddress(demon.Users.First().Address));
            msg.To.Add(new MailboxAddress(to));
            msg.Subject = "Test Subject";
            BodyBuilder bodyBuilder = new BodyBuilder();
            Multipart multipart = new Multipart("mixed");
            bodyBuilder.HtmlBody = "<html><body><b>Test Email Html Body Which is Bold 12345</b></body></html>";
            multipart.Add(bodyBuilder.ToMessageBody());
            if (file != null)
            {
                byte[] bytes = System.IO.File.ReadAllBytes(file);
                var attachment = new MimePart("binary", "bin")
                {
                    Content = new MimeContent(new MemoryStream(bytes), ContentEncoding.Binary),
                    ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                    ContentTransferEncoding = ContentEncoding.Base64, // Base64 for DATA test, Binary for BINARYMIME test
                    FileName = Path.GetFileName(file)
                };
                multipart.Add(attachment);
            }
            msg.Body = multipart;
            await client.SendAsync(msg);
            await client.DisconnectAsync(true);
            Console.WriteLine("Test message sent");
        }

        public static void Main(string[] args)
        {
            string path = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
            var builder = new ConfigurationBuilder().SetBasePath(path).AddJsonFile("appsettings.json");
            IConfigurationRoot configuration = builder.Build();
            demon = new MailDemonService(args, configuration);
            Console.CancelKeyPress += Console_CancelKeyPress;
            demon.StartAsync(cancel.Token).ConfigureAwait(false);
            MailDemonLog.Write(LogLevel.Info, "Mail demon running, press Ctrl-C to exit");

            // test sending with the server:
            // test toaddress@domain.com [full path to file to attach]
            if (args.Length > 1 && args[0].Equals("test", StringComparison.OrdinalIgnoreCase))
            {
                string file = args.Length > 1 ? args[2] : null;
                TestClientConnectionAsync(demon, args[1], file).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            cancel.Token.WaitHandle.WaitOne();
        }
    }
}
