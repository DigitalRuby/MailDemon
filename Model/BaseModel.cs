using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MailDemon
{
    [Serializable]
    public class BaseModel
    {
        public string Message { get; set; }
        public bool Error { get; set; }
    }

    [Serializable]
    public class BaseModel<T> : BaseModel
    {
        public T Value { get; set; }
    }
}
