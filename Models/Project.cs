namespace UnibouwAPI.Models
{
    public class Project
    {
        public Guid ProjectID { get; set; }
        public int? ERP_ID { get; set; }
        public string? Company { get; set; }
        public string? Number { get; set; }
        public string? Name { get; set; }
        public Guid? CustomerID { get; set; }
        public Guid? WorkPlannerID { get; set; }
        public Guid? ProjectMangerID { get; set; }
        public Guid? PersonID { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? CompletionDate { get; set; }
        public string? SharepointURL { get; set; }
        public string? TotalDimension { get; set; }
        public string? Status { get; set; }
        public DateTime? CreatedOn { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string? ModifiedBy { get; set; }
        public DateTime? DeletedOn { get; set; }
        public string? DeletedBy { get; set; }
        public bool IsDeleted { get; set; }

        // Navigation
        public Customer? Customer { get; set; }
        public WorkPlanner? WorkPlanner { get; set; }
        public ProjectManager? ProjectManager { get; set; }
    }
}
