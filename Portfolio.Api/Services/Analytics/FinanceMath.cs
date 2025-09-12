using System;

namespace Portfolio.Api.Services.Analytics
{
    /// <summary>
    /// Small, pure finance helpers with defensive checks.
    /// All methods return null if inputs are invalid (e.g., division by zero).
    /// Keep these pure so they are easy to unit test.
    /// </summary>
    public static class FinanceMath
    {
        /// <summary>
        /// Return on Equity (ROE) = NetIncome / Equity.
        /// Use average equity if you have it; otherwise pass period-end equity.
        /// </summary>
        /// <param name="netIncome">Net income for the period.</param>
        /// <param name="equity">Shareholders' equity (average or end-of-period).</param>
        /// <returns>ROE as decimal fraction (e.g., 0.18 for 18%), or null if invalid.</returns>
        public static double? Roe(double netIncome, double equity)
        {
            if (double.IsNaN(netIncome) || double.IsNaN(equity)) return null;
            if (Math.Abs(equity) < 1e-12) return null; // avoid division by zero
            return netIncome / equity;
        }

        /// <summary>
        /// Free Cash Flow Yield = FreeCashFlow / MarketCap.
        /// </summary>
        /// <param name="freeCashFlow">FCF for the last TTM or fiscal year.</param>
        /// <param name="marketCap">Current market capitalization.</param>
        /// <returns>FCF yield as decimal fraction (e.g., 0.05 for 5%), or null if invalid.</returns>
        public static double? FcfYield(double freeCashFlow, double marketCap)
        {
            if (double.IsNaN(freeCashFlow) || double.IsNaN(marketCap)) return null;
            if (Math.Abs(marketCap) < 1e-12) return null;
            return freeCashFlow / marketCap;
        }

        /// <summary>
        /// Net Margin = NetIncome / Revenue.
        /// </summary>
        public static double? NetMargin(double netIncome, double revenue)
        {
            if (double.IsNaN(netIncome) || double.IsNaN(revenue)) return null;
            if (Math.Abs(revenue) < 1e-12) return null;
            return netIncome / revenue;
        }

        /// <summary>
        /// Price/Earnings ratio = Price / EPS.
        /// Returns null for invalid inputs (e.g., EPS == 0).
        /// Note: You may want to use TTM EPS for comparability.
        /// </summary>
        /// <param name="price">Current or reference price.</param>
        /// <param name="eps">Earnings per share (FY or TTM).</param>
        /// <returns>P/E as a raw multiple, or null if invalid.</returns>
        public static double? Pe(double price, double eps)
        {
            if (double.IsNaN(price) || double.IsNaN(eps)) return null;
            if (Math.Abs(eps) < 1e-12) return null; // avoid division by zero
            return price / eps;
        }

        /// <summary>
        /// Price-to-Book ratio (P/B) = Price per Share / BookValuePerShare.
        /// Returns null if book value is zero or invalid.
        /// </summary>
        /// <param name="price">Current or reference price per share.</param>
        /// <param name="bookValuePerShare">Book value per share (equity / shares outstanding).</param>
        /// <returns>P/B multiple, or null if invalid.</returns>
        public static double? Pb(double price, double bookValuePerShare)
        {
            if (double.IsNaN(price) || double.IsNaN(bookValuePerShare)) return null;
            if (Math.Abs(bookValuePerShare) < 1e-12) return null;
            return price / bookValuePerShare;
        }

        /// <summary>
        /// Debt-to-Equity ratio = TotalLiabilities / Equity.
        /// Returns null if equity is zero or invalid.
        /// </summary>
        /// <param name="liabilities">Total liabilities.</param>
        /// <param name="equity">Total shareholders' equity.</param>
        /// <returns>D/E multiple, or null if invalid.</returns>
        public static double? DebtToEquity(double liabilities, double equity)
        {
            if (double.IsNaN(liabilities) || double.IsNaN(equity)) return null;
            if (Math.Abs(equity) < 1e-12) return null;
            return liabilities / equity;
        }

        /// <summary>
        /// Equity Ratio = Equity / TotalAssets.
        /// Returns null if assets are zero or invalid.
        /// </summary>
        /// <param name="equity">Total shareholders' equity.</param>
        /// <param name="assets">Total assets.</param>
        /// <returns>Equity ratio as decimal fraction (e.g., 0.4 = 40%), or null if invalid.</returns>
        public static double? EquityRatio(double equity, double assets)
        {
            if (double.IsNaN(equity) || double.IsNaN(assets)) return null;
            if (Math.Abs(assets) < 1e-12) return null;
            return equity / assets;
        }

        /// <summary>
        /// Debt-to-Assets ratio = TotalLiabilities / TotalAssets.
        /// Returns null if assets are zero or invalid.
        /// </summary>
        /// <param name="liabilities">Total liabilities.</param>
        /// <param name="assets">Total assets.</param>
        /// <returns>D/A as decimal fraction (e.g., 0.6 = 60%), or null if invalid.</returns>
        public static double? DebtToAssets(double liabilities, double assets)
        {
            if (double.IsNaN(liabilities) || double.IsNaN(assets)) return null;
            if (Math.Abs(assets) < 1e-12) return null;
            return liabilities / assets;
        }

        /// <summary>
        /// Owner Earnings (Buffett-style).
        /// Canonical: OE = NetIncome + DepreciationAndAmortization - CapEx - DeltaWorkingCapital
        /// If you don't want to penalize WC changes, pass deltaWorkingCapital = 0.
        /// Returns null if any required input is NaN.
        /// </summary>
        /// <param name="netIncome">Net income for the period (TTM or FY).</param>
        /// <param name="deprAmort">Depreciation + Amortization for the period.</param>
        /// <param name="capex">Capital expenditures (usually negative; pass absolute magnitude if you store it negative).</param>
        /// <param name="deltaWorkingCapital">
        /// Change in working capital for the period (positive if WC increased and consumed cash).
        /// Pass 0 if not available.
        /// </param>
        /// <returns>Owner earnings value, or null if inputs invalid.</returns>
        public static double? OwnerEarnings(double netIncome, double deprAmort, double capex, double deltaWorkingCapital = 0.0)
        {
            if (double.IsNaN(netIncome) || double.IsNaN(deprAmort) || double.IsNaN(capex) || double.IsNaN(deltaWorkingCapital))
                return null;

            // Convention: capex should be treated as a cash outflow (positive number here).
            // If your DB stores CapEx as negative, pass Math.Abs(capexDbValue) when calling this helper.
            return netIncome + deprAmort - capex - deltaWorkingCapital;
        }

        /// <summary>
        /// Owner Earnings derived from cash flow figures:
        /// OE = OperatingCashFlow - CapEx ± DeltaWorkingCapital
        /// Notes:
        /// - Pass CapEx as a positive outflow magnitude (use Math.Abs on DB value if it is stored negative).
        /// - Sign convention for DeltaWorkingCapital varies by source; this helper applies exactly what you pass.
        ///   If your DeltaWorkingCapital is positive when WC increases (cash outflow), you might want to subtract it.
        /// </summary>
        /// <param name="operatingCashFlow">Operating cash flow for the period.</param>
        /// <param name="capexAbs">Capital expenditures as a positive number (outflow magnitude).</param>
        /// <param name="deltaWorkingCapital">Working capital change; apply your desired sign convention before passing.</param>
        /// <returns>Owner earnings value, or null if inputs invalid.</returns>
        public static double? OwnerEarningsFromCashFlow(double operatingCashFlow, double capexAbs, double deltaWorkingCapital)
        {
            if (double.IsNaN(operatingCashFlow) || double.IsNaN(capexAbs) || double.IsNaN(deltaWorkingCapital))
                return null;

            return operatingCashFlow - capexAbs + deltaWorkingCapital;
        }

        /// <summary>
        /// Return on Assets (ROA) = NetIncome / TotalAssets.
        /// Returns null if assets are zero or invalid.
        /// </summary>
        public static double? Roa(double netIncome, double assets)
        {
            if (double.IsNaN(netIncome) || double.IsNaN(assets)) return null;
            if (Math.Abs(assets) < 1e-12) return null;
            return netIncome / assets;
        }

        /// <summary>
        /// Free Cash Flow Margin = FCF / Revenue.
        /// Returns null if revenue is zero or invalid.
        /// </summary>
        public static double? FcfMargin(double fcf, double revenue)
        {
            if (double.IsNaN(fcf) || double.IsNaN(revenue)) return null;
            if (Math.Abs(revenue) < 1e-12) return null;
            return fcf / revenue;
        }

        /// <summary>
        /// Compound Annual Growth Rate (CAGR) of Equity.
        /// Formula: CAGR = (EquityEnd / EquityStart)^(1/years) - 1
        /// Returns null if inputs invalid (years &lt;= 0, start &lt;= 0).
        /// </summary>
        public static double? EquityCagr(double equityStart, double equityEnd, double years)
        {
            if (equityStart <= 0 || equityEnd <= 0) return null;
            if (years <= 0) return null;
            return Math.Pow(equityEnd / equityStart, 1.0 / years) - 1.0;
        }

        /// <summary>
        /// Owner Earnings per share = OwnerEarnings / SharesOutstanding.
        /// Returns null if shares &lt;= 0 or invalid.
        /// </summary>
        public static double? OwnerEarningsPerShare(double ownerEarnings, double sharesOutstanding)
        {
            if (double.IsNaN(ownerEarnings) || double.IsNaN(sharesOutstanding)) return null;
            if (sharesOutstanding <= 0) return null;
            return ownerEarnings / sharesOutstanding;
        }

        /// <summary>
        /// Price-to-Owner-Earnings (P/OE) = Price per Share / OEPS.
        /// Returns null if OEPS == 0 or invalid.
        /// </summary>
        public static double? PriceToOwnerEarnings(double pricePerShare, double ownerEarningsPerShare)
        {
            if (double.IsNaN(pricePerShare) || double.IsNaN(ownerEarningsPerShare)) return null;
            if (Math.Abs(ownerEarningsPerShare) < 1e-12) return null;
            return pricePerShare / ownerEarningsPerShare;
        }

        /// <summary>
        /// Free Cash Flow = OperatingCashFlow - CapEx (CapEx as positive outflow).
        /// If your DB stores CapEx negative, pass Math.Abs(capexDbValue).
        /// </summary>
        public static double? Fcf(double operatingCashFlow, double capexAbs)
        {
            if (double.IsNaN(operatingCashFlow) || double.IsNaN(capexAbs)) return null;
            return operatingCashFlow - capexAbs;
        }

        /// <summary>
        /// Market capitalization = price per share * shares outstanding.
        /// Returns null if inputs invalid or non-positive.
        /// </summary>
        public static double? MarketCap(double pricePerShare, double sharesOutstanding)
        {
            if (double.IsNaN(pricePerShare) || double.IsNaN(sharesOutstanding)) return null;
            if (pricePerShare <= 0 || sharesOutstanding <= 0) return null;
            return pricePerShare * sharesOutstanding;
        }

        /// <summary>
        /// Earnings per share (EPS) = NetIncome / SharesOutstanding.
        /// Returns null if shares &lt;= 0 or invalid.
        /// </summary>
        public static double? Eps(double netIncome, double sharesOutstanding)
        {
            if (double.IsNaN(netIncome) || double.IsNaN(sharesOutstanding)) return null;
            if (sharesOutstanding <= 0) return null;
            return netIncome / sharesOutstanding;
        }

        /// <summary>
        /// Book value per share (BVPS) = Equity / SharesOutstanding.
        /// Returns null if shares &lt;= 0 or invalid.
        /// </summary>
        public static double? Bvps(double equity, double sharesOutstanding)
        {
            if (double.IsNaN(equity) || double.IsNaN(sharesOutstanding)) return null;
            if (sharesOutstanding <= 0) return null;
            return equity / sharesOutstanding;
        }

        /// <summary>
        /// Owner Earnings Yield = OwnerEarnings / MarketCap.
        /// Wrapper for clarity; returns null if marketCap &lt;= 0.
        /// </summary>
        public static double? OwnerEarningsYield(double ownerEarnings, double marketCap)
        {
            if (double.IsNaN(ownerEarnings) || double.IsNaN(marketCap)) return null;
            if (Math.Abs(marketCap) < 1e-12) return null;
            return ownerEarnings / marketCap;
        }
    }
}
