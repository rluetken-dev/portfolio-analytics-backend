using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Exceptions;
using Portfolio.Api.Services;
using Portfolio.Api.Utils;
using Swashbuckle.AspNetCore.Annotations;

namespace Portfolio.Api.Controllers;

/// <summary>
/// Provides fundamentals endpoints backed by external provider clients.
/// </summary>
[ApiController]
[Route("api/fundamentals")]
public sealed class FundamentalsController : ControllerBase
{
    private const int RevenueLimitMin = 1;
    private const int RevenueLimitMax = 12;
    private const int StatementLimitMin = 1;
    private const int StatementLimitMax = 20;
    private const int RefreshYearsMin = 1;
    private const int RefreshYearsMax = 10;

    private readonly FmpClient _fmp;
    private readonly AlphaVantageClient _alpha;
    private readonly ILogger<FundamentalsController> _logger;

    public FundamentalsController(
        FmpClient fmp,
        AlphaVantageClient alpha,
        ILogger<FundamentalsController> logger)
    {
        _fmp = fmp;
        _alpha = alpha;
        _logger = logger;
    }

    /// <summary>
    /// Returns quarterly or annual revenue rows for the requested symbol.
    /// </summary>
    /// <remarks>
    /// Tries FMP quarterly data first, then FMP annual data, then Alpha Vantage quarterly data.
    /// </remarks>
    [HttpGet("revenue")]
    [Produces("application/json")]
    [SwaggerOperation(
        Summary = "Revenue series",
        Description = "Tries FMP quarterly first, then FMP annual, then Alpha Vantage quarterly data.")]
    [ProducesResponseType(typeof(IEnumerable<RevenueDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetRevenue(
        [FromQuery, Required] string symbol,
        [FromQuery] int limit = 8,
        CancellationToken ct = default)
    {
        Guard.BadRequestIf(string.IsNullOrWhiteSpace(symbol), "Symbol required.");

        string normalizedSymbol = NormalizeSymbol(symbol);
        int normalizedLimit = Math.Clamp(limit, RevenueLimitMin, RevenueLimitMax);

        var fmpQuarterly = await _fmp.GetQuarterlyRevenueAsync(normalizedSymbol, normalizedLimit, ct);
        if (fmpQuarterly.Count > 0)
        {
            return Ok(ToRevenueDtos(normalizedSymbol, fmpQuarterly));
        }

        var fmpAnnual = await _fmp.GetAnnualRevenueAsync(normalizedSymbol, normalizedLimit, ct);
        if (fmpAnnual.Count > 0)
        {
            return Ok(ToRevenueDtos(normalizedSymbol, fmpAnnual));
        }

        var alphaVantageRows = await _alpha.GetQuarterlyRevenueAvAsync(normalizedSymbol, normalizedLimit, ct);

        return Ok(ToRevenueDtos(normalizedSymbol, alphaVantageRows));
    }

    /// <summary>
    /// Fetches income statement rows from FMP's stable API, newest first.
    /// </summary>
    /// <param name="symbol">Ticker symbol, for example AAPL.</param>
    /// <param name="period">Statement frequency: annual or quarter.</param>
    /// <param name="limit">Maximum number of rows to return, clamped to 1-20.</param>
    /// <param name="ct">Cancellation token for the request.</param>
    /// <response code="200">Returns income statement rows.</response>
    [HttpGet("{symbol}/income-statement/stable")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetIncomeStatementStable(
        string symbol,
        string period = "annual",
        int limit = 5,
        CancellationToken ct = default)
    {
        string normalizedSymbol = NormalizeSymbol(symbol);
        string normalizedPeriod = NormalizePeriod(period);
        int normalizedLimit = Math.Clamp(limit, StatementLimitMin, StatementLimitMax);

        var rows = await _fmp.GetIncomeStatementStableAsync(
            normalizedSymbol,
            normalizedLimit,
            normalizedPeriod,
            ct);

        return Ok(new
        {
            Symbol = normalizedSymbol,
            Period = normalizedPeriod,
            Count = rows?.Count ?? 0,
            Items = rows
        });
    }

    /// <summary>
    /// Returns trailing twelve months key metrics for one symbol from FMP's stable API.
    /// </summary>
    /// <param name="symbol">Ticker symbol, for example AAPL.</param>
    /// <param name="ct">Cancellation token for the request.</param>
    /// <response code="200">Returns metrics if provider data is available.</response>
    [HttpGet("{symbol}/metrics/ttm")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetKeyMetricsTtm(string symbol, CancellationToken ct = default)
    {
        string normalizedSymbol = NormalizeSymbol(symbol);
        var metrics = await _fmp.GetKeyMetricsTtmAsync(normalizedSymbol, ct);

        return Ok(new
        {
            Symbol = normalizedSymbol,
            HasData = metrics is not null,
            Metrics = metrics
        });
    }

    /// <summary>
    /// Fetches balance sheet rows from FMP's stable API, newest first.
    /// </summary>
    /// <param name="symbol">Ticker symbol, for example AAPL.</param>
    /// <param name="period">Statement frequency: annual or quarter.</param>
    /// <param name="limit">Maximum number of rows to return, clamped to 1-20.</param>
    /// <param name="ct">Cancellation token for the request.</param>
    /// <response code="200">Returns balance sheet rows.</response>
    [HttpGet("{symbol}/balance-sheet/stable")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetBalanceSheetStable(
        string symbol,
        string period = "annual",
        int limit = 3,
        CancellationToken ct = default)
    {
        string normalizedSymbol = NormalizeSymbol(symbol);
        string normalizedPeriod = NormalizePeriod(period);
        int normalizedLimit = Math.Clamp(limit, StatementLimitMin, StatementLimitMax);

        var rows = await _fmp.GetBalanceSheetStableAsync(
            normalizedSymbol,
            normalizedLimit,
            normalizedPeriod,
            ct);

        return Ok(new
        {
            Symbol = normalizedSymbol,
            Period = normalizedPeriod,
            Count = rows?.Count ?? 0,
            Items = rows
        });
    }

    /// <summary>
    /// Fetches cash flow rows from FMP's stable API, newest first.
    /// </summary>
    /// <param name="symbol">Ticker symbol, for example AAPL.</param>
    /// <param name="period">Statement frequency: annual or quarter.</param>
    /// <param name="limit">Maximum number of rows to return, clamped to 1-20.</param>
    /// <param name="ct">Cancellation token for the request.</param>
    /// <response code="200">Returns cash flow rows.</response>
    [HttpGet("{symbol}/cash-flow/stable")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetCashFlowStable(
        string symbol,
        string period = "annual",
        int limit = 3,
        CancellationToken ct = default)
    {
        string normalizedSymbol = NormalizeSymbol(symbol);
        string normalizedPeriod = NormalizePeriod(period);
        int normalizedLimit = Math.Clamp(limit, StatementLimitMin, StatementLimitMax);

        var rows = await _fmp.GetCashFlowStableAsync(
            normalizedSymbol,
            normalizedLimit,
            normalizedPeriod,
            ct);

        return Ok(new
        {
            Symbol = normalizedSymbol,
            Period = normalizedPeriod,
            Count = rows?.Count ?? 0,
            Items = rows
        });
    }

    /// <summary>
    /// Returns a compact fundamentals snapshot from FMP's stable API.
    /// </summary>
    /// <param name="symbol">Ticker symbol, for example AAPL.</param>
    /// <param name="period">Statement frequency for income, balance, and cash data.</param>
    /// <param name="limit">Maximum rows per statement, clamped to 1-20.</param>
    /// <param name="ct">Cancellation token for the request.</param>
    /// <response code="200">Returns a snapshot. Individual sections may be null if one upstream request fails.</response>
    [HttpGet("{symbol}/snapshot/stable")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSnapshotStable(
        string symbol,
        string period = "annual",
        int limit = 3,
        CancellationToken ct = default)
    {
        string normalizedSymbol = NormalizeSymbol(symbol);
        string normalizedPeriod = NormalizePeriod(period);
        int normalizedLimit = Math.Clamp(limit, StatementLimitMin, StatementLimitMax);

        List<FmpClient.IncomeStatementStableRow>? income = null;
        List<FmpClient.BalanceSheetStableRow>? balance = null;
        List<FmpClient.CashFlowStableRow>? cash = null;
        FmpClient.KeyMetricsTtm? metrics = null;

        try
        {
            income = await _fmp.GetIncomeStatementStableAsync(
                normalizedSymbol,
                normalizedLimit,
                normalizedPeriod,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Income fetch failed for {Symbol}", normalizedSymbol);
        }

        try
        {
            balance = await _fmp.GetBalanceSheetStableAsync(
                normalizedSymbol,
                normalizedLimit,
                normalizedPeriod,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Balance fetch failed for {Symbol}", normalizedSymbol);
        }

        try
        {
            cash = await _fmp.GetCashFlowStableAsync(
                normalizedSymbol,
                normalizedLimit,
                normalizedPeriod,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cash flow fetch failed for {Symbol}", normalizedSymbol);
        }

        try
        {
            metrics = await _fmp.GetKeyMetricsTtmAsync(normalizedSymbol, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Metrics fetch failed for {Symbol}", normalizedSymbol);
        }

        return Ok(new
        {
            Symbol = normalizedSymbol,
            Period = normalizedPeriod,
            Income = income,
            Balance = balance,
            Cash = cash,
            Metrics = metrics
        });
    }

    /// <summary>
    /// Fetches and stores fundamentals for a symbol by calling the ingest endpoints.
    /// </summary>
    [HttpPost("refresh")]
    [Produces("application/json")]
    [SwaggerOperation(
        Summary = "Fetch and store fundamentals",
        Description = "Calls income, balance, and cash-flow ingest endpoints and returns inserted/skipped counters.")]
    [ProducesResponseType(typeof(FundamentalsRefreshResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RefreshFundamentals(
        [FromQuery, Required] string symbol,
        [FromQuery] string period = "annual",
        [FromQuery] int years = 5,
        [FromServices] IHttpClientFactory? httpFactory = null,
        CancellationToken ct = default)
    {
        Guard.BadRequestIf(string.IsNullOrWhiteSpace(symbol), "Symbol required.");

        string normalizedSymbol = NormalizeSymbol(symbol);

        string normalizedPeriod = NormalizePeriod(period);
        if (normalizedPeriod != "annual" && normalizedPeriod != "quarter")
        {
            throw new BadRequestException("Period must be 'annual' or 'quarter'.");
        }

        int normalizedYears = Math.Clamp(years, RefreshYearsMin, RefreshYearsMax);
        int limit = Math.Max(1, normalizedYears);

        using HttpClient? fallbackClient = httpFactory is null ? CreateFallbackLocalClient() : null;
        HttpClient http = httpFactory?.CreateClient("self") ?? fallbackClient!;

        AddJsonAcceptHeader(http);

        var income = await TryHitIngestAsync(
            http,
            $"/api/ingest/income/{Uri.EscapeDataString(normalizedSymbol)}?period={normalizedPeriod}&limit={limit}",
            "Income",
            normalizedSymbol,
            ct);

        var balance = await TryHitIngestAsync(
            http,
            $"/api/ingest/balance/{Uri.EscapeDataString(normalizedSymbol)}?period={normalizedPeriod}&limit={limit}",
            "Balance",
            normalizedSymbol,
            ct);

        var cash = await TryHitIngestAsync(
            http,
            $"/api/ingest/cash/{Uri.EscapeDataString(normalizedSymbol)}?period={normalizedPeriod}&limit={limit}",
            "Cash flow",
            normalizedSymbol,
            ct);

        return Ok(new FundamentalsRefreshResponse(
            Ok: true,
            Symbol: normalizedSymbol,
            Period: normalizedPeriod,
            Years: normalizedYears,
            Inserted: new FundamentalsCounters(income.Inserted, balance.Inserted, cash.Inserted),
            Skipped: new FundamentalsCounters(income.Skipped, balance.Skipped, cash.Skipped)));
    }

    public sealed record RevenueDto
    {
        public string Symbol { get; init; } = string.Empty;
        public DateOnly PeriodEnd { get; init; }
        public decimal Revenue { get; init; }
        public string? Currency { get; init; }
    }

    public sealed record FundamentalsCounters(int Income, int Balance, int Cash);

    public sealed record FundamentalsRefreshResponse(
        bool Ok,
        string Symbol,
        string Period,
        int Years,
        FundamentalsCounters Inserted,
        FundamentalsCounters Skipped);

    private sealed record IngestCounters(int Inserted, int Skipped);

    private async Task<IngestCounters> TryHitIngestAsync(
        HttpClient http,
        string path,
        string sectionName,
        string symbol,
        CancellationToken ct)
    {
        try
        {
            return await HitIngestAsync(http, path, ct);
        }
        catch (HttpRequestException ex) when (IsPlanLimited(ex.Message))
        {
            _logger.LogInformation("{Section} ingest skipped due to provider plan limits for {Symbol}", sectionName, symbol);
            return new IngestCounters(Inserted: 0, Skipped: 0);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "{Section} ingest failed for {Symbol}", sectionName, symbol);
            return new IngestCounters(Inserted: 0, Skipped: 0);
        }
    }

    private static async Task<IngestCounters> HitIngestAsync(
        HttpClient http,
        string path,
        CancellationToken ct)
    {
        using HttpResponseMessage response = await http.GetAsync(path, ct);
        string raw = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"HTTP {(int)response.StatusCode} {response.ReasonPhrase} on {path}: {raw}");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(raw);
            JsonElement root = document.RootElement;

            return new IngestCounters(
                Inserted: ReadCounter(root, "upserted", "inserted"),
                Skipped: ReadCounter(root, "skipped"));
        }
        catch (JsonException ex)
        {
            throw new HttpRequestException($"HTTP 200 but invalid JSON on {path}: {raw}", ex);
        }
    }

    private static int ReadCounter(JsonElement root, params string[] names)
    {
        foreach (string name in names)
        {
            if (root.TryGetProperty(name, out JsonElement value) &&
                value.ValueKind == JsonValueKind.Number &&
                value.TryGetInt32(out int result))
            {
                return result;
            }
        }

        return 0;
    }

    private static HttpClient CreateFallbackLocalClient()
    {
        return new HttpClient
        {
            BaseAddress = new Uri("http://localhost:5046")
        };
    }

    private static void AddJsonAcceptHeader(HttpClient http)
    {
        if (!http.DefaultRequestHeaders.Accept.Any(header =>
                string.Equals(header.MediaType, "application/json", StringComparison.OrdinalIgnoreCase)))
        {
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }
    }

    private static bool IsPlanLimited(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        string normalizedMessage = message.ToLowerInvariant();

        return normalizedMessage.Contains("402 payment required") ||
            normalizedMessage.Contains("premium query parameter") ||
            normalizedMessage.Contains("not available under your current subscription") ||
            normalizedMessage.Contains("subscription page");
    }

    private static string NormalizeSymbol(string symbol)
    {
        return symbol.Trim().ToUpperInvariant();
    }

    private static string NormalizePeriod(string period)
    {
        return period.Trim().ToLowerInvariant();
    }

    private static List<RevenueDto> ToRevenueDtos(
        string symbol,
        IEnumerable<FmpClient.RevenuePoint> points)
    {
        return points
            .Select(point => new RevenueDto
            {
                Symbol = symbol,
                PeriodEnd = point.PeriodEnd,
                Revenue = point.Revenue,
                Currency = point.Currency
            })
            .ToList();
    }

    private static List<RevenueDto> ToRevenueDtos(
        string symbol,
        IEnumerable<AlphaVantageClient.AvRevenuePoint> points)
    {
        return points
            .Select(point => new RevenueDto
            {
                Symbol = symbol,
                PeriodEnd = point.PeriodEnd,
                Revenue = point.Revenue,
                Currency = point.Currency
            })
            .ToList();
    }
}
