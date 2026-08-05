namespace Portfolio.Api.Models;

public sealed class FallbackData
{
    public Dictionary<string, List<string>> PopularLists { get; set; } = new();
    public List<CompanyFallbackInfo> Companies { get; set; } = new();
}

public sealed class CompanyFallbackInfo
{
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Sector { get; set; }
}