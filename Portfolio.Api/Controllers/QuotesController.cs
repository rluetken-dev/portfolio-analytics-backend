using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Polly.RateLimit;
using Portfolio.Api.Data;
using Portfolio.Api.Exceptions;
using Portfolio.Api.Models;
using Portfolio.Api.Services;
using Portfolio.Api.Utils;
using Swashbuckle.AspNetCore.Annotations;

namespace Portfolio.Api.Controllers;

/// <summary>
/// Provides price ingestion and read endpoints for cached quote data.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class QuotesController : ControllerBase
{
    private const int MaxRefreshSymbols = 50;
    private const int MaxLatestRows = 50;
    private const int MaxQuarterRows = 40;
    private const int DefaultTimeseriesDays = 365;
    private const int MaxTimeseriesDays = 3650;
    private const int DefaultOhlcDays = 180;

    private readonly AppDbContext _db;
    private readonly AlphaVantageClient _alpha;
    private readonly ILogger<QuotesController> _logger;

    public QuotesController(
        AppDbContext db,
        AlphaVantageClient alpha,
        ILogger<QuotesController> logger)
    {
        _db = db;
        _alpha = alpha;
        _logger = logger;
    }

    /// <summary>
    /// Fetches daily quote data for one or more symbols and stores it locally.
    /// </summary>
    [HttpPost("refresh")]
    [Produces("application/json")]
    [SwaggerOperation(
        Summary = "Fetch and store daily quotes",
        Description = "Calls Alpha Vantage daily time series data for each symbol and stores new or updated price rows.")]
    [ProducesResponseType(typeof(RefreshResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Refresh(
        [FromQuery, Required] string symbols,
        [FromQuery] string range = "30d",
        CancellationToken ct = default)
    {
        string[] symbolList = ParseSymbols(symbols);

        if (!TryParseRange(range, out int requestedDays, out bool fullHistory))
        {
            throw new BadRequestException("Range must be like '30d', '12m', or 'full'.");
        }

        int inserted = 0;
        int skipped = 0;

        foreach (string symbol in symbolList)
        {
            try
            {
                var result = await RefreshSymbolAsync(symbol, requestedDays, fullHistory, ct);

                inserted += result.Inserted;
                skipped += result.Skipped;

                await Task.Delay(TimeSpan.FromMilliseconds(1500), ct);
            }
            catch (RateLimitRejectedException ex)
            {
                _logger.LogWarning(ex, "Rate limit reached during quote refresh for {Symbol}", symbol);

                Response.Headers["Retry-After"] = ex.RetryAfter.TotalSeconds.ToString("F0");

                return Problem(
                    title: "Rate limit reached",
                    detail: $"Please retry after {ex.RetryAfter.TotalSeconds:F0} seconds.",
                    statusCode: StatusCodes.Status429TooManyRequests);
            }
            catch (ServiceUnavailableException ex)
            {
                return Problem(
                    title: "External provider is not configured",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Quote refresh failed for {Symbol}", symbol);

                return Problem(
                    title: "Quote refresh failed",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        return Ok(new RefreshResponse
        {
            Ok = true,
            Symbols = symbolList,
            Inserted = inserted,
            Skipped = skipped
        });
    }

    /// <summary>
    /// Returns the most recent cached price rows for a symbol.
    /// </summary>
    [HttpGet("latest")]
    [Produces("application/json")]
    [SwaggerOperation(
        Summary = "Get recent cached closes",
        Description = "Returns up to N most recent cached price rows for a symbol.")]
    [ProducesResponseType(typeof(IEnumerable<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Latest(
        [FromQuery, Required] string symbol,
        [FromQuery] int take = 5,
        CancellationToken ct = default)
    {
        string normalizedSymbol = NormalizeSymbol(symbol);
        int normalizedTake = Math.Clamp(take, 1, MaxLatestRows);

        var rows = await _db.Prices
            .AsNoTracking()
            .Include(price => price.Ticker)
            .Where(price => price.Ticker.Symbol == normalizedSymbol)
            .OrderByDescending(price => price.TradingDate)
            .Take(normalizedTake)
            .Select(price => new
            {
                symbol = price.Ticker.Symbol,
                date = price.TradingDate.ToString("yyyy-MM-dd"),
                open = price.Open,
                high = price.High,
                low = price.Low,
                close = price.Close,
                adjustedClose = price.AdjustedClose,
                volume = price.Volume,
                source = price.Source
            })
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return NotFound(new ProblemDetails
            {
                Title = "No cached prices found",
                Detail = $"No recent data for symbol '{normalizedSymbol}'.",
                Status = StatusCodes.Status404NotFound
            });
        }

        return Ok(rows);
    }

    /// <summary>
    /// Gets the most recent live price for a symbol from Alpha Vantage.
    /// </summary>
    [HttpGet("current")]
    [Produces("application/json")]
    [SwaggerOperation(
        Summary = "Get current price",
        Description = "Calls Alpha Vantage GLOBAL_QUOTE and returns the most recent price.")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Current(
        [FromQuery, Required] string symbol,
        CancellationToken ct = default)
    {
        string normalizedSymbol = NormalizeSymbol(symbol);

        try
        {
            var result = await _alpha.GetLatestPriceAsync(normalizedSymbol, ct);

            if (result == null)
            {
                return Problem(
                    title: "Price temporarily unavailable",
                    detail: $"The latest quote for '{normalizedSymbol}' could not be retrieved. Please try again later.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Ok(new
            {
                symbol = result.Value.Symbol,
                price = result.Value.Price,
                latestTradingDay = result.Value.LatestTradingDay
            });
        }
        catch (ServiceUnavailableException ex)
        {
            return Problem(
                title: "External provider is not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("AlphaVantageInfo", StringComparison.Ordinal))
        {
            return Problem(
                title: "Alpha Vantage daily limit reached",
                detail: ex.Message.Replace("AlphaVantageInfo: ", string.Empty, StringComparison.Ordinal),
                statusCode: StatusCodes.Status429TooManyRequests);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("AlphaVantageNote", StringComparison.Ordinal))
        {
            return Problem(
                title: "Alpha Vantage rate limit reached",
                detail: ex.Message.Replace("AlphaVantageNote: ", string.Empty, StringComparison.Ordinal),
                statusCode: StatusCodes.Status429TooManyRequests);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Current price request timed out for {Symbol}", normalizedSymbol);

            return Problem(
                title: "External provider timeout",
                detail: "The external data provider did not respond in time.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Current price request failed for {Symbol}", normalizedSymbol);

            return Problem(
                title: "External provider network error",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Current price request failed for {Symbol}", normalizedSymbol);

            return Problem(
                title: "Current price request failed",
                detail: "An unexpected error occurred while processing the request.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Returns quarterly average close values for a symbol.
    /// </summary>
    [HttpGet("quarters")]
    [Produces("application/json")]
    [SwaggerOperation(
        Summary = "Quarterly average close values",
        Description = "Aggregates stored daily closes into quarterly buckets and returns the last N quarters.")]
    [ProducesResponseType(typeof(IEnumerable<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Quarters(
        [FromQuery, Required] string symbol,
        [FromQuery] int take = 8,
        CancellationToken ct = default)
    {
        string normalizedSymbol = NormalizeSymbol(symbol);
        int normalizedTake = Math.Clamp(take, 1, MaxQuarterRows);

        var prices = await _db.Prices
            .AsNoTracking()
            .Where(price => price.Ticker.Symbol == normalizedSymbol)
            .ToListAsync(ct);

        var quarterly = prices
            .GroupBy(price => new
            {
                price.TradingDate.Year,
                Quarter = (price.TradingDate.Month - 1) / 3 + 1
            })
            .Select(group => new
            {
                group.Key.Year,
                group.Key.Quarter,
                From = group.Min(price => price.TradingDate),
                To = group.Max(price => price.TradingDate),
                AvgClose = Math.Round(group.Average(price => price.Close), 4)
            })
            .OrderByDescending(row => row.Year)
            .ThenByDescending(row => row.Quarter)
            .Take(normalizedTake)
            .ToList();

        return Ok(quarterly);
    }

    /// <summary>
    /// Returns daily close time series for a symbol within an optional date range.
    /// </summary>
    [HttpGet("timeseries")]
    [Produces("application/json")]
    [SwaggerOperation(
        Summary = "Daily close time series",
        Description = "Reads stored daily closes for the given symbol and inclusive date range.")]
    [ProducesResponseType(typeof(IEnumerable<TimeseriesPoint>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Timeseries(
        [FromQuery, Required] string symbol,
        [FromQuery] string? from = null,
        [FromQuery] string? to = null,
        CancellationToken ct = default)
    {
        string normalizedSymbol = NormalizeSymbol(symbol);
        bool explicitRange = !string.IsNullOrWhiteSpace(from) || !string.IsNullOrWhiteSpace(to);

        (DateOnly fromDate, DateOnly toDate) = ParseDateRange(from, to, DefaultTimeseriesDays);

        var data = await GetTimeseriesAsync(normalizedSymbol, fromDate, toDate, ct);

        if (!explicitRange && data.Count == 0)
        {
            DateOnly? maxDate = await GetMaxTradingDateAsync(normalizedSymbol, ct);

            if (maxDate is null)
            {
                return Ok(Array.Empty<TimeseriesPoint>());
            }

            data = await GetTimeseriesAsync(
                normalizedSymbol,
                maxDate.Value.AddDays(-DefaultTimeseriesDays),
                maxDate.Value,
                ct);
        }

        return Ok(data);
    }

    /// <summary>
    /// Returns daily OHLCV rows for a symbol within an optional date range.
    /// </summary>
    [HttpGet("ohlc")]
    [Produces("application/json")]
    [SwaggerOperation(
        Summary = "Daily OHLCV time series",
        Description = "Reads stored daily OHLCV rows for the given symbol and inclusive date range.")]
    [ProducesResponseType(typeof(IEnumerable<OhlcRowDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Ohlc(
        [FromQuery, Required] string symbol,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        string normalizedSymbol = NormalizeSymbol(symbol);
        bool explicitRange = from.HasValue || to.HasValue;

        DateOnly toDate = DateOnly.FromDateTime((to ?? DateTime.UtcNow.Date).Date);
        DateOnly fromDate = DateOnly.FromDateTime((from ?? DateTime.UtcNow.Date.AddDays(-DefaultOhlcDays)).Date);

        Guard.BadRequestIf(fromDate > toDate, "'From' must be <= 'To'.");

        int tickerId = await _db.Tickers
            .Where(ticker => ticker.Symbol == normalizedSymbol)
            .Select(ticker => ticker.Id)
            .FirstOrDefaultAsync(ct);

        if (tickerId == 0)
        {
            return Ok(Array.Empty<OhlcRowDto>());
        }

        List<OhlcRowDto> rows = await GetOhlcRowsAsync(tickerId, fromDate, toDate, ct);

        if (!explicitRange && rows.Count == 0)
        {
            DateOnly? maxDate = await _db.Prices
                .AsNoTracking()
                .Where(price => price.TickerId == tickerId)
                .MaxAsync(price => (DateOnly?)price.TradingDate, ct);

            if (maxDate is null)
            {
                return Ok(Array.Empty<OhlcRowDto>());
            }

            rows = await GetOhlcRowsAsync(
                tickerId,
                maxDate.Value.AddDays(-DefaultOhlcDays),
                maxDate.Value,
                ct);
        }

        return Ok(rows);
    }

    public sealed record OhlcRowDto(
        string Date,
        decimal Open,
        decimal High,
        decimal Low,
        decimal Close,
        long Volume);

    private sealed record RefreshSymbolResult(int Inserted, int Skipped);

    private async Task<RefreshSymbolResult> RefreshSymbolAsync(
        string symbol,
        int requestedDays,
        bool fullHistory,
        CancellationToken ct)
    {
        Ticker ticker = await GetOrCreateTickerAsync(symbol, ct);
        int fetchDays = await CalculateFetchDaysAsync(ticker.Id, requestedDays, fullHistory, ct);

        int inserted = 0;
        int skipped = 0;

        await foreach (var (date, open, high, low, close, adjustedClose, volume) in
            _alpha.GetDailyAdjustedAsync(symbol, fetchDays, ct, fullHistory))
        {
            Price? row = await _db.Prices.FirstOrDefaultAsync(
                price => price.TickerId == ticker.Id && price.TradingDate == date,
                ct);

            if (row is null)
            {
                _db.Prices.Add(new Price
                {
                    TickerId = ticker.Id,
                    TradingDate = date,
                    Open = open,
                    High = high,
                    Low = low,
                    Close = close,
                    AdjustedClose = adjustedClose,
                    Volume = volume,
                    Source = "alpha_vantage",
                    CreatedUtc = DateTime.UtcNow
                });

                inserted++;
            }
            else
            {
                row.Open = open;
                row.High = high;
                row.Low = low;
                row.Close = close;
                row.AdjustedClose = adjustedClose;
                row.Volume = volume;
                row.Source = "alpha_vantage";
                row.UpdatedUtc = DateTime.UtcNow;

                skipped++;
            }
        }

        ticker.LastPriceUpdate = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return new RefreshSymbolResult(inserted, skipped);
    }

    private async Task<Ticker> GetOrCreateTickerAsync(string symbol, CancellationToken ct)
    {
        Ticker? ticker = await _db.Tickers.FirstOrDefaultAsync(
            existingTicker => existingTicker.Symbol == symbol,
            ct);

        if (ticker is not null)
        {
            return ticker;
        }

        ticker = new Ticker { Symbol = symbol };

        _db.Tickers.Add(ticker);
        await _db.SaveChangesAsync(ct);

        return ticker;
    }

    private async Task<int> CalculateFetchDaysAsync(
        int tickerId,
        int requestedDays,
        bool fullHistory,
        CancellationToken ct)
    {
        if (fullHistory)
        {
            return requestedDays;
        }

        DateOnly? lastKnownDate = await _db.Prices
            .Where(price => price.TickerId == tickerId)
            .OrderByDescending(price => price.TradingDate)
            .Select(price => (DateOnly?)price.TradingDate)
            .FirstOrDefaultAsync(ct);

        if (lastKnownDate is null)
        {
            return requestedDays;
        }

        int daysSinceLastUpdate = DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - lastKnownDate.Value.DayNumber + 1;

        return Math.Min(requestedDays, Math.Max(daysSinceLastUpdate, 1));
    }

    private Task<List<TimeseriesPoint>> GetTimeseriesAsync(
        string symbol,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken ct)
    {
        return _db.Prices
            .AsNoTracking()
            .Where(price => price.Ticker.Symbol == symbol &&
                price.TradingDate >= fromDate &&
                price.TradingDate <= toDate)
            .OrderBy(price => price.TradingDate)
            .Select(price => new TimeseriesPoint
            {
                Date = price.TradingDate,
                Close = price.Close
            })
            .ToListAsync(ct);
    }

    private Task<DateOnly?> GetMaxTradingDateAsync(string symbol, CancellationToken ct)
    {
        return _db.Prices
            .AsNoTracking()
            .Where(price => price.Ticker.Symbol == symbol)
            .MaxAsync(price => (DateOnly?)price.TradingDate, ct);
    }

    private Task<List<OhlcRowDto>> GetOhlcRowsAsync(
        int tickerId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken ct)
    {
        return _db.Prices
            .AsNoTracking()
            .Where(price => price.TickerId == tickerId &&
                price.TradingDate >= fromDate &&
                price.TradingDate <= toDate)
            .OrderBy(price => price.TradingDate)
            .Select(price => new OhlcRowDto(
                price.TradingDate.ToString("yyyy-MM-dd"),
                price.Open,
                price.High,
                price.Low,
                price.Close,
                price.Volume))
            .ToListAsync(ct);
    }

    private static string[] ParseSymbols(string symbols)
    {
        Guard.BadRequestIf(string.IsNullOrWhiteSpace(symbols), "Symbols required.");

        string[] parsedSymbols = symbols
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeSymbol)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (parsedSymbols.Length == 0 || parsedSymbols.Length > MaxRefreshSymbols)
        {
            throw new BadRequestException($"Symbols must contain 1-{MaxRefreshSymbols} comma-separated tickers.");
        }

        return parsedSymbols;
    }

    private static string NormalizeSymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new BadRequestException("Symbol required.");
        }

        string normalizedSymbol = symbol.Trim().ToUpperInvariant();

        if (normalizedSymbol.Length is < 1 or > 10 ||
            !normalizedSymbol.All(character => char.IsLetterOrDigit(character) || character is '.' or '-'))
        {
            throw new BadRequestException($"Invalid symbol: '{normalizedSymbol}'.");
        }

        return normalizedSymbol;
    }

    private static (DateOnly FromDate, DateOnly ToDate) ParseDateRange(
        string? from,
        string? to,
        int defaultDays)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

        DateOnly toDate;
        if (string.IsNullOrWhiteSpace(to))
        {
            toDate = today;
        }
        else if (!DateOnly.TryParse(to, out toDate))
        {
            throw new BadRequestException("Invalid 'to' date. Use yyyy-MM-dd.");
        }

        DateOnly fromDate;
        if (string.IsNullOrWhiteSpace(from))
        {
            fromDate = toDate.AddDays(-defaultDays);
        }
        else if (!DateOnly.TryParse(from, out fromDate))
        {
            throw new BadRequestException("Invalid 'from' date. Use yyyy-MM-dd.");
        }

        if (fromDate > toDate)
        {
            throw new BadRequestException("'From' must be <= 'To'.");
        }

        if (toDate.DayNumber - fromDate.DayNumber > MaxTimeseriesDays)
        {
            throw new BadRequestException($"Date range too large. Maximum is {MaxTimeseriesDays} days.");
        }

        return (fromDate, toDate);
    }

    private static bool TryParseRange(string range, out int days, out bool fullHistory)
    {
        days = 30;
        fullHistory = false;

        if (string.IsNullOrWhiteSpace(range))
        {
            return true;
        }

        string normalizedRange = range.Trim().ToLowerInvariant();

        if (normalizedRange == "full")
        {
            fullHistory = true;
            days = MaxTimeseriesDays;
            return true;
        }

        if (normalizedRange.EndsWith('m') &&
            int.TryParse(normalizedRange[..^1], out int months) &&
            months is >= 1 and <= 120)
        {
            days = Math.Clamp(months * 30, 1, MaxTimeseriesDays);
            return true;
        }

        if (normalizedRange.EndsWith('d') &&
            int.TryParse(normalizedRange[..^1], out int parsedDays) &&
            parsedDays is >= 1 and <= 1000)
        {
            days = parsedDays;
            return true;
        }

        return false;
    }
}
