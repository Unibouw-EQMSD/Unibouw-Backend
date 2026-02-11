using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories.Interfaces;
using System.Net;
using System.Text.RegularExpressions;
using UnibouwAPI.Helpers;
using UnibouwAPI.Repositories;


namespace UnibouwAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RfqController : ControllerBase
    {
        private readonly IRfq _repository;
        private readonly IEmail _emailRepository;
        private readonly IRFQConversationMessage _conversationRepo;
        private readonly ILogger<RfqController> _logger;
        DateTime amsterdamNow = DateTimeConvert.ToAmsterdamTime(DateTime.UtcNow);


        public RfqController(IRfq repository,IEmail emailRepository, IRFQConversationMessage conversationRepo, ILogger<RfqController> logger)
        {
            _repository = repository;
            _emailRepository = emailRepository;
            _conversationRepo = conversationRepo;
            _logger = logger;
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetAllRfq()
        {
            try
            {
                var items = await _repository.GetAllRfq();

                if (items == null || !items.Any())
                {
                    return NotFound(new
                    {
                        message = "No RFQ found.",
                        data = Array.Empty<Rfq>() // return empty array for consistency
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
        public async Task<IActionResult> GetRfqById(Guid id)
        {
            try
            {
                var item = await _repository.GetRfqById(id);

                if (item == null)
                {
                    return NotFound(new
                    {
                        message = $"No RFQ found for ID: {id}.",
                        data = (Rfq?)null
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


        [HttpGet("byProject/{projectId}")]
        [Authorize]
        public async Task<IActionResult> GetRfqByProjectId(Guid projectId)
        {
            try
            {
                // ⭐ CHANGE: Use GetAllRfqByProjectId to fetch ALL RFQs for the project
                var items = await _repository.GetRfqByProjectId(projectId);

                if (items == null || !items.Any())
                {
                    return NotFound(new
                    {
                        message = $"No RFQ found for Project ID: {projectId}.",
                        // ⭐ Return an empty array here for consistency
                        data = Array.Empty<Rfq>()
                    });
                }

                // ⭐ Return the collection of items
                return Ok(new
                {
                    data = items
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching RFQ for Project ID: {ProjectId}.", projectId);
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        [HttpPost("create-simple")]
        [Authorize]
        public async Task<IActionResult> CreateRfqSimple([FromBody] Rfq rfq, [FromQuery] List<Guid> subcontractorIds, [FromQuery] List<Guid> workItems, [FromQuery] bool sendEmail = true)
        {
            try
            {
                var userEmail = User.Identity?.Name;
                if (string.IsNullOrWhiteSpace(userEmail))
                    return Unauthorized(new { message = "Unable to determine logged-in user email." });

                if (rfq == null)
                    return BadRequest(new { message = "RFQ data is required." });

                if (subcontractorIds == null || !subcontractorIds.Any())
                    return BadRequest(new { message = "At least one subcontractor is required." });

                if (rfq.SubcontractorDueDates == null || !rfq.SubcontractorDueDates.Any())
                    return BadRequest(new { message = "Subcontractor due dates are required." });

                if (!rfq.GlobalDueDate.HasValue)
                {
                    return BadRequest(new { message = "Global due date is required." });
                }

                // 🔹 RFQ dates come ONLY from GlobalDueDate
                rfq.DueDate = rfq.GlobalDueDate.Value.Date;

                rfq.RfqSent = sendEmail ? 1 : 0;
                rfq.Status = sendEmail ? "Sent" : "Draft";
                rfq.CreatedBy = userEmail;
                rfq.CreatedOn = amsterdamNow;

                // 1️⃣ Create RFQ
                var rfqId = await _repository.CreateRfqAsync(rfq, subcontractorIds);

                // 2️⃣ Insert work items
                if (workItems?.Any() == true)
                {
                    await _repository.InsertRfqWorkItemsAsync(rfqId, workItems);
                }

                // 3️⃣ Save subcontractor due dates (INDIVIDUAL — DO NOT TOUCH)
                foreach (var sub in rfq.SubcontractorDueDates)
                {
                    if (sub.DueDate == null)
                        continue;

                    await _repository.SaveRfqSubcontractorDueDateAsync(
                        rfqId,
                        sub.SubcontractorID,
                        sub.DueDate.Value.Date
                    );
                }

                var createdRfq = await _repository.GetRfqById(rfqId);

                // 4️⃣ Send emails
                if (sendEmail)
                {
                    var emailRequest = new EmailRequest
                    {
                        RfqID = rfqId,
                        SubcontractorIDs = subcontractorIds,
                        WorkItems = workItems ?? new List<Guid>(),
                        Subject = "RFQ Invitation - Unibouw",
                        Body = rfq.CustomNote
                    };

                    var sentEmails = await _emailRepository.SendRfqEmailAsync(emailRequest);

                    foreach (var email in sentEmails)
                    {
                        await _conversationRepo.AddRFQConversationMessageAsync(
                            new RFQConversationMessage
                            {
                                ProjectID = rfq.ProjectID!.Value,
                                //RfqID = rfqId,
                                SubcontractorID = email.SubcontractorIDs.First(),
                                SenderType = "PM",
                                MessageText = HtmlToPlainText(email.Body),
                                MessageDateTime = amsterdamNow,
                                CreatedBy = userEmail
                            }
                        );
                    }
                }

                return Ok(new
                {
                    message = "RFQ processed successfully.",
                    data = createdRfq
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while processing the RFQ.");
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "An error occurred while processing the RFQ.",
                    error = ex.Message
                });
            }
        }
        private static string HtmlToPlainText(string html)
            {
                if (string.IsNullOrWhiteSpace(html))
                    return string.Empty;

                // 🔴 REMOVE <a>...</a> completely
                html = Regex.Replace(
                    html,
                    @"<a\b[^>]*>.*?</a>",
                    "",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline
                );

                // Convert block-level tags to line breaks
                html = Regex.Replace(html, @"<(br|BR)\s*/?>", "\n");
                html = Regex.Replace(html, @"</p>|</div>|</li>|</ul>|</ol>", "\n");

                // Remove remaining HTML tags
                html = Regex.Replace(html, "<.*?>", string.Empty);

                // Decode HTML entities
                html = WebUtility.HtmlDecode(html);

                // Normalize spaces & newlines
                html = Regex.Replace(html, @"[ \t]+", " ");
                html = Regex.Replace(html, @"\n\s*", "\n");
                html = Regex.Replace(html, @"\n{3,}", "\n\n");

                return html.Trim();
            }

        [HttpPost("update")]
        [Authorize]
        public async Task<IActionResult> UpdateRfqPost([FromQuery] Guid rfqId, [FromBody] Rfq rfq, [FromQuery] List<Guid> subcontractorIds, [FromQuery] List<Guid> workItems, [FromQuery] bool sendEmail = false)
        {
            try
            {
                if (rfq == null || rfqId == Guid.Empty)
                    return BadRequest(new { message = "RFQ data and ID are required." });

                rfq.RfqID = rfqId;
                rfq.RfqSent = sendEmail ? 1 : rfq.RfqSent;
                rfq.Status = sendEmail ? "Sent" : rfq.Status;

                // ✅ 1️⃣ Update MAIN RFQ (DO NOT touch DueDate here)
                var rfqUpdated = await _repository.UpdateRfqMainAsync(rfq);
                if (!rfqUpdated)
                    return NotFound(new { message = $"RFQ with ID {rfqId} not found." });

                // ✅ 2️⃣ Update WorkItems
                if (workItems?.Any() == true)
                    await _repository.UpdateRfqWorkItemsAsync(rfqId, workItems);

                // ✅ 3️⃣ Update ONLY per-subcontractor due dates
                if (rfq.SubcontractorDueDates?.Any() == true)
                {
                    foreach (var sub in rfq.SubcontractorDueDates)
                    {
                        if (!sub.DueDate.HasValue)
                            continue;

                        await _repository.UpdateSubcontractorDueDateAsync(
                            rfqId,
                            sub.SubcontractorID,
                            sub.DueDate.Value
                        );
                    }
                }

                // ✅ 4️⃣ Send email
                if (sendEmail)
                {
                    await _emailRepository.SendRfqEmailAsync(new EmailRequest
                    {
                        RfqID = rfqId,
                        SubcontractorIDs = subcontractorIds ?? new(),
                        WorkItems = workItems ?? new(),
                        Subject = "RFQ Invitation - Unibouw",
                        Body = ""
                    });
                }

                return Ok(new
                {
                    message = sendEmail
                        ? "RFQ updated successfully and emails sent."
                        : "RFQ updated successfully."
                });
            }
            catch (Exception ex)
            {
                // Log exception here if you have logging (ILogger)
                 _logger.LogError(ex, "Error updating RFQ {RfqId}", rfqId);

                return StatusCode(500, new
                {
                    message = "An error occurred while updating the RFQ.",
                    error = ex.Message
                });
            }
        }

        [HttpPost("save-subcontractor-workitem-mapping")]
        [Authorize]
        public async Task<IActionResult> SaveSubcontractorWorkItemMapping([FromQuery] Guid workItemId, [FromQuery] Guid subcontractorId, [FromQuery] Guid rfqId, [FromQuery] DateTime? dueDate)
        {
            try
            {
                // Only these two are mandatory
                if (subcontractorId == Guid.Empty || workItemId == Guid.Empty)
                    return BadRequest(new
                    {
                        message = "SubcontractorID and WorkItemID are required."
                    });

                // 1️⃣ Save subcontractor–work item mapping (ALWAYS)
                await _repository.SaveSubcontractorWorkItemMappingAsync(
                    subcontractorId,
                    workItemId,
                    User?.Identity?.Name ?? "System"
                );

                // 2️⃣ Update RFQ–subcontractor due date (ONLY IF RFQ EXISTS)
                if (rfqId != Guid.Empty && dueDate.HasValue)
                {
                    var success = await _repository.UpdateRfqSubcontractorDueDateAsync(
                        rfqId,
                        subcontractorId,
                        dueDate.Value
                    );

                    if (!success)
                        return NotFound(new
                        {
                            message = "RfqSubcontractorMapping update failed."
                        });
                }

                return Ok(new
                {
                    message = "Subcontractor–WorkItem mapping saved successfully."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving subcontractor-workitem mapping");

                return StatusCode(500, new
                {
                    message = "An error occurred while saving subcontractor–work item mapping.",
                    error = ex.Message
                });
            }
        }

        [HttpPost("save-or-update-rfq-subcontractor-mapping")]
        [Authorize]
        public async Task<IActionResult> SaveOrUpdateRfqSubcontractorMapping([FromQuery] Guid rfqId, [FromQuery] Guid subcontractorId, [FromQuery] Guid workItemId, [FromQuery] DateTime dueDate)
        {
            try
            {
                if (rfqId == Guid.Empty || subcontractorId == Guid.Empty || workItemId == Guid.Empty)
                    return BadRequest(new
                    {
                        message = "RFQID, SubcontractorID, and WorkItemID are required."
                    });

                var user = User?.Identity?.Name ?? "System";

                var success = await _repository.SaveOrUpdateRfqSubcontractorMappingAsync(
                    rfqId,
                    subcontractorId,
                    workItemId,
                    dueDate,
                    user
                );

                if (!success)
                    return NotFound(new
                    {
                        message = "SaveOrUpdate operation failed."
                    });

                return Ok(new
                {
                    message = "RFQ–Subcontractor mapping saved successfully."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving RFQ-subcontractor mapping");

                return StatusCode(500, new
                {
                    message = "An error occurred while saving RFQ–Subcontractor mapping.",
                    error = ex.Message
                });
            }
        }

        [HttpGet("{rfqId}/workitems")]
        public async Task<IActionResult> GetRfqWorkItems(Guid rfqId)
        {
            var workItems = await _repository.GetRfqWorkItemsAsync(rfqId);
            return Ok(new { data = workItems }); // ✅ Matches frontend { data: WorkItem[] }
        }

        [HttpGet("{rfqId}/subcontractor-duedates")]
        [Authorize]
        public async Task<IActionResult> GetRfqSubcontractorDueDates(Guid rfqId)
        {
            try
            {
                var dates = await _repository.GetRfqSubcontractorDueDatesAsync(rfqId);
                return Ok(dates);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching subcontractor due dates for RFQ {RfqId}", rfqId);
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        [HttpGet("{rfqId}/workitem-info")]
        [Authorize]
        public async Task<IActionResult> GetWorkItemInfo(Guid rfqId)
        {
            try
            {
                var result = await _repository.GetWorkItemInfoByRfqId(rfqId);

                return Ok(new
                {
                    workItem = result.WorkItemNames,
                    subcontractorCount = result.SubCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while fetching WorkItem info for RFQ ID: {RfqId}", rfqId);

                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "An error occurred while retrieving work item information."
                });
            }
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("delete/{id:guid}")]
        public async Task<IActionResult> DeleteRfq(Guid id)
        {
            try
            {
                var deletedBy = User?.Identity?.Name ?? "System";

                var result = await _repository.DeleteRfqAsync(id, deletedBy);

                if (result == null)
                    return NotFound(new { message = "RFQ no longer exists." });

                if (result == false)
                    return BadRequest(new
                    {
                        message = "This RFQ cannot be deleted because one or more subcontractors have submitted a quote."
                    });

                return Ok(new { message = "RFQ deleted successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while deleting RFQ with ID: {RfqId}. DeletedBy: {DeletedBy}", id, User?.Identity?.Name ?? "System");

                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "An error occurred while deleting the RFQ."
                });
            }

        }

    }
}
