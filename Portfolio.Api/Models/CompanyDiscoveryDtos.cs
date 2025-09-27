namespace Portfolio.Api.Models
{
    // Request to add a single company
    public record AddCompanyRequest
    {
        public string Symbol { get; init; } = string.Empty;
    }

    // Request to add popular companies in bulk
    public record AddPopularRequest
    {
        public string? Category { get; init; }
        public int? Limit { get; init; } = 20;
    }

    // Single company search result
    public record CompanySearchResult
    {
        public string Symbol { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? Exchange { get; init; }
        public string? Sector { get; init; }
        public bool IsInDatabase { get; init; }
    }

    // Response containing search results
    public record CompanySearchResponse
    {
        public string Query { get; init; } = string.Empty;
        public List<CompanySearchResult> Results { get; init; } = new();
        public int TotalFound { get; init; }
    }

    // Response for bulk add operations
    public record BulkAddResponse
    {
        public List<CompanySearchResult> Added { get; init; } = new();
        public List<string> Errors { get; init; } = new();
        public int TotalAdded { get; init; }
    }
}