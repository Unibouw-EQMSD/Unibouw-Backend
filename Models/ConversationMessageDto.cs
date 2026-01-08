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
        public Guid? ParentMessageID { get; set; }
    }
}
