using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace UnibouwAPI.Models;

public partial class Subcontractor
{
    //[System.Text.Json.Serialization.JsonIgnore]
    [SwaggerSchema(ReadOnly = true)]
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
    public bool? IsActive { get; set; } = true;
    public DateTime? CreatedOn { get; set; }
    public string? CreatedBy { get; set; }
    [DefaultValue(null)]
    public DateTime? ModifiedOn { get; set; } = null;
    [DefaultValue(null)]
    public string? ModifiedBy { get; set; } = null;
    [DefaultValue(null)]
    public DateTime? DeletedOn { get; set; } = null;
    [DefaultValue(null)]
    public string? DeletedBy { get; set; } = null;
    public bool IsDeleted { get; set; } = false; //Default false — appears in Swagger as false automatically

    // Navigation
    // [JsonIgnore]
    //[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [SwaggerSchema(ReadOnly = true)]
    public string? PersonName { get; set; }

    public List<Guid>? WorkItemIDs { get; set; }
}
