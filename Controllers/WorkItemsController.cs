using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UnibouwAPI.Models;
using System;
using System.Threading.Tasks;

namespace UnibouwAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WorkItemsController : ControllerBase
    {
        private readonly IWorkItems _repository;
        private readonly ILogger<WorkItemsController> _logger;

        public WorkItemsController(IWorkItems repository, ILogger<WorkItemsController> logger)
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
                        message = "No work items found.",
                        data = Array.Empty<WorkItems>() // return empty array for consistency
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
                        message = $"No work item found for ID: {id}.",
                        data = (WorkItems?)null
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

        [HttpPut("{id}/{isActive}")]
        [Authorize]
        public async Task<IActionResult> UpdateIsActive(Guid id, bool isActive)
        {
            try
            {
                // 1. Validate input parameters
                if (id == Guid.Empty)
                    return BadRequest(new { message = "Invalid id." });

                // 2. Get the current logged-in user from claims
                var modifiedBy = User?.Identity?.Name; // usually the username or email from the token

                if (string.IsNullOrWhiteSpace(modifiedBy))
                    return Unauthorized(new { message = "User information not found in token." });

                // 3. Call the repository method directly
                var rowsAffected = await _repository.UpdateIsActiveAsync(id, isActive, modifiedBy);

                // 4. Check result and return response
                if (rowsAffected == 0)
                    return NotFound(new { message = "Work item not found." });

                return Ok(new { message = "Status updated successfully.", rowsAffected });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching upadating IsActive status for ID: {Id}.", id);
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }


        }

    }
}
