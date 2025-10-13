using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UnibouwAPI.Models;
using System;
using System.Threading.Tasks;
using UnibouwAPI.Repositories;
using UnibouwAPI.Repositories.Interfaces;

namespace UnibouwAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WorkItemCategoryTypeController : ControllerBase
    {
        private readonly IWorkItemCategoryType _repository;
        private readonly ILogger<WorkItemCategoryTypeController> _logger;

        public WorkItemCategoryTypeController(IWorkItemCategoryType repository, ILogger<WorkItemCategoryTypeController> logger)
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
                        message = "No work items category type found.",
                        data = Array.Empty<WorkItemCategoryType>() // return empty array for consistency
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
                        message = $"No work item category type found for ID: {id}.",
                        data = (WorkItemCategoryType?)null
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

    }
}
