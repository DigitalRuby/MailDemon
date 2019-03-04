using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;

namespace MailDemon
{
    public class SubscriptionCleanup : BackgroundService
    {
        private readonly TimeSpan loopTimeSpan = TimeSpan.FromMinutes(1.0);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            do
            {
                DateTime dt = DateTime.UtcNow;
                using (MailDemonDatabase db = new MailDemonDatabase())
                {
                    foreach (MailListSubscription reg in db.Select<MailListSubscription>(r => r.Expires <= dt && r.UnsubscribeToken == null))
                    {
                        db.Delete<MailListSubscription>(reg.Id);
                    }
                }
            }
            while (!(await stoppingToken.WaitHandle.WaitOneAsync(loopTimeSpan, stoppingToken)));
        }
    }
}
