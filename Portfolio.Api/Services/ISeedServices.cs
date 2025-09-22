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

        /// <summary>
        /// Upsert ticker profile (name + sector). Returns (created, updated).
        /// </summary>
        Task<(bool created, bool updated)> SeedTickerProfileAsync(
            string symbol, string? name, string? sector, CancellationToken ct);

        // English: upsert annual Operating Cash Flow
        System.Threading.Tasks.Task SeedOperatingCashFlowAsync(
            string symbol, int year, long operatingCashFlow, System.Threading.CancellationToken ct);

        // English: upsert annual Capital Expenditures
        System.Threading.Tasks.Task SeedCapitalExpendituresAsync(
            string symbol, int year, long capitalExpenditures, System.Threading.CancellationToken ct);

        // English: upsert full daily OHLCV
        System.Threading.Tasks.Task SeedFullPriceAsync(
            string symbol,
            System.DateOnly date,
            decimal open,
            decimal high,
            decimal low,
            decimal close,
            long volume,
            System.Threading.CancellationToken ct);
    }
}
