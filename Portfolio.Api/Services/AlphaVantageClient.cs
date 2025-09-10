// File: Services/AlphaVantageClient.cs
using System.Net.Http.Json;
using System.Text.Json;
using System.Runtime.CompilerServices;

namespace Portfolio.Api.Services
{
    /// <summary>
    /// Thin wrapper around the Alpha Vantage HTTP API.
    /// 
    /// Responsibilities:
    /// - Build the request URL for TIME_SERIES_DAILY_ADJUSTED
    /// - Perform the HTTP call (HttpClient is injected by DI)
    /// - Parse the JSON payload robustly
    /// - Yield strongly typed results: (DateOnly date, decimal close)
    /// 
    /// Notes:
    /// - This class does NOT persist to the database. It only fetches and parses.
    /// - Controller/Service above this layer decide what to store/how to cache.
    /// - We intentionally do not handle retries here; we attach a Polly retry
    ///   policy when registering the HttpClient in Program.cs (next snippet).
    /// </summary>
    public class AlphaVantageClient
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly string _apiKey;
        private readonly ILogger<AlphaVantageClient> _log;

        public AlphaVantageClient(HttpClient http, IConfiguration cfg, ILogger<AlphaVantageClient> log)
        {
            _http = http;
            _log = log;

            _baseUrl = cfg["AlphaVantage:BaseUrl"] ?? "https://www.alphavantage.co";
            _apiKey = cfg["AlphaVantage:ApiKey"]
                       ?? throw new InvalidOperationException("AlphaVantage:ApiKey is missing. Configure via user-secrets.");
        }

        /// <summary>
        /// Fetches Alpha Vantage TIME_SERIES_DAILY_ADJUSTED and yields the most recent items
        /// as a stream of tuples: (date, open, high, low, close, adjustedClose, volume).
        /// 
        /// Notes:
        /// - Uses <c>outputsize=compact</c> (~100 days) by default; set <paramref name="fullHistory"/> to true for full history.
        /// - Parses values using invariant culture to avoid locale-specific decimal issues.
        /// - If Alpha Vantage returns a throttling "Note" or an "Error Message", we stop yielding data.
        /// </summary>
        /// <param name="symbol">Ticker (e.g., AAPL). Uppercase recommended.</param>
        /// <param name="days">Max number of most-recent days to return (ignored if <paramref name="fullHistory"/> is true).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <param name="fullHistory">If true, fetch the complete history instead of limiting by <paramref name="days"/>.</param>
        /// <returns>IAsyncEnumerable of (date, open, high, low, close, adjustedClose, volume), newest first.</returns>
        public async IAsyncEnumerable<(DateOnly date, decimal open, decimal high, decimal low, decimal close, decimal adjustedClose, long volume)>
            GetDailyAdjustedAsync(
                string symbol,
                int days,
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default,
                bool fullHistory = false)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("symbol is required", nameof(symbol));

            // Clamp to a reasonable range when not requesting full history.
            days = fullHistory ? days : Math.Clamp(days, 1, 500);

            var outputSize = fullHistory ? "full" : "compact";
            var url = $"{_baseUrl}/query" +
                      $"?function=TIME_SERIES_DAILY_ADJUSTED" +
                      $"&symbol={Uri.EscapeDataString(symbol)}" +
                      $"&outputsize={outputSize}" +
                      $"&datatype=json" +
                      $"&apikey={_apiKey}";

            using var resp = await _http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            // Parse root
            var root = doc.RootElement;

            // If Alpha Vantage returns an "Information" message, log it so we see the reason.
            if (root.TryGetProperty("Information", out var infoMsg))
            {
                // Premium endpoint notice: we'll fall back to non-adjusted DAILY below.
                _log.LogWarning("Alpha Vantage 'Information' for {Symbol}: {Message} — falling back to TIME_SERIES_DAILY", symbol, infoMsg.GetString());
                // Do not break here; the missing series will trigger the DAILY fallback below.
            }

            // Handle Alpha Vantage error responses early.
            if (root.TryGetProperty("Error Message", out _))
            {
                _log.LogWarning("Alpha Vantage returned 'Error Message' for {Symbol}", symbol);
                yield break;
            }
            if (root.TryGetProperty("Note", out _))
            {
                _log.LogWarning("Alpha Vantage returned 'Note' (rate limit) for {Symbol}", symbol);
                yield break;
            }

            // Try adjusted series first; if missing, fall back to non-adjusted DAILY.
            JsonElement series;
            bool adjustedPayload = true;
            JsonDocument? altDoc = null; // keep fallback document alive across enumeration

            if (!root.TryGetProperty("Time Series (Daily)", out series))
            {
                var urlDaily = $"{_baseUrl}/query" +
                               $"?function=TIME_SERIES_DAILY" +
                               $"&symbol={Uri.EscapeDataString(symbol)}" +
                               $"&outputsize={(fullHistory ? "full" : "compact")}" +
                               $"&datatype=json" +
                               $"&apikey={_apiKey}";

                using var resp2 = await _http.GetAsync(urlDaily, ct);
                resp2.EnsureSuccessStatusCode();

                await using var stream2 = await resp2.Content.ReadAsStreamAsync(ct);
                altDoc = await JsonDocument.ParseAsync(stream2, cancellationToken: ct);
                var root2 = altDoc.RootElement;

                if (!root2.TryGetProperty("Time Series (Daily)", out series))
                {
                    var keys2 = string.Join(", ", root2.EnumerateObject().Select(p => p.Name));
                    _log.LogWarning("Alpha Vantage payload (fallback DAILY) for {Symbol}: top-level keys = {Keys}", symbol, keys2);
                    altDoc.Dispose(); // safe to dispose here because we're not enumerating
                    yield break;
                }

                adjustedPayload = false;
            }

            // Parse numbers using invariant culture and keep count of yielded items.
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            int yielded = 0;

            // Ensure the fallback document (if any) stays alive while iterating.
            try
            {
                // Iterate newest-first
                foreach (var day in series.EnumerateObject().OrderByDescending(p => p.Name))
                {
                    if (ct.IsCancellationRequested) yield break;

                    if (!DateOnly.TryParse(day.Name, out var date))
                        continue;

                    var o = day.Value;

                    // Mandatory fields present in both endpoints
                    if (!o.TryGetProperty("1. open", out var openEl)) continue;
                    if (!o.TryGetProperty("2. high", out var highEl)) continue;
                    if (!o.TryGetProperty("3. low", out var lowEl)) continue;
                    if (!o.TryGetProperty("4. close", out var closeEl)) continue;

                    if (!decimal.TryParse(openEl.GetString(), System.Globalization.NumberStyles.Any, culture, out var open)) continue;
                    if (!decimal.TryParse(highEl.GetString(), System.Globalization.NumberStyles.Any, culture, out var high)) continue;
                    if (!decimal.TryParse(lowEl.GetString(), System.Globalization.NumberStyles.Any, culture, out var low)) continue;
                    if (!decimal.TryParse(closeEl.GetString(), System.Globalization.NumberStyles.Any, culture, out var close)) continue;

                    // Adjusted close exists only in ADJUSTED payload; otherwise use close.
                    decimal adjClose = close;
                    if (adjustedPayload && o.TryGetProperty("5. adjusted close", out var adjEl))
                    {
                        if (!decimal.TryParse(adjEl.GetString(), System.Globalization.NumberStyles.Any, culture, out adjClose))
                            adjClose = close;
                    }

                    // Volume index differs: ADJUSTED -> "6. volume", DAILY -> "5. volume"
                    long volume = 0;
                    var volField = adjustedPayload ? "6. volume" : "5. volume";
                    if (o.TryGetProperty(volField, out var volEl))
                    {
                        long.TryParse(volEl.GetString(), System.Globalization.NumberStyles.Any, culture, out volume);
                    }

                    yield return (date, open, high, low, close, adjClose, volume);

                    yielded++;
                    if (!fullHistory && yielded >= days)
                        yield break;
                }
            }
            finally
            {
                altDoc?.Dispose(); // dispose fallback document after enumeration completes
            }

        }

        /// <summary>
        /// Fetches the most recent quote for a single symbol using GLOBAL_QUOTE.
        /// </summary>
        /// <param name="symbol">Ticker (e.g., AAPL)</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Tuple: (symbol, price, latestTradingDay) or null if not available</returns>
        public async Task<(string Symbol, decimal Price, DateOnly LatestTradingDay)?> GetLatestPriceAsync(
            string symbol,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("symbol is required", nameof(symbol));

            var url = $"{_baseUrl}/query" +
                      $"?function=GLOBAL_QUOTE" +
                      $"&symbol={Uri.EscapeDataString(symbol)}" +
                      $"&apikey={_apiKey}";

            using var resp = await _http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            var root = doc.RootElement;

            if (!root.TryGetProperty("Global Quote", out var quote))
            {
                _log.LogWarning("Unexpected payload for {Symbol}: {Payload}", symbol, root.ToString());
                return null;
            }

            var sym = quote.GetProperty("01. symbol").GetString() ?? symbol;
            var priceStr = quote.GetProperty("05. price").GetString();
            var dateStr = quote.GetProperty("07. latest trading day").GetString();

            if (!decimal.TryParse(priceStr, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var price))
                return null;

            if (!DateOnly.TryParse(dateStr, out var latestDay))
                latestDay = DateOnly.FromDateTime(DateTime.UtcNow);

            return (sym, price, latestDay);
        }

        // NOTE (English):
        // Temporary stub to unblock the build. The controller currently calls
        // AlphaVantageClient.GetQuarterlyRevenueAvAsync(...) as a last-resort fallback.
        // Until we implement the real Alpha Vantage fetch, we return an empty list.

        /// <summary>
        /// Lightweight revenue data point for the Alpha Vantage fallback.
        /// </summary>
        /// <param name="PeriodEnd">Quarter end date (ISO yyyy-MM-dd).</param>
        /// <param name="Revenue">Revenue value as a raw decimal (reported currency).</param>
        /// <param name="Currency">Optional ISO currency code, e.g., "USD".</param>
        public record AvRevenuePoint(DateOnly PeriodEnd, decimal Revenue, string? Currency);

        /// <summary>
        /// Temporary stub: returns an empty list for quarterly revenue via Alpha Vantage.
        /// Replace with the real Alpha Vantage implementation later.
        /// </summary>
        /// <param name="symbol">Ticker symbol, e.g., "AAPL".</param>
        /// <param name="limit">Max rows requested (ignored for now).</param>
        /// <param name="ct">Cancellation token.</param>
        public Task<IReadOnlyList<AvRevenuePoint>> GetQuarterlyRevenueAvAsync(
            string symbol,
            int limit = 8,
            CancellationToken ct = default)
        {
            // NOTE (English): This stub unblocks the build by satisfying the controller call.
            // It deliberately returns no data until the real AV integration is implemented.
            return Task.FromResult<IReadOnlyList<AvRevenuePoint>>(Array.Empty<AvRevenuePoint>());
        }
    }
}
