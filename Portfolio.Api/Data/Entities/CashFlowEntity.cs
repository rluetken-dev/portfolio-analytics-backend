namespace Portfolio.Api.Data.Entities;

/// <summary>
/// Represents one cash flow statement observation stored in the local database.
/// Annual and quarterly rows are distinguished by <see cref="Frequency" />.
/// </summary>
public sealed class CashFlowEntity
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
    /// Net cash provided by operating activities.
    /// </summary>
    public long? OperatingCashFlow { get; set; }

    /// <summary>
    /// Capital expenditure. Usually reported as a negative value.
    /// </summary>
    public long? CapitalExpenditure { get; set; }

    /// <summary>
    /// Free cash flow.
    /// </summary>
    public long? FreeCashFlow { get; set; }

    /// <summary>
    /// Net income used for reconciliation.
    /// </summary>
    public long? NetIncome { get; set; }

    /// <summary>
    /// Depreciation and amortization.
    /// </summary>
    public long? DepreciationAndAmortization { get; set; }

    /// <summary>
    /// Change in working capital.
    /// </summary>
    public long? ChangeInWorkingCapital { get; set; }
}