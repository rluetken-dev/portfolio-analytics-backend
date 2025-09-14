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

    public MaintenanceService(AppDbContext db) => _db = db;

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
}
