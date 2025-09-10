using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Portfolio.Api.Services;
using System.ComponentModel.DataAnnotations;
using Portfolio.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Sqlite;


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

    /// <summary>
    /// Hard reset: deletes all rows from the Prices table.
    /// </summary>
    /// <remarks>
    /// Example:
    /// <br/>POST /api/admin/truncate
    /// 
    /// ⚠️ This is destructive. Use only in development or with explicit confirmation.
    /// </remarks>
    [HttpPost("truncate")]
    [Produces("application/json")]
    [SwaggerOperation(
        Summary = "Delete all rows",
        Description = "Wipes the Prices table completely. Use with caution.")]
    public async Task<IActionResult> Truncate(CancellationToken ct = default)
    {
        var deleted = await _maintenance.TruncateAllAsync(ct);
        return Ok(new { ok = true, deleted });
    }

    /// <summary>
    /// Diagnostics: shows which SQLite file is in use and basic table stats.
    /// </summary>
    /// <remarks>
    /// GET /api/admin/info
    /// </remarks>
    [HttpGet("info")]
    [Produces("application/json")]
    public async Task<IActionResult> Info([FromServices] AppDbContext db, CancellationToken ct = default)
    {
        // Provider/Connection ermitteln (voll qualifiziert, falls Extensions nicht per using gemapped sind)
        var isSqlite = db.Database.IsSqlite();
        string dbPathOrCxn = "(unknown)";

        if (isSqlite)
        {
            var conn = Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetDbConnection(db.Database);
            dbPathOrCxn = conn?.DataSource ?? "(no data source)";
            try
            {
                if (!string.IsNullOrWhiteSpace(dbPathOrCxn) && !System.IO.Path.IsPathRooted(dbPathOrCxn))
                    dbPathOrCxn = System.IO.Path.GetFullPath(dbPathOrCxn, AppContext.BaseDirectory);
            }
            catch { /* ignore */ }
        }
        else
        {
            // Fallback für andere Provider: ConnectionString anzeigen
            dbPathOrCxn = Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetConnectionString(db.Database)
                            ?? "(no connection string)";
        }

        // Counts & ranges from Prices table.
        // Group by ticker symbol (via navigation property) and compute row counts + min/max dates.
        var total = await db.Prices.CountAsync(ct);
        var perSymbol = await db.Prices
            .GroupBy(p => p.Ticker.Symbol)
            .Select(g => new
            {
                symbol = g.Key,
                count = g.Count(),
                minDate = g.Min(x => x.TradingDate),
                maxDate = g.Max(x => x.TradingDate)
            })
            .OrderByDescending(x => x.count)
            .ToListAsync(ct);

        return Ok(new
        {
            database = isSqlite ? "SQLite" : "Other",
            locationOrConnection = dbPathOrCxn,
            totalPrices = total,
            symbols = perSymbol
        });
    }
}
