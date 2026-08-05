//using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;     // QueryHelpers.AddQueryString
using Portfolio.Api.Models;
using Newtonsoft.Json;
using Portfolio.Api.DTOs;
using Portfolio.Api.Exceptions;

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
        private readonly FallbackData _fallback;
        private readonly string _fallbackPath;

        private bool HasApiKey =>
            !string.IsNullOrWhiteSpace(_apiKey) &&
            !_apiKey.Equals("demo-local-placeholder", StringComparison.OrdinalIgnoreCase);

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
            {
                throw new ArgumentException("symbol is required", nameof(symbol));
            }

            if (!HasApiKey)
            {
                throw new ServiceUnavailableException(
                    "FMP API key is not configured. Live FMP provider calls are disabled.");
            }

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
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();
            var arr = JsonConvert.DeserializeObject<FmpProfileRaw[]>(json);


            var first = arr?.FirstOrDefault();
            if (first is null) return null;

            return new CompanyProfile
            {
                Symbol = sym,
                Name = string.IsNullOrWhiteSpace(first.companyName) ? null : first.companyName,
                Sector = string.IsNullOrWhiteSpace(first.sector) ? null : first.sector
            };
        }

        public FmpClient(HttpClient http, IConfiguration config, ILogger<FmpClient> log, FallbackData fallback, string fallbackPath)
        {
            _http = http;
            _log = log;
            _fallback = fallback;
            _fallbackPath = fallbackPath;

            _apiKey = config["Fmp:ApiKey"] ?? string.Empty;

            if (!HasApiKey)
            {
                _log.LogInformation("FMP API key is not configured. Live FMP calls are disabled.");
            }

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
            if (!HasApiKey)
            {
                throw new ServiceUnavailableException(
                    "FMP API key is not configured. Live FMP provider calls are disabled.");
            }

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
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();
            var data = JsonConvert.DeserializeObject<T>(json);
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
            if (!HasApiKey)
            {
                throw new ServiceUnavailableException(
                    "FMP API key is not configured. Live FMP provider calls are disabled.");
            }

            var relative = QueryHelpers.AddQueryString("api/v3/sp500_constituent", new Dictionary<string, string?>
            {
                ["apikey"] = _apiKey
            });

            using var res = await _http.GetAsync(relative, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException($"FMP GET {relative} failed: {(int)res.StatusCode} {res.ReasonPhrase}. Body: {body}");

            await using var stream = await res.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();
            var arr = JsonConvert.DeserializeObject<FmpSp500Row[]>(json);


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
            if (string.IsNullOrWhiteSpace(query))
                return new List<CompanySearchResult>();

            // First: try to find matches in local fallback data (fast & no API usage)
            var localMatches = _fallback.Companies
                .Where(c =>
                    c.Symbol.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    c.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(limit)
                .Select(c => new CompanySearchResult
                {
                    Symbol = c.Symbol,
                    Name = c.Name,
                    Exchange = "Fallback",
                    Sector = c.Sector
                })
                .ToList();

            if (localMatches.Any())
            {
                return localMatches;
            }

            if (!HasApiKey)
            {
                _log.LogInformation("FMP API key is not configured. Returning fallback search results only for query {Query}.", query);
                return new List<CompanySearchResult>();
            }

            // If nothing found locally → call the remote FMP API
            try
            {
                var url = $"api/v3/search?query={Uri.EscapeDataString(query)}&limit={limit}&apikey={_apiKey}";
                var response = await _http.GetFromJsonAsync<List<CompanySearchResult>>(url, ct);

                if (response == null || response.Count == 0)
                    return new List<CompanySearchResult>();

                // Add newly discovered companies into fallback data for future offline use
                foreach (var apiResult in response)
                {
                    if (!_fallback.Companies.Any(c => c.Symbol.Equals(apiResult.Symbol, StringComparison.OrdinalIgnoreCase)))
                    {
                        _fallback.Companies.Add(new CompanyFallbackInfo
                        {
                            Symbol = apiResult.Symbol,
                            Name = apiResult.Name,
                            Sector = apiResult.Sector
                        });
                    }
                }

                // Persist updated fallback data back into JSON file
                var json = JsonConvert.SerializeObject(_fallback, Formatting.Indented);
                await File.WriteAllTextAsync(_fallbackPath, json, ct);

                return response;
            }
            catch (Exception ex)
            {
                // Log errors but return empty list (avoid breaking client flow)
                _log.LogError(ex, "FMP search failed for query {Query}", query);
                return new List<CompanySearchResult>();
            }
        }
    }
}