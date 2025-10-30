﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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


        [HttpPut("{id}/{isActive}")]
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
        public async Task<IActionResult> CreateSubcontractorWithMappings([FromBody] Subcontractor subcontractor)
        {
            if (subcontractor == null)
                return BadRequest("Invalid subcontractor data.");

            try
            {
                var success = await _repository.CreateSubcontractorWithMappings(subcontractor);

                if (success)
                {
                    return Ok(new
                    {
                        Message = "Subcontractor and mappings created successfully.",
                        SubcontractorID = subcontractor.SubcontractorID
                    });
                }
                else
                {
                    return StatusCode(500, "Something went wrong while saving data.");
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Message = "An unexpected error occurred while processing the request.",
                    Error = ex.Message
                });
            }
        }

    }
}
