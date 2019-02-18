using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MailDemon
{
    [Serializable]
    public class SignUpModel : BaseModel
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public Dictionary<string, string> Fields { get; set; } = new Dictionary<string, string>();

        public string FieldValue(string name)
        {
            Fields.TryGetValue(name, out string value);
            return value ?? string.Empty;
        }
    }
}
