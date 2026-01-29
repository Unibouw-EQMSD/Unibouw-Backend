using Microsoft.AspNetCore.Mvc;
using System.Net;
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
                var sentEmails = await _emailRepository.SendRfqEmailAsync(request);

                if (sentEmails == null || !sentEmails.Any())
                    return StatusCode(500, new { success = false, message = "Failed to send RFQ email." });

                return Ok(new
                {
                    success = true,
                    message = "RFQ email sent successfully.",
                    sentCount = sentEmails.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,"Unexpected error occurred while sending the RFQ email.");
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
                    sub.Email,
                    sub.Name,
                    req.RfqID,
                    req.EmailBody
                );

                return Ok(new { success = result });
            }
            catch(Exception ex)
            {
                _logger.LogError(ex,"Unhandled exception occurred.");
                return StatusCode(500, new
                {
                    ex.Message,
                });
            }
        }

        [HttpPost("send-mail")]
        public async Task<IActionResult> SendMail([FromBody] SendMailRequest req)
        {
            try
            {
                if (req.SubcontractorID == Guid.Empty)
                    return BadRequest("SubcontractorID is required.");

                if (string.IsNullOrWhiteSpace(req.Subject))
                    return BadRequest("Subject is required.");

                if (string.IsNullOrWhiteSpace(req.Body))
                    return BadRequest("Body is required.");

                // ✅ Fetch subcontractor details
                var sub = await _subcontractorRepository
                    .GetSubcontractorById(req.SubcontractorID);

                if (sub == null)
                    return BadRequest("Subcontractor not found.");

                // ✅ Send mail using fetched values
                var result = await _emailRepository.SendMailAsync(
                    toEmail: sub.Email,
                    subject: req.Subject,
                    body: req.Body,
                    name: sub.Name,
                    projectId: req.ProjectID,
                    attachmentFilePaths: req.AttachmentFilePaths
                );

                return Ok(new { success = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,"Unexpected error occurred while sending the email.");
                return StatusCode(500, new
                {
                    success = false,
                    message = "An unexpected error occurred while sending the email",
                    error = ex.Message // remove in prod if needed
                });
            }
        }

    }
}
