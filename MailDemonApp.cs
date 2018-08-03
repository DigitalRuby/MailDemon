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
            demon?.Dispose();
        }

        private static async Task TestClientConnectionAsync(MailDemonService demon, string to)
        {
            SmtpClient client = new SmtpClient()
            {
                SslProtocols = System.Security.Authentication.SslProtocols.None,
                Timeout = 60000 // 60 secs
            };
            await client.ConnectAsync("localhost", 25, MailKit.Security.SecureSocketOptions.StartTlsWhenAvailable);
            await client.AuthenticateAsync(new NetworkCredential(demon.Users.First().Name, demon.Users.First().Password));
            MimeMessage msg = new MimeMessage
            {
                Body = (new BodyBuilder { HtmlBody = "<html><body><b>Test Email Bold 12345</b></body></html>" }).ToMessageBody(),
                Subject = "test subject"
            };
            msg.From.Add(new MailboxAddress(demon.Users.First().Name + "@" + demon.Domain));
            msg.To.Add(new MailboxAddress(to));
            await client.SendAsync(msg);
            await client.DisconnectAsync(true);
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
            if (args.Length > 1 && args[0].Equals("test", StringComparison.OrdinalIgnoreCase))
            {
                TestClientConnectionAsync(demon, args[1]).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            new ManualResetEvent(false).WaitOne();
        }
    }
}
