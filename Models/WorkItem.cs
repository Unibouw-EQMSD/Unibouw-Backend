using System;
using System.Collections.Generic;

namespace UnibouwAPI.Models;

public partial class WorkItem
{
    public Guid Id { get; set; }

    public string? ErpId { get; set; }

    public Guid? CategoryId { get; set; }

    public string? Number { get; set; }

    public string? Name { get; set; }

    public string? WorkitemParentId { get; set; }

    public bool? IsActive { get; set; }

    public DateTime? CreatedOn { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedOn { get; set; }

    public string? ModifiedBy { get; set; }

    public DateTime? DeletedOn { get; set; }

    public string? DeletedBy { get; set; }

    public string? Description { get; set; }

    public virtual WorkItemCategoryType? Category { get; set; }
}
