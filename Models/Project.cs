namespace UnibouwAPI.Models
{
    public class Project
    {
        public Guid ProjectID { get; set; }
        public long? ERP_ID { get; set; }
        public string? Company { get; set; }
        public string? Number { get; set; }
        public string? Name { get; set; }
        public long? CustomerID { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? CompletionDate { get; set; }
        public string? SharepointURL { get; set; }
        public string? Status { get; set; }
        public DateTime? CreatedOn { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string? ModifiedBy { get; set; }
        public DateTime? DeletedOn { get; set; }
        public string? DeletedBy { get; set; }
        public bool IsDeleted { get; set; }

        // Navigation
        public string? CustomerName { get; set; }
        public Guid? PersonId { get; set; }
        public string? PersonName { get; set; }
        public string? PersonEmail { get; set; }
        public string? PersonRole { get; set; }
    }
}
