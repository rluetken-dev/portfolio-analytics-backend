using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Services;
using Polly.RateLimit;
using Portfolio.Api.Exceptions;

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
        private readonly IncomeIngestService _ingest;
        private readonly BalanceSheetIngestService _balance;
        private readonly CashFlowIngestService _cash;
        private readonly ILogger<IngestController> _log;

        public IngestController(
            IncomeIngestService ingest,
            BalanceSheetIngestService balance,
            CashFlowIngestService cash,
            ILogger<IngestController> log)
        {
            _ingest  = ingest;
            _balance = balance;
            _cash    = cash;
            _log     = log;
        }

        /// <summary>
        /// Upserts Income Statement rows into SQL via FMP /stable.
        /// Example: GET /api/ingest/income/AAPL?period=annual&amp;limit=10
        /// </summary>
        [HttpGet("income/{symbol}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> IngestIncome(
            string symbol,
            string period = "annual",
            int limit = 10,
            CancellationToken ct = default)
        {
            try
            {
                var upserted = await _ingest.IngestAsync(symbol, period, limit, ct);
                return Ok(new { Symbol = symbol, Period = period, Upserted = upserted });
            }
            catch (RateLimitRejectedException ex)
            {
                _log.LogWarning("Rate limit reached for income ingest ({Symbol})", symbol);
                return StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    title = "Rate limit reached",
                    detail = $"Please retry after {ex.RetryAfter}",
                    status = 429
                });
            }
            catch (ServiceUnavailableException ex)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    title = "External provider is not configured",
                    detail = ex.Message,
                    status = 503
                });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Income ingest failed for {Symbol}", symbol);
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    title = "Income ingest failed",
                    detail = ex.Message,
                    status = 500
                });
            }
        }

        /// <summary>
        /// Upserts Balance Sheet rows into SQL via FMP /stable.
        /// Example: GET /api/ingest/balance/AAPL?period=annual&amp;limit=5
        /// </summary>
        [HttpGet("balance/{symbol}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> IngestBalance(
            string symbol,
            string period = "annual",
            int limit = 5,
            CancellationToken ct = default)
        {
            try
            {
                var upserted = await _balance.IngestAsync(symbol, period, limit, ct);
                return Ok(new { Symbol = symbol, Period = period, Upserted = upserted });
            }
            catch (RateLimitRejectedException ex)
            {
                _log.LogWarning("Rate limit reached for balance ingest ({Symbol})", symbol);
                return StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    title = "Rate limit reached",
                    detail = $"Please retry after {ex.RetryAfter}",
                    status = 429
                });
            }
            catch (ServiceUnavailableException ex)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    title = "External provider is not configured",
                    detail = ex.Message,
                    status = 503
                });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Balance ingest failed for {Symbol}", symbol);
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    title = "Balance ingest failed",
                    detail = ex.Message,
                    status = 500
                });
            }
        }

        /// <summary>
        /// Upserts Cash Flow rows into SQL via FMP /stable.
        /// Example: GET /api/ingest/cash/AAPL?period=annual&amp;limit=5
        /// </summary>
        [HttpGet("cash/{symbol}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> IngestCash(
            string symbol,
            string period = "annual",
            int limit = 5,
            CancellationToken ct = default)
        {
            try
            {
                var upserted = await _cash.IngestAsync(symbol, period, limit, ct);
                return Ok(new { Symbol = symbol, Period = period, Upserted = upserted });
            }
            catch (RateLimitRejectedException ex)
            {
                _log.LogWarning("Rate limit reached for cash ingest ({Symbol})", symbol);
                return StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    title = "Rate limit reached",
                    detail = $"Please retry after {ex.RetryAfter}",
                    status = 429
                });
            }
            catch (ServiceUnavailableException ex)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    title = "External provider is not configured",
                    detail = ex.Message,
                    status = 503
                });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Cash ingest failed for {Symbol}", symbol);
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    title = "Cash ingest failed",
                    detail = ex.Message,
                    status = 500
                });
            }
        }
    }
}
