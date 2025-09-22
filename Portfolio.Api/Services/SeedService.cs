using Microsoft.EntityFrameworkCore;
using Portfolio.Api.Data;
using Portfolio.Api.Data.Entities;
using Portfolio.Api.Models;

namespace Portfolio.Api.Services
{
    /// <summary>
    /// Centralizes tiny upsert helpers to insert demo data.
    /// English: Keep seed code here so controllers stay thin and testable.
    /// </summary>
    public class SeedService : ISeedService
    {
        private readonly AppDbContext _db;
        public SeedService(AppDbContext db) => _db = db;

        private async Task<Ticker> EnsureTickerAsync(string symbol, string? name, CancellationToken ct)
        {
            var s = symbol.ToUpperInvariant();
            var t = await _db.Tickers.FirstOrDefaultAsync(x => x.Symbol == s, ct);
            if (t is null)
            {
                t = new Ticker { Symbol = s, Name = string.IsNullOrWhiteSpace(name) ? s : name.Trim() };
                _db.Tickers.Add(t);
                await _db.SaveChangesAsync(ct);
            }
            return t;
        }

        public async Task<(bool created, bool updated)> SeedTickerAsync(string symbol, string? name, CancellationToken ct)
        {
            var s = symbol.ToUpperInvariant();
            var t = await _db.Tickers.FirstOrDefaultAsync(x => x.Symbol == s, ct);
            if (t is null)
            {
                _db.Tickers.Add(new Ticker { Symbol = s, Name = string.IsNullOrWhiteSpace(name) ? s : name.Trim() });
                await _db.SaveChangesAsync(ct);
                return (created: true, updated: false);
            }
            bool updated = false;
            if (!string.IsNullOrWhiteSpace(name) && t.Name != name)
            {
                t.Name = name.Trim();
                await _db.SaveChangesAsync(ct);
                updated = true;
            }
            return (created: false, updated);
        }

        public async Task SeedAnnualAsync(string symbol, int year, long netIncome, long equity, CancellationToken ct)
        {
            var date = new DateOnly(year, 12, 31);
            const string freq = "annual";
            var t = await EnsureTickerAsync(symbol, name: symbol, ct);

            var inc = await _db.IncomeStatements
                .FirstOrDefaultAsync(x => x.Symbol == t.Symbol && x.Date == date && x.Frequency == freq, ct);
            if (inc is null)
            {
                inc = new IncomeStatementEntity
                {
                    Symbol = t.Symbol,
                    Date = date,
                    Frequency = freq,
                    ReportedCurrency = "USD",
                    NetIncome = netIncome
                };
                _db.IncomeStatements.Add(inc);
            }
            else inc.NetIncome = netIncome;

            var bal = await _db.BalanceSheets
                .FirstOrDefaultAsync(x => x.Symbol == t.Symbol && x.Date == date && x.Frequency == freq, ct);
            if (bal is null)
            {
                bal = new BalanceSheetEntity
                {
                    Symbol = t.Symbol,
                    Date = date,
                    Frequency = freq,
                    ReportedCurrency = "USD",
                    TotalStockholdersEquity = equity
                };
                _db.BalanceSheets.Add(bal);
            }
            else bal.TotalStockholdersEquity = equity;

            await _db.SaveChangesAsync(ct);
        }

        public async Task SeedLiabilitiesAsync(string symbol, int year, long totalLiabilities, CancellationToken ct)
        {
            var date = new DateOnly(year, 12, 31);
            const string freq = "annual";
            var t = await EnsureTickerAsync(symbol, name: symbol, ct);

            var bal = await _db.BalanceSheets
                .FirstOrDefaultAsync(x => x.Symbol == t.Symbol && x.Date == date && x.Frequency == freq, ct);
            if (bal is null)
            {
                bal = new BalanceSheetEntity { Symbol = t.Symbol, Date = date, Frequency = freq, ReportedCurrency = "USD" };
                _db.BalanceSheets.Add(bal);
            }
            bal.TotalLiabilities = totalLiabilities;
            await _db.SaveChangesAsync(ct);
        }

        public async Task SeedAssetsAsync(string symbol, int year, long totalAssets, CancellationToken ct)
        {
            var date = new DateOnly(year, 12, 31);
            const string freq = "annual";
            var t = await EnsureTickerAsync(symbol, name: symbol, ct);

            var bal = await _db.BalanceSheets
                .FirstOrDefaultAsync(x => x.Symbol == t.Symbol && x.Date == date && x.Frequency == freq, ct);
            if (bal is null)
            {
                bal = new BalanceSheetEntity { Symbol = t.Symbol, Date = date, Frequency = freq, ReportedCurrency = "USD" };
                _db.BalanceSheets.Add(bal);
            }
            bal.TotalAssets = totalAssets;
            await _db.SaveChangesAsync(ct);
        }

        public async Task SeedRevenueAsync(string symbol, int year, long revenue, CancellationToken ct)
        {
            var date = new DateOnly(year, 12, 31);
            const string freq = "annual";
            var t = await EnsureTickerAsync(symbol, name: symbol, ct);

            var inc = await _db.IncomeStatements
                .FirstOrDefaultAsync(x => x.Symbol == t.Symbol && x.Date == date && x.Frequency == freq, ct);
            if (inc is null)
            {
                inc = new IncomeStatementEntity
                {
                    Symbol = t.Symbol,
                    Date = date,
                    Frequency = freq,
                    ReportedCurrency = "USD"
                };
                _db.IncomeStatements.Add(inc);
            }
            inc.Revenue = revenue;
            await _db.SaveChangesAsync(ct);
        }

        public async Task SeedSharesAsync(string symbol, int year, long shares, CancellationToken ct)
        {
            var date = new DateOnly(year, 12, 31);
            const string freq = "annual";
            var t = await EnsureTickerAsync(symbol, name: symbol, ct);

            var inc = await _db.IncomeStatements
                .FirstOrDefaultAsync(x => x.Symbol == t.Symbol && x.Date == date && x.Frequency == freq, ct);
            if (inc is null)
            {
                inc = new IncomeStatementEntity
                {
                    Symbol = t.Symbol,
                    Date = date,
                    Frequency = freq,
                    ReportedCurrency = "USD"
                };
                _db.IncomeStatements.Add(inc);
            }
            inc.WeightedAverageShsOut = shares;
            await _db.SaveChangesAsync(ct);
        }

        public async Task SeedPriceAsync(string symbol, DateOnly date, decimal close, CancellationToken ct)
        {
            var t = await EnsureTickerAsync(symbol, name: symbol, ct);

            var p = await _db.Prices.FirstOrDefaultAsync(
                x => x.TickerId == t.Id && x.TradingDate == date, ct);

            if (p is null)
            {
                p = new Price
                {
                    TickerId = t.Id,
                    TradingDate = date,
                    Close = close,
                    AdjustedClose = close,
                    Source = "seed"
                };
                _db.Prices.Add(p);
            }
            else
            {
                p.Close = close;
                p.AdjustedClose = close;
                p.Source = "seed";
            }
            await _db.SaveChangesAsync(ct);
        }

        public async Task<(bool created, bool updated)> SeedTickerProfileAsync(string symbol, string? name, string? sector, CancellationToken ct)
        {
            // English: upsert ticker and update name/sector if changed
            var s = symbol.ToUpperInvariant();
            var t = await _db.Tickers.FirstOrDefaultAsync(x => x.Symbol == s, ct);
            if (t is null)
            {
                t = new Ticker
                {
                    Symbol = s,
                    Name = string.IsNullOrWhiteSpace(name) ? s : name.Trim(),
                    // English: sector optional; store if provided
                    Sector = string.IsNullOrWhiteSpace(sector) ? null : sector.Trim()
                };
                _db.Tickers.Add(t);
                await _db.SaveChangesAsync(ct);
                return (created: true, updated: false);
            }

            bool updated = false;
            if (!string.IsNullOrWhiteSpace(name) && t.Name != name)
            {
                t.Name = name.Trim();
                updated = true;
            }
            if (!string.IsNullOrWhiteSpace(sector) && t.Sector != sector)
            {
                t.Sector = sector.Trim();
                updated = true;
            }
            if (updated) await _db.SaveChangesAsync(ct);
            return (created: false, updated);
        }

        // English: upsert Operating Cash Flow (annual)
        public async System.Threading.Tasks.Task SeedOperatingCashFlowAsync(
            string symbol, int year, long operatingCashFlow, System.Threading.CancellationToken ct)
        {
            var date = new DateOnly(year, 12, 31);
            const string freq = "annual";

            var t = await EnsureTickerAsync(symbol, name: symbol, ct);

            // English: find or create cash-flow row for (symbol, year, annual)
            var cf = await _db.CashFlows
                .FirstOrDefaultAsync(x => x.Symbol == t.Symbol && x.Date == date && x.Frequency == freq, ct);

            if (cf is null)
            {
                cf = new CashFlowEntity
                {
                    Symbol = t.Symbol,
                    Date = date,
                    Frequency = freq,
                    ReportedCurrency = "USD"
                };
                _db.CashFlows.Add(cf);
            }

            cf.OperatingCashFlow = operatingCashFlow; // English: set CFO
            await _db.SaveChangesAsync(ct);
        }

        // English: upsert Capital Expenditures (annual)
        public async System.Threading.Tasks.Task SeedCapitalExpendituresAsync(
            string symbol, int year, long capitalExpenditures, System.Threading.CancellationToken ct)
        {
            var date = new DateOnly(year, 12, 31);
            const string freq = "annual";

            var t = await EnsureTickerAsync(symbol, name: symbol, ct);

            var cf = await _db.CashFlows
                .FirstOrDefaultAsync(x => x.Symbol == t.Symbol && x.Date == date && x.Frequency == freq, ct);

            if (cf is null)
            {
                cf = new CashFlowEntity
                {
                    Symbol = t.Symbol,
                    Date = date,
                    Frequency = freq,
                    ReportedCurrency = "USD"
                };
                _db.CashFlows.Add(cf);
            }

            // NOTE: If your entity uses a different property name (e.g., CapitalExpenditures),
            // rename the assignment below accordingly.
            cf.CapitalExpenditure = capitalExpenditures; // English: set CapEx
            await _db.SaveChangesAsync(ct);
        }

        // English: upsert full daily OHLCV (updates existing row if present)
        public async System.Threading.Tasks.Task SeedFullPriceAsync(
            string symbol,
            DateOnly date,
            decimal open,
            decimal high,
            decimal low,
            decimal close,
            long volume,
            System.Threading.CancellationToken ct)
        {
            var t = await EnsureTickerAsync(symbol, name: symbol, ct);

            // English: find existing price row for (ticker, date)
            var p = await _db.Prices.FirstOrDefaultAsync(
                x => x.TickerId == t.Id && x.TradingDate == date, ct);

            if (p is null)
            {
                p = new Price
                {
                    TickerId = t.Id,
                    TradingDate = date,
                    Source = "seed"
                };
                _db.Prices.Add(p);
            }

            // English: set OHLCV; keep AdjustedClose same as close for seed
            p.Open = open;
            p.High = high;
            p.Low = low;
            p.Close = close;
            p.AdjustedClose = close;
            p.Volume = (long)volume; // adjust cast if your column is int
            p.Source = "seed";

            await _db.SaveChangesAsync(ct);
        }

    }
}
