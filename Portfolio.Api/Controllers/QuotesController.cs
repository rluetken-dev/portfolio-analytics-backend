// File: Controllers/QuotesController.cs
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Api.Data;
using Portfolio.Api.Models;
using Portfolio.Api.Services;

namespace Portfolio.Api.Controllers
{
    /// <summary>
    /// Quotes controller:
    /// - POST /api/quotes/refresh  -> fetch N recent daily closes for given symbols and store to SQLite
    /// - GET  /api/quotes/latest   -> read back a few most recent cached closes for a symbol
    /// 
    /// Notes:
    /// - Idempotency: We rely on a UNIQUE index (Symbol, AsOfDate) to avoid duplicates.
    /// - Validation: We perform light validation on symbols and range parameter.
    /// - Resilience: External errors are logged per symbol; we continue with the next one.
    /// </summary>
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
        /// Usage:
        ///   POST /api/quotes/refresh?symbols=AAPL,MSFT&range=30d
        /// Response:
        ///   { "ok": true, "symbols": ["AAPL","MSFT"], "inserted": 42, "skipped": 18 }
        /// </summary>
        [HttpPost("refresh")]
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

            if (!TryParseDays(range, out var days))
                return BadRequest(new { error = "range must look like '30d' with 1..100 days" });

            // Simple ticker validation: A–Z, 0–9, dot, dash; length 1..10
            foreach (var s in list)
            {
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
                    await foreach (var (date, close) in _alpha.GetDailyAdjustedAsync(sym, days, ct))
                    {
                        // Check if we already have this (Symbol, Date)
                        var exists = await _db.Prices.AnyAsync(p =>
                            p.Symbol == sym && p.AsOfDate == date, ct);

                        if (exists)
                        {
                            skipped++;
                            continue;
                        }

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

                    // Persist writes for this symbol (keeps transactions small).
                    await _db.SaveChangesAsync(ct);

                    // Friendly pause for Alpha Vantage free-tier rate limiting (~5 req/min).
                    await Task.Delay(1500, ct);
                }
                catch (OperationCanceledException)
                {
                    // Honor cancellation
                    throw;
                }
                catch (Exception ex)
                {
                    // Log and continue with next symbol.
                    _log.LogWarning(ex, "Failed to refresh quotes for {Symbol}", sym);
                }
            }

            return Ok(new
            {
                ok = true,
                symbols = list,
                inserted,
                skipped
            });
        }

        /// <summary>
        /// Returns a few most recent cached closes for a symbol (default 5).
        /// Example:
        ///   GET /api/quotes/latest?symbol=AAPL
        /// </summary>
        [HttpGet("latest")]
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
