// File: Controllers/QuotesController.cs
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Api.Data;
using Portfolio.Api.Models;
using Portfolio.Api.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Portfolio.Api.Controllers
{
    /// <summary>
    /// Handles quote-related endpoints:
    /// - <c>POST /api/quotes/refresh</c>: fetch N recent daily closes for given symbols and store to SQLite
    /// - <c>GET /api/quotes/latest</c>: read back a few most recent cached closes for a symbol
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><b>Idempotency:</b> UNIQUE index on (Symbol, AsOfDate) prevents duplicates.</item>
    /// <item><b>Validation:</b> Light validation on symbols and range.</item>
    /// <item><b>Resilience:</b> External errors are logged per symbol; processing continues.</item>
    /// </list>
    /// </remarks>
    [ApiController]
    [Route("api/[controller]")]
    public class QuotesController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly AlphaVantageClient _alpha;
        private readonly ILogger<QuotesController> _log;

        public QuotesController(AppDbContext db, AlphaVantageClient alpha, ILogger<QuotesController> log)
        {
            _db = db;
            _alpha = alpha;
            _log = log;
        }

        /// <summary>
        /// Fetches recent daily closes for the given symbols and persists them into SQLite.
        /// </summary>
        /// <remarks>
        /// Example:
        /// <br/>POST <c>/api/quotes/refresh?symbols=AAPL,MSFT&amp;range=30d</c>
        /// </remarks>
        [HttpPost("refresh")]
        [Produces("application/json")]
        [SwaggerOperation(
            Summary = "Fetch & store daily quotes",
            Description = "Calls Alpha Vantage TIME_SERIES_DAILY for each symbol, stores only new rows, and returns inserted/skipped counts.")]
        [ProducesResponseType(typeof(RefreshResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Refresh(
            [FromQuery, Required] string symbols,
            [FromQuery] string range = "30d",
            CancellationToken ct = default)
        {
            // ---- 1) Parse & validate input ----
            var list = symbols
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.ToUpperInvariant())
                .ToArray();

            if (list.Length == 0 || list.Length > 50)
                return BadRequest(new { error = "symbols must contain 1..50 comma-separated tickers" });

            if (!TryParseRange(range, out var days, out var fullHistory))
                return BadRequest(new { error = "range must be like '30d', '12m', or 'full'" });

            foreach (var s in list)
            {
                // Allow A–Z, 0–9, dot, dash; length 1..10
                if (s.Length is < 1 or > 10 || !s.All(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-'))
                    return BadRequest(new { error = $"invalid symbol: '{s}'" });
            }

            // ---- 2) Fetch & upsert loop ----
            var inserted = 0;
            var skipped = 0;

            foreach (var sym in list)
            {
                try
                {
                    // NOTE: This calls your AlphaVantageClient.
                    // Method name still says "Adjusted" but implementation uses TIME_SERIES_DAILY (free).
                    // Optional: rename to GetDailyAsync in the service for clarity and update here.
                    await foreach (var (date, close) in _alpha.GetDailyAdjustedAsync(sym, days, ct, fullHistory))
                    {
                        var exists = await _db.Prices.AnyAsync(p =>
                            p.Symbol == sym && p.AsOfDate == date, ct);

                        if (exists) { skipped++; continue; }

                        _db.Prices.Add(new Price
                        {
                            Symbol = sym,
                            AsOfDate = date,
                            Close = close,
                            Source = "alpha_vantage",
                            RetrievedAt = DateTime.UtcNow
                        });
                        inserted++;
                    }

                    await _db.SaveChangesAsync(ct);

                    // Free-tier friendly delay (~5 req/min on Alpha Vantage)
                    await Task.Delay(1500, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to refresh quotes for {Symbol}", sym);
                }
            }

            // ✅ return a typed DTO instead of an anonymous object
            var response = new RefreshResponse
            {
                Ok = true,
                Symbols = list,
                Inserted = inserted,
                Skipped = skipped
            };
            return Ok(response);
        }

        /// <summary>
        /// Returns the most recent cached closes for a symbol.
        /// </summary>
        /// <remarks>
        /// Example:
        /// <br/>GET <c>/api/quotes/latest?symbol=AAPL&amp;take=5</c>
        /// </remarks>
        [HttpGet("latest")]
        [Produces("application/json")]
        [SwaggerOperation(
            Summary = "Get recent cached closes",
            Description = "Returns up to N most recent price rows for a given symbol (default 5).")]
        [ProducesResponseType(typeof(IEnumerable<Price>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Latest(
            [FromQuery, Required] string symbol,
            [FromQuery] int take = 5,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { error = "symbol required" });

            take = Math.Clamp(take, 1, 50);

            var rows = await _db.Prices
                .Where(p => p.Symbol == symbol.ToUpperInvariant())
                .OrderByDescending(p => p.AsOfDate)
                .Take(take)
                .ToListAsync(ct);

            return Ok(rows);
        }

        /// <summary>
        /// Gets the most recent price for a symbol (live from Alpha Vantage).
        /// </summary>
        /// <remarks>
        /// Example: GET /api/quotes/current?symbol=AAPL
        /// </remarks>
        [HttpGet("current")]
        [Produces("application/json")]
        [SwaggerOperation(
            Summary = "Get current price",
            Description = "Calls Alpha Vantage GLOBAL_QUOTE and returns the most recent price.")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Current(
            [FromQuery, Required] string symbol,
            CancellationToken ct = default)
        {
            var result = await _alpha.GetLatestPriceAsync(symbol.ToUpperInvariant(), ct);
            if (result == null)
                return NotFound(new { error = $"No quote found for {symbol}" });

            return Ok(new
            {
                symbol = result.Value.Symbol,
                price = result.Value.Price,
                latestTradingDay = result.Value.LatestTradingDay
            });
        }

        /// <summary>
        /// Returns aggregated quarterly data for a symbol (average close per quarter).
        /// </summary>
        /// <remarks>
        /// Example: GET /api/quotes/quarters?symbol=AAPL&amp;take=8
        /// </remarks>
        [HttpGet("quarters")]
        [Produces("application/json")]
        [SwaggerOperation(
            Summary = "Quarterly aggregates (avg close)",
            Description = "Aggregates stored daily closes into quarterly buckets and returns the last N quarters.")]
        [ProducesResponseType(typeof(IEnumerable<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Quarters(
            [FromQuery, Required] string symbol,
            [FromQuery] int take = 8,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { error = "symbol required" });

            take = Math.Clamp(take, 1, 40);
            var sym = symbol.ToUpperInvariant();

            // 1) Hole alle Preise des Symbols (für DBs mit viel Historie kann man hier filtern)
            var list = await _db.Prices
                .Where(p => p.Symbol == sym)
                .ToListAsync(ct);

            // 2) Gruppiere in Quartale (Y-Q) und berechne Durchschnitts-Close
            var quarterly = list
                .GroupBy(p => new
                {
                    p.AsOfDate.Year,
                    Quarter = (p.AsOfDate.Month - 1) / 3 + 1
                })
                .Select(g => new
                {
                    g.Key.Year,
                    g.Key.Quarter,
                    From = g.Min(x => x.AsOfDate),
                    To = g.Max(x => x.AsOfDate),
                    AvgClose = Math.Round(g.Average(x => x.Close), 4)
                })
                .OrderByDescending(x => x.Year)
                .ThenByDescending(x => x.Quarter)
                .Take(take)
                .ToList();

            return Ok(quarterly);
        }

        /// <summary>
        /// Returns daily close time series for a symbol within an optional date range.
        /// </summary>
        /// <remarks>
        /// Example:
        /// <br/>GET <c>/api/quotes/timeseries?symbol=AAPL&amp;from=2024-01-01&amp;to=2025-09-09</c>
        /// <br/>If no dates are provided, defaults to the last 365 days up to today.
        /// </remarks>
        [HttpGet("timeseries")]
        [Produces("application/json")]
        [SwaggerOperation(
            Summary = "Daily close time series",
            Description = "Reads stored daily closes from SQLite for the given symbol and date range (inclusive).")]
        [ProducesResponseType(typeof(IEnumerable<TimeseriesPoint>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Timeseries(
            [FromQuery, Required] string symbol,
            [FromQuery] string? from = null,
            [FromQuery] string? to = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { error = "symbol required" });

            var sym = symbol.ToUpperInvariant();

            // ----- Parse dates with sensible defaults -----
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            DateOnly fromDate, toDate;

            if (string.IsNullOrWhiteSpace(to))
                toDate = today;
            else if (!DateOnly.TryParse(to, out toDate))
                return BadRequest(new { error = "invalid 'to' date (use yyyy-MM-dd)" });

            if (string.IsNullOrWhiteSpace(from))
                fromDate = toDate.AddDays(-365); // default: last 365 days
            else if (!DateOnly.TryParse(from, out fromDate))
                return BadRequest(new { error = "invalid 'from' date (use yyyy-MM-dd)" });

            if (fromDate > toDate)
                return BadRequest(new { error = "'from' must be <= 'to'" });

            // Optional hard guard to avoid accidental huge ranges (tune as you like)
            var maxSpanDays = 3650; // ~10 years
            if ((toDate.DayNumber - fromDate.DayNumber) > maxSpanDays)
                return BadRequest(new { error = $"date range too large (> {maxSpanDays} days)" });

            // ----- Query DB and project to lightweight DTO -----
            var data = await _db.Prices
                .Where(p => p.Symbol == sym && p.AsOfDate >= fromDate && p.AsOfDate <= toDate)
                .OrderBy(p => p.AsOfDate)
                .Select(p => new TimeseriesPoint { Date = p.AsOfDate, Close = p.Close })
                .ToListAsync(ct);

            return Ok(data);
        }

        // ---- helpers ----

        // returns: true/false + parsed days + fullHistory flag
        private static bool TryParseRange(string range, out int days, out bool fullHistory)
        {
            days = 30;
            fullHistory = false;
            if (string.IsNullOrWhiteSpace(range)) return true;

            var s = range.Trim().ToLowerInvariant();

            if (s == "full")
            {
                fullHistory = true;
                days = 3650; // irrelevanter Platzhalter, wir streamen eh alles durch
                return true;
            }

            // Support '12m', '24m', '36m' etc.
            if (s.EndsWith('m') && int.TryParse(s[..^1], out var months) && months is >= 1 and <= 120)
            {
                days = Math.Clamp(months * 30, 1, 3650);
                return true;
            }

            // Existing 'Nd' logic
            if (s.EndsWith('d') && int.TryParse(s[..^1], out var d) && d is >= 1 and <= 1000)
            {
                days = d;
                return true;
            }

            return false;
        }
    }
}
