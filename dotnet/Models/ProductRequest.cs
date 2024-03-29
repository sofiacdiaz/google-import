﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace SheetsCatalogImport.Models
{
    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
    public class ProductRequest
    {
        [JsonProperty("Id")]
        public long? Id { get; set; }

        [JsonProperty("Name")]
        public string Name { get; set; }

        [JsonProperty("CategoryPath")]
        public string CategoryPath { get; set; }

        [JsonProperty("BrandName")]
        public string BrandName { get; set; }

        [JsonProperty("DepartmentId")]
        public long? DepartmentId { get; set; }

        [JsonProperty("CategoryId")]
        public long? CategoryId { get; set; }

        [JsonProperty("BrandId")]
        public long? BrandId { get; set; }

        [JsonProperty("LinkId")]
        public string LinkId { get; set; }

        [JsonProperty("RefId")]
        public string RefId { get; set; }

        [JsonProperty("IsVisible")]
        public bool IsVisible { get; set; }

        [JsonProperty("Description")]
        public string Description { get; set; }

        [JsonProperty("DescriptionShort")]
        public string DescriptionShort { get; set; }

        [JsonProperty("ReleaseDate")]
        public string ReleaseDate { get; set; }

        [JsonProperty("KeyWords")]
        public string KeyWords { get; set; }

        [JsonProperty("Title")]
        public string Title { get; set; }

        [JsonProperty("IsActive")]
        public bool IsActive { get; set; }

        [JsonProperty("TaxCode")]
        public string TaxCode { get; set; }

        [JsonProperty("MetaTagDescription")]
        public string MetaTagDescription { get; set; }

        [JsonProperty("SupplierId")]
        public long? SupplierId { get; set; }

        [JsonProperty("ShowWithoutStock")]
        public bool ShowWithoutStock { get; set; }

        [JsonProperty("AdWordsRemarketingCode")]
        public string AdWordsRemarketingCode { get; set; }

        [JsonProperty("LomadeeCampaignCode")]
        public string LomadeeCampaignCode { get; set; }

        [JsonProperty("Score")]
        public long? Score { get; set; }
    }
}
