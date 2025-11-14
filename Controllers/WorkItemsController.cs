using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UnibouwAPI.Models;
using System;
using System.Threading.Tasks;
using UnibouwAPI.Repositories.Interfaces;

namespace UnibouwAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WorkItemsController : ControllerBase
    {
        private readonly IWorkItems _repository;
        private readonly ICommon _repositoryCommon;
        private readonly ILogger<WorkItemsController> _logger;

        public WorkItemsController(IWorkItems repository,ICommon repositoryCommon, ILogger<WorkItemsController> logger)
        {
            _repository = repository;
            _repositoryCommon = repositoryCommon;
            _logger = logger;
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetAllWorkItems()
        {
            try
            {
                var items = await _repository.GetAllWorkItems();

                if (items == null || !items.Any())
                {              
                    return NotFound(new
                    {
                        message = "No work items found.",
                        data = Array.Empty<WorkItem>() // return empty array for consistency
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
        public async Task<IActionResult> GetWorkItemById(Guid id)
        {
            try
            {
                var item = await _repository.GetWorkItemById(id);

                if (item == null)
                {
                    return NotFound(new
                    {
                        message = $"No work item found for ID: {id}.",
                        data = (WorkItem?)null
                    });
                }

                return Ok(new
                {
                    data = item
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching work item with ID: {Id}.", id);
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        [HttpGet("WorkItemByCategory/{categoryId}")]
        [Authorize]
        public async Task<IActionResult> GetWorkItemByCategory(Guid categoryId)
        {
            try
            {
                if (categoryId == Guid.Empty)
                    return BadRequest(new { message = "Invalid category ID." });

                var items = await _repository.GetAllWorkItems();

                // Filter by category
                var filteredItems = items.Where(w => w.CategoryID == categoryId).ToList();

                if (!filteredItems.Any())
                {
                    return NotFound(new
                    {
                        message = $"No work items found for Category ID: {categoryId}",
                        data = Array.Empty<WorkItem>()
                    });
                }

                return Ok(new
                {
                    count = filteredItems.Count,
                    data = filteredItems
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching work items for Category ID: {CategoryId}", categoryId);
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }
    

        [HttpPost("{id}/{isActive}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateWorkItemIsActive(Guid id, bool isActive)
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
                var result = await _repository.UpdateWorkItemIsActive(id, isActive, modifiedBy);

                // Check result and return response
                if (result == 0)
                    return NotFound(new { message = "Work item not found." });

                return Ok(new { message = "Status updated successfully.", result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching upadating IsActive status for ID: {Id}.", id);
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        [HttpPost("{id}/description")]
        [Authorize]
        public async Task<IActionResult> UpdateWorkItemDescription(Guid id, [FromBody] string description)
        {
            try
            {
                if (id == Guid.Empty)
                    return BadRequest(new { message = "Invalid Id." });

                //Get the current logged-in user from claims
                var modifiedBy = User?.Identity?.Name; // usually the username or email from the token

                if (string.IsNullOrWhiteSpace(modifiedBy))
                    return Unauthorized(new { message = "User information not found in token." });

                if (string.IsNullOrWhiteSpace(description))
                    return BadRequest("Description cannot be empty.");

                var result = await _repository.UpdateWorkItemDescription(id, description, modifiedBy);

                if(result == 0)
                 return NotFound(new { message = "This WorkItem is not active or does not exist." });

                return Ok(new { message = "Description updated successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching upadating IsActive status for ID: {Id}.", id);
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

    }
}
