namespace Portfolio.Api.Models;

/// <summary>
/// Represents one close-price point in a time series.
/// </summary>
public sealed class TimeseriesPoint
{
    public DateOnly Date { get; set; }

    public decimal Close { get; set; }
}