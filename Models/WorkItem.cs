﻿namespace UnibouwAPI.Models;

public partial class WorkItem
{
    public Guid WorkItemID { get; set; }
    public string? ERP_ID { get; set; }
    public Guid CategoryID { get; set; }
    public string? Number { get; set; }
    public string? Name { get; set; }
    public int? WorkItemParentID { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? DeletedOn { get; set; }
    public string? DeletedBy { get; set; }
    public bool IsDeleted { get; set; }

    // Navigation
    public string? CategoryName { get; set; }
}
