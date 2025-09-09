using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Portfolio.Api.Services;
using System.ComponentModel.DataAnnotations;

namespace Portfolio.Api.Controllers;

/// <summary>
/// Administrative endpoints for database housekeeping.
/// ⚠️ Important: Secure these endpoints (authentication/authorization) in production.
/// </summary>
[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly MaintenanceService _maintenance;

    public AdminController(MaintenanceService maintenance) => _maintenance = maintenance;

    /// <summary>
    /// Deletes outdated rows from the Prices table.
    /// 
    /// Query parameters:
    /// - <c>maxAgeDays</c>: delete rows older than today - N days (default 3 years).
    /// - <c>keepPerSymbol</c>: keep at most N rows per symbol (delete older rows).
    /// 
    /// Examples:
    /// <br/>POST /api/admin/prune?maxAgeDays=1095
    /// <br/>POST /api/admin/prune?keepPerSymbol=1000
    /// <br/>POST /api/admin/prune?maxAgeDays=1825&amp;keepPerSymbol=1500
    /// </summary>
    [HttpPost("prune")]
    [Produces("application/json")]
    [SwaggerOperation(
        Summary = "Prune old data",
        Description = "Deletes old rows and/or caps rows per symbol in the Prices table.")]
    public async Task<IActionResult> Prune(
        [FromQuery] int? maxAgeDays = 3 * 365,
        [FromQuery] int? keepPerSymbol = null,
        CancellationToken ct = default)
    {
        if (maxAgeDays is < 0 || keepPerSymbol is < 0)
            return BadRequest(new { error = "Parameters must be non-negative." });

        var deleted = await _maintenance.PruneAsync(maxAgeDays, keepPerSymbol, ct);
        return Ok(new { ok = true, deleted });
    }

    /// <summary>
    /// Runs SQLite VACUUM and ANALYZE:
    /// - Reclaims free space
    /// - Refreshes statistics
    /// 
    /// Should be run after a significant prune to shrink the database file.
    /// </summary>
    [HttpPost("vacuum")]
    [Produces("application/json")]
    [SwaggerOperation(
        Summary = "VACUUM + ANALYZE (SQLite)",
        Description = "Compacts the SQLite file and refreshes query planner statistics.")]
    public async Task<IActionResult> Vacuum(CancellationToken ct = default)
    {
        await _maintenance.VacuumAnalyzeAsync(ct);
        return Ok(new { ok = true });
    }
}
