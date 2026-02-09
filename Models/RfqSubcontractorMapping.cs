namespace UnibouwAPI.Models
{
    public class RfqSubcontractorMapping
    {
        public Guid RfqID { get; set; }
        public Guid SubcontractorID { get; set; }
        public DateTime? DueDate { get; set; }
        public DateTime? CreatedOn { get; set; }
        public string? CreatedBy { get; set; }

        // Navigation
        public Rfq? Rfq { get; set; }
        public Subcontractor? Subcontractor { get; set; }
    }
}
