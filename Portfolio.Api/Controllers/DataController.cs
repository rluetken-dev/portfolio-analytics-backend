using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Api.Data;
using Portfolio.Api.Data.Entities;

namespace Portfolio.Api.Controllers;

/// <summary>
/// Provides read-only endpoints for inspecting stored financial data.
/// </summary>
[ApiController]
[Route("api/data")]
public sealed class DataController : ControllerBase
{
    private const int MinLimit = 1;
    private const int MaxLimit = 100;

    private readonly AppDbContext _db;

    public DataController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Returns stored income statements, newest first.
    /// </summary>
    /// <param name="symbol">Ticker symbol, for example AAPL.</param>
    /// <param name="period">Statement frequency: annual or quarter.</param>
    /// <param name="limit">Maximum number of rows to return, clamped to 1-100.</param>
    /// <param name="ct">Cancellation token for the request.</param>
    /// <response code="200">Returns stored income statements.</response>
    [HttpGet("income/{symbol}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetIncome(
        string symbol,
        string period = "annual",
        int limit = 10,
        CancellationToken ct = default)
    {
        string normalizedSymbol = NormalizeSymbol(symbol);
        string normalizedPeriod = NormalizePeriod(period);
        int normalizedLimit = NormalizeLimit(limit);

        var items = await _db.IncomeStatements
            .Where(statement => statement.Symbol == normalizedSymbol && statement.Frequency == normalizedPeriod)
            .OrderByDescending(statement => statement.Date)
            .Take(normalizedLimit)
            .ToListAsync(ct);

        return Ok(new
        {
            Symbol = normalizedSymbol,
            Period = normalizedPeriod,
            Count = items.Count,
            Items = items
        });
    }

    /// <summary>
    /// Returns stored balance sheet rows, newest first.
    /// </summary>
    /// <param name="symbol">Ticker symbol, for example AAPL.</param>
    /// <param name="period">Statement frequency: annual or quarter.</param>
    /// <param name="limit">Maximum number of rows to return, clamped to 1-100.</param>
    /// <param name="ct">Cancellation token for the request.</param>
    /// <response code="200">Returns stored balance sheet rows.</response>
    [HttpGet("balance/{symbol}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBalance(
        string symbol,
        string period = "annual",
        int limit = 10,
        CancellationToken ct = default)
    {
        string normalizedSymbol = NormalizeSymbol(symbol);
        string normalizedPeriod = NormalizePeriod(period);
        int normalizedLimit = NormalizeLimit(limit);

        var items = await _db.BalanceSheets
            .Where(statement => statement.Symbol == normalizedSymbol && statement.Frequency == normalizedPeriod)
            .OrderByDescending(statement => statement.Date)
            .Take(normalizedLimit)
            .ToListAsync(ct);

        return Ok(new
        {
            Symbol = normalizedSymbol,
            Period = normalizedPeriod,
            Count = items.Count,
            Items = items
        });
    }

    /// <summary>
    /// Returns stored cash flow rows, newest first.
    /// </summary>
    /// <param name="symbol">Ticker symbol, for example AAPL.</param>
    /// <param name="period">Statement frequency: annual or quarter.</param>
    /// <param name="limit">Maximum number of rows to return, clamped to 1-100.</param>
    /// <param name="ct">Cancellation token for the request.</param>
    /// <response code="200">Returns stored cash flow rows.</response>
    [HttpGet("cash/{symbol}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCash(
        string symbol,
        string period = "annual",
        int limit = 10,
        CancellationToken ct = default)
    {
        string normalizedSymbol = NormalizeSymbol(symbol);
        string normalizedPeriod = NormalizePeriod(period);
        int normalizedLimit = NormalizeLimit(limit);

        var items = await _db.CashFlows
            .Where(statement => statement.Symbol == normalizedSymbol && statement.Frequency == normalizedPeriod)
            .OrderByDescending(statement => statement.Date)
            .Take(normalizedLimit)
            .ToListAsync(ct);

        return Ok(new
        {
            Symbol = normalizedSymbol,
            Period = normalizedPeriod,
            Count = items.Count,
            Items = items
        });
    }

    /// <summary>
    /// Returns trailing twelve months metrics from stored quarterly rows.
    /// </summary>
    /// <param name="symbol">Ticker symbol, for example AAPL.</param>
    /// <param name="ct">Cancellation token for the request.</param>
    /// <response code="200">Returns TTM sums when complete quarterly data exists.</response>
    [HttpGet("ttm/{symbol}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTtm(string symbol, CancellationToken ct = default)
    {
        string normalizedSymbol = NormalizeSymbol(symbol);

        var incomeRows = await GetLatestQuarterlyIncomeRowsAsync(normalizedSymbol, ct);
        var cashFlowRows = await GetLatestQuarterlyCashFlowRowsAsync(normalizedSymbol, ct);

        long? ttmRevenue = SumIfComplete(incomeRows.Select(row => row.Revenue).ToList());
        long? ttmNetIncome = SumIfComplete(incomeRows.Select(row => row.NetIncome).ToList());
        long? ttmFreeCashFlow = SumIfComplete(cashFlowRows.Select(row => row.FreeCashFlow).ToList());

        return Ok(new
        {
            Symbol = normalizedSymbol,
            Period = "quarter",
            Has4IncomeQuarters = incomeRows.Count == 4,
            Has4CashQuarters = cashFlowRows.Count == 4,
            Currency = GetSingleCurrencyOrNull(incomeRows),
            RevenueTtm = ttmRevenue,
            NetIncomeTtm = ttmNetIncome,
            FreeCashFlowTtm = ttmFreeCashFlow,
            QuartersIncome = incomeRows.Select(row => row.Date).ToList(),
            QuartersCash = cashFlowRows.Select(row => row.Date).ToList()
        });
    }

    /// <summary>
    /// Returns trailing twelve months ratios from stored quarterly rows.
    /// </summary>
    /// <param name="symbol">Ticker symbol, for example AAPL.</param>
    /// <param name="ct">Cancellation token for the request.</param>
    /// <response code="200">Returns TTM sums and margins when complete quarterly data exists.</response>
    [HttpGet("ttm/{symbol}/ratios")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTtmRatios(string symbol, CancellationToken ct = default)
    {
        string normalizedSymbol = NormalizeSymbol(symbol);

        var incomeRows = await GetLatestQuarterlyIncomeRowsAsync(normalizedSymbol, ct);
        var cashFlowRows = await GetLatestQuarterlyCashFlowRowsAsync(normalizedSymbol, ct);

        long? ttmRevenue = SumIfComplete(incomeRows.Select(row => row.Revenue).ToList());
        long? ttmNetIncome = SumIfComplete(incomeRows.Select(row => row.NetIncome).ToList());
        long? ttmFreeCashFlow = SumIfComplete(cashFlowRows.Select(row => row.FreeCashFlow).ToList());

        return Ok(new
        {
            Symbol = normalizedSymbol,
            Has4IncomeQuarters = incomeRows.Count == 4,
            Has4CashQuarters = cashFlowRows.Count == 4,
            Currency = GetSingleCurrencyOrNull(incomeRows),
            RevenueTtm = ttmRevenue,
            NetIncomeTtm = ttmNetIncome,
            FreeCashFlowTtm = ttmFreeCashFlow,
            NetMarginTtm = DivideOrNull(ttmNetIncome, ttmRevenue),
            FcfMarginTtm = DivideOrNull(ttmFreeCashFlow, ttmRevenue)
        });
    }

    private Task<List<IncomeStatementEntity>> GetLatestQuarterlyIncomeRowsAsync(
        string symbol,
        CancellationToken ct)
    {
        return _db.IncomeStatements
            .Where(statement => statement.Symbol == symbol && statement.Frequency == "quarter")
            .OrderByDescending(statement => statement.Date)
            .Take(4)
            .ToListAsync(ct);
    }

    private Task<List<CashFlowEntity>> GetLatestQuarterlyCashFlowRowsAsync(
        string symbol,
        CancellationToken ct)
    {
        return _db.CashFlows
            .Where(statement => statement.Symbol == symbol && statement.Frequency == "quarter")
            .OrderByDescending(statement => statement.Date)
            .Take(4)
            .ToListAsync(ct);
    }

    private static string NormalizeSymbol(string symbol)
    {
        return symbol.Trim().ToUpperInvariant();
    }

    private static string NormalizePeriod(string period)
    {
        return period.Trim().ToLowerInvariant();
    }

    private static int NormalizeLimit(int limit)
    {
        return Math.Clamp(limit, MinLimit, MaxLimit);
    }

    private static long? SumIfComplete(IList<long?> values)
    {
        return values.Count == 4 && values.All(value => value.HasValue)
            ? values.Sum(value => value!.Value)
            : null;
    }

    private static double? DivideOrNull(long? numerator, long? denominator)
    {
        return numerator.HasValue && denominator.HasValue && denominator.Value != 0
            ? (double)numerator.Value / denominator.Value
            : null;
    }

    private static string? GetSingleCurrencyOrNull(IEnumerable<IncomeStatementEntity> incomeRows)
    {
        var currencies = incomeRows
            .Select(row => row.ReportedCurrency)
            .Where(currency => !string.IsNullOrWhiteSpace(currency))
            .Distinct()
            .ToList();

        return currencies.Count == 1 ? currencies[0] : null;
    }
}
