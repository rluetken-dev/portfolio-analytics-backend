namespace Portfolio.Api.Data.Entities
{
    /// <summary>
    /// SQL row for an income statement observation.
    /// English:
    /// - Stores both annual and quarterly rows distinguished by Frequency.
    /// - (Symbol, Date, Frequency) should be unique to avoid duplicates (FY vs Q4 on same date).
    /// - Keep types simple: long for big integers, double for EPS.
    /// </summary>
    public class IncomeStatementEntity
    {
        public int Id { get; set; } // Surrogate PK for EF/SQLite

        /// <summary>Ticker symbol, uppercased (e.g., "AAPL").</summary>
        public string Symbol { get; set; } = string.Empty;

        /// <summary>Period end date from FMP (ISO yyyy-MM-dd).</summary>
        public DateOnly Date { get; set; }

        /// <summary>"annual" or "quarter" to distinguish frequency.</summary>
        public string Frequency { get; set; } = "annual";

        /// <summary>Reported currency code, e.g., "USD".</summary>
        public string? ReportedCurrency { get; set; }

        /// <summary>Total revenue for the period (raw integer from API).</summary>
        public long? Revenue { get; set; }

        /// <summary>Net income for the period (raw integer from API).</summary>
        public long? NetIncome { get; set; }

        /// <summary>Basic EPS for the period.</summary>
        public double? Eps { get; set; }

        /// <summary>Diluted EPS for the period.</summary>
        public double? EpsDiluted { get; set; }

        /// <summary>Weighted average shares outstanding (basic).</summary>
        public long? WeightedAverageShsOut { get; set; }

        /// <summary>Weighted average shares outstanding (diluted).</summary>
        public long? WeightedAverageShsOutDil { get; set; }
    }
}
