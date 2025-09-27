using System.Text.Json;
using System.Net.Http.Headers; // for Accept/User-Agent headers
using Microsoft.AspNetCore.WebUtilities;     // QueryHelpers.AddQueryString
using System.Text.Json.Serialization;        // JsonNumberHandling
using System.Linq;
using Portfolio.Api.Models;

namespace Portfolio.Api.Services
{
    /// <summary>
    /// Thin HTTP client for Financial Modeling Prep (FMP) fundamentals.
    /// Reads the API key from configuration key "Fmp:ApiKey".
    /// </summary>
    public class FmpClient
    {
        private readonly HttpClient _http;
        private readonly ILogger<FmpClient> _log;
        private readonly string _apiKey;

        // JSON options: case-insensitive so PascalCase C# properties can bind to camelCase JSON.
        // Also enable strict number handling to surface bad numeric payloads early.
        private static readonly JsonSerializerOptions _jsonStable = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.Strict
        };

        /// <summary>
        /// Minimal DTO for /stable/income-statement. Add fields as you need them.
        /// Unknown JSON properties are ignored by System.Text.Json.
        /// </summary>
        public record IncomeStatementStableRow(
            string? Date,
            string? Symbol,
            long? Revenue,
            long? NetIncome,
            double? Eps,
            double? EpsDiluted,
            long? WeightedAverageShsOut,
            long? WeightedAverageShsOutDil,
            string? ReportedCurrency);

        /// <summary>
        /// Minimal DTO for /stable/balance-sheet. Extend as needed.
        /// Unknown JSON fields are ignored.
        /// </summary>
        public record BalanceSheetStableRow(
            string? Date,
            string? Symbol,
            long? TotalAssets,
            long? TotalLiabilities,
            long? TotalStockholdersEquity,
            long? CashAndCashEquivalents,
            string? ReportedCurrency);

        // ---------------------------------------------------------------
        // Key Metrics (TTM) – typed DTO matching /stable/key-metrics-ttm
        // NOTE (English):
        // - Property names are PascalCase; System.Text.Json is case-insensitive,
        //   so they bind to JSON like "enterpriseValueTTM", "evToEBITDATTM", etc.
        // - Add/remove fields as needed; unknown JSON properties are ignored.
        // ---------------------------------------------------------------
        public record KeyMetricsTtm(
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
            double? CurrentRatioTtm
        )
        {
            // English: Map-friendly constructor for common JSON field spellings.
            // If you prefer, you can annotate with [JsonPropertyName("jsonName")] instead.
            // The case-insensitive option already handles most differences like TTM vs Ttm.
        }

        /// <summary>
        /// Minimal DTO for /stable/cash-flow-statement. Extend as needed.
        /// Unknown JSON properties are ignored.
        /// </summary>
        public record CashFlowStableRow(
            string? Date,
            string? Symbol,
            long? OperatingCashFlow,
            long? CapitalExpenditure,
            long? FreeCashFlow,
            long? NetIncome,
            long? DepreciationAndAmortization,
            long? ChangeInWorkingCapital,
            string? ReportedCurrency);

        /// <summary>
        /// Raw shape returned by /api/v3/profile/{symbol}
        /// </summary>
        private sealed class FmpProfileRaw
        {
            public string? symbol { get; set; }
            public string? companyName { get; set; }
            public string? sector { get; set; }
        }

        /// <summary>
        /// Stable DTO we return to the rest of the app
        /// </summary>
        public sealed class CompanyProfile
        {
            public string Symbol { get; init; } = "";
            public string? Name { get; init; }
            public string? Sector { get; init; }
        }

        /// <summary>
        /// Fetches company profile (name, sector) for a given symbol using FMP v3 API.
        /// Returns null if not found.
        /// </summary>
        public async Task<CompanyProfile?> GetCompanyProfileAsync(string symbol, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("symbol is required", nameof(symbol));

            var sym = symbol.Trim().ToUpperInvariant();

            // Build relative URL: /api/v3/profile/{symbol}?apikey=...
            var relative = QueryHelpers.AddQueryString($"api/v3/profile/{sym}", new Dictionary<string, string?>
            {
                ["apikey"] = _apiKey
            });

            using var res = await _http.GetAsync(relative, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException($"FMP GET {relative} failed: {(int)res.StatusCode} {res.ReasonPhrase}. Body: {body}");

            await using var stream = await res.Content.ReadAsStreamAsync(ct);
            var arr = await JsonSerializer.DeserializeAsync<FmpProfileRaw[]>(stream, _jsonStable, ct);

            var first = arr?.FirstOrDefault();
            if (first is null) return null;

            return new CompanyProfile
            {
                Symbol = sym,
                Name = string.IsNullOrWhiteSpace(first.companyName) ? null : first.companyName,
                Sector = string.IsNullOrWhiteSpace(first.sector) ? null : first.sector
            };
        }

        public FmpClient(HttpClient http, IConfiguration config, ILogger<FmpClient> log)
        {
            _http = http;
            _log = log;

            // Read API key from user-secrets or env vars.
            _apiKey = config["Fmp:ApiKey"] ?? throw new InvalidOperationException(
                "Missing Fmp:ApiKey. Set it via 'dotnet user-secrets set \"Fmp:ApiKey\" \"...\"'.");

            // Use root base address so we can call /stable/... and other routes cleanly.
            // IMPORTANT: keep trailing slash to avoid bad relative joins.
            if (_http.BaseAddress is null)
            {
                _http.BaseAddress = new Uri("https://financialmodelingprep.com/");
            }

            // Set polite defaults once on the typed HttpClient.
            // English: Some APIs reject requests without Accept/User-Agent.
            if (!_http.DefaultRequestHeaders.Accept.Any())
                _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");

            if (!_http.DefaultRequestHeaders.UserAgent.Any())
                _http.DefaultRequestHeaders.UserAgent.ParseAdd("Portfolio.Api (+https://github.com/rluetken-dev)");
        }

        /// <summary>
        /// Small DTO for quarterly revenue points returned by FMP.
        /// </summary>
        public record RevenuePoint(DateOnly PeriodEnd, decimal Revenue, string? Currency);

        /// <summary>
        /// Fetches QUARTERLY revenue via the modern /stable API.
        /// English:
        /// - Calls stable/income-statement with period=quarter
        /// - Maps the response to your lightweight RevenuePoint model
        /// - Returns newest-first (defensive ordering)
        /// </summary>
        public async Task<IReadOnlyList<RevenuePoint>> GetQuarterlyRevenueAsync(
            string symbol,
            int limit = 8,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("symbol is required", nameof(symbol));

            // Keep a reasonable bound; FMP typically supports up to ~40
            limit = Math.Clamp(limit, 1, 40);
            var sym = symbol.ToUpperInvariant();

            // 1) Fetch raw rows from the /stable endpoint
            //    NOTE: /stable uses query parameters for symbol/limit/period.
            var rows = await GetStableAsync<List<IncomeStatementStableRow>>(
                path: "stable/income-statement",
                query: new Dictionary<string, string?>
                {
                    ["symbol"] = sym,
                    ["limit"] = limit.ToString(),
                    ["period"] = "quarter"
                },
                ct);

            // 2) Map to your compact RevenuePoint (date + revenue + currency)
            var list = new List<RevenuePoint>(rows?.Count ?? 0);

            if (rows != null)
            {
                foreach (var r in rows)
                {
                    // Defensive parsing: skip invalid rows
                    if (r is null || string.IsNullOrWhiteSpace(r.Date) || r.Revenue is null)
                        continue;

                    if (!DateOnly.TryParse(r.Date, out var asOf))
                        continue;

                    // FMP returns revenue as long?; convert to decimal for your DTO
                    list.Add(new RevenuePoint(
                        PeriodEnd: asOf,
                        Revenue: (decimal)r.Revenue.Value,
                        Currency: r.ReportedCurrency
                    ));
                }
            }

            // 3) Ensure newest-first and cap by 'limit' (defensive)
            return list
                .OrderByDescending(x => x.PeriodEnd)
                .Take(limit)
                .ToList();
        }

        /// <summary>
        /// Fetches ANNUAL revenue via the modern /stable API.
        /// English:
        /// - Calls stable/income-statement with period=annual
        /// - Maps the response to your lightweight RevenuePoint model
        /// - Returns newest-first (defensive ordering)
        /// </summary>
        public async Task<IReadOnlyList<RevenuePoint>> GetAnnualRevenueAsync(
            string symbol,
            int limit = 8,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("symbol is required", nameof(symbol));

            // Keep a reasonable bound; annual typically needs far fewer rows.
            limit = Math.Clamp(limit, 1, 12);
            var sym = symbol.ToUpperInvariant();

            // 1) Fetch raw rows from the /stable endpoint
            //    NOTE: /stable uses query parameters for symbol/limit/period.
            var rows = await GetStableAsync<List<IncomeStatementStableRow>>(
                path: "stable/income-statement",
                query: new Dictionary<string, string?>
                {
                    ["symbol"] = sym,
                    ["limit"] = limit.ToString(),
                    ["period"] = "annual"
                },
                ct);

            // 2) Map to your compact RevenuePoint (date + revenue + currency)
            var list = new List<RevenuePoint>(rows?.Count ?? 0);

            if (rows != null)
            {
                foreach (var r in rows)
                {
                    // Defensive parsing: skip invalid rows
                    if (r is null || string.IsNullOrWhiteSpace(r.Date) || r.Revenue is null)
                        continue;

                    if (!DateOnly.TryParse(r.Date, out var asOf))
                        continue;

                    // FMP returns revenue as long?; convert to decimal for your DTO
                    list.Add(new RevenuePoint(
                        PeriodEnd: asOf,
                        Revenue: (decimal)r.Revenue.Value,
                        Currency: r.ReportedCurrency
                    ));
                }
            }

            // 3) Ensure newest-first and cap by 'limit' (defensive)
            return list
                .OrderByDescending(x => x.PeriodEnd)
                .Take(limit)
                .ToList();
        }

        /// <summary>
        /// Generic GET helper for FMP /stable endpoints.
        /// It automatically appends the API key and deserializes the JSON payload.
        /// </summary>
        private async Task<T> GetStableAsync<T>(
            string path,
            IDictionary<string, string?> query,
            CancellationToken ct = default)
        {
            // Always include the API key; clone input to avoid side-effects.
            var q = new Dictionary<string, string?>(query ?? new Dictionary<string, string?>());
            q["apikey"] = _apiKey;

            // Build the final relative URL against HttpClient.BaseAddress
            var url = QueryHelpers.AddQueryString(path, q);

            // Execute GET; on non-success, include response body for easier debugging.
            using var res = await _http.GetAsync(url, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"FMP GET {url} failed: {(int)res.StatusCode} {res.ReasonPhrase}. Body: {body}");
            }

            // Deserialize the JSON stream into the requested type.
            await using var stream = await res.Content.ReadAsStreamAsync(ct);
            var data = await JsonSerializer.DeserializeAsync<T>(stream, _jsonStable, ct);
            if (data is null)
                throw new InvalidOperationException($"Empty/invalid JSON returned by {url}");

            return data;
        }

        /// <summary>
        /// Fetches the Income Statement from the new /stable API (most recent first).
        /// NOTE: /stable uses query parameters (symbol, limit) instead of the legacy path segment.
        /// Example: stable/income-statement?symbol=AAPL&amp;limit=5&amp;apikey=...
        /// </summary>
        /// <param name="symbol">Ticker, e.g., "AAPL". Will be uppercased defensively.</param>
        /// <param name="limit">Max rows to return (typical 1..20).</param>
        /// <param name="ct">Cancellation token.</param>
        public Task<List<IncomeStatementStableRow>> GetIncomeStatementStableAsync(
            string symbol,
            int limit = 5,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("symbol is required", nameof(symbol));

            limit = Math.Clamp(limit, 1, 40);

            return GetStableAsync<List<IncomeStatementStableRow>>(
                path: "stable/income-statement",
                query: new Dictionary<string, string?>
                {
                    ["symbol"] = symbol.ToUpperInvariant(),
                    ["limit"] = limit.ToString()
                    // Optional: ["period"] = "annual" | "quarter"  (plan-dependent)
                },
                ct);
        }

        /// <summary>
        /// Fetches TTM key metrics for a single symbol from the /stable API.
        /// Example: stable/key-metrics-ttm?symbol=AAPL&amp;apikey=...
        /// Returns the first (most recent) item or null if none.
        /// </summary>
        /// <param name="symbol">Ticker, e.g., "AAPL" (uppercased defensively).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The latest <see cref="KeyMetricsTtm"/> or null.</returns>
        /// <exception cref="ArgumentException">Thrown if symbol is null/whitespace.</exception>
        /// <exception cref="HttpRequestException">Propagated on non-success HTTP responses.</exception>
        /// <exception cref="InvalidOperationException">Thrown on empty/invalid JSON.</exception>
        public async Task<KeyMetricsTtm?> GetKeyMetricsTtmAsync(
            string symbol,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("symbol is required", nameof(symbol));

            var list = await GetStableAsync<List<KeyMetricsTtm>>(
                path: "stable/key-metrics-ttm",
                query: new Dictionary<string, string?>
                {
                    ["symbol"] = symbol.ToUpperInvariant()
                },
                ct);

            // English: Return the first element if present; otherwise null.
            return list?.FirstOrDefault();
        }

        /// <summary>
        /// Fetches Balance Sheet rows from the /stable API (most recent first).
        /// Example: stable/balance-sheet?symbol=AAPL&amp;period=annual&amp;limit=5&amp;apikey=...
        /// </summary>
        /// <param name="symbol">Ticker, e.g., "AAPL" (uppercased defensively).</param>
        /// <param name="limit">Max rows to return (typical 1–20).</param>
        /// <param name="period">"annual" or "quarter" (plan-dependent).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>List of balance sheet rows (newest first).</returns>
        public Task<List<BalanceSheetStableRow>> GetBalanceSheetStableAsync(
            string symbol,
            int limit = 5,
            string period = "annual",
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("symbol is required", nameof(symbol));

            limit = Math.Clamp(limit, 1, 40);

            // FIX (English): Use the correct stable endpoint path.
            // Old: path: "stable/balance-sheet"
            // New: path: "stable/balance-sheet-statement"
            return GetStableAsync<List<BalanceSheetStableRow>>(
                path: "stable/balance-sheet-statement",
                query: new Dictionary<string, string?>
                {
                    ["symbol"] = symbol.ToUpperInvariant(),
                    ["limit"] = limit.ToString(),
                    ["period"] = period
                },
                ct);
        }

        /// <summary>
        /// Fetches Cash Flow rows from the /stable API (most recent first).
        /// Example: stable/cash-flow-statement?symbol=AAPL&amp;period=annual&amp;limit=5&amp;apikey=…
        /// </summary>
        /// <param name="symbol">Ticker, e.g., "AAPL" (uppercased defensively).</param>
        /// <param name="limit">Max rows to return (typical 1–20).</param>
        /// <param name="period">"annual" or "quarter" (plan-dependent).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>List of cash flow rows (newest first).</returns>
        public Task<List<CashFlowStableRow>> GetCashFlowStableAsync(
            string symbol,
            int limit = 5,
            string period = "annual",
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("symbol is required", nameof(symbol));

            limit = Math.Clamp(limit, 1, 40);

            // English: /stable uses query parameters; we keep the pattern consistent.
            return GetStableAsync<List<CashFlowStableRow>>(
                path: "stable/cash-flow-statement",
                query: new Dictionary<string, string?>
                {
                    ["symbol"] = symbol.ToUpperInvariant(),
                    ["limit"] = limit.ToString(),
                    ["period"] = period
                },
                ct);
        }

        /// <summary>
        /// Fetches Income Statement rows from the /stable API (most recent first).
        /// Example: stable/income-statement?symbol=AAPL&amp;period=annual&amp;limit=5&amp;apikey=...
        /// </summary>
        /// <param name="symbol">Ticker, e.g., "AAPL" (uppercased defensively).</param>
        /// <param name="limit">Max rows to return (typical 1–20).</param>
        /// <param name="period">"annual" or "quarter" (plan-dependent).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>List of income statement rows (newest first).</returns>
        public Task<List<IncomeStatementStableRow>> GetIncomeStatementStableAsync(
            string symbol,
            int limit = 5,
            string period = "annual",
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("symbol is required", nameof(symbol));

            limit = Math.Clamp(limit, 1, 40);

            return GetStableAsync<List<IncomeStatementStableRow>>(
                path: "stable/income-statement",
                query: new Dictionary<string, string?>
                {
                    ["symbol"] = symbol.ToUpperInvariant(),
                    ["limit"] = limit.ToString(),
                    ["period"] = period
                },
                ct);
        }

        /// <summary>
        /// Minimal DTO for FMP v3 /api/v3/sp500_constituent (only used fields).
        /// </summary>
        private sealed class FmpSp500Row
        {
            /// <summary>Ticker (e.g., "AAPL").</summary>
            public string? symbol { get; set; }

            /// <summary>Company name.</summary>
            public string? name { get; set; }
        }

        /// <summary>
        /// Fetch S&amp;P 500 constituents (symbol + optional name) from FMP v3.
        /// </summary>
        public async Task<IReadOnlyList<(string Symbol, string? Name)>> GetSp500ConstituentsAsync(
            CancellationToken ct = default)
        {
            var relative = QueryHelpers.AddQueryString("api/v3/sp500_constituent", new Dictionary<string, string?>
            {
                ["apikey"] = _apiKey
            });

            using var res = await _http.GetAsync(relative, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException($"FMP GET {relative} failed: {(int)res.StatusCode} {res.ReasonPhrase}. Body: {body}");

            await using var stream = await res.Content.ReadAsStreamAsync(ct);
            var arr = await JsonSerializer.DeserializeAsync<FmpSp500Row[]>(stream, _jsonStable, ct);

            var list = new List<(string Symbol, string? Name)>(arr?.Length ?? 0);
            if (arr != null)
            {
                foreach (var r in arr)
                {
                    if (string.IsNullOrWhiteSpace(r.symbol)) continue;
                    var sym = r.symbol.Trim().ToUpperInvariant();
                    var name = string.IsNullOrWhiteSpace(r.name) ? null : r.name.Trim();
                    list.Add((sym, name));
                }
            }
            return list;
        }

        /// <summary>
        /// Extended mock search with 100+ popular stocks
        /// </summary>
        public async Task<List<CompanySearchResult>> SearchCompaniesAsync(
            string query,
            int limit,
            CancellationToken ct = default)
        {
            // simulate network delay
            await Task.Delay(300, ct);

            var mockResults = new List<CompanySearchResult>
            {
                // Mega-Cap Tech
                new() { Symbol = "AAPL", Name = "Apple Inc.", Exchange = "NASDAQ", Sector = "Technology" },
                new() { Symbol = "MSFT", Name = "Microsoft Corporation", Exchange = "NASDAQ", Sector = "Technology" },
                new() { Symbol = "GOOGL", Name = "Alphabet Inc.", Exchange = "NASDAQ", Sector = "Technology" },
                new() { Symbol = "AMZN", Name = "Amazon.com Inc.", Exchange = "NASDAQ", Sector = "Consumer Cyclical" },
                new() { Symbol = "NVDA", Name = "NVIDIA Corporation", Exchange = "NASDAQ", Sector = "Technology" },
                new() { Symbol = "META", Name = "Meta Platforms Inc.", Exchange = "NASDAQ", Sector = "Technology" },
                new() { Symbol = "TSLA", Name = "Tesla Inc.", Exchange = "NASDAQ", Sector = "Consumer Cyclical" },
                
                // Other Tech Giants
                new() { Symbol = "CRM", Name = "Salesforce Inc.", Exchange = "NYSE", Sector = "Technology" },
                new() { Symbol = "ORCL", Name = "Oracle Corporation", Exchange = "NYSE", Sector = "Technology" },
                new() { Symbol = "ADBE", Name = "Adobe Inc.", Exchange = "NASDAQ", Sector = "Technology" },
                new() { Symbol = "NFLX", Name = "Netflix Inc.", Exchange = "NASDAQ", Sector = "Communication Services" },
                new() { Symbol = "INTC", Name = "Intel Corporation", Exchange = "NASDAQ", Sector = "Technology" },
                new() { Symbol = "CSCO", Name = "Cisco Systems Inc.", Exchange = "NASDAQ", Sector = "Technology" },
                new() { Symbol = "IBM", Name = "International Business Machines Corp.", Exchange = "NYSE", Sector = "Technology" },
                
                // Financial Services
                new() { Symbol = "BRK.B", Name = "Berkshire Hathaway Inc.", Exchange = "NYSE", Sector = "Financial Services" },
                new() { Symbol = "JPM", Name = "JPMorgan Chase & Co.", Exchange = "NYSE", Sector = "Financial Services" },
                new() { Symbol = "BAC", Name = "Bank of America Corp.", Exchange = "NYSE", Sector = "Financial Services" },
                new() { Symbol = "V", Name = "Visa Inc.", Exchange = "NYSE", Sector = "Financial Services" },
                new() { Symbol = "MA", Name = "Mastercard Inc.", Exchange = "NYSE", Sector = "Financial Services" },
                
                // Healthcare
                new() { Symbol = "JNJ", Name = "Johnson & Johnson", Exchange = "NYSE", Sector = "Healthcare" },
                new() { Symbol = "UNH", Name = "UnitedHealth Group Inc.", Exchange = "NYSE", Sector = "Healthcare" },
                new() { Symbol = "PFE", Name = "Pfizer Inc.", Exchange = "NYSE", Sector = "Healthcare" },
                new() { Symbol = "MRK", Name = "Merck & Co Inc.", Exchange = "NYSE", Sector = "Healthcare" },
                
                // Consumer & Retail
                new() { Symbol = "WMT", Name = "Walmart Inc.", Exchange = "NYSE", Sector = "Consumer Defensive" },
                new() { Symbol = "HD", Name = "Home Depot Inc.", Exchange = "NYSE", Sector = "Consumer Cyclical" },
                new() { Symbol = "PG", Name = "Procter & Gamble Co.", Exchange = "NYSE", Sector = "Consumer Defensive" },
                new() { Symbol = "KO", Name = "Coca-Cola Co.", Exchange = "NYSE", Sector = "Consumer Defensive" },
                new() { Symbol = "NKE", Name = "Nike Inc.", Exchange = "NYSE", Sector = "Consumer Cyclical" },
                new() { Symbol = "MCD", Name = "McDonald's Corp.", Exchange = "NYSE", Sector = "Consumer Cyclical" },
                new() { Symbol = "DIS", Name = "Walt Disney Co.", Exchange = "NYSE", Sector = "Communication Services" },
                
                // Energy
                new() { Symbol = "XOM", Name = "Exxon Mobil Corp.", Exchange = "NYSE", Sector = "Energy" },
                new() { Symbol = "CVX", Name = "Chevron Corporation", Exchange = "NYSE", Sector = "Energy" },
                new() { Symbol = "OXY", Name = "Occidental Petroleum Corp.", Exchange = "NYSE", Sector = "Energy" },
                
                // ETFs
                new() { Symbol = "SPY", Name = "SPDR S&P 500 ETF Trust", Exchange = "NYSE", Sector = "ETF" },
                new() { Symbol = "QQQ", Name = "Invesco QQQ Trust", Exchange = "NASDAQ", Sector = "ETF" },
                new() { Symbol = "VOO", Name = "Vanguard S&P 500 ETF", Exchange = "NYSE", Sector = "ETF" },
                new() { Symbol = "VTI", Name = "Vanguard Total Stock Market ETF", Exchange = "NYSE", Sector = "ETF" }
            };

            // filter by query (case-insensitive, search both symbol and name)
            var filtered = mockResults
                .Where(r => r.Symbol.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           r.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(limit)
                .ToList();

            return filtered;
        }
    }
}