using Microsoft.AspNetCore.Mvc;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories.Interfaces;

namespace UnibouwAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EmailController : ControllerBase
    {
        private readonly IEmailRepository _emailRepository;

        public EmailController(IEmailRepository emailRepository)
        {
            _emailRepository = emailRepository;
        }

        [HttpPost("send-rfq")]
        public async Task<IActionResult> SendRfqEmail([FromBody] EmailRequest request)
        {
            if (string.IsNullOrEmpty(request.ToEmail))
                return BadRequest(new { success = false, message = "Recipient email is required" });

            bool result = await _emailRepository.SendRfqEmailAsync(request);

            if (result)
                return Ok(new { success = true, message = "RFQ email sent successfully" });

            return StatusCode(500, new { success = false, message = "Failed to send RFQ email" });
        }
    }
}
