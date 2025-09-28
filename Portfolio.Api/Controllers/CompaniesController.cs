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
        private readonly ILogger<CompaniesController> _logger;
        private readonly FallbackData _fallbackData;

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

        public CompaniesController(
            AppDbContext db,
            FmpClient fmp,
            ILogger<CompaniesController> logger,
            FallbackData fallbackData)
        {
            _db = db;
            _fmp = fmp;
            _logger = logger;
            _fallbackData = fallbackData;
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

        /// <summary>
        /// Search external API for companies (not in local DB yet)
        /// </summary>
        [HttpGet("search")]
        public async Task<ActionResult<CompanySearchResponse>> SearchCompanies(
            [FromQuery] string? q,    // search term
            [FromQuery] int? limit,   // max results
            CancellationToken ct)
        {
            // validate input
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
                return BadRequest(new { error = "Query must be at least 2 characters" });

            var query = q.Trim();
            var take = Math.Clamp(limit ?? 10, 1, 50);

            try
            {
                // search external FMP API
                var searchResults = await _fmp.SearchCompaniesAsync(query, take, ct);

                // check which ones we already have locally
                var symbols = searchResults.Select(r => r.Symbol).ToList();
                var existingSymbols = await _db.Tickers
                    .Where(t => symbols.Contains(t.Symbol))
                    .Select(t => t.Symbol)
                    .ToListAsync(ct);

                // mark existing vs addable companies
                var results = searchResults.Select(r => new CompanySearchResult
                {
                    Symbol = r.Symbol,
                    Name = r.Name,
                    Exchange = r.Exchange,
                    Sector = r.Sector,
                    IsInDatabase = existingSymbols.Contains(r.Symbol)
                }).ToList();

                return Ok(new CompanySearchResponse
                {
                    Query = query,
                    Results = results,
                    TotalFound = results.Count()
                });
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("429"))
            {
                return StatusCode(429, new { error = "API rate limit exceeded. Try again later." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Company search failed for: {Query}", query);
                return StatusCode(500, new { error = "Search failed. Please try again." });
            }
        }

        /// <summary>
        /// Add single company to local database
        /// </summary>
        [HttpPost("add")]
        public async Task<ActionResult<CompanySummaryDto>> AddCompany(
            [FromBody] AddCompanyRequest request,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.Symbol))
                return BadRequest(new { error = "Symbol is required" });

            var symbol = request.Symbol.Trim().ToUpperInvariant();

            // check for duplicates
            var existing = await _db.Tickers.FirstOrDefaultAsync(t => t.Symbol == symbol, ct);
            if (existing != null)
                return Conflict(new { error = $"Company {symbol} already exists" });

            try
            {
                // fetch from external API
                var profile = await _fmp.GetCompanyProfileAsync(symbol, ct);
                if (profile == null)
                    return NotFound(new { error = $"Company {symbol} not found in external API" });

                // create and save ticker
                var ticker = new Ticker
                {
                    Symbol = symbol,
                    Name = profile.Name ?? symbol,
                    Sector = profile.Sector
                };

                _db.Tickers.Add(ticker);
                await _db.SaveChangesAsync(ct);

                return CreatedAtAction(
                    nameof(GetCompanies),
                    new { q = symbol },
                    new CompanySummaryDto
                    {
                        Id = ticker.Id.ToString(),
                        Symbol = ticker.Symbol,
                        Name = ticker.Name,
                        Sector = ticker.Sector
                    });
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("429"))
            {
                return StatusCode(429, new { error = "API rate limit exceeded" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add company: {Symbol}", symbol);
                return StatusCode(500, new { error = "Failed to add company" });
            }
        }

        /// <summary>
        /// Bulk add popular companies from predefined lists
        /// </summary>
        [HttpPost("add-popular")]
        public async Task<ActionResult<BulkAddResponse>> AddPopularCompanies(
            [FromBody] AddPopularRequest request,
            CancellationToken ct)
        {
            // get predefined stock list
            var popularStocks = GetPopularStocksList(request.Category);
            var added = new List<CompanySummaryDto>();
            var errors = new List<string>();

            foreach (var symbol in popularStocks.Take(request.Limit ?? 20))
            {
                try
                {
                    // skip if already exists
                    var existing = await _db.Tickers.FirstOrDefaultAsync(t => t.Symbol == symbol, ct);
                    if (existing != null) continue;

                    // use fallback data to avoid API calls
                    var ticker = new Ticker
                    {
                        Symbol = symbol,
                        Name = GetFallbackCompanyName(symbol),
                        Sector = GetFallbackSector(symbol)
                    };

                    _db.Tickers.Add(ticker);
                    added.Add(new CompanySummaryDto
                    {
                        Symbol = ticker.Symbol,
                        Name = ticker.Name,
                        Sector = ticker.Sector
                    });

                    await Task.Delay(100, ct); // be nice to APIs
                }
                catch (Exception ex)
                {
                    errors.Add($"{symbol}: {ex.Message}");
                }
            }

            // save all changes
            if (added.Count > 0)
                await _db.SaveChangesAsync(ct);

            return Ok(new BulkAddResponse
            {
                Added = added,
                Errors = errors,
                TotalAdded = added.Count
            });
        }

        /// <summary>
        /// Remove company (with safety checks for existing data)
        /// </summary>
        [HttpDelete("{symbol}")]
        public async Task<IActionResult> RemoveCompany([FromRoute] string symbol, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { error = "Symbol is required" });

            var sym = symbol.Trim().ToUpperInvariant();
            var ticker = await _db.Tickers.FirstOrDefaultAsync(t => t.Symbol == sym, ct);

            if (ticker == null)
                return NotFound(new { error = $"Company {sym} not found" });

            // safety check: don't delete if has financial data
            var hasData = await _db.IncomeStatements.AnyAsync(i => i.Symbol == sym, ct) ||
                         await _db.BalanceSheets.AnyAsync(b => b.Symbol == sym, ct) ||
                         await _db.CashFlows.AnyAsync(c => c.Symbol == sym, ct) ||
                         await _db.Prices.AnyAsync(p => p.TickerId == ticker.Id, ct);

            if (hasData)
                return Conflict(new { error = $"Cannot delete {sym}: has financial data" });

            _db.Tickers.Remove(ticker);
            await _db.SaveChangesAsync(ct);

            return Ok(new { message = $"Company {sym} removed successfully" });
        }

        // ============= HELPER METHODS =============

        /// <summary>
        /// Get predefined lists of popular stocks by category
        /// </summary>
        private List<string> GetPopularStocksList(string? category = null)
        {
            if (string.IsNullOrEmpty(category)) 
                return _fallbackData.PopularLists.GetValueOrDefault("default") ?? new List<string>();

            var key = category.ToLower();
            return _fallbackData.PopularLists.GetValueOrDefault(key) ?? new List<string>();
        }

        /// <summary>
        /// Fallback company names to avoid API calls for well-known stocks
        /// </summary>
        private string GetFallbackCompanyName(string symbol)
        {
            var company = _fallbackData.Companies
                .FirstOrDefault(c => string.Equals(c.Symbol, symbol, StringComparison.OrdinalIgnoreCase));

            return company?.Name ?? symbol;
        }

        /// <summary>
        /// Fallback sector mapping for popular stocks
        /// </summary>
        private string? GetFallbackSector(string symbol)
        {
            var company = _fallbackData.Companies
                .FirstOrDefault(c => string.Equals(c.Symbol, symbol, StringComparison.OrdinalIgnoreCase));

            return company?.Sector;
        }


        // ============= NEW DTOs - Create file: Portfolio.Api/Models/CompanyDiscoveryDtos.cs =============

        public record AddCompanyRequest
        {
            public string Symbol { get; init; } = string.Empty;
        }

        public record AddPopularRequest
        {
            public string? Category { get; init; }
            public int? Limit { get; init; } = 20;
        }

        public record CompanySearchResult
        {
            public string Symbol { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public string? Exchange { get; init; }
            public string? Sector { get; init; }
            public bool IsInDatabase { get; init; }
        }

        public record CompanySearchResponse
        {
            public string Query { get; init; } = string.Empty;
            public List<CompanySearchResult> Results { get; init; } = new();
            public int TotalFound { get; init; }
        }

        public record BulkAddResponse
        {
            public List<CompanySummaryDto> Added { get; init; } = new();
            public List<string> Errors { get; init; } = new();
            public int TotalAdded { get; init; }
        }

       

    }
}
