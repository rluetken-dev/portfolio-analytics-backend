namespace Portfolio.Api.Data.Entities
{
    /// <summary>
    /// SQL row for a balance sheet observation.
    /// NOTE: Stores both annual and quarterly rows distinguished by Frequency.
    /// </summary>
    public class BalanceSheetEntity
    {
        public int Id { get; set; } // Surrogate PK

        /// <summary>Ticker symbol, uppercased (e.g., "AAPL").</summary>
        public string Symbol { get; set; } = string.Empty;

        /// <summary>Period end date from FMP (ISO yyyy-MM-dd).</summary>
        public DateOnly Date { get; set; }

        /// <summary>"annual" or "quarter" to distinguish frequency.</summary>
        public string Frequency { get; set; } = "annual";

        /// <summary>Reported currency code (e.g., "USD").</summary>
        public string? ReportedCurrency { get; set; }

        /// <summary>Total assets (raw integer from API).</summary>
        public long? TotalAssets { get; set; }

        /// <summary>Total liabilities (raw integer from API).</summary>
        public long? TotalLiabilities { get; set; }

        /// <summary>Total stockholders' equity (raw integer from API).</summary>
        public long? TotalStockholdersEquity { get; set; }

        /// <summary>Cash and cash equivalents (raw integer from API).</summary>
        public long? CashAndCashEquivalents { get; set; }
    }
}
