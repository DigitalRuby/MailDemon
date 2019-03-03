using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;

namespace MailDemon
{
    public class SubscriptionCleanup : IHostedService
    {
        private readonly TimeSpan loopTimeSpan = TimeSpan.FromMinutes(1.0);

        async Task IHostedService.StartAsync(CancellationToken cancellationToken)
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
            while (!(await cancellationToken.WaitHandle.WaitOneAsync(loopTimeSpan, cancellationToken)));
        }

        Task IHostedService.StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
