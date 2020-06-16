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
        private readonly IMailDemonDatabaseProvider dbProvider;
        private readonly TimeSpan loopTimeSpan = TimeSpan.FromMinutes(1.0);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                DateTime dt = DateTime.UtcNow;
                using MailDemonDatabase db = dbProvider.GetDatabase();
                db.Subscriptions.RemoveRange(db.Subscriptions.Where(r => r.Expires <= dt && r.UnsubscribeToken == null));
                await db.SaveChangesAsync();
                await Task.Delay(loopTimeSpan, stoppingToken);
            }
        }

        public SubscriptionCleanup(IMailDemonDatabaseProvider dbProvider)
        {
            this.dbProvider = dbProvider;
        }
    }
}
