namespace Portfolio.Api.Models;

/// <summary>
/// Response shape for the refresh endpoint.
/// </summary>
public class RefreshResponse
{
    /// <summary>Indicates whether the operation completed without fatal errors.</summary>
    public bool Ok { get; set; }

    /// <summary>Symbols that were requested (normalized).</summary>
    public string[] Symbols { get; set; } = [];

    /// <summary>Number of newly inserted rows.</summary>
    public int Inserted { get; set; }

    /// <summary>Number of rows that were detected as duplicates and skipped.</summary>
    public int Skipped { get; set; }
}
