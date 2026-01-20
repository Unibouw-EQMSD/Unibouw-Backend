namespace UnibouwAPI.Models
{
    public class LogConversation
    {
        public Guid LogConversationID { get; set; }  // Primary Key

        // Foreign Keys
        public Guid ProjectID { get; set; }
        public Guid? RfqID { get; set; }
        public Guid SubcontractorID { get; set; }
        public Guid ProjectManagerID { get; set; }

        // Conversation details
        public string ConversationType { get; set; }   // Email, Call
        public string Subject { get; set; }
        public string Message { get; set; }
        public DateTime? MessageDateTime { get; set; }

        // Audit
        public string? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
    }
}
