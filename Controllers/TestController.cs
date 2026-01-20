using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace UnibouwAPI.Controllers
{
    [ApiController]
    [Route("api/test")]
    public class TestController : ControllerBase
    {
        private readonly ILogger<TestController> _logger;

        public TestController(ILogger<TestController> logger)
        {
           _logger = logger;
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Ping()
        {
            return Ok("pong");
        }

        //--- Test logs
        [HttpGet("test-log")]
        [AllowAnonymous]
        public IActionResult TestLog()
        {
            _logger.LogInformation("Test Information log from CommonController");
            _logger.LogWarning("Test Warning log from CommonController");

            try
            {
                throw new Exception("Test exception for DB logging");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Test Error log from CommonController");
            }

            return Ok("Logs written successfully");
        }
    }
}



