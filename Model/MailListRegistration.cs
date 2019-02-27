using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace MailDemon
{
    public class MailListRegistration
    {
        private string GetField(string key)
        {
            if (Fields.TryGetValue(key, out object value))
            {
                return value.ToString();
            }
            return string.Empty;
        }

        public long Id { get; set; }
        public string ListName { get; set; }
        public string EmailAddress { get; set; }
        public string LanguageCode { get; set; }
        public IDictionary<string, object> Fields { get; set; }
        public string IPAddress { get; set; }
        public DateTime Expires { get; set; }
        public DateTime SubscribedDate { get; set; }
        public DateTime UnsubscribedDate { get; set; }
        public string SubscribeToken { get; set; }
        public string UnsubscribeToken { get; set; }

        public string FirstName => GetField("firstName");
        public string LastName => GetField("lastName");
        public string Company => GetField("company");
        public string Phone => GetField("phone");
        public string Address => GetField("address");
        public string City => GetField("city");
        public string Region => GetField("region");
        public string Country => GetField("country");
    }
}
