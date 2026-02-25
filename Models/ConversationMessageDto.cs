namespace UnibouwAPI.Models
{
    public class ConversationMessageDto
    {
        public Guid MessageID { get; set; }
        public string SenderType { get; set; } // PM | Subcontractor
        public string MessageText { get; set; }
        public DateTime MessageDateTime { get; set; }
        public string Subject { get; set; }
        public string ConversationType { get; set; }
        public Guid? SubcontractorMessageID { get; set; }

        // ✅ ADD THIS (so UI can show filenames for reply messages too)
        public List<ConversationAttachmentDto> Attachments { get; set; } = new();
    }

    // ✅ NEW DTO for attachments
    public class ConversationAttachmentDto
    {
        public Guid AttachmentID { get; set; }
        public Guid ConversationMessageID { get; set; }
        public string FileName { get; set; }
        public string FileExtension { get; set; }
        public long FileSize { get; set; }
        public string FilePath { get; set; }
    }
}