using Xunit;
using Portfolio.Api.Services.Analytics;

namespace Portfolio.Api.Tests
{
    /// <summary>
    /// Unit tests for FinanceMath helper functions.
    /// Pure, simple tests with clear expectations.
    /// </summary>
    // using Xunit;
    // using Portfolio.Api.Services.Analytics;

    public class FinanceMathTests
    {
        /// <summary>
        /// ROE should return 0.20 (20%) when NetIncome=200 and Equity=1000.
        /// </summary>
        [Fact]
        public void Roe_ShouldReturnCorrectValue()
        {
            // Arrange
            double netIncome = 200, equity = 1000;

            // Act
            var result = FinanceMath.Roe(netIncome, equity);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(0.20, result.Value, precision: 3);
        }

        /// <summary>
        /// FCF Yield should return 0.05 (5%) when FCF=500 and MarketCap=10000.
        /// </summary>
        [Fact]
        public void FcfYield_ShouldReturnCorrectValue()
        {
            // Arrange
            double fcf = 500, marketCap = 10_000;

            // Act
            var result = FinanceMath.FcfYield(fcf, marketCap);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(0.05, result.Value, precision: 3);
        }

        /// <summary>
        /// Net Margin should return 0.20 (20%) when NetIncome=300 and Revenue=1500.
        /// </summary>
        [Fact]
        public void NetMargin_ShouldReturnCorrectValue()
        {
            // Arrange
            double netIncome = 300, revenue = 1500;

            // Act
            var result = FinanceMath.NetMargin(netIncome, revenue);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(0.20, result.Value, precision: 3);
        }

        /// <summary>
        /// P/E should return 20 when Price=200 and EPS=10.
        /// </summary>
        [Fact]
        public void Pe_ShouldReturnCorrectValue()
        {
            // Arrange
            double price = 200, eps = 10;

            // Act
            var result = FinanceMath.Pe(price, eps);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(20.0, result.Value, precision: 3);
        }

        /// <summary>
        /// P/B should return 2.0 when Price=100 and BVPS=50.
        /// </summary>
        [Fact]
        public void Pb_ShouldReturnCorrectValue()
        {
            // Arrange
            double price = 100, bvps = 50;

            // Act
            var result = FinanceMath.Pb(price, bvps);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2.0, result.Value, precision: 3);
        }

        /// <summary>
        /// Debt-to-Equity should return 2.0 when Liabilities=200 and Equity=100.
        /// </summary>
        [Fact]
        public void DebtToEquity_ShouldReturnCorrectValue()
        {
            // Arrange
            double liabilities = 200, equity = 100;

            // Act
            var result = FinanceMath.DebtToEquity(liabilities, equity);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2.0, result.Value, precision: 3);
        }

        /// <summary>
        /// Equity Ratio should return 0.40 (40%) when Equity=40 and Assets=100.
        /// </summary>
        [Fact]
        public void EquityRatio_ShouldReturnCorrectValue()
        {
            // Arrange
            double equity = 40, assets = 100;

            // Act
            var result = FinanceMath.EquityRatio(equity, assets);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(0.40, result.Value, precision: 3);
        }

        /// <summary>
        /// Debt-to-Assets should return 0.60 (60%) when Liabilities=60 and Assets=100.
        /// </summary>
        [Fact]
        public void DebtToAssets_ShouldReturnCorrectValue()
        {
            // Arrange
            double liabilities = 60, assets = 100;

            // Act
            var result = FinanceMath.DebtToAssets(liabilities, assets);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(0.60, result.Value, precision: 3);
        }

        /// <summary>
        /// Owner Earnings should compute NI + D&A - CapEx - ΔWC (e.g., 100 + 30 - 20 - 5 = 105).
        /// </summary>
        [Fact]
        public void OwnerEarnings_ShouldReturnCorrectValue()
        {
            // Arrange
            double ni = 100, da = 30, capex = 20, deltaWc = 5;

            // Act
            var result = FinanceMath.OwnerEarnings(ni, da, capex, deltaWc);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(105.0, result.Value, precision: 3);
        }

        /// <summary>
        /// ROA should return 0.10 (10%) when NetIncome=10 and Assets=100.
        /// </summary>
        [Fact]
        public void Roa_ShouldReturnCorrectValue()
        {
            // Arrange
            double netIncome = 10, assets = 100;

            // Act
            var result = FinanceMath.Roa(netIncome, assets);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(0.10, result.Value, precision: 3);
        }

        /// <summary>
        /// FCF Margin should return 0.10 (10%) when FCF=100 and Revenue=1000.
        /// </summary>
        [Fact]
        public void FcfMargin_ShouldReturnCorrectValue()
        {
            // Arrange
            double fcf = 100, revenue = 1000;

            // Act
            var result = FinanceMath.FcfMargin(fcf, revenue);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(0.10, result.Value, precision: 3);
        }

        /// <summary>
        /// Equity CAGR should be ≈0.1487 (14.87%) when Start=100, End=200 over 5 years.
        /// </summary>
        [Fact]
        public void EquityCagr_ShouldReturnCorrectValue()
        {
            // Arrange
            double startEquity = 100, endEquity = 200, years = 5;

            // Act
            var result = FinanceMath.EquityCagr(startEquity, endEquity, years);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(0.1487, result.Value, precision: 3);
        }

        /// <summary>
        /// OEPS = OwnerEarnings / Shares (e.g., 100 / 10 = 10).
        /// </summary>
        [Fact]
        public void OwnerEarningsPerShare_ShouldReturnCorrectValue()
        {
            // Arrange
            double oe = 100, shares = 10;

            // Act
            var result = FinanceMath.OwnerEarningsPerShare(oe, shares);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(10.0, result.Value, precision: 3);
        }

        /// <summary>
        /// Price-to-Owner-Earnings should be Price / OEPS (e.g., 200 / 10 = 20).
        /// </summary>
        [Fact]
        public void PriceToOwnerEarnings_ShouldReturnCorrectValue()
        {
            // Arrange
            double price = 200, oeps = 10;

            // Act
            var result = FinanceMath.PriceToOwnerEarnings(price, oeps);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(20.0, result.Value, precision: 3);
        }

        /// <summary>
        /// FCF helper should return OCF − CapEx (e.g., 120 − 20 = 100).
        /// </summary>
        [Fact]
        public void Fcf_ShouldReturnOcfMinusCapex()
        {
            // Arrange
            double ocf = 120, capexAbs = 20;

            // Act
            var result = FinanceMath.Fcf(ocf, capexAbs);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(100.0, result.Value, precision: 6);
        }

        /// <summary>
        /// MarketCap should be Price × Shares (e.g., 200 × 1,000,000 = 200,000,000).
        /// </summary>
        [Fact]
        public void MarketCap_ShouldMultiplyPriceAndShares()
        {
            // Arrange
            double price = 200, shares = 1_000_000;

            // Act
            var result = FinanceMath.MarketCap(price, shares);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(200_000_000, result.Value, precision: 3);
        }

        /// <summary>
        /// EPS helper should be NetIncome / Shares (e.g., 100 / 10 = 10).
        /// </summary>
        [Fact]
        public void Eps_ShouldReturnNetIncomePerShare()
        {
            // Arrange
            double netIncome = 100, shares = 10;

            // Act
            var result = FinanceMath.Eps(netIncome, shares);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(10.0, result.Value, precision: 3);
        }

        /// <summary>
        /// BVPS helper should be Equity / Shares (e.g., 300 / 100 = 3).
        /// </summary>
        [Fact]
        public void Bvps_ShouldReturnEquityPerShare()
        {
            // Arrange
            double equity = 300, shares = 100;

            // Act
            var result = FinanceMath.Bvps(equity, shares);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(3.0, result.Value, precision: 3);
        }

        /// <summary>
        /// Owner Earnings Yield should be OE / MarketCap (e.g., 120 / 3,000 = 0.04).
        /// </summary>
        [Fact]
        public void OwnerEarningsYield_ShouldReturnOeDivMarketCap()
        {
            // Arrange
            double oe = 120, mc = 3_000;

            // Act
            var result = FinanceMath.OwnerEarningsYield(oe, mc);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(0.04, result.Value, precision: 3);
        }
    }
}
