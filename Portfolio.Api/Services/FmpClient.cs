using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Portfolio.Api.DTOs;
using Portfolio.Api.Exceptions;
using Portfolio.Api.Models;

namespace Portfolio.Api.Services;

public sealed class FmpClient
{
    private const string DefaultBaseAddress = "https://financialmodelingprep.com/";
    private const string PlaceholderApiKey = "demo-local-placeholder";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<FmpClient> _logger;
    private readonly FallbackData _fallback;
    private readonly string _apiKey;

    private bool HasApiKey =>
        !string.IsNullOrWhiteSpace(_apiKey) &&
        !_apiKey.Equals(PlaceholderApiKey, StringComparison.OrdinalIgnoreCase);

    public FmpClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<FmpClient> logger,
        FallbackData fallback)
    {
        _httpClient = httpClient;
        _logger = logger;
        _fallback = fallback;
        _apiKey = configuration["Fmp:ApiKey"] ?? string.Empty;

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(DefaultBaseAddress);
        }

        if (!_httpClient.DefaultRequestHeaders.Accept.Any())
        {
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }

        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Portfolio.Api (+https://github.com/rluetken-dev/portfolio-analytics-backend)");
        }

        if (!HasApiKey)
        {
            _logger.LogInformation("FMP API key is not configured. Live FMP calls are disabled.");
        }
    }

    public sealed record IncomeStatementStableRow(
        string? Date,
        string? Symbol,
        long? Revenue,
        long? NetIncome,
        double? Eps,
        double? EpsDiluted,
        long? WeightedAverageShsOut,
        long? WeightedAverageShsOutDil,
        string? ReportedCurrency);

    public sealed record BalanceSheetStableRow(
        string? Date,
        string? Symbol,
        long? TotalAssets,
        long? TotalLiabilities,
        long? TotalStockholdersEquity,
        long? CashAndCashEquivalents,
        string? ReportedCurrency);

    public sealed record CashFlowStableRow(
        string? Date,
        string? Symbol,
        long? OperatingCashFlow,
        long? CapitalExpenditure,
        long? FreeCashFlow,
        long? NetIncome,
        long? DepreciationAndAmortization,
        long? ChangeInWorkingCapital,
        string? ReportedCurrency);

    public sealed record KeyMetricsTtm(
        string? Symbol,
        double? MarketCap,
        double? EnterpriseValueTtm,
        double? EvToSalesTtm,
        double? EvToOperatingCashFlowTtm,
        double? EvToFreeCashFlowTtm,
        double? EvToEbitdaTtm,
        double? ReturnOnAssetsTtm,
        double? ReturnOnEquityTtm,
        double? ReturnOnInvestedCapitalTtm,
        double? EarningsYieldTtm,
        double? FreeCashFlowYieldTtm,
        double? CurrentRatioTtm);

    public sealed record RevenuePoint(DateOnly PeriodEnd, decimal Revenue, string? Currency);

    public sealed class CompanyProfile
    {
        public string Symbol { get; init; } = string.Empty;
        public string? Name { get; init; }
        public string? Sector { get; init; }
    }

    private sealed class FmpProfileRaw
    {
        public string? Symbol { get; set; }
        public string? CompanyName { get; set; }
        public string? Sector { get; set; }
    }

    private sealed class FmpSp500Row
    {
        public string? Symbol { get; set; }
        public string? Name { get; set; }
    }

    public async Task<CompanyProfile?> GetCompanyProfileAsync(
        string symbol,
        CancellationToken ct = default)
    {
        string normalizedSymbol = NormalizeSymbol(symbol);
        if (string.IsNullOrWhiteSpace(normalizedSymbol))
        {
            throw new ArgumentException("Symbol is required.", nameof(symbol));
        }

        var rows = await GetJsonAsync<List<FmpProfileRaw>>(
            $"api/v3/profile/{normalizedSymbol}",
            new Dictionary<string, string?>(),
            ct);

        FmpProfileRaw? first = rows.FirstOrDefault();
        if (first is null)
        {
            return null;
        }

        return new CompanyProfile
        {
            Symbol = normalizedSymbol,
            Name = NormalizeNullableText(first.CompanyName),
            Sector = NormalizeNullableText(first.Sector)
        };
    }

    public async Task<IReadOnlyList<RevenuePoint>> GetQuarterlyRevenueAsync(
        string symbol,
        int limit = 8,
        CancellationToken ct = default)
    {
        return await GetRevenueAsync(symbol, period: "quarter", limit, maxLimit: 40, ct);
    }

    public async Task<IReadOnlyList<RevenuePoint>> GetAnnualRevenueAsync(
        string symbol,
        int limit = 8,
        CancellationToken ct = default)
    {
        return await GetRevenueAsync(symbol, period: "annual", limit, maxLimit: 12, ct);
    }

    public Task<List<IncomeStatementStableRow>> GetIncomeStatementStableAsync(
        string symbol,
        int limit = 5,
        CancellationToken ct = default)
    {
        return GetIncomeStatementStableAsync(symbol, limit, period: "annual", ct);
    }

    public Task<List<IncomeStatementStableRow>> GetIncomeStatementStableAsync(
        string symbol,
        int limit = 5,
        string period = "annual",
        CancellationToken ct = default)
    {
        string normalizedSymbol = RequireSymbol(symbol);
        int normalizedLimit = Math.Clamp(limit, 1, 40);

        return GetJsonAsync<List<IncomeStatementStableRow>>(
            "stable/income-statement",
            new Dictionary<string, string?>
            {
                ["symbol"] = normalizedSymbol,
                ["limit"] = normalizedLimit.ToString(),
                ["period"] = NormalizePeriod(period)
            },
            ct);
    }

    public Task<List<BalanceSheetStableRow>> GetBalanceSheetStableAsync(
        string symbol,
        int limit = 5,
        string period = "annual",
        CancellationToken ct = default)
    {
        string normalizedSymbol = RequireSymbol(symbol);
        int normalizedLimit = Math.Clamp(limit, 1, 40);

        return GetJsonAsync<List<BalanceSheetStableRow>>(
            "stable/balance-sheet-statement",
            new Dictionary<string, string?>
            {
                ["symbol"] = normalizedSymbol,
                ["limit"] = normalizedLimit.ToString(),
                ["period"] = NormalizePeriod(period)
            },
            ct);
    }

    public Task<List<CashFlowStableRow>> GetCashFlowStableAsync(
        string symbol,
        int limit = 5,
        string period = "annual",
        CancellationToken ct = default)
    {
        string normalizedSymbol = RequireSymbol(symbol);
        int normalizedLimit = Math.Clamp(limit, 1, 40);

        return GetJsonAsync<List<CashFlowStableRow>>(
            "stable/cash-flow-statement",
            new Dictionary<string, string?>
            {
                ["symbol"] = normalizedSymbol,
                ["limit"] = normalizedLimit.ToString(),
                ["period"] = NormalizePeriod(period)
            },
            ct);
    }

    public async Task<KeyMetricsTtm?> GetKeyMetricsTtmAsync(
        string symbol,
        CancellationToken ct = default)
    {
        string normalizedSymbol = RequireSymbol(symbol);

        var rows = await GetJsonAsync<List<KeyMetricsTtm>>(
            "stable/key-metrics-ttm",
            new Dictionary<string, string?>
            {
                ["symbol"] = normalizedSymbol
            },
            ct);

        return rows.FirstOrDefault();
    }

    public async Task<IReadOnlyList<(string Symbol, string? Name)>> GetSp500ConstituentsAsync(
        CancellationToken ct = default)
    {
        var rows = await GetJsonAsync<List<FmpSp500Row>>(
            "api/v3/sp500_constituent",
            new Dictionary<string, string?>(),
            ct);

        return rows
            .Where(row => !string.IsNullOrWhiteSpace(row.Symbol))
            .Select(row => (
                Symbol: row.Symbol!.Trim().ToUpperInvariant(),
                Name: NormalizeNullableText(row.Name)))
            .ToList();
    }

    public async Task<List<CompanySearchResult>> SearchCompaniesAsync(
        string query,
        int limit,
        CancellationToken ct = default)
    {
        string normalizedQuery = query.Trim();

        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return [];
        }

        int normalizedLimit = Math.Clamp(limit, 1, 50);

        List<CompanySearchResult> localMatches = SearchFallbackCompanies(
            normalizedQuery,
            normalizedLimit);

        if (localMatches.Count > 0 || !HasApiKey)
        {
            if (!HasApiKey && localMatches.Count == 0)
            {
                _logger.LogInformation(
                    "FMP API key is not configured. No fallback search results found for query {Query}.",
                    normalizedQuery);
            }

            return localMatches;
        }

        try
        {
            var remoteResults = await GetJsonAsync<List<CompanySearchResult>>(
                "api/v3/search",
                new Dictionary<string, string?>
                {
                    ["query"] = normalizedQuery,
                    ["limit"] = normalizedLimit.ToString()
                },
                ct);

            return remoteResults
                .Where(result => !string.IsNullOrWhiteSpace(result.Symbol))
                .Take(normalizedLimit)
                .ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _logger.LogError(ex, "FMP company search failed for query {Query}.", normalizedQuery);
            return [];
        }
    }

    private async Task<IReadOnlyList<RevenuePoint>> GetRevenueAsync(
        string symbol,
        string period,
        int limit,
        int maxLimit,
        CancellationToken ct)
    {
        string normalizedSymbol = RequireSymbol(symbol);
        int normalizedLimit = Math.Clamp(limit, 1, maxLimit);

        List<IncomeStatementStableRow> rows = await GetIncomeStatementStableAsync(
            normalizedSymbol,
            normalizedLimit,
            period,
            ct);

        return rows
            .Where(row => !string.IsNullOrWhiteSpace(row.Date) && row.Revenue.HasValue)
            .Select(row => TryCreateRevenuePoint(row, out RevenuePoint? point) ? point : null)
            .Where(point => point is not null)
            .Cast<RevenuePoint>()
            .OrderByDescending(point => point.PeriodEnd)
            .Take(normalizedLimit)
            .ToList();
    }

    private async Task<T> GetJsonAsync<T>(
        string path,
        IDictionary<string, string?> query,
        CancellationToken ct)
    {
        EnsureApiKeyConfigured();

        var parameters = new Dictionary<string, string?>(query)
        {
            ["apikey"] = _apiKey
        };

        string url = QueryHelpers.AddQueryString(path, parameters);

        using HttpResponseMessage response = await _httpClient.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync(ct);

            throw new HttpRequestException(
                $"FMP GET {url} failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");
        }

        T? data = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);

        if (data is null)
        {
            throw new JsonException($"FMP GET {url} returned empty or invalid JSON.");
        }

        return data;
    }

    private List<CompanySearchResult> SearchFallbackCompanies(string query, int limit)
    {
        return _fallback.Companies
            .Where(company =>
                company.Symbol.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                company.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .Select(company => new CompanySearchResult
            {
                Symbol = company.Symbol,
                Name = company.Name,
                Exchange = "Local fallback",
                Sector = company.Sector,
                IsInDatabase = false,
                IsInUserPortfolio = false
            })
            .ToList();
    }

    private static bool TryCreateRevenuePoint(
        IncomeStatementStableRow row,
        out RevenuePoint? point)
    {
        point = null;

        if (!DateOnly.TryParse(row.Date, out DateOnly periodEnd) || row.Revenue is null)
        {
            return false;
        }

        point = new RevenuePoint(
            PeriodEnd: periodEnd,
            Revenue: row.Revenue.Value,
            Currency: row.ReportedCurrency);

        return true;
    }

    private void EnsureApiKeyConfigured()
    {
        if (HasApiKey)
        {
            return;
        }

        throw new ServiceUnavailableException(
            "FMP API key is not configured. Live FMP provider calls are disabled.");
    }

    private static string RequireSymbol(string symbol)
    {
        string normalizedSymbol = NormalizeSymbol(symbol);

        if (string.IsNullOrWhiteSpace(normalizedSymbol))
        {
            throw new ArgumentException("Symbol is required.", nameof(symbol));
        }

        return normalizedSymbol;
    }

    private static string NormalizeSymbol(string symbol)
    {
        return string.IsNullOrWhiteSpace(symbol)
            ? string.Empty
            : symbol.Trim().ToUpperInvariant();
    }

    private static string NormalizePeriod(string period)
    {
        return string.Equals(period, "quarter", StringComparison.OrdinalIgnoreCase)
            ? "quarter"
            : "annual";
    }

    private static string? NormalizeNullableText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}