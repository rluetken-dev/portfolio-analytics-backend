using Microsoft.EntityFrameworkCore;
using Portfolio.Api.Data;
using Portfolio.Api.Models;

namespace Portfolio.Api.Services;

/// <summary>
/// Provides database housekeeping functionality:
/// - Delete outdated rows
/// - Cap per-symbol history length
/// - Run VACUUM / ANALYZE to reclaim space and refresh stats
/// </summary>
public class MaintenanceService
{
    private readonly AppDbContext _db;
    private readonly ILogger<MaintenanceService> _log;

    public MaintenanceService(AppDbContext db, ILogger<MaintenanceService> log)
    {
        _db = db;
        _log = log;
    }

    /// <summary>
    /// Deletes old price rows from the database.
    /// 
    /// Two optional constraints:
    /// - <paramref name="maxAgeDays"/>: delete everything older than today - N days.
    /// - <paramref name="keepPerSymbol"/>: keep only the most recent N rows per symbol.
    /// 
    /// If both are set, both constraints apply (union of deletions).
    /// </summary>
    /// <returns>Total number of rows deleted.</returns>
    public async Task<int> PruneAsync(
        int? maxAgeDays = 3 * 365,
        int? keepPerSymbol = null,
        CancellationToken ct = default)
    {
        var totalDeleted = 0;

        // --- 1) Age-based delete ---
        if (maxAgeDays is > 0)
        {
            var cutoff = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-maxAgeDays.Value));
            totalDeleted += await _db.Database.ExecuteSqlInterpolatedAsync(
                $@"DELETE FROM Prices WHERE AsOfDate < {cutoff}", ct);
        }

        // --- 2) Cap rows per symbol ---
        if (keepPerSymbol is > 0)
        {
            // Use ROW_NUMBER window function to rank rows per symbol by date (newest first).
            // Delete rows whose rank > keepPerSymbol.
            var cap = keepPerSymbol.Value;
            totalDeleted += await _db.Database.ExecuteSqlInterpolatedAsync($@"
                WITH ranked AS (
                    SELECT Id,
                        ROW_NUMBER() OVER (PARTITION BY Symbol ORDER BY AsOfDate DESC) AS rn
                    FROM Prices
                )
                DELETE FROM Prices
                WHERE Id IN (SELECT Id FROM ranked WHERE rn > {cap});
            ", ct);
        }

        return totalDeleted;
    }

    /// <summary>
    /// Executes SQLite VACUUM and ANALYZE:
    /// - VACUUM: compacts the file and reclaims free space
    /// - ANALYZE: refreshes query planner statistics
    /// 
    /// Note: VACUUM cannot run inside an active transaction.
    /// </summary>
    public async Task VacuumAnalyzeAsync(CancellationToken ct = default)
    {
        // Ensure no active transaction before running VACUUM.
        await _db.Database.CloseConnectionAsync();
        await _db.Database.GetDbConnection().OpenAsync(ct);

        using var cmd = _db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "VACUUM; ANALYZE;";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Deletes all rows from the Prices table (hard reset).
    /// Use with caution – this wipes the database contents.
    /// </summary>
    public async Task<int> TruncateAllAsync(CancellationToken ct = default)
    {
        var deleted = await _db.Database.ExecuteSqlRawAsync("DELETE FROM Prices;", ct);
        return deleted;
    }

    /// <summary>
    /// Upsert tickers: creates missing symbols and updates Name when empty (or when overwrite is true).
    /// Returns the number of inserted + updated rows.
    /// </summary>
    public async Task<int> UpsertTickersAsync(
        IEnumerable<(string Symbol, string? Name)> rows,
        bool overwriteExistingName = false,
        CancellationToken ct = default)
    {
        if (rows is null) return 0;

        // Normalize & de-duplicate by symbol
        var input = rows
            .Select(r => (Symbol: (r.Symbol ?? "").Trim().ToUpperInvariant(),
                          Name: r.Name?.Trim()))
            .Where(r => !string.IsNullOrWhiteSpace(r.Symbol))
            .GroupBy(r => r.Symbol)
            .Select(g => (Symbol: g.Key, Name: g.Select(x => x.Name).FirstOrDefault(n => !string.IsNullOrEmpty(n))))
            .ToList();

        if (input.Count == 0) return 0;

        var symbols = input.Select(r => r.Symbol).ToList();

        var existing = await _db.Set<Ticker>()
            .Where(t => symbols.Contains(t.Symbol))
            .ToDictionaryAsync(t => t.Symbol, ct);

        var toInsert = new List<Ticker>();
        var updated = 0;

        foreach (var row in input)
        {
            if (existing.TryGetValue(row.Symbol, out var t))
            {
                // Update name if we have one and either overwrite is allowed or current is empty
                if (!string.IsNullOrWhiteSpace(row.Name) &&
                    (overwriteExistingName || string.IsNullOrWhiteSpace(t.Name)))
                {
                    t.Name = row.Name;
                    updated++;
                }
            }
            else
            {
                toInsert.Add(new Ticker
                {
                    Symbol = row.Symbol,
                    Name = row.Name
                });
            }
        }

        if (toInsert.Count > 0)
            await _db.Set<Ticker>().AddRangeAsync(toInsert, ct);

        if (toInsert.Count > 0 || updated > 0)
            await _db.SaveChangesAsync(ct);

        return toInsert.Count + updated;
    }

    // MaintenanceService.cs
    /// <summary>
    /// Sets <c>Ticker.Sector</c> to <c>NULL</c> for all rows.
    /// Useful to "reset" sectors before refilling them from an external source.
    /// </summary>
    /// <returns>Number of affected rows.</returns>
    public async Task<int> ClearAllTickerSectorsAsync(CancellationToken ct = default)
    {
        return await _db.Database.ExecuteSqlRawAsync("UPDATE Tickers SET Sector = NULL;", ct);
    }


    /// <summary>
    /// Result payload for a ticker hard-delete operation.
    /// English: Summarizes how many rows were removed across all related tables,
    /// allowing callers to log, assert in tests, or show UI feedback.
    /// </summary>
    /// <param name="Symbol">English: Normalized ticker symbol (uppercase) that was requested to delete.</param>
    /// <param name="PricesDeleted">English: Count of deleted daily price rows (child table by TickerId).</param>
    /// <param name="IncomeDeleted">English: Count of deleted income statement rows (filtered by Symbol).</param>
    /// <param name="BalanceDeleted">English: Count of deleted balance sheet rows (filtered by Symbol).</param>
    /// <param name="CashDeleted">English: Count of deleted cash flow rows (filtered by Symbol).</param>
    /// <param name="TickerDeleted">English: 1 if the Ticker row itself was deleted; 0 if it did not exist.</param>
    public sealed record DeleteTickerResult(
        string Symbol,
        int PricesDeleted,
        int IncomeDeleted,
        int BalanceDeleted,
        int CashDeleted,
        int TickerDeleted
    );

    /// <summary>
    /// Deletes all persisted data for a given ticker symbol in one transaction.
    /// English: Hard-delete a symbol (prices + fundamentals + ticker) atomically.
    /// </summary>https://chatgpt.com/c/68c11e64-5bcc-8330-a8f8-8b57a3e7f781
    public async Task<DeleteTickerResult> DeleteTickerAsync(string symbol, CancellationToken ct = default)
    {
        // English: Normalize input (case-insensitive handling)
        var sym = (symbol ?? string.Empty).Trim().ToUpperInvariant();

        // English: Log both original input and normalized symbol for safety
        _log.LogInformation("Requested delete for symbol={Input}, normalized={Normalized}", symbol, sym);

        // English: Find ticker (needed to delete Prices by TickerId)
        var ticker = await _db.Tickers
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.Symbol == sym, ct);

        // English: Start provider-agnostic transaction (avoids SqliteTransaction cast issues)
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        int prices = 0;
        if (ticker is not null)
        {
            // English: Delete daily prices via foreign key (faster than loading entities)
            prices = await _db.Prices
                .Where(p => p.TickerId == ticker.Id)
                .ExecuteDeleteAsync(ct);
        }

        // English: Fundamentals store Symbol (string) → delete by symbol
        var income = await _db.IncomeStatements
            .Where(x => x.Symbol == sym)
            .ExecuteDeleteAsync(ct);

        var balance = await _db.BalanceSheets
            .Where(x => x.Symbol == sym)
            .ExecuteDeleteAsync(ct);

        var cash = await _db.CashFlows
            .Where(x => x.Symbol == sym)
            .ExecuteDeleteAsync(ct);

        var tick = 0;
        if (ticker is not null)
        {
            // English: Finally remove the Ticker row
            tick = await _db.Tickers
                .Where(t => t.Id == ticker.Id)
                .ExecuteDeleteAsync(ct);
        }

        await tx.CommitAsync(ct);

        return new DeleteTickerResult(sym, prices, income, balance, cash, tick);
    }
}
