using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Api.Data;
using Portfolio.Api.Models;
using Portfolio.Api.Services;

namespace Portfolio.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CompaniesController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly FmpClient _fmp;

        public CompaniesController(AppDbContext db, FmpClient fmp) // <-- inject FmpClient
        {
            _db = db;
            _fmp = fmp;
        }

        /// <summary>
        /// GET /api/companies?q=AAP&amp;limit=50
        /// Simple search (by Symbol/Name) + limit (default 50, max 200).
        /// </summary> 
        [HttpGet]
        public async Task<ActionResult<IEnumerable<CompanySummaryDto>>> GetCompanies(
            [FromQuery] string? q,
            [FromQuery] int? limit,
            CancellationToken ct)
        {
            // Use Set<T>() so we don't depend on a specific DbSet<Ticker> property name.
            var query = _db.Set<Ticker>().AsQueryable();

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                query = query.Where(t =>
                    EF.Functions.Like(t.Symbol, $"%{term}%") ||
                    (t.Name != null && EF.Functions.Like(t.Name, $"%{term}%"))
                );
            }

            var take = Math.Clamp(limit ?? 50, 1, 200);

            var rows = await query
                .OrderBy(t => t.Symbol)
                .Select(t => new CompanySummaryDto
                {
                    Id = t.Id.ToString(),
                    Symbol = t.Symbol,
                    Name = t.Name,
                    Sector = t.Sector
                })
                .Take(take)
                .ToListAsync(ct);

            return Ok(rows);
        }

        /// <summary>
        /// Ingests/refreshes Name &amp; Sector for an existing ticker from FMP profile API.
        /// POST /api/companies/{symbol}/refresh-profile
        /// </summary>
        [HttpPost("{symbol}/refresh-profile")]
        public async Task<IActionResult> RefreshProfile([FromRoute] string symbol, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest("Symbol required.");

            var sym = symbol.Trim().ToUpperInvariant();

            var ticker = await _db.Set<Ticker>().FirstOrDefaultAsync(t => t.Symbol == sym, ct);
            if (ticker is null)
                return NotFound($"Ticker '{sym}' not found.");

            var profile = await _fmp.GetCompanyProfileAsync(sym, ct); // calls FMP v3 /profile
            if (profile is null)
                return NotFound($"No profile found at FMP for '{sym}'.");

            if (!string.IsNullOrWhiteSpace(profile.Name)) ticker.Name = profile.Name;
            if (!string.IsNullOrWhiteSpace(profile.Sector)) ticker.Sector = profile.Sector;

            await _db.SaveChangesAsync(ct);

            return Ok(new CompanySummaryDto
            {
                Id = ticker.Id.ToString(),
                Symbol = ticker.Symbol,
                Name = ticker.Name,
                Sector = ticker.Sector
            });
        }

        /// <summary>
        /// Refresh profile for up to {limit} tickers (defaults to 50) that have missing Name or Sector.
        /// POST /api/companies/refresh-profiles?limit=50
        /// </summary>
        [HttpPost("refresh-profiles")]
        public async Task<IActionResult> RefreshProfiles([FromQuery] int? limit, CancellationToken ct)
        {
            var take = Math.Clamp(limit ?? 50, 1, 500);

            var candidates = await _db.Set<Ticker>()
                .Where(t => t.Name == null || t.Sector == null)
                .OrderBy(t => t.Symbol)
                .Take(take)
                .ToListAsync(ct);

            var updated = new List<CompanySummaryDto>();

            foreach (var t in candidates)
            {
                var p = await _fmp.GetCompanyProfileAsync(t.Symbol, ct);
                if (p is null) continue;

                if (!string.IsNullOrWhiteSpace(p.Name)) t.Name = p.Name;
                if (!string.IsNullOrWhiteSpace(p.Sector)) t.Sector = p.Sector;

                updated.Add(new CompanySummaryDto
                {
                    Id = t.Id.ToString(),
                    Symbol = t.Symbol,
                    Name = t.Name,
                    Sector = t.Sector
                });
            }

            if (updated.Count > 0)
                await _db.SaveChangesAsync(ct);

            return Ok(new { count = updated.Count, items = updated });
        }
    }

    /// <summary>
    /// Lightweight DTO for the frontend list.
    /// </summary>
    public record CompanySummaryDto
    {
        public string? Id { get; init; }
        public string? Symbol { get; init; }
        public string? Name { get; init; }
        public string? Sector { get; init; }
    }
}
