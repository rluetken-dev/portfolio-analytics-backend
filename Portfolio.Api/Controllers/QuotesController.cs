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
            /// <summary>Format: <c>Nd</c> (days). Allowed: 1..100. Default: 30d.</summary>
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

            if (!TryParseDays(range, out var days))
                return BadRequest(new { error = "range must look like '30d' with 1..100 days" });

            foreach (var s in list)
            {
                // Allow A–Z, 0–9, dot, dash; length 1..10
                if (s.Length is < 1 or > 10 || !s.All(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-'))
                    return BadRequest(new { error = $"invalid symbol: '{s}'" });
            }

            // ---- 2) Fetch & upsert loop ----
            var inserted = 0;
            var skipped  = 0;

            foreach (var sym in list)
            {
                try
                {
                    // NOTE: This calls your AlphaVantageClient.
                    // Method name still says "Adjusted" but implementation uses TIME_SERIES_DAILY (free).
                    // Optional: rename to GetDailyAsync in the service for clarity and update here.
                    await foreach (var (date, close) in _alpha.GetDailyAdjustedAsync(sym, days, ct))
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
                Skipped  = skipped
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
            /// <summary>How many rows to return (1..50). Default: 5.</summary>
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

        // ---- helpers ----

        private static bool TryParseDays(string range, out int days)
        {
            days = 30;
            if (string.IsNullOrWhiteSpace(range)) return true;
            var s = range.Trim().ToLowerInvariant();
            if (!s.EndsWith('d')) return false;
            if (!int.TryParse(s[..^1], out var d)) return false;
            if (d is < 1 or > 100) return false;
            days = d;
            return true;
        }
    }
}
