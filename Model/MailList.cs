using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MailDemon
{
    /// <summary>
    /// Mail list
    /// </summary>
    public class MailList
    {
        /// <summary>
        /// Id
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// List name
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Mail list company
        /// </summary>
        public string Company { get; set; }

        /// <summary>
        /// Mail list website
        /// </summary>
        public string Website { get; set; }
    }

    public class MailListModel : BaseModel<MailList> { }
}
