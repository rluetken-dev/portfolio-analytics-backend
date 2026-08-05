using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Portfolio.Api.Exceptions;

namespace Portfolio.Api.Services;

public sealed class AlphaVantageClient
{
    private const string DailyAdjustedFunction = "TIME_SERIES_DAILY_ADJUSTED";
    private const string DailyFunction = "TIME_SERIES_DAILY";
    private const string GlobalQuoteFunction = "GLOBAL_QUOTE";

    private static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;

    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly ILogger<AlphaVantageClient> _logger;

    private bool HasApiKey => !string.IsNullOrWhiteSpace(_apiKey);

    public AlphaVantageClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<AlphaVantageClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _baseUrl = configuration["AlphaVantage:BaseUrl"]?.TrimEnd('/')
            ?? "https://www.alphavantage.co";
        _apiKey = configuration["AlphaVantage:ApiKey"] ?? string.Empty;

        if (!HasApiKey)
        {
            _logger.LogInformation(
                "Alpha Vantage API key is not configured. Live Alpha Vantage calls are disabled.");
        }
    }

    public async IAsyncEnumerable<(
        DateOnly date,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        decimal adjustedClose,
        long volume)> GetDailyAdjustedAsync(
            string symbol,
            int days,
            [EnumeratorCancellation] CancellationToken ct = default,
            bool fullHistory = false)
    {
        string normalizedSymbol = NormalizeSymbol(symbol);
        if (string.IsNullOrWhiteSpace(normalizedSymbol))
        {
            throw new ArgumentException("Symbol is required.", nameof(symbol));
        }

        EnsureApiKeyConfigured();

        int maxRows = fullHistory ? int.MaxValue : Math.Clamp(days, 1, 500);
        string outputSize = fullHistory ? "full" : "compact";

        using JsonDocument? adjustedDocument = await GetTimeSeriesDocumentAsync(
            DailyAdjustedFunction,
            normalizedSymbol,
            outputSize,
            ct);

        if (adjustedDocument is not null &&
            TryGetTimeSeries(adjustedDocument.RootElement, normalizedSymbol, out JsonElement adjustedSeries))
        {
            int yielded = 0;

            foreach (var row in ParseTimeSeries(adjustedSeries, adjustedPayload: true))
            {
                ct.ThrowIfCancellationRequested();

                yield return row;

                yielded++;
                if (yielded >= maxRows)
                {
                    yield break;
                }
            }

            yield break;
        }

        using JsonDocument? dailyDocument = await GetTimeSeriesDocumentAsync(
            DailyFunction,
            normalizedSymbol,
            outputSize,
            ct);

        if (dailyDocument is null ||
            !TryGetTimeSeries(dailyDocument.RootElement, normalizedSymbol, out JsonElement dailySeries))
        {
            yield break;
        }

        int fallbackYielded = 0;

        foreach (var row in ParseTimeSeries(dailySeries, adjustedPayload: false))
        {
            ct.ThrowIfCancellationRequested();

            yield return row;

            fallbackYielded++;
            if (fallbackYielded >= maxRows)
            {
                yield break;
            }
        }
    }

    public async Task<(string Symbol, decimal Price, DateOnly LatestTradingDay)?> GetLatestPriceAsync(
        string symbol,
        CancellationToken ct = default)
    {
        string normalizedSymbol = NormalizeSymbol(symbol);
        if (string.IsNullOrWhiteSpace(normalizedSymbol))
        {
            throw new ArgumentException("Symbol is required.", nameof(symbol));
        }

        EnsureApiKeyConfigured();

        string url = BuildUrl(GlobalQuoteFunction, normalizedSymbol);

        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            JsonElement root = document.RootElement;

            if (HasProviderMessage(root, normalizedSymbol))
            {
                return null;
            }

            if (!root.TryGetProperty("Global Quote", out JsonElement quote))
            {
                _logger.LogWarning(
                    "Alpha Vantage returned an unexpected quote payload for {Symbol}.",
                    normalizedSymbol);

                return null;
            }

            string returnedSymbol = GetStringOrDefault(quote, "01. symbol", normalizedSymbol);
            string? priceText = GetStringOrDefault(quote, "05. price", null);
            string? dateText = GetStringOrDefault(quote, "07. latest trading day", null);

            if (!decimal.TryParse(priceText, NumberStyles.Any, InvariantCulture, out decimal price))
            {
                return null;
            }

            DateOnly latestTradingDay = DateOnly.TryParse(dateText, out DateOnly parsedDate)
                ? parsedDate
                : DateOnly.FromDateTime(DateTime.UtcNow);

            return (returnedSymbol, price, latestTradingDay);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogWarning(ex, "Alpha Vantage request timed out for {Symbol}.", normalizedSymbol);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Alpha Vantage HTTP request failed for {Symbol}.", normalizedSymbol);
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Alpha Vantage returned malformed JSON for {Symbol}.", normalizedSymbol);
            return null;
        }
    }

    public Task<IReadOnlyList<AvRevenuePoint>> GetQuarterlyRevenueAvAsync(
        string symbol,
        int limit = 8,
        CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<AvRevenuePoint>>(Array.Empty<AvRevenuePoint>());
    }

    public sealed record AvRevenuePoint(DateOnly PeriodEnd, decimal Revenue, string? Currency);

    private async Task<JsonDocument?> GetTimeSeriesDocumentAsync(
        string function,
        string symbol,
        string outputSize,
        CancellationToken ct)
    {
        string url = BuildUrl(function, symbol, outputSize);

        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Alpha Vantage time series request failed for {Symbol}.", symbol);
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Alpha Vantage returned malformed time series JSON for {Symbol}.", symbol);
            return null;
        }
    }

    private bool TryGetTimeSeries(JsonElement root, string symbol, out JsonElement series)
    {
        if (HasProviderMessage(root, symbol))
        {
            series = default;
            return false;
        }

        if (root.TryGetProperty("Time Series (Daily)", out series))
        {
            return true;
        }

        _logger.LogWarning(
            "Alpha Vantage response for {Symbol} did not contain a daily time series.",
            symbol);

        return false;
    }

    private bool HasProviderMessage(JsonElement root, string symbol)
    {
        if (root.TryGetProperty("Error Message", out JsonElement error))
        {
            _logger.LogWarning(
                "Alpha Vantage returned an error for {Symbol}: {Message}",
                symbol,
                error.GetString());

            return true;
        }

        if (root.TryGetProperty("Note", out JsonElement note))
        {
            _logger.LogWarning(
                "Alpha Vantage rate limit response for {Symbol}: {Message}",
                symbol,
                note.GetString());

            return true;
        }

        if (root.TryGetProperty("Information", out JsonElement information))
        {
            _logger.LogWarning(
                "Alpha Vantage information response for {Symbol}: {Message}",
                symbol,
                information.GetString());

            return true;
        }

        return false;
    }

    private static IEnumerable<(
        DateOnly date,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        decimal adjustedClose,
        long volume)> ParseTimeSeries(JsonElement series, bool adjustedPayload)
    {
        foreach (JsonProperty day in series.EnumerateObject().OrderByDescending(property => property.Name))
        {
            if (!DateOnly.TryParse(day.Name, out DateOnly date))
            {
                continue;
            }

            JsonElement values = day.Value;

            if (!TryGetDecimal(values, "1. open", out decimal open) ||
                !TryGetDecimal(values, "2. high", out decimal high) ||
                !TryGetDecimal(values, "3. low", out decimal low) ||
                !TryGetDecimal(values, "4. close", out decimal close))
            {
                continue;
            }

            decimal adjustedClose = close;
            if (adjustedPayload && TryGetDecimal(values, "5. adjusted close", out decimal parsedAdjustedClose))
            {
                adjustedClose = parsedAdjustedClose;
            }

            string volumeProperty = adjustedPayload ? "6. volume" : "5. volume";
            long volume = TryGetLong(values, volumeProperty, out long parsedVolume)
                ? parsedVolume
                : 0;

            yield return (date, open, high, low, close, adjustedClose, volume);
        }
    }

    private static bool TryGetDecimal(JsonElement element, string propertyName, out decimal value)
    {
        value = 0;

        return element.TryGetProperty(propertyName, out JsonElement property) &&
               decimal.TryParse(property.GetString(), NumberStyles.Any, InvariantCulture, out value);
    }

    private static bool TryGetLong(JsonElement element, string propertyName, out long value)
    {
        value = 0;

        return element.TryGetProperty(propertyName, out JsonElement property) &&
               long.TryParse(property.GetString(), NumberStyles.Any, InvariantCulture, out value);
    }

    private string BuildUrl(string function, string symbol, string? outputSize = null)
    {
        var query = new Dictionary<string, string?>
        {
            ["function"] = function,
            ["symbol"] = symbol,
            ["datatype"] = "json",
            ["apikey"] = _apiKey
        };

        if (!string.IsNullOrWhiteSpace(outputSize))
        {
            query["outputsize"] = outputSize;
        }

        return QueryHelpers.AddQueryString($"{_baseUrl}/query", query);
    }

    private void EnsureApiKeyConfigured()
    {
        if (HasApiKey)
        {
            return;
        }

        throw new ServiceUnavailableException(
            "Alpha Vantage API key is not configured. Live Alpha Vantage provider calls are disabled.");
    }

    private static string NormalizeSymbol(string symbol)
    {
        return string.IsNullOrWhiteSpace(symbol)
            ? string.Empty
            : symbol.Trim().ToUpperInvariant();
    }

    private static string? GetStringOrDefault(JsonElement element, string propertyName, string? defaultValue)
    {
        return element.TryGetProperty(propertyName, out JsonElement property)
            ? property.GetString()
            : defaultValue;
    }
}