using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace MailDemon
{
    public class MailListSubscription : BaseModel
    {
        /// <summary>
        /// Mail list var name
        /// </summary>
        public const string VarMailList = "__mail-list";

        /// <summary>
        /// Confirm subscription var name
        /// </summary>
        public const string VarSubscribeUrl = "__subscribe-url";

        /// <summary>
        /// Unsubscribe var name
        /// </summary>
        public const string VarUnsubscribeUrl = "__unsubscribe-url";

        public void SetField(string key, object value)
        {
            Fields[key] = value;
        }

        public string Field(string key)
        {
            if (Fields.TryGetValue(key, out object value) && value != null)
            {
                return value.ToString();
            }
            return string.Empty;
        }

        public string GetDomainFromEmailAddress()
        {
            string emailAddress = EmailAddress;
            if (string.IsNullOrWhiteSpace(emailAddress))
            {
                return null;
            }
            int pos = emailAddress.IndexOf('@');
            if (pos < 0)
            {
                return null;
            }
            return emailAddress.Substring(++pos);
        }

        public long Id { get; set; }
        public string ListName { get; set; }
        public string LanguageCode { get; set; }
        public string IPAddress { get; set; }
        public DateTime Expires { get; set; }
        public DateTime SubscribedDate { get; set; }
        public DateTime UnsubscribedDate { get; set; }
        public string SubscribeToken { get; set; }
        public string UnsubscribeToken { get; set; }
        public string Result { get; set; }
        public DateTime ResultTimestamp { get; set; }
        public string EmailAddress { get; set; }

        [JsonIgnore]
        public string EmailAddressDomain { get; set; }

        [NotMapped]
        public IDictionary<string, object> Fields { get; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        [JsonIgnore]
        public string FieldsJson
        {
            get { return JsonConvert.SerializeObject(Fields); }
            set
            {
                Fields.Clear();
                foreach (KeyValuePair<string, object> kv in JsonConvert.DeserializeObject<Dictionary<string, object>>(value))
                {
                    Fields[kv.Key] = kv.Value;
                }
            }
        }

        [NotMapped]
        public string FirstName { get => Field("FirstName"); set => SetField("FirstName", value); }

        [NotMapped]
        public string LastName { get => Field("LastName"); set => SetField("LastName", value); }

        [NotMapped]
        public string Company { get => Field("Company"); set => SetField("Company", value); }

        [NotMapped]
        public string Phone { get => Field("Phone"); set => SetField("Phone", value); }

        [NotMapped]
        public string Address { get => Field("Address"); set => SetField("Address", value); }

        [NotMapped]
        public string City { get => Field("City"); set => SetField("City", value); }

        [NotMapped]
        public string Region { get => Field("Region"); set => SetField("Region", value); }

        [NotMapped]
        public string Country { get => Field("Country"); set => SetField("Country", value); }

        [NotMapped]
        [JsonIgnore]
        public string State { get => Region; set => Region = value; }

        [NotMapped]
        [JsonIgnore]
        public string TemplateName { get; set; }

        [NotMapped]
        [JsonIgnore]
        public MailList MailList { get; set; }

        [NotMapped]
        [JsonIgnore]
        public string SubscribeUrl { get; set; }

        [NotMapped]
        [JsonIgnore]
        public string UnsubscribeUrl { get; set; }
    }
}
