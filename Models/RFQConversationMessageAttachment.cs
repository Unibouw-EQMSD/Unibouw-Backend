namespace UnibouwAPI.Models
{
    public class RFQConversationMessageAttachment
    {
        public Guid AttachmentID { get; set; }
        public Guid ConversationMessageID { get; set; }

        public string FileName { get; set; } = string.Empty;
        public string? FileExtension { get; set; }
        public long FileSize { get; set; }

        public string FilePath { get; set; } = string.Empty;

        public Guid? UploadedBy { get; set; }
        public DateTime UploadedOn { get; set; }
        public bool IsActive { get; set; }
    }
}
