namespace SheetsCatalogImport.Models
{
    public class ResponseWrapper
    {
        public string ResponseText { get; set; }
        public string Message { get; set; }
        public bool IsSuccess { get; set; }
        public string StatusCode { get; set; }
    }
}
