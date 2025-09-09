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
            _apiKey  = cfg["AlphaVantage:ApiKey"]
                       ?? throw new InvalidOperationException("AlphaVantage:ApiKey is missing. Configure via user-secrets.");
        }

        /// <summary>
        /// Fetches daily adjusted time series for a given symbol and returns
        /// up to <paramref name="days"/> most recent items as (date, close).
        /// 
        /// Rationale:
        /// - TIME_SERIES_DAILY_ADJUSTED includes splits/dividends in "adjusted close",
        ///   but we read the plain "4. close" here for simplicity. You can switch to
        ///   "5. adjusted close" later if your analytics require it.
        /// 
        /// Error handling:
        /// - Alpha Vantage returns JSON with keys like "Error Message" or "Note" (rate limit).
        /// - We detect those and throw informative exceptions.
        /// - Callers decide how to proceed (e.g., partial import).
        /// </summary>
        /// <param name="symbol">Ticker (e.g., AAPL). Uppercase recommended.</param>
        /// <param name="days">How many most-recent days to return (1..100 typical for MVP).</param>
        /// <param name="ct">Cancellation token for request cancellation.</param>
        /// <returns>IAsyncEnumerable of (date, close) newest-first.</returns>
        public async IAsyncEnumerable<(DateOnly date, decimal close)> GetDailyAdjustedAsync(
            string symbol,
            int days,
            [EnumeratorCancellation] CancellationToken ct = default) 
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("symbol is required", nameof(symbol));

            days = Math.Clamp(days, 1, 200);

            // Build request URL.
            // We use outputsize=compact (last ~100 days). For longer history, use "full".
            var url = $"{_baseUrl}/query" +
                      $"?function=TIME_SERIES_DAILY" +
                      $"&symbol={Uri.EscapeDataString(symbol)}" +
                      $"&outputsize=compact" +
                      $"&apikey={_apiKey}";

            using var resp = await _http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();

            // We parse manually with JsonDocument:
            // - lower allocations than dynamic
            // - explicit error checks
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;
           
            // Detect Alpha Vantage error/limit responses:
            if (root.TryGetProperty("Error Message", out var err))
            {
                var msg = err.GetString() ?? "Unknown Alpha Vantage error.";
                throw new InvalidOperationException($"Alpha Vantage error for {symbol}: {msg}");
            }
            if (root.TryGetProperty("Note", out var note))
            {
                var msg = note.GetString() ?? "Alpha Vantage rate limit reached.";
                throw new InvalidOperationException($"Alpha Vantage note for {symbol}: {msg}");
            }
            if (!root.TryGetProperty("Time Series (Daily)", out var timeSeries))
            {
                // Some responses return "Information" or other keys; log full payload for diagnostics.
                _log.LogWarning("Unexpected Alpha Vantage payload for {Symbol}: keys = {Keys}",
                    symbol, string.Join(", ", root.EnumerateObject().Select(p => p.Name)));
                throw new InvalidOperationException($"Unexpected Alpha Vantage payload for {symbol} (no 'Time Series (Daily)').");
            }

            // Enumerate properties: each property name is a date string "yyyy-MM-dd".
            // The API returns newest first. We yield up to 'days' items.
            var count = 0;
            foreach (var dayEntry in timeSeries.EnumerateObject())
            {
                if (count >= days) yield break;

                var dateStr = dayEntry.Name; // e.g., "2025-09-05"
                if (!DateOnly.TryParse(dateStr, out var date))
                {
                    _log.LogWarning("Skipping malformed date '{Date}' in payload for {Symbol}", dateStr, symbol);
                    continue;
                }

                // We read "4. close". If you prefer adjusted closes, use "5. adjusted close".
                if (!dayEntry.Value.TryGetProperty("4. close", out var closeEl))
                {
                    _log.LogWarning("Missing '4. close' for {Symbol} on {Date}", symbol, dateStr);
                    continue;
                }

                var closeStr = closeEl.GetString();
                if (string.IsNullOrWhiteSpace(closeStr) ||
                    !decimal.TryParse(closeStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var close))
                {
                    _log.LogWarning("Invalid close value '{Close}' for {Symbol} on {Date}", closeStr, symbol, dateStr);
                    continue;
                }

                yield return (date, close);
                count++;
            }
        }
    }
}
