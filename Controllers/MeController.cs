using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Data;
using System.Linq;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
public class MeController : ControllerBase
{
    private readonly ILogger<MeController> _logger;
    private readonly IConfiguration _configuration;

    public MeController(ILogger<MeController> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    [HttpGet("GetMe")]
    [Authorize] // Requires a valid JWT Bearer token
    public IActionResult GetMe()
    {
        try
        {
            var user = HttpContext.User;

            if (user?.Identity == null || !user.Identity.IsAuthenticated)
                return Unauthorized();

            var name = (user.FindFirst("name")?.Value ?? user.FindFirst(ClaimTypes.Name)?.Value)?.TrimEnd('.');
            var email = user.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/upn")?.Value;
            var roles = user.FindFirst("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")?.Value;
            // Get all scopes from token
            var tokenScopes = user.FindFirst("http://schemas.microsoft.com/identity/claims/scope")?.Value?
                .Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();

            // Load RoleScopeMapping from configuration
            var roleScopeMapping = _configuration.GetSection("AzureAd:RoleScopeMapping")
                .Get<Dictionary<string, string[]>>() ?? new Dictionary<string, string[]>();

            // Determine allowed scopes based on user's role
            var allowedScopes = roles?.Split(',') // in case multiple roles
                .SelectMany(r => roleScopeMapping.TryGetValue(r.Trim(), out var mappedScopes) ? mappedScopes : Array.Empty<string>())
                .Distinct()
                .Intersect(tokenScopes) // Only include scopes actually present in token
                .ToArray() ?? Array.Empty<string>();

            return Ok(new
            {
                name,
                email,
                roles,
                //scopes = allowedScopes
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred in GetMe");
            return StatusCode(500, "An unexpected error occurred. Please contact support.");
        }
    }

    /*// Method accessible only by Admin
    [HttpGet("TestAdminRoleMethod")]
    [Authorize(Roles = "Admin")]
    public IActionResult TestAdminRoleMethod()
    {
        return Ok("Hello Admin! You have access to this endpoint.");
    }

    // Method accessible only by User
    [HttpGet("TestUserRoleMethod")]
    [Authorize(Roles = "User")]
    public IActionResult TestUserRoleMethod()
    {
        return Ok("Hello User! You have access to this endpoint.");
    }*/


}
