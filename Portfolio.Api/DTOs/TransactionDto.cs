namespace Portfolio.Api.DTOs
{
    /// <summary>
    /// Lightweight transaction data returned to the frontend.
    /// Used for displaying a user's buy/sell history per company.
    /// </summary>
    public class TransactionDto
    {
        public DateTime CreatedAt { get; set; }   // Date/time when transaction was recorded
        public int Shares { get; set; }           // Positive = buy, negative = sell
        public decimal? Price { get; set; }        // Price per share at transaction
        public string? Notes { get; set; }        // Optional note entered by user
        public string? Symbol { get; set; }       // Company ticker symbol (e.g. "AAPL")
        public string? CompanyName { get; set; }  // Company full name (for display)
        public string Type => Shares >= 0 ? "Buy" : "Sell";  // Derived for frontend clarity
        public decimal? TotalUSD => Price.HasValue ? Price * Math.Abs(Shares) : null; // convenience field
    }
}
