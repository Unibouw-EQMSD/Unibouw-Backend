namespace UnibouwAPI.Models
{
    public class RfqSubcontractorWorkItemResponse
    {
        public Guid RfqSubcontractorWorkItemResponseID { get; set; }
        public Guid RfqSubcontractorResponseID { get; set; }
        public Guid RfqID { get; set; }
        public Guid WorkItemID { get; set; }
        public bool? IsReviewed { get; set; }
        public DateTime? ReviewedOn { get; set; }
        public bool? LatestVersionAvailable { get; set; }
        public string Comment { get; set; } = string.Empty;
        public DateTime? CreatedOn { get; set; }
        public DateTime? ModifiedOn { get; set; }
    }
}
