using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Api.Data;

namespace Portfolio.Api.Controllers
{
    /// <summary>
    /// Read-only endpoints to inspect stored data (SQL).
    /// </summary>
    [ApiController]
    [Route("api/data")]
    public class DataController : ControllerBase
    {
        private readonly AppDbContext _db;
        public DataController(AppDbContext db) => _db = db;

        /// <summary>
        /// Returns stored income statements from SQL (newest first).
        /// Example: GET /api/data/income/{symbol}?period=annual&amp;limit=5
        /// </summary>
        /// <param name="symbol">Ticker, e.g., "AAPL".</param>
        /// <param name="period">"annual" or "quarter".</param>
        /// <param name="limit">Max rows to return (1–100).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Envelope with Symbol, Period, Count, Items.</returns>
        /// <response code="200">Success.</response>
        [HttpGet("income/{symbol}")]
        public async Task<IActionResult> GetIncome(
            string symbol,
            string period = "annual",
            int limit = 10,
            CancellationToken ct = default)
        {
            // WHY: Keep params safe and results predictable.
            var sym = symbol.ToUpperInvariant();
            limit = Math.Clamp(limit, 1, 100);

            var items = await _db.IncomeStatements
                .Where(x => x.Symbol == sym && x.Frequency == period)
                .OrderByDescending(x => x.Date)
                .Take(limit)
                .ToListAsync(ct);

            return Ok(new
            {
                Symbol = sym,
                Period = period,
                Count = items.Count,
                Items = items
            });
        }

        /// <summary>
        /// Returns stored balance sheet rows from SQL (newest first).
        /// Example: GET /api/data/balance/{symbol}?period=annual&amp;limit=5
        /// </summary>
        /// <param name="symbol">Ticker, e.g., "AAPL".</param>
        /// <param name="period">"annual" or "quarter".</param>
        /// <param name="limit">Max rows to return (1–100).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Envelope with Symbol, Period, Count, Items.</returns>
        /// <response code="200">Success.</response>
        [HttpGet("balance/{symbol}")]
        public async Task<IActionResult> GetBalance(
            string symbol,
            string period = "annual",
            int limit = 10,
            CancellationToken ct = default)
        {
            var sym = symbol.ToUpperInvariant();             // WHY: canonical casing
            limit = Math.Clamp(limit, 1, 100);               // WHY: defensive cap

            var items = await _db.BalanceSheets
                .Where(x => x.Symbol == sym && x.Frequency == period)
                .OrderByDescending(x => x.Date)
                .Take(limit)
                .ToListAsync(ct);

            return Ok(new
            {
                Symbol = sym,
                Period = period,
                Count = items.Count,
                Items = items
            });
        }

        /// <summary>
        /// Returns stored cash flow rows from SQL (newest first).
        /// Example: GET /api/data/cash/{symbol}?period=annual&amp;limit=5
        /// </summary>
        /// <param name="symbol">Ticker, e.g., "AAPL".</param>
        /// <param name="period">"annual" or "quarter".</param>
        /// <param name="limit">Max rows to return (1–100).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Envelope with Symbol, Period, Count, Items.</returns>
        /// <response code="200">Success.</response>
        [HttpGet("cash/{symbol}")]
        public async Task<IActionResult> GetCash(
            string symbol,
            string period = "annual",
            int limit = 10,
            CancellationToken ct = default)
        {
            // WHY: Keep parameters safe and results predictable.
            var sym = symbol.ToUpperInvariant();
            limit = Math.Clamp(limit, 1, 100);

            var items = await _db.CashFlows
                .Where(x => x.Symbol == sym && x.Frequency == period)
                .OrderByDescending(x => x.Date)
                .Take(limit)
                .ToListAsync(ct);

            return Ok(new
            {
                Symbol = sym,
                Period = period,
                Count = items.Count,
                Items = items
            });
        }

        /// <summary>
        /// Returns TTM metrics (from stored quarterly rows) without calling external APIs.
        /// Example: GET /api/data/ttm/{symbol}
        /// </summary>
        /// <param name="symbol">Ticker, e.g., "AAPL".</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Envelope with TTM sums (Revenue, NetIncome, FreeCashFlow) if 4 complete quarters exist.</returns>
        /// <response code="200">Success.</response>
        [HttpGet("ttm/{symbol}")]
        public async Task<IActionResult> GetTtm(string symbol, CancellationToken ct = default)
        {
            var sym = symbol.ToUpperInvariant();

            // Pull last 4 quarterly income rows (newest first)
            var incomeQ = await _db.IncomeStatements
                .Where(x => x.Symbol == sym && x.Frequency == "quarter")
                .OrderByDescending(x => x.Date)
                .Take(4)
                .ToListAsync(ct);

            // Pull last 4 quarterly cash-flow rows (newest first)
            var cashQ = await _db.CashFlows
                .Where(x => x.Symbol == sym && x.Frequency == "quarter")
                .OrderByDescending(x => x.Date)
                .Take(4)
                .ToListAsync(ct);

            // Helper: sum only if all 4 values are present (avoid half-baked TTM)
            static long? SumIfComplete(IList<long?> xs)
                => xs.Count == 4 && xs.All(v => v.HasValue) ? xs.Sum(v => v!.Value) : (long?)null;

            // Collect values for TTM
            var revValues = incomeQ.Select(x => x.Revenue).ToList();
            var netIncomeValues = incomeQ.Select(x => x.NetIncome).ToList();
            var fcfValues = cashQ.Select(x => x.FreeCashFlow).ToList();

            var ttmRevenue = SumIfComplete(revValues);
            var ttmNetIncome = SumIfComplete(netIncomeValues);
            var ttmFreeCashFlow = SumIfComplete(fcfValues);

            // Pick a currency if all quarters agree; otherwise null
            string? currency = null;
            var currencies = incomeQ.Select(x => x.ReportedCurrency).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
            if (currencies.Count == 1) currency = currencies[0];

            return Ok(new
            {
                Symbol = sym,
                Period = "quarter",
                Has4IncomeQuarters = incomeQ.Count == 4,
                Has4CashQuarters = cashQ.Count == 4,
                Currency = currency,
                RevenueTtm = ttmRevenue,
                NetIncomeTtm = ttmNetIncome,
                FreeCashFlowTtm = ttmFreeCashFlow,
                QuartersIncome = incomeQ.Select(x => x.Date).ToList(),
                QuartersCash = cashQ.Select(x => x.Date).ToList()
            });
        }

        /// <summary>
        /// Returns TTM ratios computed from stored quarterly rows (no external calls).
        /// Example: GET /api/data/ttm/{symbol}/ratios
        /// </summary>
        /// <param name="symbol">Ticker, e.g., "AAPL".</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Envelope with TTM sums and margins (Net/FCF).</returns>
        /// <response code="200">Success.</response>
        [HttpGet("ttm/{symbol}/ratios")]
        public async Task<IActionResult> GetTtmRatios(string symbol, CancellationToken ct = default)
        {
            var sym = symbol.ToUpperInvariant();

            // Pull last 4 quarterly rows for income & cash (newest first)
            var incomeQ = await _db.IncomeStatements
                .Where(x => x.Symbol == sym && x.Frequency == "quarter")
                .OrderByDescending(x => x.Date)
                .Take(4)
                .ToListAsync(ct);

            var cashQ = await _db.CashFlows
                .Where(x => x.Symbol == sym && x.Frequency == "quarter")
                .OrderByDescending(x => x.Date)
                .Take(4)
                .ToListAsync(ct);

            // Helper: sum only if all 4 values are present
            static long? SumIfComplete(IList<long?> xs)
                => xs.Count == 4 && xs.All(v => v.HasValue) ? xs.Sum(v => v!.Value) : (long?)null;

            var ttmRevenue = SumIfComplete(incomeQ.Select(x => x.Revenue).ToList());
            var ttmNetIncome = SumIfComplete(incomeQ.Select(x => x.NetIncome).ToList());
            var ttmFreeCashFlow = SumIfComplete(cashQ.Select(x => x.FreeCashFlow).ToList());

            // Compute margins safely (null if missing or division by zero)
            double? netMarginTtm = (ttmRevenue.HasValue && ttmRevenue.Value != 0 && ttmNetIncome.HasValue)
                ? (double)ttmNetIncome.Value / (double)ttmRevenue.Value
                : (double?)null;

            double? fcfMarginTtm = (ttmRevenue.HasValue && ttmRevenue.Value != 0 && ttmFreeCashFlow.HasValue)
                ? (double)ttmFreeCashFlow.Value / (double)ttmRevenue.Value
                : (double?)null;

            // Pick a currency if all quarters agree
            string? currency = null;
            var currencies = incomeQ.Select(x => x.ReportedCurrency).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
            if (currencies.Count == 1) currency = currencies[0];

            return Ok(new
            {
                Symbol = sym,
                Has4IncomeQuarters = incomeQ.Count == 4,
                Has4CashQuarters = cashQ.Count == 4,
                Currency = currency,
                RevenueTtm = ttmRevenue,
                NetIncomeTtm = ttmNetIncome,
                FreeCashFlowTtm = ttmFreeCashFlow,
                NetMarginTtm = netMarginTtm,   // e.g., 0.24 = 24%
                FcfMarginTtm = fcfMarginTtm    // e.g., 0.21 = 21%
            });
        }
    }
}

