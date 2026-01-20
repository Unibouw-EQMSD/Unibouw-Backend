using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    [Authorize]
    public IActionResult GetMe()
    {
        try
        {
            var user = HttpContext.User;

            if (user?.Identity == null || !user.Identity.IsAuthenticated)
                return Unauthorized("User is not authenticated.");

            // ✅ Get user name safely
            var name = user.FindFirst("name")?.Value
                        ?? user.FindFirst(ClaimTypes.Name)?.Value
                        ?? user.Identity?.Name;

            // ✅ Get email (or UPN)
            var email = user.FindFirst("preferred_username")?.Value
                        ?? user.FindFirst("upn")?.Value
                        ?? user.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/upn")?.Value
                        ?? user.FindFirst("emails")?.Value
                        ?? user.FindFirst(ClaimTypes.Email)?.Value;

            // ✅ Get first available role (single value)
            var roles = user.FindFirst(ClaimTypes.Role)?.Value
                        ?? user.FindFirst("roles")?.Value
                        ?? "User"; // default fallback if role missing

            // ✅ Get scopes from token
            var scopeClaim = user.FindFirst("http://schemas.microsoft.com/identity/claims/scope")?.Value
                             ?? user.FindFirst("scp")?.Value;
            var tokenScopes = scopeClaim?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                ?? Array.Empty<string>();

            // ✅ Load role-scope mapping from config
            var roleScopeMappingSection = _configuration.GetSection("AzureAd:RoleScopeMapping");
            var roleScopeMapping = roleScopeMappingSection.Exists()
                ? roleScopeMappingSection.Get<Dictionary<string, string[]>>()
                : new Dictionary<string, string[]>();

            // ✅ Determine allowed scopes based on the single role
            var allowedScopes = roleScopeMapping.TryGetValue(roles.Trim(), out var mappedScopes)
                ? mappedScopes.Intersect(tokenScopes, StringComparer.OrdinalIgnoreCase).ToArray()
                : Array.Empty<string>();

            // ✅ Log for debugging
            _logger.LogInformation("User fetched successfully: {Email}, Role: {Role}", email, roles);

            // ✅ Final response
            return Ok(new
            {
                name = name = name?.TrimEnd('.'),
                email,
                roles,
                scopes = allowedScopes
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GetMe() failed: {ex.Message}\n{ex.StackTrace}");
            _logger.LogError(ex, "GetMe failed due to an unexpected error.");
            return StatusCode(500, $"An unexpected error occurred: {ex.Message}");
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
