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
    public class RfqReminderController : ControllerBase
    {
        private readonly IRfqReminder _repository;
        private readonly ILogger<RfqReminderController> _logger;

        public RfqReminderController(IRfqReminder repository, ILogger<RfqReminderController> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetAllRfqReminder()
        {
            try
            {
                var items = await _repository.GetAllRfqReminder();

                if (items == null || !items.Any())
                {
                    return NotFound(new
                    {
                        message = "No items found.",
                        data = Array.Empty<IRfqReminder>() // return empty array for consistency
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
                _logger.LogError(ex, "An error occurred while fetching Rfq Reminder items.");
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        [HttpPost("CreateRfqReminder")]
        [Authorize]
        public async Task<IActionResult> CreateRfqReminder([FromBody] RfqReminder model)
        {
            if (model?.ReminderDates == null || string.IsNullOrWhiteSpace(model.ReminderTime))
                return BadRequest("ReminderDates and ReminderTime are required.");

            try
            {
                var userEmail =
                    HttpContext.User.FindFirst("preferred_username")?.Value ??
                    HttpContext.User.FindFirst(ClaimTypes.Email)?.Value ??
                    HttpContext.User.Identity?.Name ??
                    "System";

                var reminderDateTimes = new List<DateTime>();

                foreach (var dateString in model.ReminderDates)
                {
                    if (DateTime.TryParse(dateString, out var date) &&
                        TimeSpan.TryParse(model.ReminderTime, out var time))
                    {
                        reminderDateTimes.Add(date.Date.Add(time));
                    }
                }

                if (!reminderDateTimes.Any())
                    return BadRequest("No valid reminder dates.");

                await _repository.CreateOrUpdateRfqReminder(
                    model,
                    reminderDateTimes,
                    userEmail
                );

                return Ok(new { Message = "Reminder saved successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while creating RfqReminder(s)");
                return StatusCode(500, new
                {
                    Message = "An unexpected error occurred while processing the request.",
                    Error = ex.Message
                });
            }
        }



        [HttpPost("GenerateAutoSchedulesFromGlobalConfig")]
        [Authorize]
        public async Task<IActionResult> GenerateAutoSchedulesFromGlobalConfig()
        {
            try
            {
                var userEmail =
                    HttpContext.User.FindFirst("preferred_username")?.Value ??
                    HttpContext.User.FindFirst(ClaimTypes.Email)?.Value ??
                    HttpContext.User.Identity?.Name ??
                    "System";

                var result = await _repository.GenerateAutoSchedulesFromGlobalConfigAsync(userEmail);

                return Ok(new
                {
                    message = "Auto reminder schedules generated.",
                    totalEligible = result.TotalEligible,
                    totalSchedulesCreatedOrReset = result.TotalSchedulesCreatedOrReset
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating auto reminder schedules from global config.");
                return StatusCode(500, new { message = "Failed to generate auto schedules.", error = ex.Message });
            }
        }
    }
}
