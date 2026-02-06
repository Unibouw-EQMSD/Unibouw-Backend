namespace UnibouwAPI.Models;

public partial class Subcontractor
{
    //[SwaggerSchema(ReadOnly = true)]
    public Guid SubcontractorID { get; set; }
    public string? Name { get; set; }
    public decimal? Rating { get; set; }
    public string? Email { get; set; }
    public string? Location { get; set; }
    public string? Country { get; set; }
    public string? OfficeAddress { get; set; }
    public string? BillingAddress { get; set; }
    public DateTime? RegisteredDate { get; set; }
    public Guid? PersonID { get; set; }
    public bool? IsActive { get; set; } = true;
    public DateTime? CreatedOn { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; } = null;
    public string? ModifiedBy { get; set; } = null;
    public DateTime? DeletedOn { get; set; } = null;
    public string? DeletedBy { get; set; } = null;
    public bool IsDeleted { get; set; } = false; 
    public int? RemindersSent { get; set; } = null;

    public string? ContactName { get; set; }        
    public string? ContactEmail { get; set; }       
    public string? ContactPhone { get; set; }       

    public List<Guid>? WorkItemIDs { get; set; }
    public List<string>? WorkItemName { get; set; }
}
