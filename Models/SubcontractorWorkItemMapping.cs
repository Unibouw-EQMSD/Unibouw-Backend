using System;
using System.Collections.Generic;

namespace UnibouwAPI.Models;

public partial class SubcontractorWorkItemMapping
{
    public Guid SubcontractorID { get; set; }
    public Guid WorkItemID { get; set; }

    // Navigation
    public Subcontractor? Subcontractor { get; set; }
    public WorkItem? WorkItem { get; set; }
}
