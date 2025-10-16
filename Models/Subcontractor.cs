using System;
using System.Collections.Generic;

namespace UnibouwAPI.Models;

public partial class Subcontractor
{
    public Guid Id { get; set; }

    public string? ErpId { get; set; }

    public string? Name { get; set; }

    public decimal? Rating { get; set; }

    public string? ContactPerson { get; set; }

    public string? EmailId { get; set; }

    public decimal? PhoneNumber1 { get; set; }

    public decimal? PhoneNumber2 { get; set; }

    public string? Location { get; set; }

    public string? Country { get; set; }

    public string? OfficeAdress { get; set; }

    public string? BillingAddress { get; set; }

    public string? RegisteredDate { get; set; }

    public Guid? AttachmentsId { get; set; }

    public Guid? WorkItemsId { get; set; }

    public bool? IsActive { get; set; }

    public DateTime? CreatedOn { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedOn { get; set; }

    public string? ModifiedBy { get; set; }

    public bool? IsDeleted { get; set; }

    public DateTime? DeletedOn { get; set; }

    public string? DeletedBy { get; set; }

    public virtual SubcontractorAttachmentsMapping? Attachments { get; set; }

    public virtual SubcontractorWorkItemMapping? WorkItems { get; set; }
}
