using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Portfolio.Api.Data;
using Portfolio.Api.Data.Entities;

namespace Portfolio.Api.Services;

public sealed class CashFlowIngestService
{
    private const int MaxAnnualLimit = 40;
    private const int MaxQuarterlyLimit = 5;
    private const string AnnualPeriod = "annual";
    private const string QuarterPeriod = "quarter";
    private const string DateFormat = "yyyy-MM-dd";

    private readonly AppDbContext _db;
    private readonly FmpClient _fmp;
    private readonly ILogger<CashFlowIngestService> _logger;

    public CashFlowIngestService(
        AppDbContext db,
        FmpClient fmp,
        ILogger<CashFlowIngestService> logger)
    {
        _db = db;
        _fmp = fmp;
        _logger = logger;
    }

    public async Task<int> IngestAsync(
        string symbol,
        string period = AnnualPeriod,
        int limit = 10,
        CancellationToken ct = default)
    {
        string normalizedSymbol = NormalizeSymbol(symbol);
        if (string.IsNullOrWhiteSpace(normalizedSymbol))
        {
            throw new ArgumentException("Symbol is required.", nameof(symbol));
        }

        string normalizedPeriod = NormalizePeriod(period);
        int normalizedLimit = NormalizeLimit(normalizedPeriod, limit);

        var rows = await _fmp.GetCashFlowStableAsync(
            normalizedSymbol,
            normalizedLimit,
            normalizedPeriod,
            ct);

        if (rows.Count == 0)
        {
            _logger.LogInformation(
                "No cash-flow rows returned for {Symbol} ({Period}).",
                normalizedSymbol,
                normalizedPeriod);

            return 0;
        }

        int changed = 0;

        foreach (var row in rows)
        {
            if (!TryParseDate(row.Date, out DateOnly date))
            {
                _logger.LogWarning(
                    "Skipping cash-flow row for {Symbol} because date '{Date}' is invalid.",
                    normalizedSymbol,
                    row.Date);

                continue;
            }

            CashFlowEntity? existing = await _db.CashFlows
                .FirstOrDefaultAsync(
                    entity =>
                        entity.Symbol == normalizedSymbol &&
                        entity.Date == date &&
                        entity.Frequency == normalizedPeriod,
                    ct);

            if (existing is null)
            {
                _db.CashFlows.Add(new CashFlowEntity
                {
                    Symbol = normalizedSymbol,
                    Date = date,
                    Frequency = normalizedPeriod,
                    ReportedCurrency = row.ReportedCurrency,
                    OperatingCashFlow = row.OperatingCashFlow,
                    CapitalExpenditure = row.CapitalExpenditure,
                    FreeCashFlow = row.FreeCashFlow,
                    NetIncome = row.NetIncome,
                    DepreciationAndAmortization = row.DepreciationAndAmortization,
                    ChangeInWorkingCapital = row.ChangeInWorkingCapital
                });
            }
            else
            {
                existing.ReportedCurrency = row.ReportedCurrency;
                existing.OperatingCashFlow = row.OperatingCashFlow;
                existing.CapitalExpenditure = row.CapitalExpenditure;
                existing.FreeCashFlow = row.FreeCashFlow;
                existing.NetIncome = row.NetIncome;
                existing.DepreciationAndAmortization = row.DepreciationAndAmortization;
                existing.ChangeInWorkingCapital = row.ChangeInWorkingCapital;
            }

            changed++;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Cash-flow ingest completed for {Symbol} ({Period}): {Count} row(s) inserted or updated.",
            normalizedSymbol,
            normalizedPeriod,
            changed);

        return changed;
    }

    private static string NormalizeSymbol(string symbol)
    {
        return string.IsNullOrWhiteSpace(symbol)
            ? string.Empty
            : symbol.Trim().ToUpperInvariant();
    }

    private static string NormalizePeriod(string period)
    {
        if (string.Equals(period, QuarterPeriod, StringComparison.OrdinalIgnoreCase))
        {
            return QuarterPeriod;
        }

        return AnnualPeriod;
    }

    private int NormalizeLimit(string period, int limit)
    {
        int maxLimit = period == QuarterPeriod
            ? MaxQuarterlyLimit
            : MaxAnnualLimit;

        int normalizedLimit = Math.Clamp(limit, 1, maxLimit);

        if (period == QuarterPeriod && limit > MaxQuarterlyLimit)
        {
            _logger.LogInformation(
                "Capping quarterly cash-flow limit from {RequestedLimit} to {AppliedLimit}.",
                limit,
                MaxQuarterlyLimit);
        }

        return normalizedLimit;
    }

    private static bool TryParseDate(string? value, out DateOnly date)
    {
        return DateOnly.TryParseExact(
            value,
            DateFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }
}