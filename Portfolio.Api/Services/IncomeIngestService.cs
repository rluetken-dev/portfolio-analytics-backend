using Microsoft.EntityFrameworkCore;                  // EF Core (queries, SaveChangesAsync)
using Portfolio.Api.Data;                             // AppDbContext
using Portfolio.Api.Data.Entities;                    // IncomeStatementEntity
using System.Globalization;                           // CultureInfo
using Portfolio.Api.Models;

namespace Portfolio.Api.Services
{
    /// <summary>
    /// Ingests income-statement rows from FMP /stable into the local SQL DB.
    /// </summary>
    public class IncomeIngestService
    {
        private readonly AppDbContext _db;
        private readonly FmpClient _fmp;
        private readonly ILogger<IncomeIngestService> _log;

        public IncomeIngestService(AppDbContext db, FmpClient fmp, ILogger<IncomeIngestService> log)
        {
            _db = db;
            _fmp = fmp;
            _log = log;
        }

        /// <summary>
        /// Ensures a row exists in the Tickers table for the given symbol (idempotent).
        /// Creates it if missing, normalizing the symbol to uppercase and using it as the fallback name.
        /// </summary>
        /// <param name="symbol">Ticker symbol (e.g., "AAPL"). Case-insensitive.</param>
        /// <param name="ct">Cancellation token.</param>
        private async Task EnsureTickerAsync(string symbol, CancellationToken ct)
        {
            var s = symbol.ToUpperInvariant();
            var exists = await _db.Tickers.AnyAsync(t => t.Symbol == s, ct);
            if (!exists)
            {
                _db.Tickers.Add(new Ticker { Symbol = s, Name = s }); // name: fallback to symbol
                await _db.SaveChangesAsync(ct);
            }
        }

        /// <summary>
        /// Fetches income-statement rows via FMP /stable and UPSERTs them into SQL.
        /// NOTE (English):
        /// - Uniqueness is enforced by (Symbol, Date, Frequency) unique index.
        /// - For MVP: simple read-then-insert/update (fine for small N).
        /// </summary>
        /// <param name="symbol">Ticker (e.g., "AAPL").</param>
        /// <param name="period">"annual" or "quarter".</param>
        /// <param name="limit">Max rows to fetch from API.</param>
        /// <param name="ct">Cancellation token.</param>
        public async Task<int> IngestAsync(string symbol, string period, int limit, CancellationToken ct = default)
        {
            await EnsureTickerAsync(symbol, ct); // English: make sure Tickers has a row

            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("symbol is required", nameof(symbol));

            var sym = symbol.ToUpperInvariant();
            limit = Math.Clamp(limit, 1, 40);

            // Cap "quarter" to 5 due to current FMP plan restrictions.
            // English: This avoids 402 Payment Required for quarterly requests > 5.
            if (string.Equals(period, "quarter", StringComparison.OrdinalIgnoreCase) && limit > 5)
            {
                _log.LogInformation("Capping quarterly limit from {Limit} to 5 due to FMP plan.", limit);
                limit = 5;
            }

            // 1) Fetch from FMP /stable
            var rows = await _fmp.GetIncomeStatementStableAsync(sym, limit, period, ct);

            if (rows is null || rows.Count == 0)
            {
                _log.LogInformation("No income rows from FMP for {Symbol} ({Period})", sym, period);
                return 0;
            }

            var addedOrUpdated = 0;

            foreach (var r in rows)
            {
                // Defensive parsing: skip invalid rows
                if (r is null || string.IsNullOrWhiteSpace(r.Date))
                    continue;
                if (!DateOnly.TryParse(r.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var asOf))
                    continue;

                // 2) Try find existing row by unique key (Symbol, Date, Frequency)
                var existing = await _db.IncomeStatements
                    .FirstOrDefaultAsync(x => x.Symbol == sym && x.Date == asOf && x.Frequency == period, ct);

                if (existing is null)
                {
                    // 3a) Insert new
                    var entity = new IncomeStatementEntity
                    {
                        Symbol = sym,
                        Date = asOf,
                        Frequency = period,
                        ReportedCurrency = r.ReportedCurrency,
                        Revenue = r.Revenue,
                        NetIncome = r.NetIncome,
                        Eps = r.Eps,
                        EpsDiluted = r.EpsDiluted,
                        WeightedAverageShsOut = r.WeightedAverageShsOut,
                        WeightedAverageShsOutDil = r.WeightedAverageShsOutDil
                    };

                    _db.IncomeStatements.Add(entity);
                    addedOrUpdated++;
                }
                else
                {
                    // 3b) Update existing (only if values changed)
                    existing.ReportedCurrency = r.ReportedCurrency;
                    existing.Revenue = r.Revenue;
                    existing.NetIncome = r.NetIncome;
                    existing.Eps = r.Eps;
                    existing.EpsDiluted = r.EpsDiluted;
                    existing.WeightedAverageShsOut = r.WeightedAverageShsOut;
                    existing.WeightedAverageShsOutDil = r.WeightedAverageShsOutDil;

                    addedOrUpdated++;
                }
            }

            await _db.SaveChangesAsync(ct);
            _log.LogInformation("Ingest complete: {Count} row(s) upserted for {Symbol} ({Period})", addedOrUpdated, sym, period);
            return addedOrUpdated;
        }
    }
}
