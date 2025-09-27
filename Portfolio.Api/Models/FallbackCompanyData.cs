namespace Portfolio.Api.Models;

public class FallbackData
{
    public Dictionary<string, List<string>> PopularLists { get; set; } = new();
    public List<CompanyFallbackInfo> Companies { get; set; } = new();
}

public class CompanyFallbackInfo
{
    public string Symbol { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Sector { get; set; }
}
