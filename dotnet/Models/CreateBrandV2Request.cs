using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace SheetsCatalogImport.Models
{
    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
    public class CreateBrandV2Request
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("metaTagDescription")]
        public string MetaTagDescription { get; set; }

        [JsonProperty("keywords")]
        public List<object> Keywords { get; set; }

        [JsonProperty("siteTitle")]
        public string SiteTitle { get; set; }

        [JsonProperty("isActive")]
        public bool IsActive { get; set; }

        [JsonProperty("slug")]
        public string Slug { get; set; }

        [JsonProperty("score")]
        public long? Score { get; set; }

        [JsonProperty("displayOnMenu")]
        public bool DisplayOnMenu { get; set; }
    }
}
