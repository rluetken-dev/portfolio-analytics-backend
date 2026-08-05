namespace Portfolio.Api.Data.Entities;

/// <summary>
/// Represents one income statement observation stored in the local database.
/// Annual and quarterly rows are distinguished by <see cref="Frequency" />.
/// </summary>
public sealed class IncomeStatementEntity
{
    /// <summary>
    /// Surrogate primary key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Uppercase ticker symbol, for example AAPL.
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Statement period end date.
    /// </summary>
    public DateOnly Date { get; set; }

    /// <summary>
    /// Statement frequency: annual or quarter.
    /// </summary>
    public string Frequency { get; set; } = "annual";

    /// <summary>
    /// Reported currency code, for example USD.
    /// </summary>
    public string? ReportedCurrency { get; set; }

    /// <summary>
    /// Total revenue for the period.
    /// </summary>
    public long? Revenue { get; set; }

    /// <summary>
    /// Net income for the period.
    /// </summary>
    public long? NetIncome { get; set; }

    /// <summary>
    /// Basic earnings per share.
    /// </summary>
    public double? Eps { get; set; }

    /// <summary>
    /// Diluted earnings per share.
    /// </summary>
    public double? EpsDiluted { get; set; }

    /// <summary>
    /// Weighted average shares outstanding, basic.
    /// </summary>
    public long? WeightedAverageShsOut { get; set; }

    /// <summary>
    /// Weighted average shares outstanding, diluted.
    /// </summary>
    public long? WeightedAverageShsOutDil { get; set; }
}