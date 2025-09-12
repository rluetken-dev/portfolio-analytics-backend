using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Api.Data;
using Portfolio.Api.Services.Analytics; // FinanceMath helper

namespace Portfolio.Api.Controllers
{
    [ApiController]
    [Route("api/analytics")]
    public class AnalyticsController : ControllerBase
    {
        /// <summary>
        /// Returns latest annual ROE using average equity if a prior annual equity exists.
        /// English: ROE = NetIncome / AverageEquity, where AverageEquity = (Equity_t + Equity_{t-1}) / 2.
        /// Falls back to end-of-period equity if prior-year is missing or zero.
        /// </summary>
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
        /// Returns latest annual Debt-to-Equity (D/E) = TotalLiabilities / TotalStockholdersEquity.
        /// Example: GET /api/analytics/debt-to-equity?symbol=AAPL
        /// </summary>
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

            // 2) Compute D/E via helper (null if equity invalid)
            double? de = (bal.TotalLiabilities.HasValue && bal.TotalStockholdersEquity.HasValue)
                ? FinanceMath.DebtToEquity((double)bal.TotalLiabilities.Value, (double)bal.TotalStockholdersEquity.Value)
                : null;

            return Ok(new
            {
                ticker,
                date = bal.Date,
                totalLiabilities = bal.TotalLiabilities,
                debtToEquity = de,
                debtToEquityRounded = de is null ? (double?)null : Math.Round(de.Value, 2)
            });
        }

        /// <summary>
        /// Returns latest annual Net Margin = NetIncome / Revenue.
        /// Example: GET /api/analytics/net-margin?symbol=AAPL
        /// </summary>
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
        /// Returns latest annual ROA = NetIncome / TotalAssets.
        /// Example: GET /api/analytics/roa?symbol=AAPL
        /// </summary>
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
        /// Returns latest annual Equity Ratio = TotalStockholdersEquity / TotalAssets.
        /// Example: GET /api/analytics/equity-ratio?symbol=AAPL
        /// </summary>
        [HttpGet("equity-ratio")]
        public async Task<IActionResult> GetEquityRatio(
            [FromQuery] string symbol,
            [FromServices] AppDbContext db,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { error = "Missing ?symbol=..." });

            var ticker = symbol.Trim().ToUpperInvariant();

            // Find latest annual balance row (we need Assets and Equity)
            var bal = await db.BalanceSheets.AsNoTracking()
                .Where(b => b.Symbol == ticker && b.Frequency == "annual")
                .OrderByDescending(b => b.Date)
                .Select(b => new { b.Date, b.TotalAssets, b.TotalStockholdersEquity })
                .FirstOrDefaultAsync(ct);

            if (bal is null)
                return NotFound(new { error = $"No annual balance row for {ticker}." });

            double? equityRatio = (bal.TotalAssets.HasValue && bal.TotalStockholdersEquity.HasValue)
                ? FinanceMath.EquityRatio((double)bal.TotalStockholdersEquity.Value, (double)bal.TotalAssets.Value)
                : (double?)null;

            return Ok(new
            {
                ticker,
                date = bal.Date,
                totalAssets = bal.TotalAssets,
                equity = bal.TotalStockholdersEquity,
                equityRatio,
                equityRatioPct = equityRatio * 100.0,
                equityRatioRounded = equityRatio is null ? (double?)null : Math.Round(equityRatio.Value, 4)
            });
        }

        /// <summary>
        /// Returns latest annual Debt-to-Assets = TotalLiabilities / TotalAssets.
        /// Example: GET /api/analytics/debt-to-assets?symbol=AAPL
        /// </summary>
        [HttpGet("debt-to-assets")]
        public async Task<IActionResult> GetDebtToAssets(
            [FromQuery] string symbol,
            [FromServices] AppDbContext db,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { error = "Missing ?symbol=..." });

            var ticker = symbol.Trim().ToUpperInvariant();

            // Read latest annual balance row
            var bal = await db.BalanceSheets.AsNoTracking()
                .Where(b => b.Symbol == ticker && b.Frequency == "annual")
                .OrderByDescending(b => b.Date)
                .Select(b => new { b.Date, b.TotalAssets, b.TotalLiabilities })
                .FirstOrDefaultAsync(ct);

            if (bal is null)
                return NotFound(new { error = $"No annual balance row for {ticker}." });

            double? da = (bal.TotalAssets.HasValue && bal.TotalLiabilities.HasValue)
                ? FinanceMath.DebtToAssets((double)bal.TotalLiabilities.Value, (double)bal.TotalAssets.Value)
                : (double?)null;

            return Ok(new
            {
                ticker,
                date = bal.Date,
                totalLiabilities = bal.TotalLiabilities,
                totalAssets = bal.TotalAssets,
                debtToAssets = da,
                debtToAssetsPct = da * 100.0,
                debtToAssetsRounded = da is null ? (double?)null : Math.Round(da.Value, 4)
            });
        }

        /// <summary>
        /// Returns latest available close price for a ticker.
        /// Example: GET /api/analytics/price?symbol=AAPL
        /// </summary>
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
        /// Returns latest annual EPS = NetIncome / WeightedAverageShsOut.
        /// Example: GET /api/analytics/eps?symbol=AAPL
        /// </summary>
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
        /// Returns latest P/E ratio = Price per Share / EPS (annual EPS).
        /// Example: GET /api/analytics/pe?symbol=AAPL
        /// </summary>
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
        /// Returns latest annual BVPS (Book Value per Share) = Equity / SharesOutstanding.
        /// Example: GET /api/analytics/bvps?symbol=AAPL
        /// </summary>
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
        /// Returns latest P/B ratio = Price per Share / Book Value per Share.
        /// Example: GET /api/analytics/pb?symbol=AAPL
        /// </summary>
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
        /// Returns latest annual Asset Turnover = Revenue / TotalAssets.
        /// Example: GET /api/analytics/asset-turnover?symbol=AAPL
        /// </summary>
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
        /// Returns equity CAGR (Compound Annual Growth Rate) using earliest and latest annual balance rows.
        /// Example: GET /api/analytics/equity-cagr?symbol=AAPL
        /// </summary>
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
        /// English: FCF = OperatingCashFlow - CapitalExpenditure (latest annual period).
        /// </summary>
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
        /// Returns latest annual FCF Yield = FCF / MarketCap with robust fallbacks.
        /// English:
        /// - FCF = OperatingCashFlow - CapitalExpenditure (latest annual CF row)
        /// - Shares = latest annual income row with shares on/before CF date (fallback: latest annual)
        /// - Price  = first price on/after CF date (fallback: latest available price)
        /// </summary>
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
        /// Returns latest annual FCF Margin = FCF / Revenue.
        /// English:
        /// - FCF from latest annual cash flow = OperatingCashFlow - CapitalExpenditure
        /// - Revenue from latest annual income at the same (or nearest prior) date
        /// </summary>
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
        /// English: OwnerEarnings = OperatingCashFlow - CapEx ± ChangeInWorkingCapital.
        /// Falls back to FCF (OCF - CapEx) if working-capital change is missing.
        /// </summary>
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
        /// English:
        /// - OwnerEarnings = (OCF - CapEx) ± ChangeInWorkingCapital
        /// - MarketCap = Price * Shares (first price on/after OE date; fallback: latest price)
        /// - Shares from latest annual income on/before OE date (fallback: latest annual)
        /// </summary>
        [HttpGet("owner-earnings-yield")]
        public async Task<IActionResult> GetOwnerEarningsYield(
            [FromQuery] string symbol,
            [FromServices] AppDbContext db,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { error = "Missing ?symbol=..." });
            var ticker = symbol.Trim().ToUpperInvariant();

            // 1) Latest annual CF row (to compute Owner Earnings)
            var cf = await db.CashFlows.AsNoTracking()
                .Where(c => c.Symbol == ticker && c.Frequency == "annual")
                .OrderByDescending(c => c.Date)
                .Select(c => new { c.Date, c.OperatingCashFlow, c.CapitalExpenditure, c.ChangeInWorkingCapital })
                .FirstOrDefaultAsync(ct);

            // --- Guard cf first (before using cf.Date anywhere)
            if (cf is null || !cf.OperatingCashFlow.HasValue || !cf.CapitalExpenditure.HasValue)
                return NotFound(new { error = $"No annual CF row with OCF+CapEx for {ticker}." });

            // --- Lift optionals to non-null locals (kills CS8602 on these)
            var cfDate = cf.Date;
            double ocf = (double)cf.OperatingCashFlow.Value;
            double capexAbs = Math.Abs((double)cf.CapitalExpenditure.Value);
            double deltaWc = cf.ChangeInWorkingCapital ?? 0.0;

            // 2) Shares: latest annual income <= OE date; fallback to latest annual
            var incRows = await db.IncomeStatements.AsNoTracking()
                .Where(i => i.Symbol == ticker && i.Frequency == "annual" && i.WeightedAverageShsOut.HasValue)
                .OrderByDescending(i => i.Date)
                .ToListAsync(ct);

            var inc = incRows.FirstOrDefault(i => i.Date <= cfDate) ?? incRows.FirstOrDefault();
            if (inc is null || !inc.WeightedAverageShsOut.HasValue || inc.WeightedAverageShsOut.Value <= 0)
                return NotFound(new { error = $"No valid shares (WeightedAverageShsOut) for {ticker} around {cfDate}." });

            double sharesVal = (double)inc.WeightedAverageShsOut.Value;

            // 3) Resolve ticker id to read Prices
            var tid = await db.Tickers.AsNoTracking()
                .Where(t => t.Symbol == ticker)
                .Select(t => t.Id)
                .FirstOrDefaultAsync(ct);
            if (tid == 0)
                return NotFound(new { error = $"Ticker {ticker} not found in Tickers." });

            // 4) Price: first on/after OE date; fallback to latest overall (filter Close != null)
            var pxAfter = await db.Prices.AsNoTracking()
                .Where(p => p.TickerId == tid && p.TradingDate >= cfDate)
                .OrderBy(p => p.TradingDate)
                .Select(p => new { p.TradingDate, Close = (double)p.Close! })
                .FirstOrDefaultAsync(ct);

            var px = pxAfter ?? await db.Prices.AsNoTracking()
                .Where(p => p.TickerId == tid)
                .OrderByDescending(p => p.TradingDate)
                .Select(p => new { p.TradingDate, Close = (double)p.Close! })
                .FirstOrDefaultAsync(ct);

            if (px is null)
                return NotFound(new { error = $"No price data available for {ticker}." });

            // 5) Compute OE via helper, then yield
            double? ownerEarnings = FinanceMath.OwnerEarningsFromCashFlow(ocf, capexAbs, deltaWc);
            if (ownerEarnings is null)
                return BadRequest(new { error = "Cannot compute Owner Earnings." });

            double marketCap = px.Close * sharesVal;
            if (marketCap <= 0)
                return BadRequest(new { error = "Computed market cap is invalid (<= 0)." });

            double? oeYield = FinanceMath.FcfYield(ownerEarnings.Value, marketCap);

            // Response (use non-null locals)
            return Ok(new
            {
                ticker,
                date = cfDate,
                operatingCashFlow = ocf,
                capitalExpenditureAbs = capexAbs,
                changeInWorkingCapital = cf.ChangeInWorkingCapital, // darf nullable bleiben
                ownerEarnings,
                shares = sharesVal,
                priceDateUsed = px.TradingDate,
                priceUsed = px.Close,
                marketCap,
                ownerEarningsYield = oeYield,
                ownerEarningsYieldPct = oeYield * 100.0,
                ownerEarningsYieldRounded = oeYield is null ? (double?)null : Math.Round(oeYield.Value, 4)
            });
        }

        /// <summary>
        /// Returns latest annual Owner Earnings per Share (OEPS).
        /// English: OEPS = OwnerEarnings / Shares, where
        /// - OwnerEarnings = (OCF - CapEx) ± ChangeInWorkingCapital (latest annual)
        /// - Shares = latest annual income on/before OE date (fallback: latest annual)
        /// </summary>
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
        /// English: P/OE = Price / OEPS, using first price on/after OE date (fallback: latest price).
        /// </summary>
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
