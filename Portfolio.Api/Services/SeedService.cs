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
                    Symbol = t.Symbol, Date = date, Frequency = freq, ReportedCurrency = "USD"
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
                    Symbol = t.Symbol, Date = date, Frequency = freq, ReportedCurrency = "USD"
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
    }
}
