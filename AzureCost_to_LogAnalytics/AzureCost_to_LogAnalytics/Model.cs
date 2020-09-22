using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace AzureCost_to_LogAnalytics
{

    public class QueryResults
    {
        public string id { get; set; }
        public string name { get; set; }
        public string type { get; set; }
        public object location { get; set; }
        public object sku { get; set; }
        public object eTag { get; set; }
        public Properties properties { get; set; }
    }

    public class Properties
    {
        public object nextLink { get; set; }
        public Column[] columns { get; set; }
        public object[][] rows { get; set; }
    }

    public class Column
    {
        public string name { get; set; }
        public string type { get; set; }
    }


}
