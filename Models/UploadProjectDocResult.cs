namespace UnibouwAPI.Models
{
    public class UploadProjectDocResult
    {
        public ProjectDocumentDto Document { get; set; } = new();
        public bool IsNewForProject { get; set; } // AC4
    }
}
