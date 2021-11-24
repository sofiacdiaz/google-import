using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace SheetsCatalogImport.Models
{
    public class CreateCategoryV2Response
    {
        [JsonProperty("value")]
        public CategoryV2Value Value { get; set; }

        [JsonProperty("children")]
        public List<object> Children { get; set; }
    }

    public class CategoryV2Value
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("isActive")]
        public bool IsActive { get; set; }

        [JsonProperty("displayOnMenu")]
        public bool DisplayOnMenu { get; set; }

        [JsonProperty("score")]
        public long Score { get; set; }

        [JsonProperty("filterByBrand")]
        public bool FilterByBrand { get; set; }

        [JsonProperty("isClickable")]
        public bool IsClickable { get; set; }
    }
}
