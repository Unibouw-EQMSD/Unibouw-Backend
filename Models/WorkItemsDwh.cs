using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace UnibouwAPI.Models;

public partial class WorkItemsDwh
{
    public int Id { get; set; }

    public string Type { get; set; } = null!;

    public string Number { get; set; } = null!;

    public string Name { get; set; } = null!;

    [Column("WorkItemParent_ID")]
    public int? WorkItemParent_ID { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }
}
