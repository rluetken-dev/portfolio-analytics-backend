using System;

namespace Portfolio.Api.Models
{
    /// <summary>
    /// Represents one end-of-day price record for a tradable instrument (e.g., stock or ETF).
    /// 
    /// Why store daily closes?
    /// - They are stable reference points for charts, performance, P&amp;L calculations.
    /// - Daily data is small enough to persist locally (SQLite) yet useful for most analytics.
    /// 
    /// Notes:
    /// - We use <see cref="DateOnly"/> for the trading day. EF Core (7/8+) maps this cleanly to TEXT in SQLite.
    /// - We keep a "Source" column to track from which external API the record came (useful if you switch providers later).
    /// - "RetrievedAt" captures when we saved the record (audit/debugging; not the exchange timestamp).
    /// </summary>
    public class Price
    {
        /// <summary>
        /// Surrogate primary key (auto-increment by the database).
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Ticker symbol (e.g., "AAPL", "MSFT", "SPY").
        /// Keep it short and uppercase; validation is enforced at the API layer.
        /// </summary>
        public string Symbol { get; set; } = string.Empty;

        /// <summary>
        /// Trading day of this price (no time-of-day component).
        /// We explicitly avoid DateTime here to prevent ambiguity with time zones.
        /// </summary>
        public DateOnly AsOfDate { get; set; }

        /// <summary>
        /// Official close price for the given trading day.
        /// Decimal is used for money to avoid floating-point rounding errors.
        /// The database column precision is configured in the DbContext.
        /// </summary>
        public decimal Close { get; set; }

        /// <summary>
        /// Name of the data source/provider (e.g., "alpha_vantage").
        /// Useful for transparency when combining multiple providers.
        /// </summary>
        public string Source { get; set; } = "alpha_vantage";

        /// <summary>
        /// UTC timestamp of when this record was retrieved and persisted.
        /// Helps with auditing, troubleshooting, and freshness checks.
        /// </summary>
        public DateTime RetrievedAt { get; set; } = DateTime.UtcNow;
    }
}
