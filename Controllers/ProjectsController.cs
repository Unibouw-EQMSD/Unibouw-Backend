using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UnibouwAPI.Models;
using System;
using System.Threading.Tasks;
using UnibouwAPI.Repositories.Interfaces;
using UnibouwAPI.Repositories;
using System.Security.Claims;

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
                var user = HttpContext.User;

                var email = user.FindFirst("preferred_username")?.Value
                         ?? user.FindFirst("upn")?.Value
                         ?? user.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/upn")?.Value
                         ?? user.FindFirst("emails")?.Value
                         ?? user.FindFirst(ClaimTypes.Email)?.Value;

                var role = user.FindFirst(ClaimTypes.Role)?.Value
                        ?? user.FindFirst("roles")?.Value
                        ?? "User"; // default fallback if role missing

                if (email == null || role == null )
                    return Unauthorized("Unable to detect user email or role from token.");

                var items = await _repository.GetAllProject(email, role);

               // var items = await _repository.GetAllProject();

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
                var user = HttpContext.User;

                var email = user.FindFirst("preferred_username")?.Value
                         ?? user.FindFirst("upn")?.Value
                         ?? user.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/upn")?.Value
                         ?? user.FindFirst("emails")?.Value
                         ?? user.FindFirst(ClaimTypes.Email)?.Value;

                var role = user.FindFirst(ClaimTypes.Role)?.Value
                        ?? user.FindFirst("roles")?.Value
                        ?? "User"; // default fallback if role missing

                if (email == null || role == null)
                    return Unauthorized("Unable to detect user email or role from token.");

                var item = await _repository.GetProjectById(id, email, role);

                // var item = await _repository.GetProjectById(id);

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
