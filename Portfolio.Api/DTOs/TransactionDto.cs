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
    }
}
