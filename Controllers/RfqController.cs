using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories.Interfaces;
using System.Net;
using System.Text.RegularExpressions;


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
        public async Task<IActionResult> CreateRfqSimple([FromBody] Rfq rfq,[FromQuery] List<Guid> subcontractorIds,[FromQuery] List<Guid> workItems,[FromQuery] bool sendEmail = true)
        {
            try
            {
                // Get logged-in user email from token
                var userEmail = User.Identity.Name;

                if (string.IsNullOrWhiteSpace(userEmail))
                    return Unauthorized(new { message = "Unable to determine logged-in user email." });

                // get email to assign CreatedBy
                var PMEmail = userEmail;

                if (rfq == null || subcontractorIds == null || !subcontractorIds.Any())
                    return BadRequest(new { message = "RFQ data and subcontractor IDs are required." });

                rfq.DeadLine = rfq.DueDate;
                rfq.RfqSent = sendEmail ? 1 : 0;
                rfq.Status = sendEmail ? "Sent" : "Draft";

                // 1️⃣ Create RFQ
                var rfqId = await _repository.CreateRfqAsync(rfq);

                // 2️⃣ Insert work items
                if (workItems != null && workItems.Any())
                    await _repository.InsertRfqWorkItemsAsync(rfqId, workItems);

                // 3️⃣ Get created RFQ (needed for ProjectID, CreatedBy)
                var createdRfq = await _repository.GetRfqById(rfqId);

                // 4️⃣ Send email + save conversation
                if (sendEmail)
                {
                    var emailRequest = new EmailRequest
                    {
                        RfqID = rfqId,
                        SubcontractorIDs = subcontractorIds,
                        WorkItems = workItems ?? new List<Guid>(),
                        Subject = rfq.CustomerNote ?? "RFQ Invitation - Unibouw",
                        Body = rfq.CustomerNote
                    };

                    var sentEmails = await _emailRepository.SendRfqEmailAsync(emailRequest);

                    foreach (var email in sentEmails)
                    {
                        var conversation = new RFQConversationMessage
                        {
                            ProjectID = rfq.ProjectID.Value,
                            RfqID = rfqId,
                            SubcontractorID = email.SubcontractorIDs.First(),
                            // ProjectManagerID = Guid.Parse("34522246-1C79-4A2A-83FF-06283E1DD82D"), // adjust if different type
                            SenderType = "PM",
                            MessageText = HtmlToPlainText(email.Body),
                            MessageDateTime = DateTime.UtcNow,
                            CreatedBy = PMEmail

                        };

                        await _conversationRepo.AddRFQConversationMessageAsync(conversation);
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
                _logger.LogError(ex, "An error occurred.",ex);
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
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




    [HttpPut("{rfqId}")]
        [Authorize]
        public async Task<IActionResult> UpdateRfq(
      Guid rfqId,
      [FromBody] Rfq rfq,
      [FromQuery] List<Guid> subcontractorIds,
      [FromQuery] List<Guid> workItems,
      [FromQuery] bool sendEmail = false)
        {
            if (rfq == null || rfqId == Guid.Empty)
                return BadRequest(new { message = "RFQ data and ID are required." });

            if (rfqId != rfq.RfqID)
                return BadRequest(new { message = "Mismatched RFQ ID." });

            // Match CreateRfqSimple logic
            rfq.DeadLine = rfq.DueDate;
            rfq.RfqSent = sendEmail ? 1 : rfq.RfqSent;
            rfq.Status = sendEmail ? "Sent" : rfq.Status;

            // 1️⃣ Update RFQ
            var updateSuccess = await _repository.UpdateRfqAsync(rfq);
            if (!updateSuccess)
                return NotFound(new { message = $"RFQ with ID {rfqId} not found or update failed." });

            // 2️⃣ Update WorkItems
            if (workItems != null && workItems.Any())
                await _repository.UpdateRfqWorkItemsAsync(rfqId, workItems);

            // 3️⃣ Update Subcontractors
            if (subcontractorIds != null && subcontractorIds.Any())
                await _repository.UpdateRfqSubcontractorsAsync(rfqId, subcontractorIds);

            // 4️⃣ Email sending (same pattern as CreateRfqSimple)
            if (sendEmail)
            {
                var emailRequest = new EmailRequest
                {
                    RfqID = rfqId,
                    SubcontractorIDs = subcontractorIds ?? new List<Guid>(),
                    WorkItems = workItems ?? new List<Guid>(),
                    Subject = rfq.CustomerNote ?? "RFQ Invitation - Unibouw",
                    Body = rfq.CustomerNote
                };

                await _emailRepository.SendRfqEmailAsync(emailRequest);
            }

            var updatedRfq = await _repository.GetRfqById(rfqId);

            return Ok(new
            {
                message = sendEmail
                    ? "RFQ updated successfully and emails sent."
                    : "RFQ updated successfully.",
                data = updatedRfq
            });
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
            var result = await _repository.GetWorkItemInfoByRfqId(rfqId);

            return Ok(new
            {
                workItem = result.WorkItemNames,
                subcontractorCount = result.SubCount
            });
        }

        [HttpPost("delete/{id:guid}")]
        public async Task<IActionResult> DeleteRfq(Guid id)
        {
            // 🔒 Role check (System Admin only)
            if (!User.IsInRole("Admin"))
            {
                return Forbid("You do not have permission to delete this RFQ.");
            }

            var deletedBy = User?.Identity?.Name ?? "System";

            var result = await _repository.DeleteRfqAsync(id, deletedBy);

            if (result == null)
            {
                return NotFound(new
                {
                    message = "RFQ no longer exists."
                });
            }

            if (result == false)
            {
                return BadRequest(new
                {
                    message = "This RFQ cannot be deleted because one or more subcontractors have submitted a quote."
                });
            }

            return Ok(new
            {
                message = "RFQ deleted successfully."
            });
        }

    }
}
