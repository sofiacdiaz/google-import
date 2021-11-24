using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace SheetsCatalogImport.Models
{
    public class CreateCategoryV2Request
    {
        [JsonProperty("parentId")]
        public string ParentId { get; set; }

        [JsonProperty("Name")]
        public string Name { get; set; }
    }
}
