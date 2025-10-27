namespace Portfolio.Api.DTOs
{
    /// <summary>
    /// Represents a single holding (stock position) in the user's portfolio.
    /// </summary>
    public class HoldingDto
    {
        public int TickerId { get; set; }                // Internal ID from database
        public string Symbol { get; set; } = string.Empty; // Stock ticker (e.g., AAPL)
        public string CompanyName { get; set; } = string.Empty; // Company name
        public decimal? Shares { get; set; }              // Shares owned
        public decimal? PurchasePriceUSD { get; set; }    // Average purchase price (USD)
        public decimal? CurrentPriceUSD { get; set; }     // Latest market price (USD)

        // Derived computed properties
        public decimal? CurrentValueUSD => Shares.HasValue && CurrentPriceUSD.HasValue
            ? Shares.Value * CurrentPriceUSD.Value
            : null;

        // Optional analytics metrics (Step 6a)
        public decimal? AvgBuyPriceUSD { get; set; }      // Average buy price (USD)
        public decimal? UnrealizedPLUSD { get; set; }     // Unrealized profit/loss (USD)
        public decimal? UnrealizedPLPercent { get; set; } // Unrealized profit/loss (%)
        public decimal? RealizedPLUSD { get; set; }  // Realized profit/loss (USD)
        public decimal? RealizedPLPercent { get; set; }  // Realized profit/loss (%)


    }
}
