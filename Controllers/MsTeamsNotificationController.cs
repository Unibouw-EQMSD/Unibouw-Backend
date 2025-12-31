using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories.Interfaces;

namespace UnibouwAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MsTeamsNotificationController : ControllerBase
    {
        private readonly IMsTeamsNotification _teamsService;
        private readonly ILogger<MsTeamsNotificationController> _logger;

        public MsTeamsNotificationController(IMsTeamsNotification teamsService, ILogger<MsTeamsNotificationController> logger)
        {
            _teamsService = teamsService;
            _logger = logger;
        }

        // -----------------------------
        // Send Simple Message
        // -----------------------------
        [HttpPost("ms-teams-notification")]
        [Authorize]
        public async Task<IActionResult> SendMessage([FromBody] MsTeamsMessageDto dto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.Message))
                    return BadRequest("Message cannot be empty.");

                await _teamsService.SendTeamsMessageAsync(dto.Message);

                return Ok(new
                {
                    success = true,
                    message = "Message sent to Teams successfully."
                });
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Error while sending message to Teams");

                return StatusCode(502, new
                {
                    success = false,
                    error = "Failed to reach Microsoft Teams webhook."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while sending Teams message");

                return StatusCode(500, new
                {
                    success = false,
                    error = "An unexpected error occurred."
                });
            }
        }

        // -----------------------------
        // Send RFQ Notification
        // -----------------------------
        [HttpPost("rfq")]
        [Authorize]
        public async Task<IActionResult> SendRfqNotification()
        {
            try
            {
                await _teamsService.SendRfqTeamsNotificationAsync(
                    rfqId: "RFQ-1023",
                    client: "UniBouw",
                    status: "Submitted"
                );

                return Ok(new
                {
                    success = true,
                    message = "RFQ notification sent to Teams."
                });
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Error while sending RFQ notification to Teams");

                return StatusCode(502, new
                {
                    success = false,
                    error = "Microsoft Teams webhook is not reachable."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while sending RFQ notification");

                return StatusCode(500, new
                {
                    success = false,
                    error = "An unexpected error occurred."
                });
            }
        }
    }
}