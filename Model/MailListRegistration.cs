using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MailDemon
{
    public class MailListRegistration
    {
        public long Id { get; set; }
        public string ListName { get; set; }
        public string EmailAddress { get; set; }
        public IDictionary<string, object> Fields { get; set; }
        public string IPAddress { get; set; }
        public DateTime Expires { get; set; }
        public DateTime SubscribedDate { get; set; }
        public DateTime UnsubscribedDate { get; set; }
        public string SubscribeToken { get; set; }
        public string UnsubscribeToken { get; set; }
    }
}
