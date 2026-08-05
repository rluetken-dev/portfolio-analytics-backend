using Microsoft.AspNetCore.Mvc;

namespace Portfolio.Api.Controllers;

/// <summary>
/// Provides basic system status endpoints.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class SystemController : ControllerBase
{
    /// <summary>
    /// Returns basic runtime status information.
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            status = "ok",
            environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "unknown",
            timeUtc = DateTime.UtcNow
        });
    }
}