using System;
using System.Collections.Generic;

namespace UnibouwAPI.Models;

public partial class SubcontractorAttachmentsMapping
{
    public Guid Id { get; set; }

    public string? FileName { get; set; }

    public string? FileType { get; set; }

    public string? FilePath { get; set; }

    public DateTime? UploadedOn { get; set; }

    public string? UploadedBy { get; set; }

    public virtual ICollection<Subcontractor> Subcontractors { get; set; } = new List<Subcontractor>();
}
