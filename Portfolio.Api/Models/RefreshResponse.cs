namespace Portfolio.Api.Models;

/// <summary>
/// Response returned after refreshing cached market data.
/// </summary>
public sealed class RefreshResponse
{
    public bool Ok { get; set; }
    public string[] Symbols { get; set; } = [];
    public int Inserted { get; set; }
    public int Skipped { get; set; }
}