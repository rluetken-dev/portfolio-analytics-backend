using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Api.Data;
using Portfolio.Api.Services.Analytics; // FinanceMath helper
using Swashbuckle.AspNetCore.Annotations;


namespace Portfolio.Api.Controllers
{
    [ApiController]
    [Route("api/analytics")]
    public class AnalyticsController : ControllerBase
    {
        /// <summary>
        /// Returns latest annual ROE (Return on Equity).
        /// Uses average equity if a prior annual equity exists; otherwise falls back to end-of-period equity.
        /// </summary>
        /// <remarks>
        /// **Formula**
        /// <c>ROE = NetIncome / AverageEquity</c>, where
        /// <c>AverageEquity = (Equity_t + Equity_{t-1}) / 2</c>.
        /// If the prior-year equity is missing or non-positive, the endpoint falls back to <c>Equity_t</c>.
        ///
        /// **Example**
        /// <code>
        /// GET /api/analytics/roe?symbol=AAPL
        /// </code>
        ///
        /// **Sample response**
        /// <code>
        /// {
        ///   "ticker": "AAPL",
        ///   "date": "2024-09-28",
        ///   "netIncome": 93736000000,
        ///   "equityEnd": 56950000000,
        ///   "equityPrior": 62146000000,
        ///   "equityPriorDate": "2023-09-30",
        ///   "equityBasis": 59548000000,
        ///   "roe": 1.5741,
        ///   "roePct": 157.41,
        ///   "roeRounded": 1.5741
        /// }
        /// </code>
        /// </remarks>
        [SwaggerOperation(
            Summary = "ROE (annual, average-equity fallback)",
            Description = "Computes latest annual ROE = NetIncome / AverageEquity. Uses prior-year equity when available; otherwise uses end-of-period equity.",
            OperationId = "Analytics_GetRoe",
            Tags = new[] { "Analytics" }
        )]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
        [HttpGet("roe")]
        public async Task<IActionResult> GetRoe(
                    [FromQuery] string symbol,
                    [FromServices] AppDbContext db,
                    CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { error = "Missing ?symbol=..." });

            var ticker = symbol.Trim().ToUpperInvariant();

            // Latest annual income (need NetIncome and its period end date)
            var inc = await db.IncomeStatements.AsNoTracking()
                .Where(i => i.Symbol == ticker && i.Frequency == "annual")
                .OrderByDescending(i => i.Date)
                .Select(i => new { i.Date, i.NetIncome })
                .FirstOrDefaultAsync(ct);

            if (inc is null || !inc.NetIncome.HasValue)
                return NotFound(new { error = $"No annual income row for {ticker}." });

            // Equity for same period (t)
            var eqT = await db.BalanceSheets.AsNoTracking()
                .Where(b => b.Symbol == ticker && b.Frequency == "annual" && b.Date == inc.Date)
                .Select(b => b.TotalStockholdersEquity)
                .FirstOrDefaultAsync(ct);

            if (!eqT.HasValue || eqT.Value == 0)
                return NotFound(new { error = $"No equity for {ticker} at {inc.Date}." });

            // English: prior equity = most recent annual balance sheet strictly BEFORE the current income period end.
            // More robust than "same month/day one year earlier" because fiscal-year dates can shift slightly.
            var priorBal = await db.BalanceSheets.AsNoTracking()
                .Where(b => b.Symbol == ticker && b.Frequency == "annual" && b.Date < inc.Date)
                .OrderByDescending(b => b.Date)
                .Select(b => new { b.Date, b.TotalStockholdersEquity })
                .FirstOrDefaultAsync(ct);

            long? eqTminus1 = priorBal?.TotalStockholdersEquity;

            // Prefer average equity if prior-year exists and is positive; otherwise fallback to end-of-period equity.
            double equityBasis = (eqTminus1.HasValue && eqTminus1.Value > 0)
                ? (((double)eqT.Value + (double)eqTminus1.Value) / 2.0)
                : (double)eqT.Value;

            // Use pure helper (null when invalid, e.g., equityBasis == 0)
            double? roe = FinanceMath.Roe((double)inc.NetIncome.Value, equityBasis);

            return Ok(new
            {
                ticker,
                date = inc.Date,
                netIncome = inc.NetIncome,
                equityEnd = eqT,
                equityPrior = eqTminus1,
                equityPriorDate = priorBal?.Date,
                equityBasis,
                roe,
                roePct = roe.HasValue ? roe.Value * 100.0 : (double?)null,
                roeRounded = roe.HasValue ? Math.Round(roe.Value, 4) : (double?)null // 4 dec for ratio; UI can format as %
            });
        }

        /// <summary>
        /// Returns the latest annual Debt-to-Equity ratio (D/E).
        /// </summary>
        /// <remarks>
        /// **Formula**
        /// <c>D/E = TotalLiabilities / TotalStockholdersEquity</c>.
        ///
        /// **Example**
        /// <code>
        /// GET /api/analytics/debt-to-equity?symbol=AAPL
        /// </code>
        ///
        /// **Sample response**
        /// <code>
        /// {
        ///   "ticker": "AAPL",
        ///   "date": "2024-09-28",
        ///   "totalLiabilities": 308030000000,
        ///   "equity": 56950000000,
        ///   "debtToEquity": 5.41
        /// }
        /// </code>
        /// </remarks>
        [SwaggerOperation(
            Summary = "Debt-to-Equity ratio (annual)",
            Description = "Computes latest annual D/E = TotalLiabilities / TotalStockholdersEquity.",
            OperationId = "Analytics_GetDebtToEquity",
            Tags = new[] { "Analytics" }
        )]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
        [HttpGet("debt-to-equity")]
        public async Task<IActionResult> GetDebtToEquity(
                    [FromQuery] string symbol,
                    [FromServices] AppDbContext db,
                    CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { error = "Missing ?symbol=..." });

            var ticker = symbol.Trim().ToUpperInvariant();

            // 1) Nimm die JÜNGSTE 'annual' Bilanzzeile
            var bal = await db.BalanceSheets.AsNoTracking()
                .Where(b => b.Symbol == ticker && b.Frequency == "annual")
                .OrderByDescending(b => b.Date)
                .Select(b => new { b.Date, b.TotalLiabilities, b.TotalStockholdersEquity })
                .FirstOrDefaultAsync(ct);

            if (bal is null)
                return NotFound(new { error = $"No annual balance row for {ticker}." });

            // 2) Compute D/E via helper (null if equity is zero/invalid)
            double? de = null;
            if (bal.TotalLiabilities.HasValue && bal.TotalStockholdersEquity.HasValue)
            {
                de = FinanceMath.DebtToEquity(
                    (double)bal.TotalLiabilities.Value,
                    (double)bal.TotalStockholdersEquity.Value
                );
            }

            // 3) Return values (raw, percentage, rounded)
            return Ok(new
            {
                ticker,
                date = bal.Date,
                totalLiabilities = bal.TotalLiabilities,
                equity = bal.TotalStockholdersEquity,
                debtToEquity = de,
                debtToEquityPct = de is null ? (double?)null : de.Value * 100.0,
                debtToEquityRounded = de is null ? (double?)null : Math.Round(de.Value, 4)
            });
        }

        /// <summary>
        /// Returns the latest annual Net Margin.
        /// </summary>
        /// <remarks>
        /// **Formula**
        /// <c>NetMargin = NetIncome / Revenue</c>.
        ///
        /// **Example**
        /// <code>
        /// GET /api/analytics/net-margin?symbol=AAPL
        /// </code>
        ///
        /// **Sample response**
        /// <code>
        /// {
        ///   "ticker": "AAPL",
        ///   "date": "2024-09-28",
        ///   "netIncome": 93736000000,
        ///   "revenue": 391035000000,
        ///   "netMargin": 0.2397,
        ///   "netMarginPct": 23.97,
        ///   "netMarginRounded": 0.2397
        /// }
        /// </code>
        /// </remarks>
        [SwaggerOperation(
            Summary = "Net Margin (annual)",
            Description = "Computes latest annual Net Margin = NetIncome / Revenue.",
            OperationId = "Analytics_GetNetMargin",
            Tags = new[] { "Analytics" }
        )]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
        [HttpGet("net-margin")]
        public async Task<IActionResult> GetNetMargin(
                    [FromQuery] string symbol,
                    [FromServices] AppDbContext db,
                    CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { error = "Missing ?symbol=..." });

            var ticker = symbol.Trim().ToUpperInvariant();

            // 1) Latest annual income row
            var inc = await db.IncomeStatements.AsNoTracking()
                .Where(i => i.Symbol == ticker && i.Frequency == "annual")
                .OrderByDescending(i => i.Date)
                .Select(i => new { i.Date, i.NetIncome, i.Revenue })
                .FirstOrDefaultAsync(ct);

            if (inc is null)
                return NotFound(new { error = $"No annual income row for {ticker}." });

            // 2) Compute Net Margin via helper (null if revenue is zero/invalid)
            double? netMargin = (inc.NetIncome.HasValue && inc.Revenue.HasValue)
                ? FinanceMath.NetMargin((double)inc.NetIncome.Value, (double)inc.Revenue.Value)
                : (double?)null;

            return Ok(new
            {
                ticker,
                date = inc.Date,
                netIncome = inc.NetIncome,
                revenue = inc.Revenue,
                netMargin,
                netMarginPct = netMargin * 100.0,
                netMarginRounded = netMargin is null ? (double?)null : Math.Round(netMargin.Value, 4)
            });
        }

        /// <summary>
        /// Returns the latest annual Return on Assets (ROA).
        /// </summary>
        /// <remarks>
        /// **Formula**  
        /// <c>ROA = NetIncome / TotalAssets</c>.
        ///
        /// **Example**  
        /// <code>
        /// GET /api/analytics/roa?symbol=AAPL
        /// </code>
        ///
        /// **Sample response**
        /// <code>
        /// {
        ///   "ticker": "AAPL",
        ///   "date": "2024-09-28",
        ///   "netIncome": 93736000000,
        ///   "totalAssets": 364980000000,
        ///   "roa": 0.257
        /// }
        /// </code>
        /// </remarks>
        [SwaggerOperation(
            Summary = "Return on Assets (ROA, annual)",
            Description = "Computes latest annual ROA = NetIncome / TotalAssets.",
            OperationId = "Analytics_GetRoa",
            Tags = new[] { "Analytics" }
        )]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
        [HttpGet("roa")]
        public async Task<IActionResult> GetRoa(
                    [FromQuery] string symbol,
                    [FromServices] AppDbContext db,
                    CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { error = "Missing ?symbol=..." });

            var ticker = symbol.Trim().ToUpperInvariant();

            // Latest annual income row
            var inc = await db.IncomeStatements.AsNoTracking()
                .Where(i => i.Symbol == ticker && i.Frequency == "annual")
                .OrderByDescending(i => i.Date)
                .Select(i => new { i.Date, i.NetIncome })
                .FirstOrDefaultAsync(ct);
            if (inc is null)
                return NotFound(new { error = $"No annual income row for {ticker}." });

            // Matching balance row (same period end)
            var bal = await db.BalanceSheets.AsNoTracking()
                .Where(b => b.Symbol == ticker && b.Frequency == "annual" && b.Date == inc.Date)
                .Select(b => new { b.TotalAssets })
                .FirstOrDefaultAsync(ct);
            if (bal is null)
                return NotFound(new { error = $"No annual balance row for {ticker} at {inc.Date}." });

            double? roa = (inc.NetIncome.HasValue && bal.TotalAssets.HasValue)
                ? FinanceMath.Roa((double)inc.NetIncome.Value, (double)bal.TotalAssets.Value)
                : (double?)null;

            return Ok(new
            {
                ticker,
                date = inc.Date,
                netIncome = inc.NetIncome,
                totalAssets = bal.TotalAssets,
                roa,
                roaPct = roa * 100.0,
                roaRounded = roa is null ? (double?)null : Math.Round(roa.Value, 4)
            });
        }

        /// <summary>
        /// Returns the latest annual Equity Ratio.
        /// </summary>
        /// <remarks>
        /// **Formula**  
        /// <c>Equity Ratio = TotalStockholdersEquity / TotalAssets</c>.
        ///
        /// **Example**  
        /// <code>
        /// GET /api/analytics/equity-ratio?symbol=AAPL
        /// </code>
        ///
        /// **Sample response**
        /// <code>
        /// {
        ///   "ticker": "AAPL",
        ///   "date": "2024-09-28",
        ///   "totalAssets": 364980000000,
        ///   "equity": 56950000000,
        ///   "equityRatio": 0.156,
        ///   "equityRatioPct": 15.6,
        ///   "equityRatioRounded": 0.156
        /// }
        /// </code>
        /// </remarks>
        [SwaggerOperation(
            Summary = "Equity Ratio (annual)",
            Description = "Computes latest annual Equity Ratio = TotalStockholdersEquity / TotalAssets.",
            OperationId = "Analytics_GetEquityRatio",
            Tags = new[] { "Analytics" }
        )]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
        [HttpGet("equity-ratio")]
        public async Task<IActionResult> GetEquityRatio(
                    [FromQuery] string symbol,
                    [FromServices] AppDbContext db,
                    CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { error = "Missing ?symbol=..." });

            var ticker = symbol.Trim().ToUpperInvariant();

            // 1) Get the most recent annual balance sheet row (needs Assets and Equity)
            var bal = await db.BalanceSheets.AsNoTracking()
                .Where(b => b.Symbol == ticker && b.Frequency == "annual")
                .OrderByDescending(b => b.Date)
                .Select(b => new { b.Date, b.TotalAssets, b.TotalStockholdersEquity })
                .FirstOrDefaultAsync(ct);

            if (bal is null)
                return NotFound(new { error = $"No annual balance row for {ticker}." });

            // 2) Compute Equity Ratio via helper (null if Assets invalid/zero)
            double? equityRatio = null;
            if (bal.TotalAssets.HasValue && bal.TotalStockholdersEquity.HasValue)
            {
                equityRatio = FinanceMath.EquityRatio(
                    (double)bal.TotalStockholdersEquity.Value,
                    (double)bal.TotalAssets.Value
                );
            }

            // 3) Return values (raw, percentage, rounded)
            return Ok(new
            {
                ticker,
                date = bal.Date,
                totalAssets = bal.TotalAssets,
                equity = bal.TotalStockholdersEquity,
                equityRatio,
                equityRatioPct = equityRatio is null ? (double?)null : equityRatio.Value * 100.0,
                equityRatioRounded = equityRatio is null ? (double?)null : Math.Round(equityRatio.Value, 4)
            });
        }

        /// <summary>
        /// Returns the latest annual Debt-to-Assets ratio.
        /// </summary>
        /// <remarks>
        /// **Formula**  
        /// <c>Debt-to-Assets = TotalLiabilities / TotalAssets</c>.
        ///
        /// **Example**  
        /// <code>
        /// GET /api/analytics/debt-to-assets?symbol=AAPL
        /// </code>
        ///
        /// **Sample response**
        /// <code>
        /// {
        ///   "ticker": "AAPL",
        ///   "date": "2024-09-28",
        ///   "totalLiabilities": 308030000000,
        ///   "totalAssets": 364980000000,
        ///   "debtToAssets": 0.844,
        ///   "debtToAssetsPct": 84.4,
        ///   "debtToAssetsRounded": 0.844
        /// }
        /// </code>
        /// </remarks>
        [SwaggerOperation(
            Summary = "Debt-to-Assets ratio (annual)",
            Description = "Computes latest annual Debt-to-Assets = TotalLiabilities / TotalAssets.",
            OperationId = "Analytics_GetDebtToAssets",
            Tags = new[] { "Analytics" }
        )]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
        [HttpGet("debt-to-assets")]
        public async Task<IActionResult> GetDebtToAssets(
                    [FromQuery] string symbol,
                    [FromServices] AppDbContext db,
                    CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { error = "Missing ?symbol=..." });

            var ticker = symbol.Trim().ToUpperInvariant();

            // 1) Read most recent annual balance row
            var bal = await db.BalanceSheets.AsNoTracking()
                .Where(b => b.Symbol == ticker && b.Frequency == "annual")
                .OrderByDescending(b => b.Date)
                .Select(b => new { b.Date, b.TotalAssets, b.TotalLiabilities })
                .FirstOrDefaultAsync(ct);

            if (bal is null)
                return NotFound(new { error = $"No annual balance row for {ticker}." });

            // 2) Compute Debt-to-Assets via helper (null if assets invalid/zero)
            double? dta = null;
            if (bal.TotalAssets.HasValue && bal.TotalLiabilities.HasValue)
            {
                dta = FinanceMath.DebtToAssets(
                    (double)bal.TotalLiabilities.Value,
                    (double)bal.TotalAssets.Value
                );
            }

            // 3) Return values (raw, percentage, rounded)
            return Ok(new
            {
                ticker,
                date = bal.Date,
                totalLiabilities = bal.TotalLiabilities,
                totalAssets = bal.TotalAssets,
                debtToAssets = dta,
                debtToAssetsPct = dta is null ? (double?)null : dta.Value * 100.0,
                debtToAssetsRounded = dta is null ? (double?)null : Math.Round(dta.Value, 4)
            });
        }

        /// <summary>
        /// Returns the latest available close price for a ticker.
        /// </summary>
        /// <remarks>
        /// **Example**  
        /// <code>
        /// GET /api/analytics/price?symbol=AAPL
        /// </code>
        ///
        /// **Sample response**
        /// <code>
        /// {
        ///   "ticker": "AAPL",
        ///   "date": "2024-09-30",
        ///   "close": 200.0
        /// }
        /// </code>
        /// </remarks>
        [SwaggerOperation(
            Summary = "Latest price",
            Description = "Returns the latest available close price for a given ticker symbol.",
            OperationId = "Analytics_GetLatestPrice",
            Tags = new[] { "Analytics" }
        )]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
        [HttpGet("price")]
        public async Task<IActionResult> GetLatestPrice(
                    [FromQuery] string symbol,
                    [FromServices] AppDbContext db,
                    CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { error = "Missing ?symbol=..." });

            var ticker = symbol.Trim().ToUpperInvariant();

            var row = await db.Prices.AsNoTracking()
                .Where(p => p.Ticker.Symbol == ticker)
                .OrderByDescending(p => p.TradingDate)
                .Select(p => new { p.TradingDate, p.Close })
                .FirstOrDefaultAsync(ct);

            if (row is null)
                return NotFound(new { error = $"No prices for {ticker}." });

            return Ok(new
            {
                ticker,
                date = row.TradingDate,
                close = row.Close
            });
        }

        /// <summary>
        /// Returns the latest annual Earnings Per Share (EPS).
        /// </summary>
        /// <remarks>
        /// **Formula**  
        /// <c>EPS = NetIncome / WeightedAverageShsOut</c>
        ///
        /// **Example**  
        /// <code>
        /// GET /api/analytics/eps?symbol=AAPL
        /// </code>
        ///
        /// **Sample response**
        /// <code>
        /// {
        ///   "ticker": "AAPL",
        ///   "date": "2024-09-28",
        ///   "netIncome": 93736000000,
        ///   "shares": 15343783000,
        ///   "eps": 6.11
        /// }
        /// </code>
        /// </remarks>
        [SwaggerOperation(
            Summary = "Earnings Per Share (EPS, annual)",
            Description = "Computes latest annual EPS = NetIncome / WeightedAverageShsOut.",
            OperationId = "Analytics_GetEps",
            Tags = new[] { "Analytics" }
        )]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
        [HttpGet("eps")]
        public async Task<IActionResult> GetEps(
                    [FromQuery] string symbol,
                    [FromServices] AppDbContext db,
                    CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { error = "Missing ?symbol=..." });

            var ticker = symbol.Trim().ToUpperInvariant();

            var inc = await db.IncomeStatements.AsNoTracking()
                .Where(i => i.Symbol == ticker && i.Frequency == "annual")
                .OrderByDescending(i => i.Date)
                .Select(i => new { i.Date, i.NetIncome, i.WeightedAverageShsOut })
                .FirstOrDefaultAsync(ct);

            if (inc is null)
                return NotFound(new { error = $"No annual income row for {ticker}." });

            double? eps = null;
            if (inc.WeightedAverageShsOut.HasValue && inc.WeightedAverageShsOut.Value != 0)
            {
                eps = (double)inc.NetIncome! / (double)inc.WeightedAverageShsOut.Value;
            }

            return Ok(new
            {
                ticker,
                date = inc.Date,
                netIncome = inc.NetIncome,
                shares = inc.WeightedAverageShsOut,
                eps
            });
        }

        /// <summary>
        /// Returns the latest annual Price-to-Earnings ratio (P/E).
        /// </summary>
        /// <remarks>
        /// **Formula**  
        /// <c>P/E = Price per Share / EPS</c>  
        /// where <c>EPS = NetIncome / WeightedAverageShsOut</c>.
        ///
        /// **Example**  
        /// <code>
        /// GET /api/analytics/pe?symbol=AAPL
        /// </code>
        ///
        /// **Sample response**
        /// <code>
        /// {
        ///   "ticker": "AAPL",
        ///   "eps": 6.11,
        ///   "price": 200.0,
        ///   "pe": 32.7,
        ///   "dateEps": "2024-09-28",
        ///   "datePrice": "2024-09-30"
        /// }
        /// </code>
        /// </remarks>
        [SwaggerOperation(
            Summary = "Price-to-Earnings ratio (P/E)",
            Description = "Computes latest annual P/E = Price per Share / EPS (annual).",
            OperationId = "Analytics_GetPe",
            Tags = new[] { "Analytics" }
        )]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
        [HttpGet("pe")]
        public async Task<IActionResult> GetPe(
                    [FromQuery] string symbol,
                    [FromServices] AppDbContext db,
                    CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { error = "Missing ?symbol=..." });

            var ticker = symbol.Trim().ToUpperInvariant();

            // 1) Resolve ticker id (so we can read Prices by FK)
            var t = await db.Tickers.AsNoTracking()
                .Where(x => x.Symbol == ticker)
                .Select(x => new { x.Id })
                .FirstOrDefaultAsync(ct);
            if (t is null)
                return NotFound(new { error = $"Ticker {ticker} not found." });

            // 2) Latest annual EPS = NetIncome / WeightedAverageShsOut (via helper)
            var inc = await db.IncomeStatements.AsNoTracking()
                .Where(i => i.Symbol == ticker && i.Frequency == "annual")
                .OrderByDescending(i => i.Date)
                .Select(i => new { i.Date, i.NetIncome, i.WeightedAverageShsOut })
                .FirstOrDefaultAsync(ct);

            if (inc is null || !inc.NetIncome.HasValue || !inc.WeightedAverageShsOut.HasValue || inc.WeightedAverageShsOut.Value <= 0)
                return NotFound(new { error = $"No annual EPS data for {ticker}." });

            var epsOpt = FinanceMath.Eps((double)inc.NetIncome.Value, (double)inc.WeightedAverageShsOut.Value);
            if (epsOpt is null)
                return BadRequest(new { error = "Cannot compute EPS (invalid inputs)." });

            double eps = epsOpt.Value;

            // 3) Price on/after EPS date
            var price = await db.Prices.AsNoTracking()
                .Where(p => p.TickerId == t.Id && p.TradingDate >= inc.Date)
                .OrderBy(p => p.TradingDate)
                .Select(p => new { p.TradingDate, p.Close })
                .FirstOrDefaultAsync(ct);
            if (price is null)
                return NotFound(new { error = $"No price data for {ticker} on/after {inc.Date}." });

            double priceVal = (double)price.Close;

            // Helper (null-safe)
            double? pe = FinanceMath.Pe(priceVal, eps);

            return Ok(new
            {
                ticker,
                eps,
                price = priceVal,
                pe,
                dateEps = inc.Date,
                datePrice = price.TradingDate,
                peRounded = pe is null ? (double?)null : Math.Round(pe.Value, 4)
            });
        }

        /// <summary>
        /// Returns the latest annual Book Value per Share (BVPS).
        /// </summary>
        /// <remarks>
        /// **Formula**  
        /// <c>BVPS = Equity / WeightedAverageShsOut</c>  
        ///
        /// **Example**  
        /// <code>
        /// GET /api/analytics/bvps?symbol=AAPL
        /// </code>
        ///
        /// **Sample response**
        /// <code>
        /// {
        ///   "ticker": "AAPL",
        ///   "date": "2024-09-28",
        ///   "equity": 56950000000,
        ///   "shares": 15343783000,
        ///   "bvps": 3.71
        /// }
        /// </code>
        /// </remarks>
        [SwaggerOperation(
            Summary = "Book Value per Share (BVPS)",
            Description = "Computes latest annual BVPS = Equity / Shares Outstanding.",
            OperationId = "Analytics_GetBvps",
            Tags = new[] { "Analytics" }
        )]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
        [HttpGet("bvps")]
        public async Task<IActionResult> GetBvps(
                    [FromQuery] string symbol,
                    [FromServices] AppDbContext db,
                    CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { error = "Missing ?symbol=..." });

            var ticker = symbol.Trim().ToUpperInvariant();

            // 1) Latest annual balance row (for Equity)
            var bal = await db.BalanceSheets.AsNoTracking()
                .Where(b => b.Symbol == ticker && b.Frequency == "annual")
                .OrderByDescending(b => b.Date)
                .Select(b => new { b.Date, b.TotalStockholdersEquity })
                .FirstOrDefaultAsync(ct);
            if (bal is null || !bal.TotalStockholdersEquity.HasValue)
                return NotFound(new { error = $"No annual equity data for {ticker}." });

            // 2) Matching/latest annual income row (for SharesOutstanding)
            var inc = await db.IncomeStatements.AsNoTracking()
                .Where(i => i.Symbol == ticker && i.Frequency == "annual" && i.Date == bal.Date)
                .Select(i => new { i.WeightedAverageShsOut })
                .FirstOrDefaultAsync(ct);
            if (inc is null || !inc.WeightedAverageShsOut.HasValue || inc.WeightedAverageShsOut.Value == 0)
                return NotFound(new { error = $"No shares data for {ticker} at {bal.Date}." });

            // 3) Compute BVPS safely
            var bvps = (double)bal.TotalStockholdersEquity.Value / (double)inc.WeightedAverageShsOut.Value;

            return Ok(new
            {
                ticker,
                date = bal.Date,
                equity = bal.TotalStockholdersEquity,
                shares = inc.WeightedAverageShsOut,
                bvps
            });
        }

        /// <summary>
        /// Returns the latest Price-to-Book ratio (P/B).
        /// </summary>
        /// <remarks>
        /// **Formula**  
        /// <c>P/B = Price per Share / Book Value per Share</c>  
        ///
        /// **Example**  
        /// <code>
        /// GET /api/analytics/pb?symbol=AAPL
        /// </code>
        ///
        /// **Sample response**
        /// <code>
        /// {
        ///   "ticker": "AAPL",
        ///   "bvps": 3.71,
        ///   "price": 200,
        ///   "pb": 53.89,
        ///   "pbRounded": 53.89,
        ///   "dateEquity": "2024-09-28",
        ///   "datePrice": "2024-09-30"
        /// }
        /// </code>
        /// </remarks>
        [SwaggerOperation(
            Summary = "Price-to-Book ratio (P/B)",
            Description = "Computes the latest P/B ratio = Price per Share / Book Value per Share (BVPS).",
            OperationId = "Analytics_GetPb",
            Tags = new[] { "Analytics" }
        )]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
        [HttpGet("pb")]
        public async Task<IActionResult> GetPb(
                    [FromQuery] string symbol,
                    [FromServices] AppDbContext db,
                    CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { error = "Missing ?symbol=..." });

            var ticker = symbol.Trim().ToUpperInvariant();

            // 1) Equity + Shares (für BVPS)
            var bal = await db.BalanceSheets.AsNoTracking()
                .Where(b => b.Symbol == ticker && b.Frequency == "annual")
                .OrderByDescending(b => b.Date)
                .Select(b => new { b.Date, b.TotalStockholdersEquity })
                .FirstOrDefaultAsync(ct);

            if (bal is null || !bal.TotalStockholdersEquity.HasValue)
                return NotFound(new { error = $"No annual equity data for {ticker}." });

            var inc = await db.IncomeStatements.AsNoTracking()
                .Where(i => i.Symbol == ticker && i.Frequency == "annual" && i.Date == bal.Date)
                .Select(i => new { i.WeightedAverageShsOut })
                .FirstOrDefaultAsync(ct);

            if (inc is null || !inc.WeightedAverageShsOut.HasValue || inc.WeightedAverageShsOut.Value <= 0)
                return NotFound(new { error = $"No shares data for {ticker} at {bal.Date}." });

            // BVPS via helper (null-safe)
            var bvpsOpt = FinanceMath.Bvps(
                (double)bal.TotalStockholdersEquity!.Value,
                (double)inc.WeightedAverageShsOut!.Value
            );
            if (bvpsOpt is null)
                return BadRequest(new { error = "Cannot compute BVPS (invalid inputs)." });
            double bvps = bvpsOpt.Value;

            // 2) Last price from this date
            var t = await db.Tickers.AsNoTracking()
                .Where(x => x.Symbol == ticker)
                .Select(x => new { x.Id })
                .FirstOrDefaultAsync(ct);

            if (t is null) return NotFound(new { error = $"Ticker {ticker} not found." });

            var price = await db.Prices.AsNoTracking()
                .Where(p => p.TickerId == t.Id && p.TradingDate >= bal.Date)
                .OrderBy(p => p.TradingDate)
                .Select(p => new { p.TradingDate, p.Close })
                .FirstOrDefaultAsync(ct);
            if (price is null) return NotFound(new { error = $"No price data for {ticker}." });

            double priceVal = (double)price.Close;

            // 3) P/B via helper (null if BVPS invalid/zero)
            double? pb = FinanceMath.Pb(priceVal, bvps);

            return Ok(new
            {
                ticker,
                bvps,
                price = priceVal,
                pb,
                pbRounded = pb is null ? (double?)null : Math.Round(pb.Value, 2),
                dateEquity = bal.Date,
                datePrice = price.TradingDate
            });
        }

        /// <summary>
        /// Returns the latest annual Asset Turnover.
        /// </summary>
        /// <remarks>
        /// **Formula**  
        /// <c>Asset Turnover = Revenue / TotalAssets</c>.
        ///
        /// **Example**  
        /// <code>
        /// GET /api/analytics/asset-turnover?symbol=AAPL
        /// </code>
        ///
        /// **Sample response**
        /// <code>
        /// {
        ///   "ticker": "AAPL",
        ///   "date": "2024-09-28",
        ///   "revenue": 391035000000,
        ///   "totalAssets": 364980000000,
        ///   "assetTurnover": 1.07,
        ///   "assetTurnoverRounded": 1.07
        /// }
        /// </code>
        /// </remarks>
        [SwaggerOperation(
            Summary = "Asset Turnover (annual)",
            Description = "Computes latest annual Asset Turnover = Revenue / TotalAssets.",
            OperationId = "Analytics_GetAssetTurnover",
            Tags = new[] { "Analytics" }
        )]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
        [HttpGet("asset-turnover")]
        public async Task<IActionResult> GetAssetTurnover(
                    [FromQuery] string symbol,
                    [FromServices] AppDbContext db,
                    CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { error = "Missing ?symbol=..." });

            var ticker = symbol.Trim().ToUpperInvariant();

            // 1) Latest annual income row for Revenue
            var inc = await db.IncomeStatements.AsNoTracking()
                .Where(i => i.Symbol == ticker && i.Frequency == "annual")
                .OrderByDescending(i => i.Date)
                .Select(i => new { i.Date, i.Revenue })
                .FirstOrDefaultAsync(ct);

            if (inc is null)
                return NotFound(new { error = $"No annual income row for {ticker}." });

            // 2) Matching balance row (same period end) for TotalAssets
            var bal = await db.BalanceSheets.AsNoTracking()
                .Where(b => b.Symbol == ticker && b.Frequency == "annual" && b.Date == inc.Date)
                .Select(b => new { b.TotalAssets })
                .FirstOrDefaultAsync(ct);

            if (bal is null)
                return NotFound(new { error = $"No annual balance row for {ticker} at {inc.Date}." });

            // 3) Compute ratio safely (null/zero checks)
            double? assetTurnover = null;
            if (inc.Revenue.HasValue && bal.TotalAssets.HasValue && bal.TotalAssets.Value != 0)
            {
                assetTurnover = (double)inc.Revenue.Value / (double)bal.TotalAssets.Value;
            }

            return Ok(new
            {
                ticker,
                date = inc.Date,
                revenue = inc.Revenue,
                totalAssets = bal.TotalAssets,
                assetTurnover // e.g., 0.8 = 0.8x
            });
        }

        /// <summary>
        /// Returns Equity CAGR (Compound Annual Growth Rate) using earliest and latest annual balance rows.
        /// </summary>
        /// <remarks>
        /// **Formula**  
        /// <c>CAGR = (EndingEquity / BeginningEquity)^(1/Years) - 1</c>
        ///
        /// **Example**  
        /// <code>
        /// GET /api/analytics/equity-cagr?symbol=AAPL
        /// </code>
        ///
        /// **Sample response**
        /// <code>
        /// {
        ///   "ticker": "AAPL",
        ///   "from": "2019-09-28",
        ///   "to": "2024-09-28",
        ///   "startEquity": 50000000000,
        ///   "endEquity": 112000000000,
        ///   "years": 5,
        ///   "equityCagr": 0.171
        /// }
        /// </code>
        /// </remarks>
        [SwaggerOperation(
            Summary = "Equity CAGR",
            Description = "Computes Compound Annual Growth Rate of equity using earliest and latest annual balance rows.",
            OperationId = "Analytics_GetEquityCagr",
            Tags = new[] { "Analytics" }
        )]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
        [HttpGet("equity-cagr")]
        public async Task<IActionResult> GetEquityCagr(
                    [FromQuery] string symbol,
                    [FromServices] AppDbContext db,
                    CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { error = "Missing ?symbol=..." });
            var ticker = symbol.Trim().ToUpperInvariant();

            // Load ALL annual balance rows for this ticker (only the fields we need)
            var rows = await db.BalanceSheets.AsNoTracking()
                .Where(b => b.Symbol == ticker && b.Frequency == "annual" && b.TotalStockholdersEquity.HasValue)
                .OrderBy(b => b.Date) // ascending: first is earliest, last is latest
                .Select(b => new { b.Date, Equity = b.TotalStockholdersEquity!.Value })
                .ToListAsync(ct);

            if (rows.Count < 2)
                return BadRequest(new { error = $"Need at least 2 annual balance rows for {ticker} to compute CAGR." });

            var first = rows.First();
            var last = rows.Last();

            // Years between period ends (use whole years; guard against zero)
            var years = (last.Date.Year - first.Date.Year);
            if (years <= 0 || first.Equity <= 0)
                return BadRequest(new { error = "Invalid data span or non-positive starting equity." });

            var cagr = FinanceMath.EquityCagr(first.Equity, last.Equity, years);

            return Ok(new
            {
                ticker,
                from = first.Date,
                to = last.Date,
                startEquity = first.Equity,
                endEquity = last.Equity,
                years,
                equityCagr = cagr,
                equityCagrPct = cagr * 100.0,
                equityCagrRounded = cagr is null ? (double?)null : Math.Round(cagr.Value, 4)
            });
        }

        /// <summary>
        /// Returns latest annual Free Cash Flow (FCF).
        /// </summary>
        /// <remarks>
        /// **Formula**  
        /// <c>FCF = OperatingCashFlow - CapitalExpenditure</c>  
        /// (using the latest available annual cash flow row)
        ///
        /// **Example**  
        /// <code>
        /// GET /api/analytics/fcf?symbol=AAPL
        /// </code>
        ///
        /// **Sample response**
        /// <code>
        /// {
        ///   "ticker": "AAPL",
        ///   "date": "2024-09-28",
        ///   "operatingCashFlow": 118254000000,
        ///   "capitalExpenditure": -9447000000,
        ///   "fcf": 127701000000
        /// }
        /// </code>
        /// </remarks>
        [SwaggerOperation(
            Summary = "Free Cash Flow (FCF)",
            Description = "Computes latest annual Free Cash Flow (Operating Cash Flow - Capital Expenditure).",
            OperationId = "Analytics_GetFcf",
            Tags = new[] { "Analytics" }
        )]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
        [HttpGet("fcf")]
        public async Task<IActionResult> GetFcf(
                    [FromQuery] string symbol,
                    [FromServices] AppDbContext db,
                    CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { error = "Missing ?symbol=..." });

            var ticker = symbol.Trim().ToUpperInvariant();

            // English: pull latest annual cash flow row
            var cf = await db.CashFlows.AsNoTracking()
                .Where(c => c.Symbol == ticker && c.Frequency == "annual")
                .OrderByDescending(c => c.Date)
                .Select(c => new
                {
                    c.Date,
                    c.OperatingCashFlow,
                    c.CapitalExpenditure
                })
                .FirstOrDefaultAsync(ct);

            if (cf is null)
                return NotFound(new { error = $"No annual cash flow row for {ticker}." });

            long? fcf = null;
            if (cf.OperatingCashFlow.HasValue && cf.CapitalExpenditure.HasValue)
            {
                // Note: CapEx is typically negative in statements; subtracting a negative adds.
                fcf = cf.OperatingCashFlow.Value - cf.CapitalExpenditure.Value;
            }

            return Ok(new
            {
                ticker,
                date = cf.Date,
                operatingCashFlow = cf.OperatingCashFlow,
                capitalExpenditure = cf.CapitalExpenditure,
                fcf
            });
        }

        /// <summary>
        /// Returns latest annual Free Cash Flow (FCF) Yield = FCF / MarketCap.
        /// </summary>
        /// <remarks>
        /// **Formula**  
        /// <c>FCF = OperatingCashFlow - CapitalExpenditure</c>  
        /// <c>MarketCap = Price × Shares</c>  
        /// <c>FCF Yield = FCF / MarketCap</c>  
        ///
        /// **Data sources**  
        /// - **FCF**: Latest annual cash flow row (OCF − CapEx)  
        /// - **Shares**: Latest annual income row with WeightedAverageShsOut on/before CF date (fallback: latest annual)  
        /// - **Price**: First price on/after CF date (fallback: latest available price)  
        ///
        /// **Example**  
        /// <code>
        /// GET /api/analytics/fcf-yield?symbol=AAPL
        /// </code>
        ///
        /// **Sample response**
        /// <code>
        /// {
        ///   "ticker": "AAPL",
        ///   "cfDate": "2024-09-28",
        ///   "operatingCashFlow": 118254000000,
        ///   "capitalExpenditure": -9447000000,
        ///   "fcf": 127701000000,
        ///   "shares": 15343783000,
        ///   "priceDateUsed": "2024-09-30",
        ///   "priceUsed": 200,
        ///   "marketCap": 3068756600000,
        ///   "fcfYield": 0.0416,
        ///   "fcfYieldPct": 4.16,
        ///   "fcfYieldRounded": 0.0416
        /// }
        /// </code>
        /// </remarks>
        [SwaggerOperation(
            Summary = "Free Cash Flow Yield",
            Description = "Computes FCF Yield = FCF / MarketCap using latest annual data and fallback rules.",
            OperationId = "Analytics_GetFcfYield",
            Tags = new[] { "Analytics" }
        )]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
        [HttpGet("fcf-yield")]
        public async Task<IActionResult> GetFcfYield(
                    [FromQuery] string symbol,
                    [FromServices] AppDbContext db,
                    CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { error = "Missing ?symbol=..." });
            var ticker = symbol.Trim().ToUpperInvariant();

            // 1) Cash flow (annual)
            var cf = await db.CashFlows.AsNoTracking()
                .Where(c => c.Symbol == ticker && c.Frequency == "annual")
                .OrderByDescending(c => c.Date)
                .Select(c => new { c.Date, c.OperatingCashFlow, c.CapitalExpenditure })
                .FirstOrDefaultAsync(ct);
            if (cf is null || !cf.OperatingCashFlow.HasValue || !cf.CapitalExpenditure.HasValue)
                return NotFound(new { error = $"No annual cash flow row with OCF+CapEx for {ticker}." });

            var fcf = cf.OperatingCashFlow.Value - cf.CapitalExpenditure.Value; // CapEx is typically negative

            // 2) Shares: latest annual income <= CF date; fallback to latest annual
            var inc = await db.IncomeStatements.AsNoTracking()
                .Where(i => i.Symbol == ticker && i.Frequency == "annual" && i.WeightedAverageShsOut.HasValue)
                .OrderByDescending(i => i.Date)
                .ToListAsync(ct);

            var incOnOrBefore = inc.FirstOrDefault(i => i.Date <= cf.Date);
            var shares = (incOnOrBefore ?? inc.FirstOrDefault())?.WeightedAverageShsOut;

            if (!shares.HasValue || shares.Value <= 0)
                return NotFound(new { error = $"No valid shares (WeightedAverageShsOut) for {ticker} around {cf.Date}." });

            // 3) TickerId for prices
            var tid = await db.Tickers.AsNoTracking()
                .Where(t => t.Symbol == ticker)
                .Select(t => t.Id)
                .FirstOrDefaultAsync(ct);
            if (tid == 0)
                return NotFound(new { error = $"Ticker {ticker} not found in Tickers." });

            // 4) Price: first on/after CF date; fallback to latest overall
            var pxAfter = await db.Prices.AsNoTracking()
                .Where(p => p.TickerId == tid && p.TradingDate >= cf.Date)
                .OrderBy(p => p.TradingDate)
                .Select(p => new { p.TradingDate, p.Close })
                .FirstOrDefaultAsync(ct);

            var pxLatest = pxAfter ?? await db.Prices.AsNoTracking()
                .Where(p => p.TickerId == tid)
                .OrderByDescending(p => p.TradingDate)
                .Select(p => new { p.TradingDate, p.Close })
                .FirstOrDefaultAsync(ct);

            if (pxLatest is null)
                return NotFound(new { error = $"No price data available for {ticker}." });

            double marketCap = (double)pxLatest.Close * (double)shares.Value;

            // Use pure helper (null when invalid, e.g., marketCap == 0)
            double? fcfYield = FinanceMath.FcfYield((double)fcf, marketCap);

            return Ok(new
            {
                ticker,
                cfDate = cf.Date,
                operatingCashFlow = cf.OperatingCashFlow,
                capitalExpenditure = cf.CapitalExpenditure,
                fcf,
                shares = shares,
                priceDateUsed = pxLatest.TradingDate,
                priceUsed = pxLatest.Close,
                marketCap,
                fcfYield,
                fcfYieldPct = fcfYield.HasValue ? fcfYield.Value * 100.0 : (double?)null,
                fcfYieldRounded = fcfYield.HasValue ? Math.Round(fcfYield.Value, 4) : (double?)null
            });
        }

        /// <summary>
        /// Returns latest annual Free Cash Flow (FCF) Margin = FCF / Revenue.
        /// </summary>
        /// <remarks>
        /// **Formula**  
        /// <c>FCF = OperatingCashFlow - CapitalExpenditure</c>  
        /// <c>FCF Margin = FCF / Revenue</c>  
        ///
        /// **Data sources**  
        /// - **FCF**: Latest annual cash flow row (OCF − CapEx)  
        /// - **Revenue**: Latest annual income row on/before the same CF date (fallback: latest annual)  
        ///
        /// **Example**  
        /// <code>
        /// GET /api/analytics/fcf-margin?symbol=AAPL
        /// </code>
        ///
        /// **Sample response**
        /// <code>
        /// {
        ///   "ticker": "AAPL",
        ///   "date": "2024-09-28",
        ///   "revenue": 391035000000,
        ///   "fcf": 127701000000,
        ///   "fcfMargin": 0.3265,
        ///   "fcfMarginPct": 32.65,
        ///   "fcfMarginRounded": 0.3265
        /// }
        /// </code>
        /// </remarks>
        [SwaggerOperation(
            Summary = "Free Cash Flow Margin",
            Description = "Computes FCF Margin = FCF / Revenue using latest annual data.",
            OperationId = "Analytics_GetFcfMargin",
            Tags = new[] { "Analytics" }
        )]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
        [HttpGet("fcf-margin")]
        public async Task<IActionResult> GetFcfMargin(
                    [FromQuery] string symbol,
                    [FromServices] AppDbContext db,
                    CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { error = "Missing ?symbol=..." });
            var ticker = symbol.Trim().ToUpperInvariant();

            // 1) Latest annual CF row (for FCF)
            var cf = await db.CashFlows.AsNoTracking()
                .Where(c => c.Symbol == ticker && c.Frequency == "annual")
                .OrderByDescending(c => c.Date)
                .Select(c => new { c.Date, c.OperatingCashFlow, c.CapitalExpenditure })
                .FirstOrDefaultAsync(ct);

            if (cf is null || !cf.OperatingCashFlow.HasValue || !cf.CapitalExpenditure.HasValue)
                return NotFound(new { error = $"No annual cash flow row with OCF+CapEx for {ticker}." });

            long fcf = cf.OperatingCashFlow.Value - cf.CapitalExpenditure.Value; // CapEx often negative

            // 2) Revenue from the latest annual income on/before CF date (fallback: latest annual)
            var incRows = await db.IncomeStatements.AsNoTracking()
                .Where(i => i.Symbol == ticker && i.Frequency == "annual" && i.Revenue.HasValue)
                .OrderByDescending(i => i.Date)
                .ToListAsync(ct);

            var inc = incRows.FirstOrDefault(i => i.Date <= cf.Date) ?? incRows.FirstOrDefault();
            if (inc is null)
                return NotFound(new { error = $"No annual income row with revenue for {ticker}." });

            if (!inc.Revenue.HasValue)
                return NotFound(new { error = $"Revenue is null for {ticker} at {inc.Date}." });

            double? fcfMargin = FinanceMath.FcfMargin((double)fcf, (double)inc.Revenue.Value);

            return Ok(new
            {
                ticker,
                date = cf.Date,
                revenue = inc.Revenue,
                fcf,
                fcfMargin,
                fcfMarginPct = fcfMargin.HasValue ? fcfMargin.Value * 100.0 : (double?)null,
                fcfMarginRounded = fcfMargin.HasValue ? Math.Round(fcfMargin.Value, 4) : (double?)null
            });
        }

        /// <summary>
        /// Returns latest annual Owner Earnings (Buffett-style).
        /// </summary>
        /// <remarks>
        /// **Formula**  
        /// <c>OwnerEarnings = OperatingCashFlow - CapitalExpenditure ± ChangeInWorkingCapital</c>  
        /// Falls back to Free Cash Flow (OCF − CapEx) if ΔWC is missing.  
        ///
        /// **Data sources**  
        /// - **OperatingCashFlow** and **CapEx**: Latest annual cash flow row  
        /// - **Δ Working Capital**: Same row if available (else ignored)  
        ///
        /// **Example**  
        /// <code>
        /// GET /api/analytics/owner-earnings?symbol=AAPL
        /// </code>
        ///
        /// **Sample response**
        /// <code>
        /// {
        ///   "ticker": "AAPL",
        ///   "date": "2024-09-28",
        ///   "operatingCashFlow": 118254000000,
        ///   "capitalExpenditureAbs": 9447000000,
        ///   "changeInWorkingCapital": 3651000000,
        ///   "fcf": 108807000000,
        ///   "ownerEarnings": 112458000000
        /// }
        /// </code>
        /// </remarks>
        [SwaggerOperation(
            Summary = "Owner Earnings (Buffett-style)",
            Description = "Computes Owner Earnings = OCF − CapEx ± ΔWC, fallback to FCF if ΔWC missing.",
            OperationId = "Analytics_GetOwnerEarnings",
            Tags = new[] { "Analytics" }
        )]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
        [HttpGet("owner-earnings")]
        public async Task<IActionResult> GetOwnerEarnings(
                    [FromQuery] string symbol,
                    [FromServices] AppDbContext db,
                    CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { error = "Missing ?symbol=..." });
            var ticker = symbol.Trim().ToUpperInvariant();

            // Latest annual CF row
            var cf = await db.CashFlows.AsNoTracking()
                .Where(c => c.Symbol == ticker && c.Frequency == "annual")
                .OrderByDescending(c => c.Date)
                .Select(c => new
                {
                    c.Date,
                    c.OperatingCashFlow,
                    c.CapitalExpenditure,
                    c.ChangeInWorkingCapital
                })
                .FirstOrDefaultAsync(ct);

            if (cf is null || !cf.OperatingCashFlow.HasValue || !cf.CapitalExpenditure.HasValue)
                return NotFound(new { error = $"No annual cash flow row with OCF+CapEx for {ticker}." });

            // Normalize inputs (treat CapEx as positive outflow)
            double ocf = (double)cf.OperatingCashFlow!.Value;
            double capexAbs = Math.Abs((double)cf.CapitalExpenditure!.Value);
            double deltaWc = cf.ChangeInWorkingCapital.HasValue ? (double)cf.ChangeInWorkingCapital.Value : 0.0;

            // Compute via helper: OE = OCF - CapEx + ΔWC  (matches your current semantics)
            double? ownerEarnings = FinanceMath.OwnerEarningsFromCashFlow(ocf, capexAbs, deltaWc);

            // For reference: FCF = OCF - CapEx (same semantics as before)
            double fcf = ocf - capexAbs;


            return Ok(new
            {
                ticker,
                date = cf.Date,
                operatingCashFlow = ocf,
                capitalExpenditureAbs = capexAbs,
                changeInWorkingCapital = cf.ChangeInWorkingCapital,
                fcf,
                ownerEarnings
            });
        }

        /// <summary>
        /// Returns latest annual Owner Earnings Yield = OwnerEarnings / MarketCap.
        /// </summary>
        /// <remarks>
        /// **Formula**  
        /// <c>OwnerEarnings = OCF − CapEx ± ΔWC</c>  
        /// <c>OwnerEarningsYield = OwnerEarnings / (Price × Shares)</c>  
        ///
        /// **Data sources**  
        /// - **OwnerEarnings**: From latest annual cash flow (OCF − CapEx ± ΔWC)  
        /// - **Shares**: Latest annual income row on/before CF date (fallback: latest annual)  
        /// - **Price**: First close price on/after CF date (fallback: latest available)  
        ///
        /// **Example**  
        /// <code>
        /// GET /api/analytics/owner-earnings-yield?symbol=AAPL
        /// </code>
        ///
        /// **Sample response**
        /// <code>
        /// {
        ///   "ticker": "AAPL",
        ///   "date": "2024-09-28",
        ///   "ownerEarnings": 112458000000,
        ///   "shares": 15343783000,
        ///   "priceUsed": 200,
        ///   "marketCap": 3068756600000,
        ///   "ownerEarningsYield": 0.0366,
        ///   "ownerEarningsYieldPct": 3.66,
        ///   "ownerEarningsYieldRounded": 0.0366
        /// }
        /// </code>
        /// </remarks>
        [SwaggerOperation(
            Summary = "Owner Earnings Yield",
            Description = "Computes Owner Earnings Yield = OwnerEarnings / (Price × Shares), based on latest annual data.",
            OperationId = "Analytics_GetOwnerEarningsYield",
            Tags = new[] { "Analytics" }
        )]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
        [HttpGet("owner-earnings-yield")]
        public async Task<IActionResult> GetOwnerEarningsYield(
                    [FromQuery] string symbol,
                    [FromServices] AppDbContext db,
                    CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { error = "Missing ?symbol=..." });
            var ticker = symbol.Trim().ToUpperInvariant();

            // 1) Latest annual Cash Flow row
            var cf = await db.CashFlows.AsNoTracking()
                .Where(c => c.Symbol == ticker && c.Frequency == "annual")
                .OrderByDescending(c => c.Date)
                .Select(c => new { c.Date, c.OperatingCashFlow, c.CapitalExpenditure, c.ChangeInWorkingCapital })
                .FirstOrDefaultAsync(ct);

            if (cf is null || !cf.OperatingCashFlow.HasValue || !cf.CapitalExpenditure.HasValue)
                return NotFound(new { error = $"No annual CF row with OCF+CapEx for {ticker}." });

            // Normalize inputs and compute Owner Earnings via helper
            double ocf = (double)cf.OperatingCashFlow!.Value;
            double capexAbs = Math.Abs((double)cf.CapitalExpenditure!.Value); // treat CapEx as positive outflow
            double deltaWc = cf.ChangeInWorkingCapital ?? 0.0;

            double? ownerEarningsOpt = FinanceMath.OwnerEarningsFromCashFlow(ocf, capexAbs, deltaWc);
            if (ownerEarningsOpt is null)
                return BadRequest(new { error = "Cannot compute Owner Earnings." });
            double ownerEarnings = ownerEarningsOpt.Value;

            // 2) Shares: latest annual income on/before CF date; fallback latest annual
            var incRows = await db.IncomeStatements.AsNoTracking()
                .Where(i => i.Symbol == ticker && i.Frequency == "annual" && i.WeightedAverageShsOut.HasValue)
                .OrderByDescending(i => i.Date)
                .ToListAsync(ct);

            var inc = incRows.FirstOrDefault(i => i.Date <= cf.Date) ?? incRows.FirstOrDefault();
            if (inc is null || !inc.WeightedAverageShsOut.HasValue || inc.WeightedAverageShsOut.Value <= 0)
                return NotFound(new { error = $"No valid shares (WeightedAverageShsOut) for {ticker} around {cf.Date}." });
            double shares = (double)inc.WeightedAverageShsOut.Value;

            // 3) Resolve ticker id for prices
            var tid = await db.Tickers.AsNoTracking()
                .Where(t => t.Symbol == ticker)
                .Select(t => t.Id)
                .FirstOrDefaultAsync(ct);
            if (tid == 0)
                return NotFound(new { error = $"Ticker {ticker} not found in Tickers." });

            // 4) Price: first on/after CF date; fallback to latest
            var pxAfter = await db.Prices.AsNoTracking()
                .Where(p => p.TickerId == tid && p.TradingDate >= cf.Date)
                .OrderBy(p => p.TradingDate)
                .Select(p => new { p.TradingDate, p.Close })
                .FirstOrDefaultAsync(ct);

            var px = pxAfter ?? await db.Prices.AsNoTracking()
                .Where(p => p.TickerId == tid)
                .OrderByDescending(p => p.TradingDate)
                .Select(p => new { p.TradingDate, p.Close })
                .FirstOrDefaultAsync(ct);

            if (px is null)
                return NotFound(new { error = $"No price data available for {ticker}." });

            double price = (double)px.Close;

            // 5) MarketCap & Yield via helpers
            double? marketCapOpt = FinanceMath.MarketCap(price, shares);
            if (marketCapOpt is null || marketCapOpt.Value <= 0)
                return BadRequest(new { error = "Computed market cap is invalid." });
            double marketCap = marketCapOpt.Value;

            double? yieldOpt = FinanceMath.OwnerEarningsYield(ownerEarnings, marketCap);

            return Ok(new
            {
                ticker,
                date = cf.Date,
                operatingCashFlow = ocf,
                capitalExpenditureAbs = capexAbs,
                changeInWorkingCapital = cf.ChangeInWorkingCapital, // keep original (nullable) for traceability
                ownerEarnings,
                shares,
                priceDateUsed = px.TradingDate,
                priceUsed = price,
                marketCap,
                ownerEarningsYield = yieldOpt,
                ownerEarningsYieldPct = yieldOpt is null ? (double?)null : yieldOpt.Value * 100.0,
                ownerEarningsYieldRounded = yieldOpt is null ? (double?)null : Math.Round(yieldOpt.Value, 4)
            });
        }

        /// <summary>
        /// Returns latest annual Owner Earnings per Share (OEPS).
        /// </summary>
        /// <remarks>
        /// **Formula**  
        /// <c>OEPS = OwnerEarnings / Shares</c>  
        ///
        /// **Where**  
        /// - **OwnerEarnings** = (OperatingCashFlow − CapEx) ± ΔWorkingCapital  
        /// - **Shares** = WeightedAverageShsOut from the latest annual income row on/before CF date (fallback: latest annual)  
        ///
        /// **Example**  
        /// <code>
        /// GET /api/analytics/oeps?symbol=AAPL
        /// </code>
        ///
        /// **Sample response**
        /// <code>
        /// {
        ///   "ticker": "AAPL",
        ///   "date": "2024-09-28",
        ///   "ownerEarnings": 112458000000,
        ///   "shares": 15343783000,
        ///   "oeps": 7.3292
        /// }
        /// </code>
        /// </remarks>
        [SwaggerOperation(
            Summary = "Owner Earnings per Share (OEPS)",
            Description = "Computes OEPS = OwnerEarnings / Shares, based on latest annual cash flow and income data.",
            OperationId = "Analytics_GetOeps",
            Tags = new[] { "Analytics" }
        )]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
        [HttpGet("oeps")]
        public async Task<IActionResult> GetOwnerEarningsPerShare(
                    [FromQuery] string symbol,
                    [FromServices] AppDbContext db,
                    CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { error = "Missing ?symbol=..." });
            var ticker = symbol.Trim().ToUpperInvariant();

            // Owner Earnings basis (latest annual CF)
            var cf = await db.CashFlows.AsNoTracking()
                .Where(c => c.Symbol == ticker && c.Frequency == "annual")
                .OrderByDescending(c => c.Date)
                .Select(c => new { c.Date, c.OperatingCashFlow, c.CapitalExpenditure, c.ChangeInWorkingCapital })
                .FirstOrDefaultAsync(ct);
            if (cf is null || !cf.OperatingCashFlow.HasValue || !cf.CapitalExpenditure.HasValue)
                return NotFound(new { error = $"No annual CF row with OCF+CapEx for {ticker}." });

            long owner = (cf.OperatingCashFlow.Value - cf.CapitalExpenditure.Value) + (cf.ChangeInWorkingCapital ?? 0);

            // Shares: latest annual income on/before OE date; fallback to latest annual
            var incRows = await db.IncomeStatements.AsNoTracking()
                .Where(i => i.Symbol == ticker && i.Frequency == "annual" && i.WeightedAverageShsOut.HasValue)
                .OrderByDescending(i => i.Date)
                .ToListAsync(ct);
            var inc = incRows.FirstOrDefault(i => i.Date <= cf.Date) ?? incRows.FirstOrDefault();
            var shares = inc?.WeightedAverageShsOut;
            if (!shares.HasValue || shares.Value <= 0)
                return NotFound(new { error = $"No valid shares for {ticker} around {cf.Date}." });

            var oeps = (double)owner / (double)shares.Value;

            return Ok(new
            {
                ticker,
                date = cf.Date,
                ownerEarnings = owner,
                shares = shares,
                oeps,
                oepsRounded = Math.Round(oeps, 4)
            });
        }

        /// <summary>
        /// Returns latest annual Price-to-Owner-Earnings ratio (P/OE).
        /// </summary>
        /// <remarks>
        /// **Formula**  
        /// <c>P/OE = Price / OEPS</c>  
        ///
        /// **Where**  
        /// - **OEPS** = OwnerEarnings / Shares  
        ///   - OwnerEarnings = (OperatingCashFlow − CapEx) ± ΔWorkingCapital (latest annual CF row)  
        ///   - Shares = WeightedAverageShsOut from latest annual income row on/before OE date (fallback: latest annual)  
        /// - **Price** = First available close price on/after OE date (fallback: latest available price)  
        ///
        /// **Example**  
        /// <code>
        /// GET /api/analytics/p-to-oe?symbol=AAPL
        /// </code>
        ///
        /// **Sample response**
        /// <code>
        /// {
        ///   "ticker": "AAPL",
        ///   "date": "2024-09-28",
        ///   "ownerEarnings": 112458000000,
        ///   "shares": 15343783000,
        ///   "oeps": 7.3292,
        ///   "priceDateUsed": "2024-09-30",
        ///   "priceUsed": 200,
        ///   "pToOe": 27.29
        /// }
        /// </code>
        /// </remarks>
        [SwaggerOperation(
            Summary = "Price-to-Owner-Earnings ratio (P/OE)",
            Description = "Computes P/OE = Price / OEPS, using latest Owner Earnings per Share and the nearest price.",
            OperationId = "Analytics_GetPriceToOwnerEarnings",
            Tags = new[] { "Analytics" }
        )]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
        [HttpGet("p-to-oe")]
        public async Task<IActionResult> GetPriceToOwnerEarnings(
                    [FromQuery] string symbol,
                    [FromServices] AppDbContext db,
                    CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { error = "Missing ?symbol=..." });
            var ticker = symbol.Trim().ToUpperInvariant();

            // Re-use OEPS calculation (inline to keep it self-contained)
            var cf = await db.CashFlows.AsNoTracking()
                .Where(c => c.Symbol == ticker && c.Frequency == "annual")
                .OrderByDescending(c => c.Date)
                .Select(c => new { c.Date, c.OperatingCashFlow, c.CapitalExpenditure, c.ChangeInWorkingCapital })
                .FirstOrDefaultAsync(ct);
            if (cf is null || !cf.OperatingCashFlow.HasValue || !cf.CapitalExpenditure.HasValue)
                return NotFound(new { error = $"No annual CF row with OCF+CapEx for {ticker}." });

            var incRows = await db.IncomeStatements.AsNoTracking()
                .Where(i => i.Symbol == ticker && i.Frequency == "annual" && i.WeightedAverageShsOut.HasValue)
                .OrderByDescending(i => i.Date)
                .ToListAsync(ct);
            var inc = incRows.FirstOrDefault(i => i.Date <= cf.Date) ?? incRows.FirstOrDefault();
            var shares = inc?.WeightedAverageShsOut;
            if (!shares.HasValue || shares.Value <= 0)
                return NotFound(new { error = $"No valid shares for {ticker} around {cf.Date}." });

            // Resolve ticker id and pick price on/after OE date (fallback: latest)
            var tid = await db.Tickers.AsNoTracking()
                .Where(t => t.Symbol == ticker)
                .Select(t => t.Id)
                .FirstOrDefaultAsync(ct);
            if (tid == 0) return NotFound(new { error = $"Ticker {ticker} not found." });

            var pxAfter = await db.Prices.AsNoTracking()
                .Where(p => p.TickerId == tid && p.TradingDate >= cf.Date)
                .OrderBy(p => p.TradingDate)
                .Select(p => new { p.TradingDate, p.Close })
                .FirstOrDefaultAsync(ct);

            var px = pxAfter ?? await db.Prices.AsNoTracking()
                .Where(p => p.TickerId == tid)
                .OrderByDescending(p => p.TradingDate)
                .Select(p => new { p.TradingDate, p.Close })
                .FirstOrDefaultAsync(ct);
            if (px is null) return NotFound(new { error = $"No price data available for {ticker}." });

            // Normalize inputs (CapEx as positive outflow)
            double ocf = (double)cf.OperatingCashFlow!.Value;
            double capexAbs = Math.Abs((double)cf.CapitalExpenditure!.Value);
            double deltaWc = cf.ChangeInWorkingCapital ?? 0.0;

            // Owner Earnings via helper (OCF - CapEx + ΔWC)
            double? owner = FinanceMath.OwnerEarningsFromCashFlow(ocf, capexAbs, deltaWc);
            if (owner is null) return BadRequest(new { error = "Cannot compute Owner Earnings." });

            // Shares already validated; lift to non-null local
            double sh = (double)shares!.Value;

            // OEPS + P/OE via helpers
            double? oeps = FinanceMath.OwnerEarningsPerShare(owner.Value, sh);

            // Close ist i. d. R. decimal (non-null) → in double wandeln
            double priceVal = (double)px.Close;

            double? pToOe = (oeps is null) ? (double?)null
                                           : FinanceMath.PriceToOwnerEarnings(priceVal, oeps.Value);
            return Ok(new
            {
                ticker,
                date = cf.Date,
                ownerEarnings = owner,     // jetzt double?
                shares = sh,
                oeps,
                priceDateUsed = px.TradingDate,
                priceUsed = priceVal,
                pToOe,
                pToOeRounded = pToOe is null ? (double?)null : Math.Round(pToOe.Value, 2)
            });
        }
    }
}
