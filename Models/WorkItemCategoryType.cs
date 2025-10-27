using System;
using System.Collections.Generic;

namespace UnibouwAPI.Models;

public partial class WorkItemCategoryType
{
    public Guid CategoryId { get; set; }

    public string? CategoryName { get; set; }

    public virtual ICollection<SubcontractorWorkItemMapping> SubcontractorWorkItemMappings { get; set; } = new List<SubcontractorWorkItemMapping>();

    public virtual ICollection<WorkItem> WorkItems { get; set; } = new List<WorkItem>();
}
