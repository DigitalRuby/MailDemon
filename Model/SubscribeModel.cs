using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MailDemon
{
    [Serializable]
    public class SubscribeModel : MailListRegistration
    {
        public string Title { get; set; }
        public string TemplateName { get; set; }
    }
}
