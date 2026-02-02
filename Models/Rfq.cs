namespace UnibouwAPI.Models
{
    public class Rfq
    {
        public Guid RfqID { get; set; }
        public string? RfqNumber { get; set; }
        public DateTime? SentDate { get; set; }
        public DateTime? DueDate { get; set; }
        public DateTime? GlobalDueDate { get; set; }
        public int? RfqSent { get; set; }
        public int? QuoteReceived { get; set; }
        public long? CustomerID { get; set; }
        public Guid? ProjectID { get; set; }
        public string? Status { get; set; }
        public DateTime? CreatedOn { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string? ModifiedBy { get; set; }
        public DateTime? DeletedOn { get; set; }
        public string? DeletedBy { get; set; }
        public bool IsDeleted { get; set; }
        public string? CustomNote { get; set; }

        // Navigation
        public string? CustomerName { get; set; }
        public string? ProjectName { get; set; }
        public List<WorkItem> WorkItems { get; set; } = new();
        public List<RfqSubcontractorMapping> SubcontractorDueDates { get; set; } = new();

    }
}

