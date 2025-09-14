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
        /// Refresh profile (name + sector) for a single symbol.
        /// DEV fallback: if the upstream (FMP) rate-limit is hit (429), fill Sector from a small in-memory map
        /// so local testing can proceed without external calls. Remove this block for production.
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

            try
            {
                var profile = await _fmp.GetCompanyProfileAsync(sym, ct); // FMP v3 /profile
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
            // --- Friendly mapping for common upstream issues ---
            catch (HttpRequestException ex) when (
                ex.Message.Contains("429") ||
                ex.Message.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("Limit Reach", StringComparison.OrdinalIgnoreCase))
            {
                // DEV-ONLY FALLBACK: sector map for quick demos/tests
                var devSectorMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["AAPL"] = "Technology",
                    ["MSFT"] = "Technology",
                    ["AMZN"] = "Consumer Cyclical",
                    ["GOOGL"] = "Communication Services",
                    ["NVDA"] = "Technology",
                    ["KO"] = "Consumer Defensive",
                    ["JPM"] = "Financial Services",
                    ["V"] = "Financial Services",
                    // add more of your seeded symbols here as needed
                };

                if (devSectorMap.TryGetValue(sym, out var sector))
                {
                    // keep existing name; only set sector if missing
                    if (string.IsNullOrWhiteSpace(ticker.Sector))
                        ticker.Sector = sector;

                    await _db.SaveChangesAsync(ct);

                    return Ok(new CompanySummaryDto
                    {
                        Id = ticker.Id.ToString(),
                        Symbol = ticker.Symbol,
                        Name = ticker.Name,
                        Sector = ticker.Sector
                    });
                }

                // If we have no fallback for this symbol, tell the client to try later
                return StatusCode(StatusCodes.Status429TooManyRequests,
                    new { error = "FMP rate limit hit. Try again later." });
            }
            catch (HttpRequestException ex) when (
                ex.Message.Contains("403") ||
                ex.Message.Contains("Forbidden", StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(StatusCodes.Status502BadGateway,
                    new { error = "FMP access denied (plan). Check API key/plan.", detail = "403 from FMP" });
            }
            catch (HttpRequestException ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway,
                    new { error = "Upstream FMP call failed.", detail = ex.Message });
            }
            catch (OperationCanceledException)
            {
                return StatusCode(StatusCodes.Status408RequestTimeout,
                    new { error = "Request cancelled or timed out." });
            }
        }

        /// <summary>
        /// Refresh profile (name + sector) for up to {limit} tickers that are missing data.
        /// DEV: if FMP returns 429 (rate limit), fill Sector from a small local map and
        /// throttle between calls to avoid hammering the API. Remove the fallback for prod.
        /// </summary>
        [HttpPost("refresh-profiles")]
        public async Task<IActionResult> RefreshProfiles([FromQuery] int? limit, CancellationToken ct)
        {
            var take = Math.Clamp(limit ?? 25, 1, 100);

            var candidates = await _db.Set<Ticker>()
                .Where(t => t.Name == null || t.Sector == null)
                .OrderBy(t => t.Symbol)
                .Take(take)
                .ToListAsync(ct);

            var updated = new List<CompanySummaryDto>();

            // DEV-ONLY fallback map for sectors
            var devSectorMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AAPL"] = "Technology",
                ["MSFT"] = "Technology",
                ["AMZN"] = "Consumer Cyclical",
                ["GOOGL"] = "Communication Services",
                ["NVDA"] = "Technology",
                ["KO"] = "Consumer Defensive",
                ["JPM"] = "Financial Services",
                ["V"] = "Financial Services",
                // add more of your seeded symbols here if you like
            };

            foreach (var t in candidates)
            {
                try
                {
                    var p = await _fmp.GetCompanyProfileAsync(t.Symbol, ct);
                    if (p is null) continue;

                    if (!string.IsNullOrWhiteSpace(p.Name)) t.Name = p.Name;
                    if (!string.IsNullOrWhiteSpace(p.Sector)) t.Sector = p.Sector;

                    updated.Add(new CompanySummaryDto { Id = t.Id.ToString(), Symbol = t.Symbol, Name = t.Name, Sector = t.Sector });
                }
                catch (HttpRequestException ex) when (
                    ex.Message.Contains("429") ||
                    ex.Message.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("Limit Reach", StringComparison.OrdinalIgnoreCase))
                {
                    // DEV fallback: fill sector if we have a local mapping
                    if (string.IsNullOrWhiteSpace(t.Sector) && devSectorMap.TryGetValue(t.Symbol, out var sector))
                    {
                        t.Sector = sector;
                        updated.Add(new CompanySummaryDto { Id = t.Id.ToString(), Symbol = t.Symbol, Name = t.Name, Sector = t.Sector });
                    }
                    // else: keep silently; user can retry later
                }
                // small delay between calls to be nice to the upstream (also smooths rate limits)
                finally
                {
                    await Task.Delay(350, ct);
                }
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
