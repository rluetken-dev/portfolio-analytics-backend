using System.Threading.Tasks;
using Portfolio.Api.Seed.Dto;

namespace Portfolio.Api.Seed
{
    /// <summary>
    /// Loads and validates company seed files from disk (no DB writes).
    /// </summary>
    public interface ISeedFileService
    {
        // English: Load one company seed file (by ticker), validate, and return it
        Task<SeedLoadResult<CompanySeedFile>> LoadCompanyAsync(string symbol);
    }

    /// <summary>
    /// Simple result wrapper for loading + validation.
    /// </summary>
    /// <typeparam name="T">Parsed payload type</typeparam>
    public sealed class SeedLoadResult<T>
    {
        // English: indicates whether loading + validation succeeded
        public bool Success { get; init; }

        // English: parsed object (null on failure)
        public T? Data { get; init; }

        // English: human-readable error(s) if failed
        public string? Error { get; init; }

        public static SeedLoadResult<T> Ok(T data) => new() { Success = true, Data = data };
        public static SeedLoadResult<T> Fail(string error) => new() { Success = false, Error = error };
    }
}
