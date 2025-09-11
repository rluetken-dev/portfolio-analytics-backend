namespace Portfolio.Api.Services
{
    public interface ISeedService
    {
        // Ticker only
        Task<(bool created, bool updated)> SeedTickerAsync(string symbol, string? name, CancellationToken ct);

        // Annual combo (Income + Balance minimal)
        Task SeedAnnualAsync(string symbol, int year, long netIncome, long equity, CancellationToken ct);

        Task SeedLiabilitiesAsync(string symbol, int year, long totalLiabilities, CancellationToken ct);
        Task SeedAssetsAsync(string symbol, int year, long totalAssets, CancellationToken ct);
        Task SeedRevenueAsync(string symbol, int year, long revenue, CancellationToken ct);
        Task SeedSharesAsync(string symbol, int year, long shares, CancellationToken ct);
        Task SeedPriceAsync(string symbol, DateOnly date, decimal close, CancellationToken ct);
    }
}
