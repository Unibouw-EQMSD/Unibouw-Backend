using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace UnibouwAPI.Models;

public partial class SubcontractorAttachmentMapping
{
    public Guid SubcontractorID { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? FileType { get; set; }
    public string? FilePath { get; set; }
    public DateTime? UploadedOn { get; set; }
    public string? UploadedBy { get; set; }

    // Navigation
    public string? SubcontractorName { get; set; }
    //This is for multi-file uploads (not mapped to DB)
    [NotMapped]
    public IFormFileCollection? Files { get; set; }
}
