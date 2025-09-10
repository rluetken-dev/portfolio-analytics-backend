using Microsoft.EntityFrameworkCore;
using Portfolio.Api.Data;

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
}
