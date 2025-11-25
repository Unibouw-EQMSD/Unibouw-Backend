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
                var item = await _repository.GetRfqByProjectId(projectId);

                if (item == null)
                {
                    return NotFound(new
                    {
                        message = $"No RFQ found for Project ID: {projectId}.",
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
                _logger.LogError(ex, "An error occurred while fetching RFQ for Project ID: {ProjectId}.", projectId);
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        [HttpPost("create-simple")]
        [Authorize]
        public async Task<IActionResult> CreateRfqSimple([FromBody] Rfq rfq, [FromQuery] List<Guid> subcontractorIds, [FromQuery] List<Guid> workItems)
        {
            try
            {
                if (rfq == null || subcontractorIds == null || !subcontractorIds.Any())
                    return BadRequest(new { message = "RFQ data and subcontractor IDs are required." });

                // 1️⃣ Create RFQ
                var rfqId = await _repository.CreateRfqAsync(rfq);

                // 2️⃣ Insert RFQ → WorkItem mappings
                if (workItems != null && workItems.Any())
                    await _repository.InsertRfqWorkItemsAsync(rfqId, workItems);

                // 3️⃣ Prepare email request
                var emailRequest = new EmailRequest
                {
                    RfqID = rfqId,
                    SubcontractorIDs = subcontractorIds,
                    WorkItems = workItems ?? new List<Guid>(),
                    Subject = rfq.CustomerNote ?? "RFQ Invitation - Unibouw"
                };

                await _emailRepository.SendRfqEmailAsync(emailRequest);

                // 4️⃣ Return created RFQ
                var createdRfq = await _repository.GetRfqById(rfqId);
                return Ok(new
                {
                    message = "RFQ created successfully and emails sent to subcontractors.",
                    data = createdRfq
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating RFQ");
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
                workItem = result.WorkItemName,
                subcontractorCount = result.SubCount
            });
        }


    }
}
