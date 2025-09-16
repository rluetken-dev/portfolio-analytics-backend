using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Portfolio.Api.Services;
using System.Text.Json;

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
        /// Example: GET /api/fundamentals/{symbol}/income-statement/stable?period=quarter&amp;limit=5
        /// </summary>
        /// <param name="symbol">Ticker, e.g., "AAPL".</param>
        /// <param name="period">"annual" or "quarter" (plan-dependent).</param>
        /// <param name="limit">Max rows to return (typical 1–20).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Envelope with Symbol, Period, Count, Items.</returns>
        /// <response code="200">Success.</response>
        [HttpGet("{symbol}/income-statement/stable")]
        public async Task<IActionResult> GetIncomeStatementStable(
            string symbol,
            string period = "annual",
            int limit = 5,
            CancellationToken ct = default)
        {
            // WHY: Pass `period` through so quarterly works; include it in the envelope for clarity.
            var rows = await _fmp.GetIncomeStatementStableAsync(symbol, limit, period, ct);

            return Ok(new
            {
                Symbol = symbol,
                Period = period,
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
        /// NOTE: `period` applies to Income, Balance and Cash (Metrics are TTM and ignore `period`).
        /// </summary>
        /// <param name="symbol">Ticker, e.g., "AAPL".</param>
        /// <param name="period">"annual" or "quarter" (plan-dependent).</param>
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
            // WHY: Fetch each part independently; failures shouldn't break the whole snapshot.
            List<Portfolio.Api.Services.FmpClient.IncomeStatementStableRow>? income = null;
            List<Portfolio.Api.Services.FmpClient.BalanceSheetStableRow>? balance = null;
            List<Portfolio.Api.Services.FmpClient.CashFlowStableRow>? cash = null;
            Portfolio.Api.Services.FmpClient.KeyMetricsTtm? metrics = null;

            try
            {
                // FIX: pass `period` through so Income respects annual/quarter choice.
                income = await _fmp.GetIncomeStatementStableAsync(symbol, limit, period, ct);
            }
            catch (Exception ex) { _log.LogWarning(ex, "Income fetch failed for {Symbol}", symbol); }

            try
            {
                balance = await _fmp.GetBalanceSheetStableAsync(symbol, limit, period, ct);
            }
            catch (Exception ex) { _log.LogWarning(ex, "Balance fetch failed for {Symbol}", symbol); }

            try
            {
                cash = await _fmp.GetCashFlowStableAsync(symbol, limit, period, ct);
            }
            catch (Exception ex) { _log.LogWarning(ex, "Cash flow fetch failed for {Symbol}", symbol); }

            try
            {
                // NOTE: TTM metrics are period-agnostic.
                metrics = await _fmp.GetKeyMetricsTtmAsync(symbol, ct);
            }
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

        // English: DTOs for request/response of the refresh endpoint
        public sealed record FundamentalsCounters(int Income, int Balance, int Cash);
        public sealed record FundamentalsRefreshResponse(
            bool Ok,
            string Symbol,
            string Period, // "annual" | "quarter"
            int Years,
            FundamentalsCounters Inserted,
            FundamentalsCounters Skipped
        );

        // English: call our own ingest endpoints and normalize counters
        private static async Task<(int inserted, int skipped)> HitIngestAsync(
            HttpClient http, string path, CancellationToken ct)
        {
            using var resp = await http.GetAsync(path, ct);
            resp.EnsureSuccessStatusCode();

            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            // English: tolerate different shapes: inserted/upserts + skipped
            int inserted = 0;
            if (root.TryGetProperty("inserted", out var iProp) && iProp.TryGetInt32(out var iVal))
                inserted = iVal;
            else if (root.TryGetProperty("upserts", out var uProp) && uProp.TryGetInt32(out var uVal))
                inserted = uVal;

            int skipped = 0;
            if (root.TryGetProperty("skipped", out var sProp) && sProp.TryGetInt32(out var sVal))
                skipped = sVal;

            return (inserted, skipped);
        }

        // English: detect plan/paid-tier errors in upstream message
        static bool IsPlanLimited(string? msg)
        {
            if (string.IsNullOrWhiteSpace(msg)) return false;
            var m = msg.ToLowerInvariant();
            return m.Contains("402 payment required") ||
                   m.Contains("premium query parameter") ||
                   m.Contains("not available under your current subscription") ||
                   m.Contains("subscription page");
        }

        /// <summary>
        /// Persist fundamentals (annual or quarter) for a symbol into the DB.
        /// </summary>
        [HttpPost("refresh")]
        [Produces("application/json")]
        [SwaggerOperation(
            Summary = "Fetch & store fundamentals (income/balance/cash)",
            Description = "Calls ingest endpoints and returns counters; income has one retry; errors include upstream body."
        )]
        [ProducesResponseType(typeof(FundamentalsRefreshResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
        public async Task<IActionResult> RefreshFundamentals(
            [FromQuery, Required] string symbol,
            [FromQuery] string period = "annual",   // "annual" | "quarter"
            [FromQuery] int years = 5,
            [FromServices] IHttpClientFactory? httpFactory = null,
            CancellationToken ct = default
        )
        {
            // English: normalize + validate
            var sym = symbol?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(sym))
                return BadRequest(new ProblemDetails { Title = "Bad Request", Detail = "symbol required" });

            var per = (period ?? "annual").Trim().ToLowerInvariant();
            if (per != "annual" && per != "quarter")
                return BadRequest(new ProblemDetails { Title = "Bad Request", Detail = "period must be 'annual' or 'quarter'" });

            years = Math.Clamp(years, 1, 10);
            var limit = Math.Max(1, years);

            // English: get an HttpClient; fall back to a local client if DI not configured
            var http = httpFactory?.CreateClient("self") ?? new HttpClient
            {
                BaseAddress = new Uri("http://localhost:5046")
            };
            http.DefaultRequestHeaders.Accept.ParseAdd("application/json");

            // English: call ingest endpoint; include status/body on errors for diagnostics
            static async Task<(int inserted, int skipped)> HitAsync(HttpClient c, string path, CancellationToken token)
            {
                using var resp = await c.GetAsync(path, token);
                var raw = await resp.Content.ReadAsStringAsync(token);

                if (!resp.IsSuccessStatusCode)
                {
                    // English: propagate status + body so callers can show the real cause
                    throw new HttpRequestException($"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase} on {path}: {raw}");
                }

                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(raw);
                    var root = doc.RootElement;

                    // English: support both 'upserted' and 'inserted'
                    int inserted = 0;
                    if (root.TryGetProperty("upserted", out var up) && up.ValueKind == System.Text.Json.JsonValueKind.Number && up.TryGetInt32(out var upVal))
                        inserted = upVal;
                    else if (root.TryGetProperty("inserted", out var ins) && ins.ValueKind == System.Text.Json.JsonValueKind.Number && ins.TryGetInt32(out var insVal))
                        inserted = insVal;

                    int skipped = 0;
                    if (root.TryGetProperty("skipped", out var sk) && sk.ValueKind == System.Text.Json.JsonValueKind.Number && sk.TryGetInt32(out var skVal))
                        skipped = skVal;

                    return (inserted, skipped);
                }
                catch (System.Text.Json.JsonException)
                {
                    // English: surface unexpected payloads clearly
                    throw new HttpRequestException($"HTTP 200 but invalid JSON on {path}: {raw}");
                }
            }

            // English: declare counters upfront so they're in scope after try/catch
            var income = (inserted: 0, skipped: 0);
            var balance = (inserted: 0, skipped: 0);
            var cash = (inserted: 0, skipped: 0);

            // English: income ingest with one retry (handles transient 429/500/DB locks)
            try
            {
                income = await HitAsync(http, $"/api/ingest/income/{sym}?period={per}&limit={limit}", ct);
            }
            catch (HttpRequestException ex2)
            {
                if (IsPlanLimited(ex2.Message))
                {
                    // English: treat plan-limited symbol as a non-fatal skip
                    _log.LogInformation("Income ingest skipped (plan limit) for {Symbol}: {Msg}", sym, ex2.Message);
                    // keep 'income' counters at 0
                }
                else
                {
                    return StatusCode(StatusCodes.Status502BadGateway,
                        new ProblemDetails { Title = "Income ingest failed (after retry)", Detail = ex2.Message });
                }
            }

            // English: balance ingest with explicit error wrapping
            try
            {
                balance = await HitAsync(http, $"/api/ingest/balance/{sym}?period={per}&limit={limit}", ct);
            }
            catch (HttpRequestException ex)
            {
                if (IsPlanLimited(ex.Message))
                {
                    _log.LogInformation("Balance ingest skipped (plan limit) for {Symbol}: {Msg}", sym, ex.Message);
                    // keep 'balance' counters at 0
                }
                else
                {
                    return StatusCode(StatusCodes.Status502BadGateway,
                        new ProblemDetails { Title = "Balance ingest failed", Detail = ex.Message });
                }
            }

            // English: cash ingest with explicit error wrapping
            try
            {
                cash = await HitAsync(http, $"/api/ingest/cash/{sym}?period={per}&limit={limit}", ct);
            }
            catch (HttpRequestException ex)
            {
                if (IsPlanLimited(ex.Message))
                {
                    _log.LogInformation("Cash ingest skipped (plan limit) for {Symbol}: {Msg}", sym, ex.Message);
                    // keep 'cash' counters at 0
                }
                else
                {
                    return StatusCode(StatusCodes.Status502BadGateway,
                        new ProblemDetails { Title = "Cash ingest failed", Detail = ex.Message });
                }
            }

            var payload = new FundamentalsRefreshResponse(
                Ok: true,
                Symbol: sym!,
                Period: per,
                Years: years,
                Inserted: new FundamentalsCounters(income.inserted, balance.inserted, cash.inserted),
                Skipped: new FundamentalsCounters(income.skipped, balance.skipped, cash.skipped)
            );
            return Ok(payload);
        }
    }
}
