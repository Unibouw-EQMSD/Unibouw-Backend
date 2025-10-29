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
    public class ProjectsController : ControllerBase
    {
        private readonly IProjects _repository;
        private readonly ILogger<ProjectsController> _logger;

        public ProjectsController(IProjects repository, ILogger<ProjectsController> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetAllProject()
        {
            try
            {
                var items = await _repository.GetAllProject();

                if (items == null || !items.Any())
                {
                    return NotFound(new
                    {
                        message = "No Projects found.",
                        data = Array.Empty<Project>() // return empty array for consistency
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
        public async Task<IActionResult> GetProjectById(Guid id)
        {
            try
            {
                var item = await _repository.GetProjectById(id);

                if (item == null)
                {
                    return NotFound(new
                    {
                        message = $"No Project found for ID: {id}.",
                        data = (Project?)null
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


    }
}
