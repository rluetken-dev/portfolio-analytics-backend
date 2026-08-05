using Microsoft.EntityFrameworkCore;
using Portfolio.Api.Data;
using Portfolio.Api.Models;

namespace Portfolio.Api.Services;

public sealed class MaintenanceService
{
    private readonly AppDbContext _db;
    private readonly ILogger<MaintenanceService> _logger;

    public MaintenanceService(AppDbContext db, ILogger<MaintenanceService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<int> PruneAsync(
        int? maxAgeDays = 3 * 365,
        int? keepPerSymbol = null,
        CancellationToken ct = default)
    {
        int totalDeleted = 0;

        if (maxAgeDays is > 0)
        {
            DateOnly cutoff = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-maxAgeDays.Value));

            totalDeleted += await _db.Prices
                .Where(price => price.TradingDate < cutoff)
                .ExecuteDeleteAsync(ct);
        }

        if (keepPerSymbol is > 0)
        {
            int keep = keepPerSymbol.Value;

            List<int> idsToDelete = await _db.Prices
                .GroupBy(price => price.TickerId)
                .SelectMany(group => group
                    .OrderByDescending(price => price.TradingDate)
                    .Skip(keep)
                    .Select(price => price.Id))
                .ToListAsync(ct);

            if (idsToDelete.Count > 0)
            {
                totalDeleted += await _db.Prices
                    .Where(price => idsToDelete.Contains(price.Id))
                    .ExecuteDeleteAsync(ct);
            }
        }

        return totalDeleted;
    }

    public async Task VacuumAnalyzeAsync(CancellationToken ct = default)
    {
        await _db.Database.CloseConnectionAsync();
        await _db.Database.GetDbConnection().OpenAsync(ct);

        using var command = _db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "VACUUM; ANALYZE;";

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> TruncateAllAsync(CancellationToken ct = default)
    {
        return await _db.Prices.ExecuteDeleteAsync(ct);
    }

    public async Task<int> UpsertTickersAsync(
        IEnumerable<(string Symbol, string? Name)> rows,
        bool overwriteExistingName = false,
        bool createIfMissing = true,
        CancellationToken ct = default)
    {
        if (rows is null)
        {
            return 0;
        }

        var input = rows
            .Select(row => new
            {
                Symbol = NormalizeSymbol(row.Symbol),
                Name = NormalizeNullableText(row.Name)
            })
            .Where(row => !string.IsNullOrWhiteSpace(row.Symbol))
            .GroupBy(row => row.Symbol)
            .Select(group => new
            {
                Symbol = group.Key,
                Name = group.Select(row => row.Name).FirstOrDefault(name => name is not null)
            })
            .ToList();

        if (input.Count == 0)
        {
            return 0;
        }

        List<string> symbols = input
            .Select(row => row.Symbol)
            .ToList();

        Dictionary<string, Ticker> existingTickers = await _db.Tickers
            .Where(ticker => symbols.Contains(ticker.Symbol))
            .ToDictionaryAsync(ticker => ticker.Symbol, ct);

        var tickersToInsert = new List<Ticker>();
        int updated = 0;
        int skippedInserts = 0;

        foreach (var row in input)
        {
            if (existingTickers.TryGetValue(row.Symbol, out Ticker? ticker))
            {
                if (row.Name is not null &&
                    (overwriteExistingName || string.IsNullOrWhiteSpace(ticker.Name)) &&
                    ticker.Name != row.Name)
                {
                    ticker.Name = row.Name;
                    updated++;
                }

                continue;
            }

            if (!createIfMissing)
            {
                skippedInserts++;
                continue;
            }

            tickersToInsert.Add(new Ticker
            {
                Symbol = row.Symbol,
                Name = row.Name ?? row.Symbol
            });
        }

        if (skippedInserts > 0)
        {
            _logger.LogInformation(
                "Skipped {Count} ticker insert(s) because createIfMissing is false.",
                skippedInserts);
        }

        if (tickersToInsert.Count > 0)
        {
            await _db.Tickers.AddRangeAsync(tickersToInsert, ct);
        }

        if (tickersToInsert.Count > 0 || updated > 0)
        {
            await _db.SaveChangesAsync(ct);
        }

        return tickersToInsert.Count + updated;
    }

    public async Task<int> ClearAllTickerSectorsAsync(CancellationToken ct = default)
    {
        return await _db.Tickers
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(ticker => ticker.Sector, (string?)null),
                ct);
    }

    public async Task<DeleteTickerResult> DeleteTickerAsync(
        string symbol,
        CancellationToken ct = default)
    {
        string normalizedSymbol = NormalizeSymbol(symbol);

        if (string.IsNullOrWhiteSpace(normalizedSymbol))
        {
            return new DeleteTickerResult(
                Symbol: string.Empty,
                PricesDeleted: 0,
                IncomeDeleted: 0,
                BalanceDeleted: 0,
                CashDeleted: 0,
                TickerDeleted: 0);
        }

        _logger.LogInformation(
            "Deleting ticker data for {Symbol}.",
            normalizedSymbol);

        Ticker? ticker = await _db.Tickers
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.Symbol == normalizedSymbol, ct);

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        int pricesDeleted = 0;

        if (ticker is not null)
        {
            pricesDeleted = await _db.Prices
                .Where(price => price.TickerId == ticker.Id)
                .ExecuteDeleteAsync(ct);
        }

        int incomeDeleted = await _db.IncomeStatements
            .Where(row => row.Symbol == normalizedSymbol)
            .ExecuteDeleteAsync(ct);

        int balanceDeleted = await _db.BalanceSheets
            .Where(row => row.Symbol == normalizedSymbol)
            .ExecuteDeleteAsync(ct);

        int cashDeleted = await _db.CashFlows
            .Where(row => row.Symbol == normalizedSymbol)
            .ExecuteDeleteAsync(ct);

        int tickerDeleted = 0;

        if (ticker is not null)
        {
            tickerDeleted = await _db.Tickers
                .Where(row => row.Id == ticker.Id)
                .ExecuteDeleteAsync(ct);
        }

        await transaction.CommitAsync(ct);

        return new DeleteTickerResult(
            Symbol: normalizedSymbol,
            PricesDeleted: pricesDeleted,
            IncomeDeleted: incomeDeleted,
            BalanceDeleted: balanceDeleted,
            CashDeleted: cashDeleted,
            TickerDeleted: tickerDeleted);
    }

    public sealed record DeleteTickerResult(
        string Symbol,
        int PricesDeleted,
        int IncomeDeleted,
        int BalanceDeleted,
        int CashDeleted,
        int TickerDeleted);

    private static string NormalizeSymbol(string? symbol)
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