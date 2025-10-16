using System;
using System.Collections.Generic;

namespace UnibouwAPI.Models;

public partial class SubcontractorWorkItemMapping
{
    public Guid Id { get; set; }

    public Guid? WorkItemId { get; set; }

    public Guid? CategoryId { get; set; }

    public virtual WorkItem? Category { get; set; }

    public virtual ICollection<Subcontractor> Subcontractors { get; set; } = new List<Subcontractor>();

    public virtual WorkItem? WorkItem { get; set; }
}
