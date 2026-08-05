using Microsoft.EntityFrameworkCore;
using Portfolio.Api.Data;
using Portfolio.Api.Data.Entities;
using Portfolio.Api.Models;

namespace Portfolio.Api.Services;

public sealed class SeedService : ISeedService
{
    private const string AnnualFrequency = "annual";
    private const string SeedSource = "seed";
    private const string DefaultCurrency = "USD";

    private readonly AppDbContext _db;

    public SeedService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<(bool created, bool updated)> SeedTickerAsync(
        string symbol,
        string? name,
        CancellationToken ct)
    {
        string normalizedSymbol = RequireSymbol(symbol);
        string? normalizedName = NormalizeNullableText(name);

        Ticker? ticker = await _db.Tickers
            .FirstOrDefaultAsync(x => x.Symbol == normalizedSymbol, ct);

        if (ticker is null)
        {
            _db.Tickers.Add(new Ticker
            {
                Symbol = normalizedSymbol,
                Name = normalizedName ?? normalizedSymbol
            });

            await _db.SaveChangesAsync(ct);
            return (created: true, updated: false);
        }

        if (normalizedName is null || ticker.Name == normalizedName)
        {
            return (created: false, updated: false);
        }

        ticker.Name = normalizedName;
        await _db.SaveChangesAsync(ct);

        return (created: false, updated: true);
    }

    public async Task<(bool created, bool updated)> SeedTickerProfileAsync(
        string symbol,
        string? name,
        string? sector,
        CancellationToken ct)
    {
        string normalizedSymbol = RequireSymbol(symbol);
        string? normalizedName = NormalizeNullableText(name);
        string? normalizedSector = NormalizeNullableText(sector);

        Ticker? ticker = await _db.Tickers
            .FirstOrDefaultAsync(x => x.Symbol == normalizedSymbol, ct);

        if (ticker is null)
        {
            _db.Tickers.Add(new Ticker
            {
                Symbol = normalizedSymbol,
                Name = normalizedName ?? normalizedSymbol,
                Sector = normalizedSector
            });

            await _db.SaveChangesAsync(ct);
            return (created: true, updated: false);
        }

        bool updated = false;

        if (normalizedName is not null && ticker.Name != normalizedName)
        {
            ticker.Name = normalizedName;
            updated = true;
        }

        if (normalizedSector is not null && ticker.Sector != normalizedSector)
        {
            ticker.Sector = normalizedSector;
            updated = true;
        }

        if (updated)
        {
            await _db.SaveChangesAsync(ct);
        }

        return (created: false, updated);
    }

    public async Task SeedAnnualAsync(
        string symbol,
        int year,
        long netIncome,
        long equity,
        CancellationToken ct)
    {
        string normalizedSymbol = RequireSymbol(symbol);
        DateOnly date = GetAnnualDate(year);

        await EnsureTickerAsync(normalizedSymbol, normalizedSymbol, ct);

        IncomeStatementEntity income = await GetOrCreateIncomeStatementAsync(
            normalizedSymbol,
            date,
            ct);

        income.NetIncome = netIncome;

        BalanceSheetEntity balanceSheet = await GetOrCreateBalanceSheetAsync(
            normalizedSymbol,
            date,
            ct);

        balanceSheet.TotalStockholdersEquity = equity;

        await _db.SaveChangesAsync(ct);
    }

    public async Task SeedRevenueAsync(
        string symbol,
        int year,
        long revenue,
        CancellationToken ct)
    {
        string normalizedSymbol = RequireSymbol(symbol);
        DateOnly date = GetAnnualDate(year);

        await EnsureTickerAsync(normalizedSymbol, normalizedSymbol, ct);

        IncomeStatementEntity income = await GetOrCreateIncomeStatementAsync(
            normalizedSymbol,
            date,
            ct);

        income.Revenue = revenue;

        await _db.SaveChangesAsync(ct);
    }

    public async Task SeedAssetsAsync(
        string symbol,
        int year,
        long totalAssets,
        CancellationToken ct)
    {
        string normalizedSymbol = RequireSymbol(symbol);
        DateOnly date = GetAnnualDate(year);

        await EnsureTickerAsync(normalizedSymbol, normalizedSymbol, ct);

        BalanceSheetEntity balanceSheet = await GetOrCreateBalanceSheetAsync(
            normalizedSymbol,
            date,
            ct);

        balanceSheet.TotalAssets = totalAssets;

        await _db.SaveChangesAsync(ct);
    }

    public async Task SeedLiabilitiesAsync(
        string symbol,
        int year,
        long totalLiabilities,
        CancellationToken ct)
    {
        string normalizedSymbol = RequireSymbol(symbol);
        DateOnly date = GetAnnualDate(year);

        await EnsureTickerAsync(normalizedSymbol, normalizedSymbol, ct);

        BalanceSheetEntity balanceSheet = await GetOrCreateBalanceSheetAsync(
            normalizedSymbol,
            date,
            ct);

        balanceSheet.TotalLiabilities = totalLiabilities;

        await _db.SaveChangesAsync(ct);
    }

    public async Task SeedSharesAsync(
        string symbol,
        int year,
        long shares,
        CancellationToken ct)
    {
        string normalizedSymbol = RequireSymbol(symbol);
        DateOnly date = GetAnnualDate(year);

        await EnsureTickerAsync(normalizedSymbol, normalizedSymbol, ct);

        IncomeStatementEntity income = await GetOrCreateIncomeStatementAsync(
            normalizedSymbol,
            date,
            ct);

        income.WeightedAverageShsOut = shares;

        await _db.SaveChangesAsync(ct);
    }

    public async Task SeedOperatingCashFlowAsync(
        string symbol,
        int year,
        long operatingCashFlow,
        CancellationToken ct)
    {
        string normalizedSymbol = RequireSymbol(symbol);
        DateOnly date = GetAnnualDate(year);

        await EnsureTickerAsync(normalizedSymbol, normalizedSymbol, ct);

        CashFlowEntity cashFlow = await GetOrCreateCashFlowAsync(
            normalizedSymbol,
            date,
            ct);

        cashFlow.OperatingCashFlow = operatingCashFlow;

        await _db.SaveChangesAsync(ct);
    }

    public async Task SeedCapitalExpendituresAsync(
        string symbol,
        int year,
        long capitalExpenditures,
        CancellationToken ct)
    {
        string normalizedSymbol = RequireSymbol(symbol);
        DateOnly date = GetAnnualDate(year);

        await EnsureTickerAsync(normalizedSymbol, normalizedSymbol, ct);

        CashFlowEntity cashFlow = await GetOrCreateCashFlowAsync(
            normalizedSymbol,
            date,
            ct);

        cashFlow.CapitalExpenditure = capitalExpenditures;

        await _db.SaveChangesAsync(ct);
    }

    public async Task SeedChangeInWorkingCapitalAsync(
        string symbol,
        int year,
        long changeInWorkingCapital,
        CancellationToken ct)
    {
        string normalizedSymbol = RequireSymbol(symbol);
        DateOnly date = GetAnnualDate(year);

        await EnsureTickerAsync(normalizedSymbol, normalizedSymbol, ct);

        CashFlowEntity cashFlow = await GetOrCreateCashFlowAsync(
            normalizedSymbol,
            date,
            ct);

        cashFlow.ChangeInWorkingCapital = changeInWorkingCapital;

        await _db.SaveChangesAsync(ct);
    }

    public async Task SeedPriceAsync(
        string symbol,
        DateOnly date,
        decimal close,
        CancellationToken ct)
    {
        await SeedFullPriceAsync(
            symbol,
            date,
            open: close,
            high: close,
            low: close,
            close: close,
            volume: 0,
            ct);
    }

    public async Task SeedFullPriceAsync(
        string symbol,
        DateOnly date,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        long volume,
        CancellationToken ct)
    {
        string normalizedSymbol = RequireSymbol(symbol);

        Ticker ticker = await EnsureTickerAsync(
            normalizedSymbol,
            normalizedSymbol,
            ct);

        Price? price = await _db.Prices
            .FirstOrDefaultAsync(
                x => x.TickerId == ticker.Id && x.TradingDate == date,
                ct);

        if (price is null)
        {
            price = new Price
            {
                TickerId = ticker.Id,
                TradingDate = date,
                Source = SeedSource
            };

            _db.Prices.Add(price);
        }

        price.Open = open;
        price.High = high;
        price.Low = low;
        price.Close = close;
        price.AdjustedClose = close;
        price.Volume = volume;
        price.Source = SeedSource;
        price.UpdatedUtc = DateTime.UtcNow;

        ticker.LastPriceUpdate = date.ToDateTime(TimeOnly.MinValue);

        await _db.SaveChangesAsync(ct);
    }

    private async Task<Ticker> EnsureTickerAsync(
        string symbol,
        string? name,
        CancellationToken ct)
    {
        Ticker? ticker = await _db.Tickers
            .FirstOrDefaultAsync(x => x.Symbol == symbol, ct);

        if (ticker is not null)
        {
            return ticker;
        }

        ticker = new Ticker
        {
            Symbol = symbol,
            Name = NormalizeNullableText(name) ?? symbol
        };

        _db.Tickers.Add(ticker);
        await _db.SaveChangesAsync(ct);

        return ticker;
    }

    private async Task<IncomeStatementEntity> GetOrCreateIncomeStatementAsync(
        string symbol,
        DateOnly date,
        CancellationToken ct)
    {
        IncomeStatementEntity? entity = await _db.IncomeStatements
            .FirstOrDefaultAsync(
                x => x.Symbol == symbol &&
                     x.Date == date &&
                     x.Frequency == AnnualFrequency,
                ct);

        if (entity is not null)
        {
            return entity;
        }

        entity = new IncomeStatementEntity
        {
            Symbol = symbol,
            Date = date,
            Frequency = AnnualFrequency,
            ReportedCurrency = DefaultCurrency
        };

        _db.IncomeStatements.Add(entity);

        return entity;
    }

    private async Task<BalanceSheetEntity> GetOrCreateBalanceSheetAsync(
        string symbol,
        DateOnly date,
        CancellationToken ct)
    {
        BalanceSheetEntity? entity = await _db.BalanceSheets
            .FirstOrDefaultAsync(
                x => x.Symbol == symbol &&
                     x.Date == date &&
                     x.Frequency == AnnualFrequency,
                ct);

        if (entity is not null)
        {
            return entity;
        }

        entity = new BalanceSheetEntity
        {
            Symbol = symbol,
            Date = date,
            Frequency = AnnualFrequency,
            ReportedCurrency = DefaultCurrency
        };

        _db.BalanceSheets.Add(entity);

        return entity;
    }

    private async Task<CashFlowEntity> GetOrCreateCashFlowAsync(
        string symbol,
        DateOnly date,
        CancellationToken ct)
    {
        CashFlowEntity? entity = await _db.CashFlows
            .FirstOrDefaultAsync(
                x => x.Symbol == symbol &&
                     x.Date == date &&
                     x.Frequency == AnnualFrequency,
                ct);

        if (entity is not null)
        {
            return entity;
        }

        entity = new CashFlowEntity
        {
            Symbol = symbol,
            Date = date,
            Frequency = AnnualFrequency,
            ReportedCurrency = DefaultCurrency
        };

        _db.CashFlows.Add(entity);

        return entity;
    }

    private static DateOnly GetAnnualDate(int year)
    {
        return new DateOnly(year, 12, 31);
    }

    private static string RequireSymbol(string symbol)
    {
        string normalizedSymbol = NormalizeSymbol(symbol);

        if (string.IsNullOrWhiteSpace(normalizedSymbol))
        {
            throw new ArgumentException("Symbol is required.", nameof(symbol));
        }

        return normalizedSymbol;
    }

    private static string NormalizeSymbol(string symbol)
    {
        return string.IsNullOrWhiteSpace(symbol)
            ? string.Empty
            : symbol.Trim().ToUpperInvariant();
    }

    private static string? NormalizeNullableText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}