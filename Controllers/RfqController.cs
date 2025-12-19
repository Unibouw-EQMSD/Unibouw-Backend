using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UnibouwAPI.Models;
using System;
using System.Threading.Tasks;
using UnibouwAPI.Repositories.Interfaces;
using UnibouwAPI.Repositories;

namespace UnibouwAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RfqController : ControllerBase
    {
        private readonly IRfq _repository;
        private readonly IEmail _emailRepository;
        private readonly ILogger<RfqController> _logger;

        public RfqController(IRfq repository,IEmail emailRepository, ILogger<RfqController> logger)
        {
            _repository = repository;
            _emailRepository = emailRepository;
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
        public async Task<IActionResult> CreateRfqSimple(
     [FromBody] Rfq rfq,
     [FromQuery] List<Guid> subcontractorIds,
     [FromQuery] List<Guid> workItems,
     [FromQuery] bool sendEmail = true)
        {
            if (rfq == null || subcontractorIds == null || !subcontractorIds.Any())
                return BadRequest(new { message = "RFQ data and subcontractor IDs are required." });

            rfq.DeadLine = rfq.DueDate;
            rfq.RfqSent = sendEmail ? 1 : 0;
            rfq.Status = sendEmail ? "Sent" : "Draft";

            var rfqId = await _repository.CreateRfqAsync(rfq);

            if (workItems != null && workItems.Any())
                await _repository.InsertRfqWorkItemsAsync(rfqId, workItems);

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
                await _emailRepository.SendRfqEmailAsync(emailRequest);
            }

            var createdRfq = await _repository.GetRfqById(rfqId);
            return Ok(new { message = "RFQ processed successfully.", data = createdRfq });
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


    }
}
