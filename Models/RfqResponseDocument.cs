using System;

namespace UnibouwAPI.Models
{
    public class RfqResponseDocument
    {
        public Guid RfqResponseDocumentID { get; set; }
        public Guid RfqID { get; set; }
        public Guid SubcontractorID { get; set; }
        public string FileName { get; set; } = string.Empty;
        public byte[] FileData { get; set; } = Array.Empty<byte>();
        public DateTime UploadedOn { get; set; } = DateTime.Now;
    }

}
