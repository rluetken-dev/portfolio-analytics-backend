using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Services;

namespace Portfolio.Api.Controllers
{
    /// <summary>
    /// Triggers server-side data ingestion into the SQL DB.
    /// Thin endpoints that call ingest services (Upsert).
    /// </summary>
    [ApiController]
    [Route("api/ingest")]
    public class IngestController : ControllerBase
    {
        // Fields (non-nullable; provided by DI)
        private readonly IncomeIngestService _ingest;
        private readonly BalanceSheetIngestService _balance;
        private readonly CashFlowIngestService _cash;
        private readonly ILogger<IngestController> _log;

        // Single DI constructor: assign all fields
        public IngestController(
            IncomeIngestService ingest,
            BalanceSheetIngestService balance,
            CashFlowIngestService cash,
            ILogger<IngestController> log)
        {
            _ingest  = ingest;   // WHY: used for income upserts
            _balance = balance;  // WHY: used for balance-sheet upserts
            _cash    = cash;     // WHY: used for cash-flow upserts
            _log     = log;      // WHY: logging inside endpoints
        }

        /// <summary>
        /// Upserts Income Statement rows into SQL via FMP /stable.
        /// Example: GET /api/ingest/income/AAPL?period=annual&amp;limit=10
        /// </summary>
        /// <param name="symbol">Ticker, e.g., "AAPL".</param>
        /// <param name="period">"annual" or "quarter".</param>
        /// <param name="limit">Max rows to pull from API.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Envelope with Symbol, Period, Upserted.</returns>
        /// <response code="200">Success.</response>
        [HttpGet("income/{symbol}")]
        public async Task<IActionResult> IngestIncome(
            string symbol,
            string period = "annual",
            int limit = 10,
            CancellationToken ct = default)
        {
            var upserted = await _ingest.IngestAsync(symbol, period, limit, ct);
            return Ok(new
            {
                Symbol = symbol,
                Period = period,
                Upserted = upserted
            });
        }

        /// <summary>
        /// Upserts Balance Sheet rows into SQL via FMP /stable.
        /// Example: GET /api/ingest/balance/AAPL?period=annual&amp;limit=5
        /// </summary>
        /// <param name="symbol">Ticker, e.g., "AAPL".</param>
        /// <param name="period">"annual" or "quarter".</param>
        /// <param name="limit">Max rows to pull from API (quarter may be capped by plan).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Envelope with Symbol, Period, Upserted.</returns>
        /// <response code="200">Success.</response>
        [HttpGet("balance/{symbol}")]
        public async Task<IActionResult> IngestBalance(
            string symbol,
            string period = "annual",
            int limit = 5,
            CancellationToken ct = default)
        {
            var upserted = await _balance.IngestAsync(symbol, period, limit, ct);
            return Ok(new
            {
                Symbol = symbol,
                Period = period,
                Upserted = upserted
            });
        }

        /// <summary>
        /// Upserts Cash Flow rows into SQL via FMP /stable.
        /// Example: GET /api/ingest/cash/AAPL?period=annual&amp;limit=5
        /// </summary>
        /// <param name="symbol">Ticker, e.g., "AAPL".</param>
        /// <param name="period">"annual" or "quarter".</param>
        /// <param name="limit">Max rows to pull from API (quarter may be capped by plan).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Envelope with Symbol, Period, Upserted.</returns>
        /// <response code="200">Success.</response>
        [HttpGet("cash/{symbol}")]
        public async Task<IActionResult> IngestCash(
            string symbol,
            string period = "annual",
            int limit = 5,
            CancellationToken ct = default)
        {
            // NOTE: Service applies safe caps (e.g., quarter → max 5) to avoid 402 errors.
            var upserted = await _cash.IngestAsync(symbol, period, limit, ct);
            return Ok(new
            {
                Symbol = symbol,
                Period = period,
                Upserted = upserted
            });
        }
    }
}
