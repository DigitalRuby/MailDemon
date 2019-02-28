using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;

namespace MailDemon
{
    [Serializable]
    public class BaseModel
    {
        [NotMapped]
        [LiteDB.BsonIgnore]
        public string Message { get; set; }

        [NotMapped]
        [LiteDB.BsonIgnore]
        public bool Error { get; set; }
    }

    [Serializable]
    public class BaseModel<T> : BaseModel
    {
        public T Value { get; set; }
    }
}
