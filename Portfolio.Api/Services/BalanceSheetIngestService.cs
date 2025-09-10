using Microsoft.EntityFrameworkCore;                  // EF Core (queries, SaveChangesAsync)
using Portfolio.Api.Data;                             // AppDbContext
using Portfolio.Api.Data.Entities;                    // BalanceSheetEntity
using System.Globalization;

namespace Portfolio.Api.Services
{
    /// <summary>
    /// Upserts balance-sheet rows from FMP /stable into SQL.
    /// </summary>
    public class BalanceSheetIngestService
    {
        private readonly AppDbContext _db;
        private readonly FmpClient _fmp;
        private readonly ILogger<BalanceSheetIngestService> _log;

        public BalanceSheetIngestService(AppDbContext db, FmpClient fmp, ILogger<BalanceSheetIngestService> log)
        {
            _db = db;
            _fmp = fmp;
            _log = log;
        }

        /// <summary>
        /// Fetches balance-sheet rows and UPSERTs them into SQL.
        /// </summary>
        /// <param name="symbol">Ticker, e.g., "AAPL".</param>
        /// <param name="period">"annual" or "quarter".</param>
        /// <param name="limit">Max rows to fetch (plan-dependent for quarter).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Number of rows inserted or updated.</returns>
        public async Task<int> IngestAsync(string symbol, string period = "annual", int limit = 10, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("symbol is required", nameof(symbol));

            var sym = symbol.ToUpperInvariant();

            // NOTE: Keep limits sane; FMP usually allows up to ~40
            limit = Math.Clamp(limit, 1, 40);

            // NOTE: On your plan, quarterly endpoints may allow only up to 5 → avoid 402 errors.
            if (string.Equals(period, "quarter", StringComparison.OrdinalIgnoreCase) && limit > 5)
            {
                _log.LogInformation("Capping quarterly limit from {Limit} to 5 due to FMP plan.", limit);
                limit = 5;
            }

            // Pull from FMP stable API
            var rows = await _fmp.GetBalanceSheetStableAsync(sym, limit, period, ct);
            if (rows is null || rows.Count == 0)
            {
                _log.LogInformation("No balance-sheet rows from FMP for {Symbol} ({Period})", sym, period);
                return 0;
            }

            var changed = 0;
            foreach (var r in rows)
            {
                // Defensive parsing
                if (r is null || string.IsNullOrWhiteSpace(r.Date))
                    continue;
                if (!DateOnly.TryParse(r.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var asOf))
                    continue;

                // Find existing unique row (Symbol, Date, Frequency)
                var existing = await _db.BalanceSheets
                    .FirstOrDefaultAsync(x => x.Symbol == sym && x.Date == asOf && x.Frequency == period, ct);

                if (existing is null)
                {
                    // Insert
                    var entity = new BalanceSheetEntity
                    {
                        Symbol = sym,
                        Date = asOf,
                        Frequency = period,
                        ReportedCurrency = r.ReportedCurrency,
                        TotalAssets = r.TotalAssets,
                        TotalLiabilities = r.TotalLiabilities,
                        TotalStockholdersEquity = r.TotalStockholdersEquity,
                        CashAndCashEquivalents = r.CashAndCashEquivalents
                    };
                    _db.BalanceSheets.Add(entity);
                    changed++;
                }
                else
                {
                    // Update existing
                    existing.ReportedCurrency = r.ReportedCurrency;
                    existing.TotalAssets = r.TotalAssets;
                    existing.TotalLiabilities = r.TotalLiabilities;
                    existing.TotalStockholdersEquity = r.TotalStockholdersEquity;
                    existing.CashAndCashEquivalents = r.CashAndCashEquivalents;
                    changed++;
                }
            }

            await _db.SaveChangesAsync(ct);
            _log.LogInformation("Balance-sheet ingest complete: {Count} row(s) upserted for {Symbol} ({Period})", changed, sym, period);
            return changed;
        }
    }
}
