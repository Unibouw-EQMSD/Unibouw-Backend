using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using UnibouwAPI.Helpers;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories.Interfaces;

namespace UnibouwAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SubcontractorController : ControllerBase
    {
        private readonly ISubcontractors _repository;
        private readonly ILogger<SubcontractorController> _logger;
        DateTime amsterdamNow = DateTimeConvert.ToAmsterdamTime(DateTime.UtcNow);

        public SubcontractorController(ISubcontractors repository, ILogger<SubcontractorController> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetAllSubcontractor()
        {
            try
            {
                var items = await _repository.GetAllSubcontractor();

                if (items == null || !items.Any())
                {
                    return NotFound(new
                    {
                        message = "No Subcontractor found.",
                        data = Array.Empty<Subcontractor>() // return empty array for consistency
                    });
                }
                return Ok(new
                {
                    count = items.Count(),
                    data = items
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching work items.");
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        [HttpGet("{id}")]
        [Authorize]
        public async Task<IActionResult> GetSubcontractorById(Guid id)
        {
            try
            {
                var item = await _repository.GetSubcontractorById(id);

                if (item == null)
                {
                    return NotFound(new
                    {
                        message = $"No Subcontractor found for ID: {id}.",
                        data = (Subcontractor?)null
                    });
                }

                return Ok(new
                {
                    data = item
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching work item category type with ID: {Id}.", id);
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }


        [HttpPost("{id}/{isActive}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateSubcontractorIsActive(Guid id, bool isActive)
        {
            try
            {
                // Validate input parameters
                if (id == Guid.Empty)
                    return BadRequest(new { message = "Invalid id." });

                // Get the current logged-in user from claims
                var modifiedBy = User?.Identity?.Name; // usually the username or email from the token

                if (string.IsNullOrWhiteSpace(modifiedBy))
                    return Unauthorized(new { message = "User information not found in token." });

                // Call the repository method directly
                var result = await _repository.UpdateSubcontractorIsActive(id, isActive, modifiedBy);

                // Check result and return response
                if (result == 0)
                    return NotFound(new { message = "Subcontractor not found." });

                return Ok(new { message = "Status updated successfully.", result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching upadating IsActive status for ID: {Id}.", id);
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        [HttpPost("createSubcontractorWithMappings")]
        [Authorize]
        public async Task<IActionResult> CreateSubcontractorWithMappings([FromBody] Subcontractor subcontractor)
        {
            if (subcontractor == null)
            {
                _logger.LogWarning("CreateSubcontractorWithMappings called with null subcontractor data.");
                return BadRequest("Invalid subcontractor data.");
            }

            try
            {
                // Get logged-in user's email
                var userEmail =
                       HttpContext.User.FindFirst("preferred_username")?.Value
                    ?? HttpContext.User.FindFirst(ClaimTypes.Email)?.Value
                    ?? HttpContext.User.FindFirst("emails")?.Value
                    ?? HttpContext.User.Identity?.Name;

                if (string.IsNullOrWhiteSpace(userEmail))
                {
                    _logger.LogWarning("User email claim missing, defaulting to 'System'.");
                    userEmail = "System"; // fallback if claims missing
                }

                // Set createdBy in subcontractor
                subcontractor.CreatedBy = userEmail;
                subcontractor.CreatedOn = amsterdamNow;
                subcontractor.RegisteredDate = amsterdamNow;

                // Pass it to repository
                var success = await _repository.CreateSubcontractorWithMappings(subcontractor);

                if (success)
                {
                    _logger.LogInformation("Subcontractor created successfully. SubcontractorID: {SubcontractorID}, CreatedBy: {UserEmail}", subcontractor.SubcontractorID, userEmail);

                    return Ok(new
                    {
                        Message = "Subcontractor and mappings created successfully.",
                        SubcontractorID = subcontractor.SubcontractorID
                    });
                }
                else
                {
                    _logger.LogError("Failed to create subcontractor. Subcontractor: {@Subcontractor}", subcontractor);
                    return StatusCode(500, "Something went wrong while saving data.");
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("email", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(ex, "Duplicate email detected for subcontractor: {Email}", subcontractor.Email);
                return Conflict(new
                {
                    Field = "email",
                    Message = "A subcontractor with this email address already exists.",
                    Error = ex.Message
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("name", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(ex, "Duplicate name detected for subcontractor: {Name}", subcontractor.Name);
                return Conflict(new
                {
                    Field = "name",
                    Message = "A subcontractor with this name already exists.",
                    Error = ex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while creating subcontractor: {@Subcontractor}", subcontractor);
                return StatusCode(500, new
                {
                    Message = "An unexpected error occurred while processing the request.",
                    Error = ex.Message
                });
            }
        }

        [HttpGet("{id}/reminders-sent")]
        [Authorize]
        public async Task<IActionResult> GetSubcontractorRemindersSent(Guid id)
        {
            try
            {
                if (id == Guid.Empty)
                    return BadRequest(new { message = "Invalid subcontractor id." });

                var subcontractor = await _repository.GetSubcontractorRemindersSent(id);

                if (subcontractor == null)
                {
                    return NotFound(new
                    {
                        message = $"No subcontractor found for ID: {id}.",
                        data = (int?)null
                    });
                }

                return Ok(new
                {
                    SubcontractorID = id,
                    RemindersSent = subcontractor.RemindersSent
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while fetching RemindersSent for Subcontractor ID: {Id}", id);
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        [HttpPost("{id}/reminders-sent/{reminderSent}")]
        [Authorize]
        public async Task<IActionResult> UpdateSubcontractorRemindersSent(Guid id, int reminderSent)
        {
            try
            {
                if (id == Guid.Empty)
                    return BadRequest(new { message = "Invalid subcontractor id." });

                if (reminderSent < 0)
                    return BadRequest(new { message = "ReminderSent value cannot be negative." });

                var result = await _repository.UpdateSubcontractorRemindersSent(id, reminderSent);

                if (result == 0)
                {
                    return NotFound(new
                    {
                        message = "Subcontractor not found or already deleted."
                    });
                }

                return Ok(new
                {
                    message = "RemindersSent updated successfully.",
                    SubcontractorID = id,
                    RemindersSent = reminderSent
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while updating RemindersSent for Subcontractor ID: {Id}", id);
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }


    }
}
