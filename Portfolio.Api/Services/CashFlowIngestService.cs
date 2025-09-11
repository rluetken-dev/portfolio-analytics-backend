using Microsoft.EntityFrameworkCore;                  // EF Core (queries, SaveChangesAsync)
using Portfolio.Api.Data;                             // AppDbContext
using Portfolio.Api.Data.Entities;                    // CashFlowEntity
using System.Globalization;                           // CultureInfo

namespace Portfolio.Api.Services
{
    /// <summary>
    /// Upserts cash-flow rows from FMP /stable into SQL.
    /// </summary>
    public class CashFlowIngestService
    {
        private readonly AppDbContext _db;
        private readonly FmpClient _fmp;
        private readonly ILogger<CashFlowIngestService> _log;

        public CashFlowIngestService(AppDbContext db, FmpClient fmp, ILogger<CashFlowIngestService> log)
        {
            _db = db;
            _fmp = fmp;
            _log = log;
        }

        /// <summary>
        /// Fetches cash-flow rows and UPSERTs them into SQL.
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
            limit = Math.Clamp(limit, 1, 40);

            // NOTE: On current FMP plan, quarterly limit is <= 5 → avoid 402.
            if (string.Equals(period, "quarter", StringComparison.OrdinalIgnoreCase) && limit > 5)
            {
                _log.LogInformation("Capping quarterly limit from {Limit} to 5 due to FMP plan.", limit);
                limit = 5;
            }

            // Pull from FMP /stable
            var rows = await _fmp.GetCashFlowStableAsync(sym, limit, period, ct);
            if (rows is null || rows.Count == 0)
            {
                _log.LogInformation("No cash-flow rows from FMP for {Symbol} ({Period})", sym, period);
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

                // Find existing row by unique key (Symbol, Date, Frequency)
                var existing = await _db.CashFlows
                    .FirstOrDefaultAsync(x => x.Symbol == sym && x.Date == asOf && x.Frequency == period, ct);

                if (existing is null)
                {
                    // Insert
                    var entity = new CashFlowEntity
                    {
                        Symbol = sym,
                        Date = asOf,
                        Frequency = period,
                        ReportedCurrency = r.ReportedCurrency,
                        OperatingCashFlow = r.OperatingCashFlow,
                        CapitalExpenditure = r.CapitalExpenditure,
                        FreeCashFlow = r.FreeCashFlow,
                        NetIncome = r.NetIncome,
                        DepreciationAndAmortization = r.DepreciationAndAmortization,
                        ChangeInWorkingCapital = r.ChangeInWorkingCapital  // <- NEW
                    };
                    _db.CashFlows.Add(entity);
                    changed++;
                }
                else
                {
                    // Update
                    existing.ReportedCurrency = r.ReportedCurrency;
                    existing.OperatingCashFlow = r.OperatingCashFlow;
                    existing.CapitalExpenditure = r.CapitalExpenditure;
                    existing.FreeCashFlow = r.FreeCashFlow;
                    existing.NetIncome = r.NetIncome;
                    existing.DepreciationAndAmortization = r.DepreciationAndAmortization;
                    existing.ChangeInWorkingCapital = r.ChangeInWorkingCapital; // <- NEW
                    changed++;
                }
            }

            await _db.SaveChangesAsync(ct);
            _log.LogInformation("Cash-flow ingest complete: {Count} row(s) upserted for {Symbol} ({Period})", changed, sym, period);
            return changed;
        }
    }
}
