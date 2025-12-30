namespace UnibouwAPI.Models
{
    public class SubcontractorLatestMessageDto
    {
        public Guid SubcontractorID { get; set; }
        public string SubcontractorName { get; set; } = string.Empty;
        public DateTime LatestMessageDateTime { get; set; }
    }
}
