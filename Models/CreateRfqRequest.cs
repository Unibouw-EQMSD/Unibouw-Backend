namespace UnibouwAPI.Models
{
    public class CreateRfqRequest
    {
        public Rfq RfqDetails { get; set; }
        public List<string> SubcontractorIds { get; set; }
        public List<Guid> WorkItemIds { get; set; }
        public string Status { get; set; } = "Draft";
    }
}
