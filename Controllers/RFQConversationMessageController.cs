using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Security.Claims;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories.Interfaces;

namespace UnibouwAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RFQConversationMessageController : ControllerBase
    {
        private readonly IRFQConversationMessage _repository;
        private readonly ILogger<RFQConversationMessageController> _logger;

        public RFQConversationMessageController(IRFQConversationMessage repository, ILogger<RFQConversationMessageController> logger)
        {
            _repository = repository;
            _logger = logger;
        }


        [HttpPost("AddRFQConversationMessage")]
        public async Task<IActionResult> AddRFQConversationMessage([FromBody] RFQConversationMessage message)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);        

            try
            {
                var insertedMessage = await _repository.AddRFQConversationMessageAsync(message);

                return Ok(new
                {
                    message = "RFQ conversation message created successfully.",
                    data = insertedMessage
                });
            }
            catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
            {
                _logger.LogWarning(ex, "Attempted to insert a duplicate RFQ conversation message with ID: {MessageId}", message.ConversationMessageID);

                return Conflict(new
                {
                    message = "A message with the same ID already exists."
                });
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Database error occurred while adding RFQ conversation message with ID: {MessageId}", message.ConversationMessageID);

                return StatusCode(500, new
                {
                    message = "Database error occurred.",
                    error = ex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error occurred while adding RFQ conversation message with ID: {MessageId}", message.ConversationMessageID);

                return StatusCode(500, new
                {
                    message = "An unexpected error occurred.",
                    error = ex.Message
                });
            }
        }

        [HttpGet("GetConvoByProjectAndSubcontractor")]
        public async Task<IActionResult> GetConvoByProjectAndSubcontractor([FromQuery] Guid projectId,[FromQuery] Guid subcontractorId)
        {
            if (projectId == Guid.Empty || subcontractorId == Guid.Empty)
                return BadRequest(new { message = "ProjectID and SubcontractorID are required." });

            try
            {
                var messages = await _repository
                    .GetMessagesByProjectAndSubcontractorAsync(projectId, subcontractorId);

                return Ok(new
                {
                    count = messages.Count(),
                    data = messages
                });
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Database error occurred while processing RFQ conversation message");

                return StatusCode(500, new
                {
                    message = "Database error occurred.",
                    error = ex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error occurred while processing RFQ conversation message.");

                return StatusCode(500, new
                {
                    message = "An unexpected error occurred.",
                    error = ex.Message
                });
            }
        }

        [HttpPost("AddLogConversation")]
        public async Task<IActionResult> AddLogConversation([FromBody] RfqLogConversation logConversation)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var result = await _repository.AddLogConversationAsync(logConversation);

                return Ok(new
                {
                    message = "Log conversation created successfully.",
                    data = result
                });
            }
            catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
            {
                _logger.LogWarning(ex, "Database error occurred while processing RFQ conversation message");

                return Conflict(new
                {
                    message = "A log conversation with the same ID already exists."
                });
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Database error occurred while processing RFQ conversation message");

                return StatusCode(500, new
                {
                    message = "Database error occurred.",
                    error = ex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database error occurred while processing RFQ conversation message");

                return StatusCode(500, new
                {
                    message = "An unexpected error occurred.",
                    error = ex.Message
                });
            }
        }

        [HttpGet("GetLogConversationsByConversationMessageId/{conversationMessageId}")]
        public async Task<IActionResult> GetLogConversationsByConversationMessageId(Guid conversationMessageId)
        {
            if (conversationMessageId == Guid.Empty)
                return BadRequest(new { message = "Invalid ConversationMessageID." });

            try
            {
                var logs = await _repository.GetLogConversationsByProjectIdAsync(conversationMessageId);

                return Ok(new
                {
                    message = "Log conversations fetched successfully.",
                    data = logs
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch log conversations.");

                return StatusCode(500, new
                {
                    message = "Failed to fetch log conversations.",
                    error = ex.Message
                });
            }
        }

        [HttpGet("conversation")]
        public async Task<IActionResult> GetConversation(Guid projectId,Guid rfqId,Guid subcontractorId)
        {
            try
            {
                var data = await _repository.GetConversationAsync(
                projectId, rfqId, subcontractorId
            );

                return Ok(data);
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch conversations.");

                return StatusCode(500, new
                {
                    message = "Failed to fetch conversations.",
                    error = ex.Message
                });
            }
            
        }

        // Upload Attachment
        [HttpPost("AddAttachmentAsync/{conversationMessageId}")]
        public async Task<IActionResult> AddAttachmentAsync(Guid conversationMessageId, IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("File is required.");

            string? filePath = null;

            try
            {
                var uploadsFolder = Path.Combine("Uploads", "RFQ");
                Directory.CreateDirectory(uploadsFolder);

                //var storedFileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
                //filePath = Path.Combine(uploadsFolder, storedFileName);
                // ORIGINAL FILE NAME
                var originalFileName = Path.GetFileName(file.FileName);
                filePath = Path.Combine(uploadsFolder, originalFileName);
                

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var attachment = new RFQConversationMessageAttachment
                {
                    ConversationMessageID = conversationMessageId,
                    FileName = file.FileName,
                    FileExtension = Path.GetExtension(file.FileName),
                    FileSize = file.Length,
                    FilePath = filePath,
                    UploadedBy = null // extract from JWT if required
                };

                var result = await _repository.AddAttachmentAsync(attachment);
                return Ok(result);
            }
            catch (Exception ex)
            {
                // Rollback file if DB save fails
                if (!string.IsNullOrEmpty(filePath) && System.IO.File.Exists(filePath))
                {
                    try
                    {
                        System.IO.File.Delete(filePath);
                        _logger.LogInformation("Rolled back file deletion: {FilePath}", filePath);
                    }
                    catch (Exception fileEx)
                    {
                        _logger.LogWarning(fileEx, "Failed to delete file during rollback: {FilePath}", filePath);
                    }
                }

                _logger.LogError(ex, "Error uploading attachment");

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    "An error occurred while uploading the attachment."
                );
            }

        }
        [HttpPost("Reply")]
        [Authorize]
        public async Task<IActionResult> Reply(
          [FromForm(Name = "subcontractorMessageID")] Guid SubcontractorMessageID,
          [FromForm] string message,
          [FromForm] string subject,
          [FromForm(Name = "attachments")] List<IFormFile>? files)
        {
            try
            {
                var pmEmail = User.Identity?.Name;
                if (string.IsNullOrWhiteSpace(pmEmail))
                    return BadRequest("PM email could not be captured. Please log in again.");

                var reply = await _repository.ReplyToConversationAsync(
                  SubcontractorMessageID, message, subject, pmEmail, files
                );

                if (reply.Status == "Draft")
                    return StatusCode(500, "Email failed. Reply saved as draft.");

                return Ok(reply);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred.");
                return StatusCode(500, ex.Message);
            }
        }
    }
}
