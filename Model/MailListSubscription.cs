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

        public void SetViewBag(string key, object value)
        {
            ViewBagObject[key] = value;
        }

        public void SetField(string key, object value)
        {
            Fields[key] = value;
        }

        public T ViewBag<T>(string key)
        {
            if (ViewBagObject.TryGetValue(key, out object value) && value != null)
            {
                return (T)value;
            }
            return default;
        }

        public string Field(string key)
        {
            if (Fields.TryGetValue(key, out object value) && value != null)
            {
                return value.ToString();
            }
            return string.Empty;
        }

        public MailListSubscription MakePending(DateTime dt, bool all)
        {
            if (all || string.IsNullOrWhiteSpace(Result) || Result == "Pending")
            {
                Result = "Pending";
                ResultTimestamp = dt;
            }
            return this;
        }

        public long Id { get; set; }
        public string ListName { get; set; }
        public string LanguageCode { get; set; }
        public IDictionary<string, object> Fields { get; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        public string IPAddress { get; set; }
        public DateTime Expires { get; set; }
        public DateTime SubscribedDate { get; set; }
        public DateTime UnsubscribedDate { get; set; }
        public string SubscribeToken { get; set; }
        public string UnsubscribeToken { get; set; }
        public string Result { get; set; }
        public DateTime ResultTimestamp { get; set; }

        public string EmailAddressDomain
        {
            get
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
        }

        public string EmailAddress { get => Field("EmailAddress"); set => SetField("EmailAddress", value); }
        public string FirstName { get => Field("FirstName"); set => SetField("FirstName", value); }
        public string LastName { get => Field("LastName"); set => SetField("LastName", value); }
        public string Company { get => Field("Company"); set => SetField("Company", value); }
        public string Phone { get => Field("Phone"); set => SetField("Phone", value); }
        public string Address { get => Field("Address"); set => SetField("Address", value); }
        public string City { get => Field("City"); set => SetField("City", value); }
        public string Region { get => Field("Region"); set => SetField("Region", value); }
        public string State { get => Region; set => Region = value; }
        public string Country { get => Field("Country"); set => SetField("Country", value); }

        [NotMapped]
        [LiteDB.BsonIgnore]
        public string TemplateName { get; set; }

        [NotMapped]
        [LiteDB.BsonIgnore]
        public MailList MailList
        {
            get => ViewBag<MailList>(VarMailList);
            set => SetViewBag(VarMailList, value);
        }

        [NotMapped]
        [LiteDB.BsonIgnore]
        public string SubscribeUrl
        {
            get => ViewBag<string>(VarSubscribeUrl);
            set => SetViewBag(VarSubscribeUrl, value);
        }

        [NotMapped]
        [LiteDB.BsonIgnore]
        public string UnsubscribeUrl
        {
            get => ViewBag<string>(VarUnsubscribeUrl);
            set => SetViewBag(VarUnsubscribeUrl, value);
        }

        /// <summary>
        /// View bag, not saved to database, can cast to ExpandoObject
        /// </summary>
        [NotMapped]
        [LiteDB.BsonIgnore]
        public IDictionary<string, object> ViewBagObject { get; } = new ExpandoObject();
    }
}
