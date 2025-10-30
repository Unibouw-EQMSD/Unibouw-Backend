namespace UnibouwAPI.Models
{
    public class Rfq
    {
        public Guid RfqID { get; set; }
        public DateTime? SentDate { get; set; }
        public DateTime? DueDate { get; set; }
        public int? RfqSent { get; set; }
        public int? QuoteReceived { get; set; }
        public Guid? CustomerID { get; set; }
        public Guid? ProjectID { get; set; }
        public Guid? RfqResponseID { get; set; }
        public string? CustomerNote { get; set; }
        public DateTime? DeadLine { get; set; }
        public DateTime? CreatedOn { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string? ModifiedBy { get; set; }
        public DateTime? DeletedOn { get; set; }
        public string? DeletedBy { get; set; }
        public bool IsDeleted { get; set; }

        // Navigation
        public string? CustomerName { get; set; }
        public string? ProjectName { get; set; }
        public string? RfqResponseStatusName { get; set; }
    }
}
