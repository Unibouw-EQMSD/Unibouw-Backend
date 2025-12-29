namespace UnibouwAPI.Models
{
    public class SendMailRequest
    {
        public Guid SubcontractorID { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
        public List<string>? AttachmentFilePaths { get; set; }
    }
}
