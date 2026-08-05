namespace Portfolio.Api.Services;

public interface ISeedService
{
    Task<(bool created, bool updated)> SeedTickerAsync(
        string symbol,
        string? name,
        CancellationToken ct);

    Task<(bool created, bool updated)> SeedTickerProfileAsync(
        string symbol,
        string? name,
        string? sector,
        CancellationToken ct);

    Task SeedAnnualAsync(
        string symbol,
        int year,
        long netIncome,
        long equity,
        CancellationToken ct);

    Task SeedRevenueAsync(
        string symbol,
        int year,
        long revenue,
        CancellationToken ct);

    Task SeedAssetsAsync(
        string symbol,
        int year,
        long totalAssets,
        CancellationToken ct);

    Task SeedLiabilitiesAsync(
        string symbol,
        int year,
        long totalLiabilities,
        CancellationToken ct);

    Task SeedSharesAsync(
        string symbol,
        int year,
        long shares,
        CancellationToken ct);

    Task SeedOperatingCashFlowAsync(
        string symbol,
        int year,
        long operatingCashFlow,
        CancellationToken ct);

    Task SeedCapitalExpendituresAsync(
        string symbol,
        int year,
        long capitalExpenditures,
        CancellationToken ct);

    Task SeedPriceAsync(
        string symbol,
        DateOnly date,
        decimal close,
        CancellationToken ct);

    Task SeedFullPriceAsync(
        string symbol,
        DateOnly date,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        long volume,
        CancellationToken ct);

    Task SeedChangeInWorkingCapitalAsync(
        string symbol,
        int year,
        long changeInWorkingCapital,
        CancellationToken ct);
}