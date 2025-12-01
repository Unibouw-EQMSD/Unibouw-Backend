using Microsoft.AspNetCore.Mvc;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories;
using UnibouwAPI.Repositories.Interfaces;

namespace UnibouwAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EmailController : ControllerBase
    {
        private readonly IEmail _emailRepository;
        private readonly ISubcontractors _subcontractorRepository;
        private readonly ILogger<EmailController> _logger;

        public EmailController(IEmail emailRepository, ISubcontractors subcontractorRepository, ILogger<EmailController> logger)
        {
            _emailRepository = emailRepository;
            _subcontractorRepository = subcontractorRepository;
            _logger = logger;
        }

        [HttpPost("send-rfq")]
        public async Task<IActionResult> SendRfqEmail([FromBody] EmailRequest request)
        {
            try
            {
                // Basic validation
                if (request == null)
                    return BadRequest(new { success = false, message = "Invalid request payload." });

                if (string.IsNullOrWhiteSpace(request.ToEmail))
                    return BadRequest(new { success = false, message = "Recipient email is required." });

                // Call service
                bool isSent = await _emailRepository.SendRfqEmailAsync(request);

                return isSent
                    ? Ok(new { success = true, message = "RFQ email sent successfully." })
                    : StatusCode(500, new { success = false, message = "Failed to send RFQ email." });
            }
            catch (Exception ex)
            {
                // You can log ex.Message here
                return StatusCode(500, new
                {
                    success = false,
                    message = "An unexpected error occurred while sending the RFQ email",
                    error = ex.Message  // Optional: remove this in production
                });
            }
        }

        [HttpPost("send-reminder")]
        public async Task<IActionResult> SendReminder([FromBody] ReminderRequest req)
        {
            try
            {
                if (req.SubcontractorId == null) return BadRequest();
                // Get subcontractor details
                var sub = await _subcontractorRepository.GetSubcontractorById(req.SubcontractorId);

                if (sub == null)
                    return BadRequest("Subcontractor not found.");

                var result = await _emailRepository.SendReminderEmailAsync(
                    sub.SubcontractorID,
                    sub.EmailID,
                    sub.Name,
                    req.RfqID
                );

                return Ok(new { success = result });
            }
            catch(Exception ex)
            {
                return StatusCode(500, new
                {
                    ex.Message,
                });
            }
        }


    }
}
