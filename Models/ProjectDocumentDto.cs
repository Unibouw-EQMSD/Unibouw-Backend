namespace UnibouwAPI.Models
{
    public class ProjectDocumentDto
    {
        public Guid ProjectDocumentID { get; set; }
        public Guid ProjectID { get; set; }
        public string FileName { get; set; } = "";
        public string OriginalFileName { get; set; } = "";
        public string? ContentType { get; set; }
        public long SizeBytes { get; set; }
        public string StoragePath { get; set; } = "";
        public string ChecksumSha256 { get; set; } = "";
        public DateTime CreatedOn { get; set; }
        public string? CreatedBy { get; set; }
        public bool IsDeleted { get; set; }
    }
}
