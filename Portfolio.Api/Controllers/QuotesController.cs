// File: Controllers/QuotesController.cs
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Api.Data;
using Portfolio.Api.Models;
using Portfolio.Api.Services;
using Swashbuckle.AspNetCore.Annotations;
using Polly.RateLimit;
using Portfolio.Api.Exceptions;
using Portfolio.Api.Utils;

namespace Portfolio.Api.Controllers
{
    /// <summary>
    /// Price ingestion &amp; read API.
    /// 
    /// Pipeline overview:
    /// 1) <c>POST /api/quotes/refresh</c>
    ///    - Calls Alpha Vantage (tries DAILY_ADJUSTED, falls back to DAILY).
    ///    - Upserts daily OHLCV (+ adjusted close when available) into SQLite.
    ///    - Enforces idempotency via UNIQUE index on (TickerId, TradingDate).
    /// 2) <c>GET /api/quotes/latest</c>
    ///    - Returns the N most recent rows for a symbol (quick checks/monitoring).
    /// 3) <c>GET /api/quotes/timeseries</c>
    ///    - Returns daily closes for a symbol in a date range (charting/analytics).
    /// 4) <c>GET /api/quotes/quarters</c>
    ///    - Aggregates per quarter (average close) for lightweight KPI views.
    /// 
    /// Data model:
    /// - <see cref="Ticker"/>: master list of instruments (Symbol, optional Name).
    /// - <see cref="Price"/>: daily records (TradingDate, OHLCV, AdjustedClose, Volume, Source, audit).
    /// 
    /// Resilience:
    /// - Premium-guarded endpoints return an 'Information' message → we fall back to DAILY.
    /// - Partial failures are logged per symbol; loop continues for others.
    /// - Free-tier friendly throttling: small delay between symbols.
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
                throw new BadRequestException("Symbols must contain 1–50 comma-separated tickers.");

            if (!TryParseRange(range, out var days, out var fullHistory))
                throw new BadRequestException("Range must be like '30d', '12m', or 'full'.");

            foreach (var s in list)
            {
                // Allow A–Z, 0–9, dot, dash; length 1..10
                if (s.Length is < 1 or > 10 || !s.All(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-'))
                    throw new BadRequestException($"Invalid symbol: '{s}'.");
            }

            // ---- 2) Fetch & upsert loop ----
            var inserted = 0;
            var skipped = 0;

            foreach (var sym in list)
            {
                try
                {
                    // Fetch and persist daily OHLCV + adjusted close using TIME_SERIES_DAILY_ADJUSTED.
                    // NOTE: We resolve the ticker once (outside the loop) to avoid repeated DB lookups per row.
                    var ticker = await _db.Tickers.FirstOrDefaultAsync(t => t.Symbol == sym, ct);
                    if (ticker is null)
                    {
                        ticker = new Ticker { Symbol = sym };
                        _db.Tickers.Add(ticker);
                        await _db.SaveChangesAsync(ct);
                    }
                    
                    // 🧠 Find latest known trading date for this ticker
                    var lastKnownDate = await _db.Prices
                        .Where(p => p.TickerId == ticker.Id)
                        .Select(p => (DateTime?)p.TradingDate.ToDateTime(TimeOnly.MinValue))
                        .MaxAsync(ct);

                    // Calculate days since last update to limit fetch window
                    if (lastKnownDate.HasValue && !fullHistory)
                    {
                        var daysSince = (DateTime.UtcNow.Date - lastKnownDate.Value.Date).Days + 1; // +1 ensures we fetch the next day
                        days = Math.Min(days, Math.Max(daysSince, 1)); // shrink range dynamically, but always >=1
                    }

                    await foreach (var (date, open, high, low, close, adjClose, volume) in _alpha.GetDailyAdjustedAsync(sym, days, ct, fullHistory))
                    {
                        // Idempotent upsert: update if row exists, otherwise insert a new one.
                        var row = await _db.Prices.FirstOrDefaultAsync(
                            p => p.TickerId == ticker.Id && p.TradingDate == date, ct);

                        if (row is null)
                        {
                            // Insert new row
                            _db.Prices.Add(new Price
                            {
                                TickerId = ticker.Id,
                                TradingDate = date,
                                Open = open,
                                High = high,
                                Low = low,
                                Close = close,
                                AdjustedClose = adjClose,
                                Volume = volume,
                                Source = "alpha_vantage",
                                CreatedUtc = DateTime.UtcNow
                            });
                            inserted++;
                        }
                        else
                        {
                            // Update existing row (fill/refresh full OHLCV payload)
                            row.Open = open;
                            row.High = high;
                            row.Low = low;
                            row.Close = close;
                            row.AdjustedClose = adjClose;
                            row.Volume = volume;
                            row.Source = "alpha_vantage";
                            row.UpdatedUtc = DateTime.UtcNow;
                            skipped++; // we touched an existing row; counting as "skipped/new=0" keeps totals simple
                        }
                    }

                    // Persist batched inserts once (keeps transaction short and efficient).
                    await _db.SaveChangesAsync(ct);

                    // ✅ Update ticker refresh timestamp
                    ticker.LastPriceUpdate = DateTime.UtcNow;
                    await _db.SaveChangesAsync(ct);

                    // Free-tier friendly delay (~5 req/min on Alpha Vantage).
                    await Task.Delay(1500, ct);
                }
                catch (RateLimitRejectedException ex)
                {
                    _log.LogWarning(ex, "Rate limit hit during fundamentals ingest for {Symbol}", sym);

                    // ex.RetryAfter ist direkt ein TimeSpan
                    Response.Headers["Retry-After"] = ex.RetryAfter.TotalSeconds.ToString("F0");

                    return StatusCode(StatusCodes.Status429TooManyRequests, new ProblemDetails
                    {
                        Title = "Rate limit reached",
                        Status = StatusCodes.Status429TooManyRequests,
                        Detail = $"Please retry after {ex.RetryAfter.TotalSeconds:F0} seconds."
                    });
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Fundamentals ingest failed for {Symbol}", sym);
                    return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
                    {
                        Title = "Fundamentals ingest failed",
                        Status = StatusCodes.Status500InternalServerError,
                        Detail = ex.Message
                    });
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
            Guard.BadRequestIf(string.IsNullOrWhiteSpace(symbol), "Symbol required.");

            take = Math.Clamp(take, 1, 50);

            // Load recent rows including the Ticker navigation and project to a lean DTO.
            // NOTE: We avoid returning EF entities directly and include the symbol to prevent nulls.
            var sym = symbol.ToUpperInvariant();

            var rows = await _db.Prices
                .AsNoTracking()                         // read-only query: faster, no tracking overhead
                .Include(p => p.Ticker)                 // ensure navigation is populated
                .Where(p => p.Ticker.Symbol == sym)     // filter by symbol via navigation
                .OrderByDescending(p => p.TradingDate)  // newest first
                .Take(take)
                .Select(p => new
                {
                    symbol = p.Ticker.Symbol,
                    date = p.TradingDate,
                    open = p.Open,
                    high = p.High,
                    low = p.Low,
                    close = p.Close,
                    adjustedClose = p.AdjustedClose,
                    volume = p.Volume,
                    source = p.Source
                })
                .ToListAsync(ct);

            // ✅ if no prices found
            if (rows.Count == 0)
            {
                return NotFound(new ProblemDetails
                {
                    Title = "No cached prices found",
                    Detail = $"No recent data for symbol '{sym}'.",
                    Status = StatusCodes.Status404NotFound
                });
            }

            // ✅ Use the most recent record for summary
            var latest = rows.First();

            return Ok(new
            {
                symbol = latest.symbol,
                value = latest.close,
                asOf = latest.date.ToString("yyyy-MM-dd"),
                source = latest.source
            });
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
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Current(
    [FromQuery, Required] string symbol,
    CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                throw new BadRequestException("Bad Request: Symbol is required.");
            }

            try
            {
                var result = await _alpha.GetLatestPriceAsync(symbol.ToUpperInvariant(), ct);
                if (result == null)
                    throw new NotFoundException($"No quote found for {symbol}.");

                return Ok(new
                {
                    symbol = result.Value.Symbol,
                    price = result.Value.Price,
                    latestTradingDay = result.Value.LatestTradingDay
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("AlphaVantageInfo"))
            {
                // Daily limit reached (e.g., 25 calls/day in the Free Plan)
                return StatusCode(StatusCodes.Status429TooManyRequests, new ProblemDetails
                {
                    Title = "Alpha Vantage daily limit reached",
                    Detail = ex.Message.Replace("AlphaVantageInfo: ", ""),
                    Status = StatusCodes.Status429TooManyRequests
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("AlphaVantageNote"))
            {
                // Per-minute limit reached (e.g., 5 calls/minute in the Free Plan)
                return StatusCode(StatusCodes.Status429TooManyRequests, new ProblemDetails
                {
                    Title = "Alpha Vantage per-minute rate limit",
                    Detail = ex.Message.Replace("AlphaVantageNote: ", ""),
                    Status = StatusCodes.Status429TooManyRequests
                });
            }
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
            Guard.BadRequestIf(string.IsNullOrWhiteSpace(symbol), "Symbol required.");

            take = Math.Clamp(take, 1, 40);
            var sym = symbol.ToUpperInvariant();

            // 1) Fetch all price records for the given symbol.
            //    Note: For very large histories, you might want to add an explicit date filter here.
            var list = await _db.Prices
                .Where(p => p.Ticker.Symbol == sym)
                .ToListAsync(ct);

            // 2) Group records by quarter (Year + Quarter) and calculate average closing price.
            var quarterly = list
                .GroupBy(p => new
                {
                    p.TradingDate.Year,
                    Quarter = (p.TradingDate.Month - 1) / 3 + 1
                })
                .Select(g => new
                {
                    g.Key.Year,
                    g.Key.Quarter,
                    From = g.Min(x => x.TradingDate),
                    To = g.Max(x => x.TradingDate),
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
        /// <para>Examples:</para>
        /// <para>GET <c>/api/quotes/timeseries?symbol=AAPL&amp;from=2024-01-01&amp;to=2025-09-09</c></para>
        /// <para>If no dates are provided, the endpoint defaults to the last 365 days up to today.</para>
        /// <para>If that window returns no rows and the client did not provide <c>from</c>/<c>to</c>,
        /// we fallback to the last 365 days relative to the DB max date for that symbol.</para>
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
            Guard.BadRequestIf(string.IsNullOrWhiteSpace(symbol), "Symbol required.");

            var sym = symbol.ToUpperInvariant();

            // ----- Parse dates with sensible defaults -----
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            DateOnly fromDate, toDate;

            if (string.IsNullOrWhiteSpace(to))
                toDate = today;
            else if (!DateOnly.TryParse(to, out toDate))
                throw new BadRequestException("Invalid 'to' date (use yyyy-MM-dd).");

            if (string.IsNullOrWhiteSpace(from))
                fromDate = toDate.AddDays(-365); // default: last 365 days
            else if (!DateOnly.TryParse(from, out fromDate))
                throw new BadRequestException("Invalid 'from' date (use yyyy-MM-dd).");

            if (fromDate > toDate)
                throw new BadRequestException("'From' must be <= 'To'.");

            // Optional guard to avoid accidental huge ranges
            var maxSpanDays = 3650; // ~10 years
            if ((toDate.DayNumber - fromDate.DayNumber) > maxSpanDays)
                throw new BadRequestException($"Date range too large (> {maxSpanDays} days).");

            // English: track if caller explicitly set a range
            bool explicitRange = !string.IsNullOrWhiteSpace(from) || !string.IsNullOrWhiteSpace(to);

            // ----- Primary query: requested (or default) window relative to 'today' -----
            var data = await _db.Prices
                .Where(p => p.Ticker.Symbol == sym && p.TradingDate >= fromDate && p.TradingDate <= toDate)
                .OrderBy(p => p.TradingDate)
                .Select(p => new TimeseriesPoint { Date = p.TradingDate, Close = p.Close }) // English: lightweight projection
                .ToListAsync(ct);

            // English: Fallback only if client did not set from/to AND result is empty
            if (!explicitRange && data.Count == 0)
            {
                // English: find DB max date for this symbol
                var maxDate = await _db.Prices
                    .Where(p => p.Ticker.Symbol == sym)
                    .MaxAsync(p => (DateOnly?)p.TradingDate, ct);

                if (maxDate is null)
                    return Ok(Array.Empty<TimeseriesPoint>()); // English: still no data in DB

                var fbFrom = maxDate.Value.AddDays(-365); // same default span
                data = await _db.Prices
                    .Where(p => p.Ticker.Symbol == sym && p.TradingDate >= fbFrom && p.TradingDate <= maxDate.Value)
                    .OrderBy(p => p.TradingDate)
                    .Select(p => new TimeseriesPoint { Date = p.TradingDate, Close = p.Close })
                    .ToListAsync(ct);
            }

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

        // Slim DTO for candles
        public record OhlcRowDto(string Date, decimal Open, decimal High, decimal Low, decimal Close, decimal? Volume);

        // Compact helper to format DateOnly as ISO (no timezone surprises)
        private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd");

        /// <summary>
        /// Daily OHLCV time series in ascending order.
        /// </summary>
        /// <remarks>
        /// <para>Examples:</para>
        /// <para>GET <c>/api/quotes/ohlc?symbol=AAPL</c></para>
        /// <para>GET <c>/api/quotes/ohlc?symbol=AAPL&amp;from=2025-03-01&amp;to=2025-09-15</c></para>
        /// <para>Fallback: if <c>from</c>/<c>to</c> are omitted and the default window is empty,
        /// the endpoint returns the last 180 days relative to the DB max date.</para>
        /// </remarks>
        [HttpGet("ohlc")]
        [Produces("application/json")]
        public async Task<IActionResult> Ohlc(
            [FromQuery] string symbol,
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            CancellationToken ct = default)
        {
            // English: normalize inputs
            var sym = (symbol ?? string.Empty).Trim().ToUpperInvariant();
            Guard.BadRequestIf(string.IsNullOrWhiteSpace(sym), "Symbol required.");

            // English: track if caller explicitly set a range
            bool explicitRange = from.HasValue || to.HasValue;

            // Default window: last 180 calendar days relative to 'today'
            var toDt = (to ?? DateTime.UtcNow.Date).Date;
            var fromDt = (from ?? toDt.AddDays(-180)).Date;

            var fromD = DateOnly.FromDateTime(fromDt);
            var toD = DateOnly.FromDateTime(toDt);

            // Resolve ticker id once
            var tickerId = await _db.Tickers
                .Where(t => t.Symbol == sym)
                .Select(t => t.Id)
                .FirstOrDefaultAsync(ct);

            if (tickerId == 0)
                return Ok(Array.Empty<OhlcRowDto>()); // no ticker → empty array

            // Base query
            var baseQ = _db.Prices.AsNoTracking().Where(p => p.TickerId == tickerId);

            // Primary window (today-relative or explicit)
            var rowsRaw = await baseQ
                .Where(p => p.TradingDate >= fromD && p.TradingDate <= toD)
                .OrderBy(p => p.TradingDate)
                .Select(p => new { p.TradingDate, p.Open, p.High, p.Low, p.Close, p.Volume })
                .ToListAsync(ct);

            // Fallback only when client did NOT provide from/to AND result is empty
            if (!explicitRange && rowsRaw.Count == 0)
            {
                var maxDate = await baseQ.MaxAsync(p => (DateOnly?)p.TradingDate, ct);
                if (maxDate is null)
                    return Ok(Array.Empty<OhlcRowDto>());

                var fbFrom = maxDate.Value.AddDays(-180);
                rowsRaw = await baseQ
                    .Where(p => p.TradingDate >= fbFrom && p.TradingDate <= maxDate.Value)
                    .OrderBy(p => p.TradingDate)
                    .Select(p => new { p.TradingDate, p.Open, p.High, p.Low, p.Close, p.Volume })
                    .ToListAsync(ct);
            }

            // Map to DTO
            var rows = rowsRaw.Select(p => new OhlcRowDto(
                Iso(p.TradingDate),   // date "YYYY-MM-DD"
                p.Open,
                p.High,
                p.Low,
                p.Close,
                p.Volume
            )).ToList();

            return Ok(rows);
        }
    }
}

