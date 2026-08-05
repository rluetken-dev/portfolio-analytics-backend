using Portfolio.Api.Seed.Dto;

namespace Portfolio.Api.Seed;

/// <summary>
/// Loads and validates company seed files without writing to the database.
/// </summary>
public interface ISeedFileService
{
    Task<SeedLoadResult<CompanySeedFile>> LoadCompanyAsync(string symbol);
}

public sealed class SeedLoadResult<T>
{
    public bool Success { get; init; }

    public T? Data { get; init; }

    public string? Error { get; init; }

    public static SeedLoadResult<T> Ok(T data)
    {
        return new SeedLoadResult<T>
        {
            Success = true,
            Data = data
        };
    }

    public static SeedLoadResult<T> Fail(string error)
    {
        return new SeedLoadResult<T>
        {
            Success = false,
            Error = error
        };
    }
}