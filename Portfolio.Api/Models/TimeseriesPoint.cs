// File: Models/TimeseriesPoint.cs
namespace Portfolio.Api.Models
{
    /// <summary>
    /// Lightweight projection for time series output (date + close only).
    /// </summary>
    public sealed class TimeseriesPoint
    {
        /// <summary>Trading date (UTC, day precision).</summary>
        public DateOnly Date { get; set; }

        /// <summary>Close price for the given date (unadjusted close).</summary>
        public decimal Close { get; set; }
    }
}
