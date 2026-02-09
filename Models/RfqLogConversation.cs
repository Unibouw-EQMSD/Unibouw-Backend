namespace UnibouwAPI.Models
{
    public class RfqLogConversation
    {
        public Guid LogConversationID { get; set; }  // Primary Key

        // Foreign Keys
        public Guid ProjectID { get; set; }
        public Guid SubcontractorID { get; set; }

        // Conversation details
        public string ConversationType { get; set; }   // Email, Call
        public DateTime? MessageDateTime { get; set; }
        public string Subject { get; set; }
        public string Message { get; set; }

        // Audit
        public string? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
    }
}
