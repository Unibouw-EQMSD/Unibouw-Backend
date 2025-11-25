namespace UnibouwAPI.Models
{
    public class RfqResponseDocument
    {
        public Guid RfqResponseDocumentID { get; set; }
        public Guid RfqID { get; set; }
        public Guid SubcontractorID { get; set; }
        public string FileName { get; set; } = string.Empty;
        public byte[] FileData { get; set; } = Array.Empty<byte>();
        public DateTime? UploadedOn { get; set; } = DateTime.Now;
        public bool? IsDeleted { get; set; }
        public string IsDeletedBy { get; set; } = string.Empty;
        public DateTime? DeletedOn { get; set; } = DateTime.Now;
    }
}
