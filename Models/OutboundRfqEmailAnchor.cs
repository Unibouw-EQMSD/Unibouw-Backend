namespace UnibouwAPI.Models
{
    public class OutboundRfqEmailAnchor
    {
        public Guid AnchorId { get; set; }
        public Guid ProjectId { get; set; }
        public Guid SubcontractorId { get; set; }
        public Guid? RfqId { get; set; }
        public string PmMailbox { get; set; } = string.Empty;
        public string? ConversationId { get; set; }
        public string? InternetMessageId { get; set; }
        public string? GraphMessageId { get; set; }
        public DateTime SentUtc { get; set; }
        public string? Subject { get; set; }
        public bool IsActive { get; set; }
    }
}