using System.Text.Json.Serialization;

namespace Portfolio.Api.Seed.Dto
{
    /// <summary>
    /// Root object for a company seed file (one file per symbol).
    /// </summary>
    public sealed partial class CompanySeedFile
    {
        // English: stock ticker symbol (e.g., "AAPL")
        [JsonPropertyName("symbol")]
        public string Symbol { get; set; } = string.Empty;

        // English: basic profile (name, sector)
        [JsonPropertyName("profile")]
        public CompanyProfile Profile { get; set; } = new();

        // English: daily quotes (currency + rows)
        [JsonPropertyName("quotes")]
        public QuoteBlock Quotes { get; set; } = new();
    }

    public sealed class CompanyProfile
    {
        // English: company display name
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        // English: GICS/sector label
        [JsonPropertyName("sector")]
        public string Sector { get; set; } = string.Empty;
    }

    public sealed class QuoteBlock
    {
        // English: currency code for quotes (e.g., "USD")
        [JsonPropertyName("currency")]
        public string Currency { get; set; } = "USD";

        // English: ordered list of daily OHLCV rows
        [JsonPropertyName("rows")]
        public List<QuoteRow> Rows { get; set; } = new();
    }

    public sealed class QuoteRow
    {
        // English: trading day in ISO format (YYYY-MM-DD)
        [JsonPropertyName("date")]
        public string Date { get; set; } = string.Empty;

        // English: OHLC + volume
        [JsonPropertyName("open")]
        public decimal Open { get; set; }

        [JsonPropertyName("high")]
        public decimal High { get; set; }

        [JsonPropertyName("low")]
        public decimal Low { get; set; }

        [JsonPropertyName("close")]
        public decimal Close { get; set; }

        [JsonPropertyName("volume")]
        public long Volume { get; set; }
    }

    // English: extend CompanySeedFile with optional fundamentals block
    public sealed partial class CompanySeedFile
    {
        [JsonPropertyName("fundamentals")]
        public FundamentalsBlock? Fundamentals { get; set; }
    }

    /// <summary>
    /// Optional fundamentals block (annual rows).
    /// </summary>
    public sealed class FundamentalsBlock
    {
        // English: optional currency hint for fundamentals
        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        // English: list of annual rows
        [JsonPropertyName("annual")]
        public List<FundamentalsAnnualRow> Annual { get; set; } = new();
    }

    public sealed class FundamentalsAnnualRow
    {
        // English: financial year (e.g., 2024)
        [JsonPropertyName("year")]
        public int Year { get; set; }

        // English: at least one metric should be present
        [JsonPropertyName("revenue")]
        public long? Revenue { get; set; }

        [JsonPropertyName("netIncome")]
        public long? NetIncome { get; set; }

        [JsonPropertyName("totalAssets")]
        public long? TotalAssets { get; set; }

        [JsonPropertyName("totalLiabilities")]
        public long? TotalLiabilities { get; set; }

        [JsonPropertyName("equity")]
        public long? Equity { get; set; }

        [JsonPropertyName("shares")]
        public long? Shares { get; set; }
        
        [JsonPropertyName("operatingCashFlow")]
        public long? OperatingCashFlow { get; set; }

        [JsonPropertyName("capitalExpenditures")]
        public long? CapitalExpenditures { get; set; }
    }
}
