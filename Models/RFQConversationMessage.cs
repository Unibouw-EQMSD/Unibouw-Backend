using UnibouwAPI.Repositories.Interfaces;

namespace UnibouwAPI.Models
{
    public class RFQConversationMessage
    {
        public Guid ConversationMessageID { get; set; }

        public Guid ProjectID { get; set; }
        public Guid? RfqID { get; set; }
        public Guid? WorkItemID { get; set; }
        public Guid SubcontractorID { get; set; }
        public Guid ProjectManagerID { get; set; }
        public Guid? ParentMessageID { get; set; }

        public string SenderType { get; set; } = null!;
        public string MessageText { get; set; } = null!;
        public DateTime MessageDateTime { get; set; }
        public string Status { get; set; } = "Active";
        public string CreatedBy { get; set; } = null!;
        public DateTime CreatedOn { get; set; }
        public string Subject { get; set; } = null;
        public string? Tag { get; set; }

    }
}
