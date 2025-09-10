using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Portfolio.Api.Services;

namespace Portfolio.Api.Controllers
{
    /// <summary>
    /// Simple fundamentals API (FMP-backed).
    /// Currently exposes quarterly revenue; easy to extend with more metrics later.
    /// </summary>
    [ApiController]
    [Route("api/fundamentals")]
    public class FundamentalsController : ControllerBase
    {
        private readonly FmpClient _fmp;
        private readonly AlphaVantageClient _alpha;

        private readonly ILogger<FundamentalsController> _log;

        public FundamentalsController(FmpClient fmp, AlphaVantageClient alpha, ILogger<FundamentalsController> log)
        {
            _fmp = fmp;
            _alpha = alpha;
            _log = log;
        }

        /// <summary>
        /// Lightweight DTO for revenue rows returned to clients.
        /// </summary>
        public record RevenueDto
        {
            /// <summary>Requested ticker symbol (uppercased).</summary>
            public string Symbol { get; init; } = string.Empty;

            /// <summary>Quarter period end date (as returned by FMP, ISO yyyy-MM-dd).</summary>
            public DateOnly PeriodEnd { get; init; }

            /// <summary>Revenue for the quarter (reported currency; raw value).</summary>
            public decimal Revenue { get; init; }

            /// <summary>Reported currency code if provided by FMP (e.g., USD).</summary>
            public string? Currency { get; init; }
        }

        /// <summary>
        /// Returns quarterly revenue (most recent first) for the given symbol via FMP.
        /// </summary>
        /// <remarks>
        /// Example:
        /// <br/>GET <c>/api/fundamentals/revenue?symbol=AAPL&amp;limit=8</c>
        /// </remarks>
        [HttpGet("revenue")]
        [Produces("application/json")]
        [SwaggerOperation(
     Summary = "Revenue series (FMP quarterly → FMP annual → AV quarterly fallback)",
     Description = "Tries FMP quarterly first; if unavailable, falls back to FMP annual; if still empty, uses Alpha Vantage INCOME_STATEMENT (quarterlyReports).")]
        [ProducesResponseType(typeof(IEnumerable<RevenueDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> GetRevenue(
     [FromQuery, Required] string symbol,
     [FromQuery] int limit = 8,
     CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { error = "symbol required" });

            limit = Math.Clamp(limit, 1, 12);
            var sym = symbol.ToUpperInvariant();

            // 1) Try FMP quarterly
            var fmpQuarterly = await _fmp.GetQuarterlyRevenueAsync(sym, limit, ct);
            if (fmpQuarterly.Count > 0)
            {
                var dtoQ = fmpQuarterly.Select(p => new RevenueDto
                {
                    Symbol = sym,
                    PeriodEnd = p.PeriodEnd,
                    Revenue = p.Revenue,
                    Currency = p.Currency
                }).ToList();
                return Ok(dtoQ);
            }

            // 2) Fallback to FMP annual
            var fmpAnnual = await _fmp.GetAnnualRevenueAsync(sym, limit, ct);
            if (fmpAnnual.Count > 0)
            {
                var dtoA = fmpAnnual.Select(p => new RevenueDto
                {
                    Symbol = sym,
                    PeriodEnd = p.PeriodEnd,
                    Revenue = p.Revenue,
                    Currency = p.Currency
                }).ToList();
                return Ok(dtoA);
            }

            // 3) Fallback to Alpha Vantage quarterly (INCOME_STATEMENT)
            var avRows = await _alpha.GetQuarterlyRevenueAvAsync(sym, limit, ct);
            var dtoAv = avRows.Select(p => new RevenueDto
            {
                Symbol = sym,
                PeriodEnd = p.PeriodEnd,
                Revenue = p.Revenue,
                Currency = p.Currency
            }).ToList();

            return Ok(dtoAv);
        }

        /// <summary>
        /// Fetches Income Statement rows from FMP's /stable API (most recent first).
        /// Example: GET /api/fundamentals/{symbol}/income-statement/stable?limit=5
        /// </summary>
        /// <param name="symbol">Ticker, e.g., "AAPL".</param>
        /// <param name="limit">Max rows to return (typical 1–20).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Envelope with Symbol, Count, and Items.</returns>
        /// <response code="200">Success.</response>
        [HttpGet("{symbol}/income-statement/stable")]
        public async Task<IActionResult> GetIncomeStatementStable(
            string symbol,
            int limit = 5,
            CancellationToken ct = default)
        {
            // INFO (English):
            // - Calls our new /stable endpoint wrapper in FmpClient.
            // - Returns a small envelope (symbol, count, items) to ease debugging in clients.
            // - `limit` controls how many most-recent rows we fetch.
            // - Add or remove fields from the DTO in FmpClient if you need more columns later.

            var rows = await _fmp.GetIncomeStatementStableAsync(symbol, limit, ct);

            return Ok(new
            {
                Symbol = symbol,
                Count = rows?.Count ?? 0,
                Items = rows
            });
        }

        /// <summary>
        /// Returns trailing-twelve-months (TTM) key metrics for a single symbol
        /// via FMP's /stable API.
        /// Example: GET /api/fundamentals/{symbol}/metrics/ttm
        /// </summary>
        /// <param name="symbol">Ticker symbol, e.g., "AAPL".</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Envelope with Symbol, HasData, and Metrics.</returns>
        /// <response code="200">
        /// Success. May return HasData = false if no metrics are available.
        /// </response>
        [HttpGet("{symbol}/metrics/ttm")]
        public async Task<IActionResult> GetKeyMetricsTtm(
            string symbol,
            CancellationToken ct = default)
        {
            // English:
            // - Ask FMP /stable for TTM key metrics of a single symbol.
            // - Return a small envelope for easier client debugging.
            // - If nothing is returned, we still respond 200 with HasData = false.
            var metrics = await _fmp.GetKeyMetricsTtmAsync(symbol, ct);

            return Ok(new
            {
                Symbol = symbol,
                HasData = metrics is not null,
                Metrics = metrics
            });
        }

        /// <summary>
        /// Fetches Balance Sheet rows from FMP's /stable API (most recent first).
        /// Example: GET /api/fundamentals/{symbol}/balance-sheet/stable?period=annual&amp;limit=3
        /// </summary>
        /// <param name="symbol">Ticker, e.g., "AAPL".</param>
        /// <param name="period">"annual" or "quarter" (plan-dependent).</param>
        /// <param name="limit">Max rows to return (typical 1–20).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Envelope with Symbol, Period, Count, and Items.</returns>
        /// <response code="200">Success.</response>
        // GET /api/fundamentals/{symbol}/balance-sheet/stable?period=annual&limit=3
        [HttpGet("{symbol}/balance-sheet/stable")]
        public async Task<IActionResult> GetBalanceSheetStable(
            string symbol,
            string period = "annual",
            int limit = 3,
            CancellationToken ct = default)
        {
            // English: Call the client wrapper for /stable/balance-sheet and return a small envelope.
            var rows = await _fmp.GetBalanceSheetStableAsync(symbol, limit, period, ct);

            return Ok(new
            {
                Symbol = symbol,
                Period = period,
                Count = rows?.Count ?? 0,
                Items = rows
            });
        }

        /// <summary>
        /// Fetches Cash Flow rows from FMP's /stable API (most recent first).
        /// Example: GET /api/fundamentals/{symbol}/cash-flow/stable?period=annual&amp;limit=3
        /// </summary>
        /// <param name="symbol">Ticker, e.g., "AAPL".</param>
        /// <param name="period">"annual" or "quarter" (plan-dependent).</param>
        /// <param name="limit">Max rows to return (typical 1–20).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Envelope with Symbol, Period, Count, and Items.</returns>
        /// <response code="200">Success.</response>
        // GET /api/fundamentals/{symbol}/cash-flow/stable?period=annual&limit=3
        [HttpGet("{symbol}/cash-flow/stable")]
        public async Task<IActionResult> GetCashFlowStable(
            string symbol,
            string period = "annual",
            int limit = 3,
            CancellationToken ct = default)
        {
            // English: Call client wrapper for /stable/cash-flow-statement and wrap response.
            var rows = await _fmp.GetCashFlowStableAsync(symbol, limit, period, ct);

            return Ok(new
            {
                Symbol = symbol,
                Period = period,
                Count = rows?.Count ?? 0,
                Items = rows
            });
        }

        /// <summary>
        /// Returns a compact fundamentals snapshot (Income, Balance, Cash, Metrics) via FMP's /stable API.
        /// Example: GET /api/fundamentals/{symbol}/snapshot/stable?period=annual&amp;limit=3
        /// </summary>
        /// <param name="symbol">Ticker, e.g., "AAPL".</param>
        /// <param name="period">"annual" or "quarter" for Balance/Cash (plan-dependent).</param>
        /// <param name="limit">Max rows per statement (typical 1–20).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Envelope with Symbol, Period, and sections: Income, Balance, Cash, Metrics.</returns>
        /// <response code="200">Success; individual sections may be null on upstream errors.</response>
        // GET /api/fundamentals/{symbol}/snapshot/stable?period=annual&limit=3
        [HttpGet("{symbol}/snapshot/stable")]
        public async Task<IActionResult> GetSnapshotStable(
            string symbol,
            string period = "annual",
            int limit = 3,
            CancellationToken ct = default)
        {
            // English: Ask each source; failures are logged and do not break the response.
            List<Portfolio.Api.Services.FmpClient.IncomeStatementStableRow>? income = null;
            List<Portfolio.Api.Services.FmpClient.BalanceSheetStableRow>? balance = null;
            List<Portfolio.Api.Services.FmpClient.CashFlowStableRow>? cash = null;
            Portfolio.Api.Services.FmpClient.KeyMetricsTtm? metrics = null;

            try { income = await _fmp.GetIncomeStatementStableAsync(symbol, limit, ct); }
            catch (Exception ex) { _log.LogWarning(ex, "Income fetch failed for {Symbol}", symbol); }

            try { balance = await _fmp.GetBalanceSheetStableAsync(symbol, limit, period, ct); }
            catch (Exception ex) { _log.LogWarning(ex, "Balance fetch failed for {Symbol}", symbol); }

            try { cash = await _fmp.GetCashFlowStableAsync(symbol, limit, period, ct); }
            catch (Exception ex) { _log.LogWarning(ex, "Cash flow fetch failed for {Symbol}", symbol); }

            try { metrics = await _fmp.GetKeyMetricsTtmAsync(symbol, ct); }
            catch (Exception ex) { _log.LogWarning(ex, "Metrics fetch failed for {Symbol}", symbol); }

            return Ok(new
            {
                Symbol = symbol,
                Period = period,
                Income = income,
                Balance = balance,
                Cash = cash,
                Metrics = metrics
            });
        }
    }
}
