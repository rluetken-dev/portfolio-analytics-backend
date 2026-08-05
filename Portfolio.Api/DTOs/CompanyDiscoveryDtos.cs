using System.ComponentModel.DataAnnotations;

namespace Portfolio.Api.DTOs;

public sealed record AddCompanyRequest
{
    [Required]
    [MinLength(1)]
    [MaxLength(16)]
    [RegularExpression(@"^[A-Za-z0-9.\-]{1,16}$")]
    public string Symbol { get; init; } = string.Empty;
}

public sealed record AddPopularRequest
{
    [MaxLength(64)]
    public string? Category { get; init; }

    [Range(1, 100)]
    public int? Limit { get; init; } = 20;
}

public sealed record CompanySearchResult
{
    public int Id { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Exchange { get; init; }
    public string? Sector { get; init; }
    public bool IsInDatabase { get; init; }
    public bool IsInUserPortfolio { get; init; }
}

public sealed record CompanySearchResponse
{
    public string Query { get; init; } = string.Empty;
    public List<CompanySearchResult> Results { get; init; } = new();
    public int TotalFound { get; init; }
}

public sealed record BulkAddResponse
{
    public List<CompanySearchResult> Added { get; init; } = new();
    public List<CompanySearchResult> Existing { get; init; } = new();
    public List<string> Errors { get; init; } = new();

    public int TotalAdded => Added.Count;
    public int TotalExisting => Existing.Count;
}