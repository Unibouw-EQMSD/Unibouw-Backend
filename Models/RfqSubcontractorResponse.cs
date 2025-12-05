namespace UnibouwAPI.Models
{
    public class RfqSubcontractorResponse
    {
        public Guid RfqSubcontractorResponseID { get; set; }
        public Guid RfqID { get; set; }
        public Guid SubcontractorID { get; set; }
        public Guid? RfqResponseID { get; set; } // Maps to Interested / Not Interested etc.
        public bool? Viewed { get; set; }
        public DateTime? ViewedOn { get; set; }
        public bool? Downloaded { get; set; }
        public DateTime? DownloadedOn { get; set; }
        public string Comment { get; set; } = string.Empty;
        public DateTime CreatedOn { get; set; } = DateTime.Now;
        public DateTime? ModifiedOn { get; set; }
        public decimal? TotalQuoteAmount { get; set; }
        public  int? SubmissionCount { get; set; }  
    }
}
