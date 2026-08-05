using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Portfolio.Api.Data;
using Portfolio.Api.Data.Entities;
using Portfolio.Api.Models;

namespace Portfolio.Api.Services;

public sealed class BalanceSheetIngestService
{
    private const int MaxAnnualLimit = 40;
    private const int MaxQuarterlyLimit = 5;
    private const string AnnualPeriod = "annual";
    private const string QuarterPeriod = "quarter";

    private readonly AppDbContext _db;
    private readonly FmpClient _fmp;
    private readonly ILogger<BalanceSheetIngestService> _logger;

    public BalanceSheetIngestService(
        AppDbContext db,
        FmpClient fmp,
        ILogger<BalanceSheetIngestService> logger)
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

        await EnsureTickerAsync(normalizedSymbol, ct);

        var rows = await _fmp.GetBalanceSheetStableAsync(
            normalizedSymbol,
            normalizedLimit,
            normalizedPeriod,
            ct);

        if (rows.Count == 0)
        {
            _logger.LogInformation(
                "No balance-sheet rows returned for {Symbol} ({Period}).",
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
                    "Skipping balance-sheet row for {Symbol} because date '{Date}' is invalid.",
                    normalizedSymbol,
                    row.Date);

                continue;
            }

            BalanceSheetEntity? existing = await _db.BalanceSheets
                .FirstOrDefaultAsync(
                    entity =>
                        entity.Symbol == normalizedSymbol &&
                        entity.Date == date &&
                        entity.Frequency == normalizedPeriod,
                    ct);

            if (existing is null)
            {
                _db.BalanceSheets.Add(new BalanceSheetEntity
                {
                    Symbol = normalizedSymbol,
                    Date = date,
                    Frequency = normalizedPeriod,
                    ReportedCurrency = row.ReportedCurrency,
                    TotalAssets = row.TotalAssets,
                    TotalLiabilities = row.TotalLiabilities,
                    TotalStockholdersEquity = row.TotalStockholdersEquity,
                    CashAndCashEquivalents = row.CashAndCashEquivalents
                });
            }
            else
            {
                existing.ReportedCurrency = row.ReportedCurrency;
                existing.TotalAssets = row.TotalAssets;
                existing.TotalLiabilities = row.TotalLiabilities;
                existing.TotalStockholdersEquity = row.TotalStockholdersEquity;
                existing.CashAndCashEquivalents = row.CashAndCashEquivalents;
            }

            changed++;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Balance-sheet ingest completed for {Symbol} ({Period}): {Count} row(s) inserted or updated.",
            normalizedSymbol,
            normalizedPeriod,
            changed);

        return changed;
    }

    private async Task EnsureTickerAsync(string symbol, CancellationToken ct)
    {
        bool exists = await _db.Tickers.AnyAsync(ticker => ticker.Symbol == symbol, ct);
        if (exists)
        {
            return;
        }

        _db.Tickers.Add(new Ticker
        {
            Symbol = symbol,
            Name = symbol
        });

        await _db.SaveChangesAsync(ct);
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
                "Capping quarterly balance-sheet limit from {RequestedLimit} to {AppliedLimit}.",
                limit,
                MaxQuarterlyLimit);
        }

        return normalizedLimit;
    }

    private static bool TryParseDate(string? value, out DateOnly date)
    {
        return DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }
}