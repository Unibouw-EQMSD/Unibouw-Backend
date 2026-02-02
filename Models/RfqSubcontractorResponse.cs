namespace UnibouwAPI.Models
{
    public class RfqSubcontractorResponse
    {
        public Guid RfqSubcontractorResponseID { get; set; }
        public Guid RfqID { get; set; }
        public Guid? RfqResponseStatusID { get; set; }
        public Guid? WorkItemID { get; set; }
        public Guid SubcontractorID { get; set; }
        public bool? Viewed { get; set; }
        public DateTime? ViewedOn { get; set; }
        public decimal? TotalQuoteAmount { get; set; }
        public string FileComment { get; set; } = string.Empty;
        public string NotIntrestedComment { get; set; }
        public int? SubmissionCount { get; set; }
        public DateTime? CreatedOn { get; set; } = DateTime.Now;
        public string CreatedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string ModifiedBy { get; set; }
    }
}
