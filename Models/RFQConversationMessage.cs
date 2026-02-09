using Newtonsoft.Json;
using UnibouwAPI.Repositories.Interfaces;

namespace UnibouwAPI.Models
{
    public class RFQConversationMessage
    {
        public Guid ConversationMessageID { get; set; }

        public Guid ProjectID { get; set; }
        public Guid SubcontractorID { get; set; }        
        public string SenderType { get; set; } = null!;
        public string Subject { get; set; } = null;
        public string MessageText { get; set; } = null!;
        public DateTime MessageDateTime { get; set; }
        public string Status { get; set; } = "Active";
        public Guid? SubcontractorMessageID { get; set; }
        public string? Tag { get; set; }
        public string CreatedBy { get; set; } = null!;
        public DateTime CreatedOn { get; set; }
        

        [JsonProperty("attachments")]
        public List<RFQConversationMessageAttachment> Attachments { get; set; } = new List<RFQConversationMessageAttachment>();
    }
}
