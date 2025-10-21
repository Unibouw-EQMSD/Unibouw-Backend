using System;
using System.Collections.Generic;

namespace UnibouwAPI.Models;

public partial class WorkItemsLocal
{
    public int Id { get; set; }

    public string Type { get; set; } = null!;

    public string Number { get; set; } = null!;

    public string Name { get; set; } = null!;

    public int? WorkItemParent_ID { get; set; }  

    public DateTime? created_at { get; set; }   

    public DateTime? updated_at { get; set; }   

    public DateTime? deleted_at { get; set; }   
}
