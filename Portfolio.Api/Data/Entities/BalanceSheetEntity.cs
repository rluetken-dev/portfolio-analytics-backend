namespace Portfolio.Api.Data.Entities;

/// <summary>
/// Represents one balance sheet observation stored in the local database.
/// Annual and quarterly rows are distinguished by <see cref="Frequency" />.
/// </summary>
public sealed class BalanceSheetEntity
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
    /// Total assets.
    /// </summary>
    public long? TotalAssets { get; set; }

    /// <summary>
    /// Total liabilities.
    /// </summary>
    public long? TotalLiabilities { get; set; }

    /// <summary>
    /// Total stockholders' equity.
    /// </summary>
    public long? TotalStockholdersEquity { get; set; }

    /// <summary>
    /// Cash and cash equivalents.
    /// </summary>
    public long? CashAndCashEquivalents { get; set; }
}