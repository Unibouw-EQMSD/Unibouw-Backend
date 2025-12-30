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
                return Conflict(new
                {
                    message = "A message with the same ID already exists."
                });
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new
                {
                    message = "Database error occurred.",
                    error = ex.Message
                });
            }
            catch (Exception ex)
            {
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
                return StatusCode(500, new
                {
                    message = "Database error occurred.",
                    error = ex.Message
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An unexpected error occurred.",
                    error = ex.Message
                });
            }
        }

        [HttpPost("AddLogConversation")]
        public async Task<IActionResult> AddLogConversation([FromBody] LogConversation logConversation)
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
                return Conflict(new
                {
                    message = "A log conversation with the same ID already exists."
                });
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new
                {
                    message = "Database error occurred.",
                    error = ex.Message
                });
            }
            catch (Exception ex)
            {
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
                    System.IO.File.Delete(filePath);
                }

                // Optional: log error
                // _logger.LogError(ex, "Error uploading attachment");

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    "An error occurred while uploading the attachment."
                );
            }
        }



    }
}
