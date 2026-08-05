using System.Text.Json.Serialization;

namespace Portfolio.Api.Seed.Dto;

public sealed class CompanySeedFile
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("profile")]
    public CompanyProfile Profile { get; set; } = new();

    [JsonPropertyName("quotes")]
    public QuoteBlock Quotes { get; set; } = new();

    [JsonPropertyName("fundamentals")]
    public FundamentalsBlock? Fundamentals { get; set; }
}

public sealed class CompanyProfile
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("sector")]
    public string Sector { get; set; } = string.Empty;
}

public sealed class QuoteBlock
{
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "USD";

    [JsonPropertyName("rows")]
    public List<QuoteRow> Rows { get; set; } = new();
}

public sealed class QuoteRow
{
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

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

public sealed class FundamentalsBlock
{
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("annual")]
    public List<FundamentalsAnnualRow> Annual { get; set; } = new();
}

public sealed class FundamentalsAnnualRow
{
    [JsonPropertyName("year")]
    public int Year { get; set; }

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

    [JsonPropertyName("changeInWorkingCapital")]
    public long? ChangeInWorkingCapital { get; set; }
}