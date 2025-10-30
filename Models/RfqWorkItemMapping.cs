namespace UnibouwAPI.Models
{
    public class RfqWorkItemMapping
    {
        public Guid RfqID { get; set; }
        public Guid WorkItemID { get; set; }

        // Navigation
        public Rfq? Rfq { get; set; }
        public WorkItem? WorkItem { get; set; }
    }
}
