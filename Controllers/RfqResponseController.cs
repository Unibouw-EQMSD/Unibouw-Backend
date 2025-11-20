using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Data;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories;
using UnibouwAPI.Repositories.Interfaces;

namespace UnibouwAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RfqResponseController : ControllerBase
    {
        private readonly IRfqResponse _repository;
        private readonly ILogger<RfqResponseController> _logger;

        public RfqResponseController(IRfqResponse repository, ILogger<RfqResponseController> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        //---------RfqResponseDocuments
        [HttpGet("GetAllRfqResponseDocuments")]
        [Authorize]
        public async Task<IActionResult> GetAllRfqResponseDocuments()
        {
            try
            {
                var items = await _repository.GetAllRfqResponseDocuments();

                if (items == null || !items.Any())
                {
                    return NotFound(new
                    {
                        message = "No RFQ Response Documents found.",
                        data = Array.Empty<RfqResponseDocument>()
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
                _logger.LogError(ex, "Error fetching all RFQ Response Documents.");
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }


        [HttpGet("GetRfqResponseDocumentsById/{id}")]
        [Authorize]
        public async Task<IActionResult> GetRfqResponseDocumentsById(Guid id)
        {
            try
            {
                var item = await _repository.GetRfqResponseDocumentsById(id);

                if (item == null)
                {
                    return NotFound(new
                    {
                        message = $"No RFQ Response Document found for ID: {id}.",
                        data = (RfqResponseDocument?)null
                    });
                }

                return Ok(new { data = item });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching RFQ Response Document with ID {Id}.", id);
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }


        //--------RfqSubcontractorResponse
        [HttpGet("GetAllRfqSubcontractorResponse")]
        [Authorize]
        public async Task<IActionResult> GetAllRfqSubcontractorResponse()
        {
            try
            {
                var items = await _repository.GetAllRfqSubcontractorResponse();

                if (items == null || !items.Any())
                {
                    return NotFound(new
                    {
                        message = "No RFQ Subcontractor Responses found.",
                        data = Array.Empty<RfqSubcontractorResponse>()
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
                _logger.LogError(ex, "Error fetching all RFQ Subcontractor Responses.");
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        [HttpGet("GetRfqSubcontractorResponseById/{id}")]
        [Authorize]
        public async Task<IActionResult> GetRfqSubcontractorResponseById(Guid id)
        {
            try
            {
                var item = await _repository.GetRfqSubcontractorResponseById(id);

                if (item == null)
                {
                    return NotFound(new
                    {
                        message = $"No RFQ Subcontractor Response found for ID: {id}.",
                        data = (RfqSubcontractorResponse?)null
                    });
                }

                return Ok(new { data = item });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching RFQ Subcontractor Response with ID {Id}.", id);
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }


        //-----------RfqSubcontractorWorkItemResponse
        [HttpGet("GetAllRfqSubcontractorWorkItemResponse")]
        [Authorize]
        public async Task<IActionResult> GetAllRfqSubcontractorWorkItemResponse()
        {
            try
            {
                var items = await _repository.GetAllRfqSubcontractorWorkItemResponse();

                if (items == null || !items.Any())
                {
                    return NotFound(new
                    {
                        message = "No RFQ Subcontractor Work Item Responses found.",
                        data = Array.Empty<RfqSubcontractorWorkItemResponse>()
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
                _logger.LogError(ex, "Error fetching all RFQ Subcontractor Work Item Responses.");
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        [HttpGet("GetRfqSubcontractorWorkItemResponseById/{id}")]
        [Authorize]
        public async Task<IActionResult> GetRfqSubcontractorWorkItemResponseById(Guid id)
        {
            try
            {
                var item = await _repository.GetRfqSubcontractorWorkItemResponseById(id);

                if (item == null)
                {
                    return NotFound(new
                    {
                        message = $"No RFQ Subcontractor Work Item Response found for ID: {id}.",
                        data = (RfqSubcontractorWorkItemResponse?)null
                    });
                }

                return Ok(new { data = item });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching RFQ Subcontractor Work Item Response with ID {Id}.", id);
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }
    }
}
