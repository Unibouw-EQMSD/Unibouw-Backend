using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace UnibouwAPI.Models;

public partial class SubcontractorWorkItemMapping
{
    public Guid SubcontractorID { get; set; }
    public Guid WorkItemID { get; set; }

    // Navigation
    public string? SubcontractorName { get; set; }
    public string? WorkItemName { get; set; }
}
