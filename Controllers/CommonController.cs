using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Security.Claims;
using UnibouwAPI.Helpers;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories.Interfaces;

namespace UnibouwAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CommonController : ControllerBase
    {
        private readonly ICommon _repositoryCommon;
        private readonly ILogger<CommonController> _logger;
        DateTime amsterdamNow = DateTimeConvert.ToAmsterdamTime(DateTime.UtcNow);

        public CommonController(ICommon repositoryCommon, ILogger<CommonController> logger)
        {
            _repositoryCommon = repositoryCommon;
            _logger = logger;
        }

        //--- WorkItem Category Type
        [HttpGet("workitemcategorytype")]
        [Authorize]
        public async Task<IActionResult> GetAllWorkItemCategoryTypes()
        {
            try
            {
                var items = await _repositoryCommon.GetAllWorkItemCategoryTypes();

                if (items == null || !items.Any())
                    return NotFound(new { message = "No work item category types found.", data = Array.Empty<WorkItemCategoryType>() });

                return Ok(new { count = items.Count(), data = items });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all work item category types.");
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        [HttpGet("workitemcategorytype/{id}")]
        [Authorize]
        public async Task<IActionResult> GetWorkItemCategoryTypeById(Guid id)
        {
            try
            {
                var item = await _repositoryCommon.GetWorkItemCategoryTypeById(id);

                if (item == null)
                    return NotFound(new { message = $"No work item category type found for ID: {id}.", data = (WorkItemCategoryType?)null });

                return Ok(new { data = item });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching work item category type by ID: {Id}", id);
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        //--- Person
        [HttpGet("person")]
        [Authorize]
        public async Task<IActionResult> GetAllPerson()
        {
            try
            {
                var items = await _repositoryCommon.GetAllPerson();

                if (items == null || !items.Any())
                    return NotFound(new { message = "No persons found.", data = Array.Empty<Person>() });

                return Ok(new { count = items.Count(), data = items });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching persons details.");
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        [HttpGet("person/{id}")]
        [Authorize]
        public async Task<IActionResult> GetPersonById(Guid id)
        {
            try
            {
                var item = await _repositoryCommon.GetPersonById(id);

                if (item == null)
                    return NotFound(new { message = $"No person found for ID: {id}.", data = (Person?)null });

                return Ok(new { data = item });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching person by ID: {Id}", id);
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        [HttpPost("create")]
        public async Task<IActionResult> CreatePerson([FromBody] Person person)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                var result = await _repositoryCommon.CreatePerson(person);

                if (result > 0)
                    return Ok(new { message = "Person created successfully", personID = person.PersonID });

                return StatusCode(500, "Failed to create person.");
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Database error while creating person. Data: {@Person}", person);
                return StatusCode(500, "Database error occurred.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while creating person. Data: {@Person}", person);
                return StatusCode(500, "An unexpected error occurred.");
            }
        }
    
        //--- Subcontractor WorkItem Mapping
        [HttpGet("subcontractorworkitemmapping")]
        [Authorize]
        public async Task<IActionResult> GetAllSubcontractorWorkItemMapping()
        {
            try
            {
                var items = await _repositoryCommon.GetAllSubcontractorWorkItemMapping();

                if (items == null || !items.Any())
                    return NotFound(new { message = "No subcontractor work item mappings found.", data = Array.Empty<SubcontractorWorkItemMapping>() });

                return Ok(new { count = items.Count(), data = items });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching subcontractor work item mappings.");
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        [HttpGet("subcontractorworkitemmapping/{id}")]
        [Authorize]
        public async Task<IActionResult> GetSubcontractorWorkItemMappingById(Guid id)
        {
            try
            {
                var item = await _repositoryCommon.GetSubcontractorWorkItemMappingById(id);

                if (item == null)
                    return NotFound(new { message = $"No subcontractor work item mapping found for ID: {id}.", data = (SubcontractorWorkItemMapping?)null });

                return Ok(new { data = item });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching subcontractor work item mapping by ID: {Id}", id);
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        // POST: api/subcontractors/workitemmapping
        [HttpPost("workitemmapping")]
        public async Task<IActionResult> CreateWorkItemMapping([FromBody] SubcontractorWorkItemMapping mapping)
        {
            if (!ModelState.IsValid)
            {
                _logger.LogWarning(
                    "Model validation failed while creating subcontractor-workitem mapping. Data: {@Mapping}",
                    mapping
                );
                return BadRequest(ModelState);
            }

            try
            {
                var success = await _repositoryCommon.CreateSubcontractorWorkItemMapping(mapping);

                if (!success)
                {
                    _logger.LogWarning(
                        "Repository returned failure while creating subcontractor-workitem mapping. SubcontractorID: {SubcontractorID}, WorkItemID: {WorkItemID}",
                        mapping.SubcontractorID,
                        mapping.WorkItemID
                    );

                    return StatusCode(500, "Failed to create subcontractor-workitem mapping.");
                }

                return Ok(new { message = "Mapping created successfully." });
            }
            catch (SqlException ex)
            {
                if (ex.Number == 2627 || ex.Number == 2601)
                {
                    _logger.LogWarning(
                        ex,
                        "Duplicate subcontractor-workitem mapping. SubcontractorID: {SubcontractorID}, WorkItemID: {WorkItemID}",
                        mapping.SubcontractorID,
                        mapping.WorkItemID
                    );

                    return Conflict(new
                    {
                        message = "A mapping with the same SubcontractorID and WorkItemID already exists."
                    });
                }

                _logger.LogError(
                    ex,
                    "SQL failure while creating subcontractor-workitem mapping. Data: {@Mapping}",
                    mapping
                );

                return StatusCode(500, new { message = "Database error occurred." });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unexpected failure while creating subcontractor-workitem mapping. Data: {@Mapping}",
                    mapping
                );

                return StatusCode(500, new { message = "An unexpected error occurred." });
            }
        }

        //--- Subcontractor Attachment Mapping
        [HttpGet("subcontractorattachmentmapping")]
        [Authorize]
        public async Task<IActionResult> GetAllSubcontractorAttachmentMapping()
        {
            try
            {
                var items = await _repositoryCommon.GetAllSubcontractorAttachmentMapping();

                if (items == null || !items.Any())
                    return NotFound(new { message = "No subcontractor attachment mappings found.", data = Array.Empty<SubcontractorAttachmentMapping>() });

                return Ok(new { count = items.Count(), data = items });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching subcontractor attachment mappings.");
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        [HttpGet("subcontractorattachmentmapping/{id}")]
        [Authorize] 
        public async Task<IActionResult> GetSubcontractorAttachmentMappingById(Guid id)
        {
            try
            {
                var item = await _repositoryCommon.GetSubcontractorAttachmentMappingById(id);

                if (item == null)
                    return NotFound(new { message = $"No subcontractor attachment mapping found for ID: {id}.", data = (SubcontractorAttachmentMapping?)null });

                return Ok(new { data = item });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching subcontractor attachment mapping by ID: {Id}", id);
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        [HttpPost("subcontractorattachmentmapping/upload")]
        [Authorize]
        [RequestSizeLimit(100_000_000)] // ~100 MB
        public async Task<IActionResult> UploadSubcontractorAttachments([FromForm] SubcontractorAttachmentUploadDto model)
        {
            try
            {
                if (model == null || model.SubcontractorID == Guid.Empty)
                    return BadRequest("Invalid subcontractor data.");

                if (model.Files == null || model.Files.Count == 0)
                    return BadRequest("Please upload at least one file.");

                var mapping = new SubcontractorAttachmentMapping
                {
                    SubcontractorID = model.SubcontractorID,
                    Files = model.Files,
                    UploadedBy = User.Identity?.Name ?? "System", // handle in backend
                    UploadedOn = amsterdamNow
                };

                var success = await _repositoryCommon.CreateSubcontractorAttachmentMappingsAsync(mapping);

                if (success)
                    return Ok(new
                    {
                        Message = "Files uploaded successfully.",
                        SubcontractorID = model.SubcontractorID,
                        FileCount = model.Files.Count
                    });

                return StatusCode(500, new { Message = "Failed to upload files." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading subcontractor attachments.");
                return StatusCode(500, new { Message = "An unexpected error occurred. Try again later." });
            }
        }


        //--- Customer
        [HttpGet("customer")]
        [Authorize]
        public async Task<IActionResult> GetAllCustomer()
        {
            try
            {
                var items = await _repositoryCommon.GetAllCustomer();

                if (items == null || !items.Any())
                    return NotFound(new { message = "No customers found.", data = Array.Empty<Customer>() });

                return Ok(new { count = items.Count(), data = items });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching customers.");
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        [HttpGet("customer/{id}")]
        [Authorize]
        public async Task<IActionResult> GetCustomerById(Guid id)
        {
            try
            {
                var item = await _repositoryCommon.GetCustomerById(id);

                if (item == null)
                    return NotFound(new { message = $"No customer found for ID: {id}.", data = (Customer?)null });

                return Ok(new { data = item });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching customer by ID: {Id}", id);
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        //------------ Global RFQ Reminder 
        // GET: api/RfqGlobalReminder
        [HttpGet("GetRfqGlobalReminder")]
        [Authorize]
        public async Task<IActionResult> GetRfqGlobalReminder()
        {
            try
            {
                var data = await _repositoryCommon.GetRfqGlobalReminder();
                return Ok(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,"Failed to fetch RFQ global reminder set.");
                return StatusCode(500, new { message = "Error fetching reminder values", error = ex.Message });
            }
        }

        // POST: api/RfqGlobalReminder/update
        [HttpPost("SaveRfqGlobalReminder")]
        [Authorize]
        public async Task<IActionResult> SaveRfqGlobalReminder([FromBody] RfqGlobalReminder reminder)
        {
            if (reminder == null)
                return BadRequest("Invalid data");

            try
            {
                // Get logged-in user's email
                var userEmail =
                       HttpContext.User.FindFirst("preferred_username")?.Value
                    ?? HttpContext.User.FindFirst(ClaimTypes.Email)?.Value
                    ?? HttpContext.User.FindFirst("emails")?.Value
                    ?? HttpContext.User.Identity?.Name;

                if (string.IsNullOrWhiteSpace(userEmail))
                    userEmail = "System"; // fallback if claims missing

                // Set createdBy in subcontractor
                reminder.UpdatedBy = userEmail;

                reminder.UpdatedAt = amsterdamNow;

                var result = await _repositoryCommon.SaveRfqGlobalReminder(reminder);

                if (result > 0)
                    return Ok(new { message = "Reminder configuration updated successfully" });

                return NotFound(new { message = "Record not found" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,"Error updating reminder configuration.");
                return StatusCode(500, new { message = "Error updating reminder configuration", error = ex.Message });
            }
        }
    }
}
