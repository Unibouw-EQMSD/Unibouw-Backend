using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using UnibouwAPI.Repositories.Interfaces;

namespace UnibouwAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GraphWebhookController : ControllerBase
    {
        private readonly IInboundMailService _service;

        public GraphWebhookController(IInboundMailService service)
        {
            _service = service;
        }

        [HttpPost("email")]
        public async Task<IActionResult> Email()
        {
            // Graph validation handshake
            if (Request.Query.ContainsKey("validationToken"))
                return Content(Request.Query["validationToken"], "text/plain");

            string json;
            using (var reader = new StreamReader(Request.Body))
                json = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(json))
                return Ok(); // ✅ do not throw, just acknowledge

            await _service.ProcessNotificationAsync(json);
            return Ok();
        }
    }
}