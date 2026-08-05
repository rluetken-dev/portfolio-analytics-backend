using Microsoft.AspNetCore.Mvc;
using Polly.RateLimit;
using Portfolio.Api.Exceptions;
using Portfolio.Api.Services;

namespace Portfolio.Api.Controllers;

/// <summary>
/// Provides server-side data ingestion endpoints for storing external provider data locally.
/// </summary>
[ApiController]
[Route("api/ingest")]
public sealed class IngestController : ControllerBase
{
    private const int MinLimit = 1;
    private const int MaxLimit = 100;

    private readonly IncomeIngestService _incomeIngest;
    private readonly BalanceSheetIngestService _balanceSheetIngest;
    private readonly CashFlowIngestService _cashFlowIngest;
    private readonly ILogger<IngestController> _logger;

    public IngestController(
        IncomeIngestService incomeIngest,
        BalanceSheetIngestService balanceSheetIngest,
        CashFlowIngestService cashFlowIngest,
        ILogger<IngestController> logger)
    {
        _incomeIngest = incomeIngest;
        _balanceSheetIngest = balanceSheetIngest;
        _cashFlowIngest = cashFlowIngest;
        _logger = logger;
    }

    /// <summary>
    /// Upserts income statement rows into local storage.
    /// </summary>
    /// <param name="symbol">Ticker symbol, for example AAPL.</param>
    /// <param name="period">Statement frequency: annual or quarter.</param>
    /// <param name="limit">Maximum number of rows to ingest, clamped to 1-100.</param>
    /// <param name="ct">Cancellation token for the request.</param>
    [HttpGet("income/{symbol}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public Task<IActionResult> IngestIncome(
        string symbol,
        string period = "annual",
        int limit = 10,
        CancellationToken ct = default)
    {
        return ExecuteIngestAsync(
            symbol,
            period,
            limit,
            "Income",
            (normalizedSymbol, normalizedPeriod, normalizedLimit, token) =>
                _incomeIngest.IngestAsync(normalizedSymbol, normalizedPeriod, normalizedLimit, token),
            ct);
    }

    /// <summary>
    /// Upserts balance sheet rows into local storage.
    /// </summary>
    /// <param name="symbol">Ticker symbol, for example AAPL.</param>
    /// <param name="period">Statement frequency: annual or quarter.</param>
    /// <param name="limit">Maximum number of rows to ingest, clamped to 1-100.</param>
    /// <param name="ct">Cancellation token for the request.</param>
    [HttpGet("balance/{symbol}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public Task<IActionResult> IngestBalance(
        string symbol,
        string period = "annual",
        int limit = 5,
        CancellationToken ct = default)
    {
        return ExecuteIngestAsync(
            symbol,
            period,
            limit,
            "Balance",
            (normalizedSymbol, normalizedPeriod, normalizedLimit, token) =>
                _balanceSheetIngest.IngestAsync(normalizedSymbol, normalizedPeriod, normalizedLimit, token),
            ct);
    }

    /// <summary>
    /// Upserts cash flow rows into local storage.
    /// </summary>
    /// <param name="symbol">Ticker symbol, for example AAPL.</param>
    /// <param name="period">Statement frequency: annual or quarter.</param>
    /// <param name="limit">Maximum number of rows to ingest, clamped to 1-100.</param>
    /// <param name="ct">Cancellation token for the request.</param>
    [HttpGet("cash/{symbol}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public Task<IActionResult> IngestCash(
        string symbol,
        string period = "annual",
        int limit = 5,
        CancellationToken ct = default)
    {
        return ExecuteIngestAsync(
            symbol,
            period,
            limit,
            "Cash flow",
            (normalizedSymbol, normalizedPeriod, normalizedLimit, token) =>
                _cashFlowIngest.IngestAsync(normalizedSymbol, normalizedPeriod, normalizedLimit, token),
            ct);
    }

    private async Task<IActionResult> ExecuteIngestAsync(
        string symbol,
        string period,
        int limit,
        string ingestName,
        Func<string, string, int, CancellationToken, Task<int>> ingest,
        CancellationToken ct)
    {
        string normalizedSymbol = NormalizeSymbol(symbol);
        string normalizedPeriod = NormalizePeriod(period);
        int normalizedLimit = NormalizeLimit(limit);

        try
        {
            int upserted = await ingest(normalizedSymbol, normalizedPeriod, normalizedLimit, ct);

            return Ok(new
            {
                Symbol = normalizedSymbol,
                Period = normalizedPeriod,
                Upserted = upserted
            });
        }
        catch (RateLimitRejectedException ex)
        {
            _logger.LogWarning(ex, "Rate limit reached for {IngestName} ingest ({Symbol})", ingestName, normalizedSymbol);

            return Problem(
                title: "Rate limit reached",
                detail: $"Please retry after {ex.RetryAfter}.",
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
            _logger.LogError(ex, "{IngestName} ingest failed for {Symbol}", ingestName, normalizedSymbol);

            return Problem(
                title: $"{ingestName} ingest failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static string NormalizeSymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new BadRequestException("Symbol is required.");
        }

        return symbol.Trim().ToUpperInvariant();
    }

    private static string NormalizePeriod(string period)
    {
        string normalizedPeriod = string.IsNullOrWhiteSpace(period)
            ? "annual"
            : period.Trim().ToLowerInvariant();

        if (normalizedPeriod != "annual" && normalizedPeriod != "quarter")
        {
            throw new BadRequestException("Period must be 'annual' or 'quarter'.");
        }

        return normalizedPeriod;
    }

    private static int NormalizeLimit(int limit)
    {
        return Math.Clamp(limit, MinLimit, MaxLimit);
    }
}
