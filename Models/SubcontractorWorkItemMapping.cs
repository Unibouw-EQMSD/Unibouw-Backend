using System;
using System.Collections.Generic;

namespace UnibouwAPI.Models;

public partial class SubcontractorWorkItemMapping
{
    public Guid Id { get; set; }

    public Guid? WorkItemId { get; set; }

    public Guid? CategoryId { get; set; }

    public Guid? SubcontractorId { get; set; }

    public virtual WorkItemCategoryType? Category { get; set; }

    public virtual Subcontractor? Subcontractor { get; set; }

    public virtual WorkItem? WorkItem { get; set; }
}
