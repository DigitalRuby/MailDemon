using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MailDemon
{
    public class SubscriptionCleanup : BackgroundService
    {
        private readonly IServiceProvider serviceProvider;
        private readonly TimeSpan loopTimeSpan = TimeSpan.FromMinutes(1.0);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            do
            {
                DateTime dt = DateTime.UtcNow;
                using (IMailDemonDatabase db = serviceProvider.GetService<IMailDemonDatabase>())
                {
                    db.Delete<MailListSubscription>(r => r.Expires <= dt && r.UnsubscribeToken == null);
                }
            }
            while (!(await stoppingToken.WaitHandle.WaitOneAsync(loopTimeSpan, stoppingToken)));
        }

        public SubscriptionCleanup(IServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider;
        }
    }
}
