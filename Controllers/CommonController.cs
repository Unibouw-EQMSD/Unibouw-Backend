using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories.Interfaces;

namespace UnibouwAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CommonController : ControllerBase
    {
        private readonly ICommon _repositoryCommon;
        private readonly ILogger<CommonController> _logger;

        public CommonController(ICommon repositoryCommon, ILogger<CommonController> logger)
        {
            _repositoryCommon = repositoryCommon;
            _logger = logger;
        }

        //--- WorkItem Category Type
        [HttpGet("workitemcategorytype")]
        [Authorize]
        public async Task<IActionResult> GetAllWorkItemCategoryTypes()
        {
            try
            {
                var items = await _repositoryCommon.GetAllWorkItemCategoryTypes();

                if (items == null || !items.Any())
                    return NotFound(new { message = "No work item category types found.", data = Array.Empty<WorkItemCategoryType>() });

                return Ok(new { count = items.Count(), data = items });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all work item category types.");
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        [HttpGet("workitemcategorytype/{id}")]
        [Authorize]
        public async Task<IActionResult> GetWorkItemCategoryTypeById(Guid id)
        {
            try
            {
                var item = await _repositoryCommon.GetWorkItemCategoryTypeById(id);

                if (item == null)
                    return NotFound(new { message = $"No work item category type found for ID: {id}.", data = (WorkItemCategoryType?)null });

                return Ok(new { data = item });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching work item category type by ID: {Id}", id);
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        //--- Person
        [HttpGet("person")]
        [Authorize]
        public async Task<IActionResult> GetAllPerson()
        {
            try
            {
                var items = await _repositoryCommon.GetAllPerson();

                if (items == null || !items.Any())
                    return NotFound(new { message = "No persons found.", data = Array.Empty<Person>() });

                return Ok(new { count = items.Count(), data = items });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching persons.");
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        [HttpGet("person/{id}")]
        [Authorize]
        public async Task<IActionResult> GetPersonById(Guid id)
        {
            try
            {
                var item = await _repositoryCommon.GetPersonById(id);

                if (item == null)
                    return NotFound(new { message = $"No person found for ID: {id}.", data = (Person?)null });

                return Ok(new { data = item });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching person by ID: {Id}", id);
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        //--- Subcontractor WorkItem Mapping
        [HttpGet("subcontractorworkitemmapping")]
        [Authorize]
        public async Task<IActionResult> GetAllSubcontractorWorkItemMapping()
        {
            try
            {
                var items = await _repositoryCommon.GetAllSubcontractorWorkItemMapping();

                if (items == null || !items.Any())
                    return NotFound(new { message = "No subcontractor work item mappings found.", data = Array.Empty<SubcontractorWorkItemMapping>() });

                return Ok(new { count = items.Count(), data = items });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching subcontractor work item mappings.");
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        [HttpGet("subcontractorworkitemmapping/{id}")]
        [Authorize]
        public async Task<IActionResult> GetSubcontractorWorkItemMappingById(Guid id)
        {
            try
            {
                var item = await _repositoryCommon.GetSubcontractorWorkItemMappingById(id);

                if (item == null)
                    return NotFound(new { message = $"No subcontractor work item mapping found for ID: {id}.", data = (SubcontractorWorkItemMapping?)null });

                return Ok(new { data = item });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching subcontractor work item mapping by ID: {Id}", id);
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        //--- Subcontractor Attachment Mapping
        [HttpGet("subcontractorattachmentmapping")]
        [Authorize]
        public async Task<IActionResult> GetAllSubcontractorAttachmentMapping()
        {
            try
            {
                var items = await _repositoryCommon.GetAllSubcontractorAttachmentMapping();

                if (items == null || !items.Any())
                    return NotFound(new { message = "No subcontractor attachment mappings found.", data = Array.Empty<SubcontractorAttachmentMapping>() });

                return Ok(new { count = items.Count(), data = items });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching subcontractor attachment mappings.");
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        [HttpGet("subcontractorattachmentmapping/{id}")]
        [Authorize] 
        public async Task<IActionResult> GetSubcontractorAttachmentMappingById(Guid id)
        {
            try
            {
                var item = await _repositoryCommon.GetSubcontractorAttachmentMappingById(id);

                if (item == null)
                    return NotFound(new { message = $"No subcontractor attachment mapping found for ID: {id}.", data = (SubcontractorAttachmentMapping?)null });

                return Ok(new { data = item });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching subcontractor attachment mapping by ID: {Id}", id);
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        //--- Customer
        [HttpGet("customer")]
        [Authorize]
        public async Task<IActionResult> GetAllCustomer()
        {
            try
            {
                var items = await _repositoryCommon.GetAllCustomer();

                if (items == null || !items.Any())
                    return NotFound(new { message = "No customers found.", data = Array.Empty<Customer>() });

                return Ok(new { count = items.Count(), data = items });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching customers.");
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        [HttpGet("customer/{id}")]
        [Authorize]
        public async Task<IActionResult> GetCustomerById(Guid id)
        {
            try
            {
                var item = await _repositoryCommon.GetCustomerById(id);

                if (item == null)
                    return NotFound(new { message = $"No customer found for ID: {id}.", data = (Customer?)null });

                return Ok(new { data = item });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching customer by ID: {Id}", id);
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        //--- Work Planner
        [HttpGet("workplanner")]
        [Authorize]
        public async Task<IActionResult> GetAllWorkPlanner()
        {
            try
            {
                var items = await _repositoryCommon.GetAllWorkPlanner();

                if (items == null || !items.Any())
                    return NotFound(new { message = "No work planners found.", data = Array.Empty<WorkPlanner>() });

                return Ok(new { count = items.Count(), data = items });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching work planners.");
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        [HttpGet("workplanner/{id}")]
        [Authorize]
        public async Task<IActionResult> GetWorkPlannerById(Guid id)
        {
            try
            {
                var item = await _repositoryCommon.GetWorkPlannerById(id);

                if (item == null)
                    return NotFound(new { message = $"No work planner found for ID: {id}.", data = (WorkPlanner?)null });

                return Ok(new { data = item });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching work planner by ID: {Id}", id);
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }
    }
}
