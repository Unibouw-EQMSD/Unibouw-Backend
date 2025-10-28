using System;
using System.Collections.Generic;

namespace UnibouwAPI.Models;

public partial class Subcontractor
{
    public Guid SubcontractorID { get; set; }
    public string? ERP_ID { get; set; }
    public string? Name { get; set; }
    public decimal? Rating { get; set; }
    public string? EmailID { get; set; }
    public decimal? PhoneNumber1 { get; set; }
    public decimal? PhoneNumber2 { get; set; }
    public string? Location { get; set; }
    public string? Country { get; set; }
    public string? OfficeAdress { get; set; }
    public string? BillingAddress { get; set; }
    public DateTime? RegisteredDate { get; set; }
    public Guid? PersonID { get; set; }
    public bool? IsActive { get; set; }
    public DateTime? CreatedOn { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? DeletedOn { get; set; }
    public string? DeletedBy { get; set; }
    public bool IsDeleted { get; set; }

    // Navigation
    public Person? Person { get; set; }
}
