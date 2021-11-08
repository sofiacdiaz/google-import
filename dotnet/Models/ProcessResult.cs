using Newtonsoft.Json;

namespace SheetsCatalogImport.Models
{
    public class ProcessResult
    {
        [JsonProperty("done")]
        public int Done { get; set; }
        [JsonProperty("error")]
        public int Error { get; set; }
        [JsonProperty("message")]
        public string Message { get; set; }
    }
}
