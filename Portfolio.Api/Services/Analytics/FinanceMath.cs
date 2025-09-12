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
    }
}
