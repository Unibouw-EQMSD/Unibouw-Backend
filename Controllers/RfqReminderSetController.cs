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
    public class RfqReminderSetController : ControllerBase
    {
        private readonly IRfqReminderSet _repository;
        private readonly ILogger<RfqReminderSetController> _logger;

        public RfqReminderSetController(IRfqReminderSet repository, ILogger<RfqReminderSetController> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetAllRfqReminderSet()
        {
            try
            {
                var items = await _repository.GetAllRfqReminderSet();

                if (items == null || !items.Any())
                {
                    return NotFound(new
                    {
                        message = "No items found.",
                        data = Array.Empty<IRfqReminderSet>() // return empty array for consistency
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
                _logger.LogError(ex, "An error occurred while fetching Rfq Reminder Set items.");
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        [HttpPost("CreateRfqReminderSet")]
        [Authorize]
        public async Task<IActionResult> CreateRfqReminderSet([FromBody] RfqReminderSet model)
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

                await _repository.CreateOrUpdateRfqReminderSet(
                    model,
                    reminderDateTimes,
                    userEmail
                );

                return Ok(new { Message = "Reminder set saved successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while creating RfqReminderSet(s)");
                return StatusCode(500, new
                {
                    Message = "An unexpected error occurred while processing the request.",
                    Error = ex.Message
                });
            }
        }

    }
}
