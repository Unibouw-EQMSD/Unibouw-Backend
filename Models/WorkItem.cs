using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace UnibouwAPI.Models;

public partial class WorkItem
{
    [Key]
    [Column("ID")]
    public Guid Id { get; set; }

    [Column("ERP_ID")]
    [StringLength(255)]
    public string? ErpId { get; set; }

    [Column("CategoryID")]
    public Guid? CategoryId { get; set; }

    [StringLength(255)]
    public string? Number { get; set; }

    [StringLength(255)]
    public string? Name { get; set; }

    [Column("WorkitemParent_ID")]
    [StringLength(255)]
    public string? WorkitemParentId { get; set; }

    public bool? IsActive { get; set; }

    public DateTime? CreatedOn { get; set; }

    [StringLength(255)]
    public string? CreatedBy { get; set; }

    public DateTime? ModifiedOn { get; set; }

    [StringLength(255)]
    public string? ModifiedBy { get; set; }

    public DateTime? DeletedOn { get; set; }

    [StringLength(255)]
    public string? DeletedBy { get; set; }

    [ForeignKey("CategoryId")]
    [InverseProperty("WorkItems")]
    public virtual WorkItemCategoryType? Category { get; set; }
}
