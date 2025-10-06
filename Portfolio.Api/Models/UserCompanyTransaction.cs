using System;

namespace Portfolio.Api.Models
{
    /// <summary>
    /// Represents a single buy or sell transaction for a user's company position.
    /// </summary>
    public class UserCompanyTransaction
    {
        public int Id { get; set; }

        // FK: reference to the user performing the transaction
        public int UserId { get; set; }

        // FK: reference to the company (ticker)
        public int TickerId { get; set; }

        // Positive = Buy, Negative = Sell
        public int Shares { get; set; }

        // Price per share at transaction time
        public decimal? Price { get; set; }

        // Optional user notes (e.g., "Bought the dip", "Sold for rebalancing")
        public string? Notes { get; set; }

        // UTC timestamp when the transaction occurred
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation property for EF Core (optional for relational mapping)
        public Ticker? Ticker { get; set; }
    }
}
