namespace UnibouwAPI.Models
{
    public class EmailRequest
    {
        public Guid RfqID { get; set; }
        public List<Guid> SubcontractorIDs { get; set; } = new();  // <-- list now
        public string ToEmail { get; set; } = string.Empty;
        public string Subject { get; set; } = "RFQ Invitation - Unibouw";
        public List<Guid> WorkItems { get; set; } = new();
    }
}
