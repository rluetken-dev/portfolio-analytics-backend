namespace Portfolio.Api.Data.Entities
{
    /// <summary>
    /// SQL row for a cash flow statement observation.
    /// NOTE: Stores both annual and quarterly rows distinguished by Frequency.
    /// </summary>
    public class CashFlowEntity
    {
        public int Id { get; set; } // Surrogate PK

        /// <summary>Ticker symbol, uppercased (e.g., "AAPL").</summary>
        public string Symbol { get; set; } = string.Empty;

        /// <summary>Period end date from FMP (ISO yyyy-MM-dd).</summary>
        public DateOnly Date { get; set; }

        /// <summary>"annual" or "quarter" to distinguish frequency.</summary>
        public string Frequency { get; set; } = "annual";

        /// <summary>Reported currency code, e.g., "USD".</summary>
        public string? ReportedCurrency { get; set; }

        /// <summary>Net cash from operating activities.</summary>
        public long? OperatingCashFlow { get; set; }

        /// <summary>Capital expenditure (usually negative).</summary>
        public long? CapitalExpenditure { get; set; }

        /// <summary>Free cash flow (OperatingCashFlow + CapitalExpenditure).</summary>
        public long? FreeCashFlow { get; set; }

        /// <summary>Net income (for reconciliation).</summary>
        public long? NetIncome { get; set; }

        /// <summary>Depreciation &amp; amortization.</summary>
        public long? DepreciationAndAmortization { get; set; }

        /// <summary>Change in working capital (adjustment for Owner Earnings).</summary>
        public long? ChangeInWorkingCapital { get; set; }
    }
}
