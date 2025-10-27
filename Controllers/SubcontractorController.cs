using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories;
using UnibouwAPI.Repositories.Interfaces;

namespace UnibouwAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SubcontractorController : ControllerBase
    {
        private readonly ISubcontractor _repository;
        private readonly ILogger<SubcontractorController> _logger;

        public SubcontractorController(ISubcontractor repository, ILogger<SubcontractorController> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var items = await _repository.GetAllAsync();

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
        public async Task<IActionResult> GetById(Guid id)
        {
            try
            {
                var item = await _repository.GetByIdAsync(id);

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

        [HttpPut("{id}/{isActive}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateIsActive(Guid id, bool isActive)
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
                var result = await _repository.UpdateIsActiveAsync(id, isActive, modifiedBy);

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

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] Subcontractor subcontractor)
        {
            if (subcontractor == null)
                return BadRequest(new { message = "Invalid subcontractor data." });

            if (subcontractor.SubcontractorWorkItemMappings == null || !subcontractor.SubcontractorWorkItemMappings.Any())
                return BadRequest(new { message = "At least one WorkItem mapping is required." });

            foreach (var mapping in subcontractor.SubcontractorWorkItemMappings)
            {
                if (!Guid.TryParse(mapping.WorkItemId.ToString(), out var workItemGuid) ||
                    !Guid.TryParse(mapping.CategoryId.ToString(), out var categoryGuid))
                    return BadRequest(new { message = "Invalid GUID format for WorkItemId or CategoryId." });

                mapping.WorkItemId = workItemGuid;
                mapping.CategoryId = categoryGuid;
            }

            var result = await _repository.CreateAsync(subcontractor);

            if (result > 0)
                return Ok(new { message = "Subcontractor added successfully." });

            return StatusCode(500, new { message = "Failed to add subcontractor." });
        }



    }
}
