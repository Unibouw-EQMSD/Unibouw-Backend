namespace UnibouwAPI.Models;

public partial class SubcontractorWorkItemMapping
{
    public Guid SubcontractorID { get; set; }
    public Guid WorkItemID { get; set; }
    public DateTime? CreatedOn { get; set; }
    public string? CreatedBy { get; set; }

    // Navigation
    public string? SubcontractorName { get; set; }
    public string? WorkItemName { get; set; }
}
