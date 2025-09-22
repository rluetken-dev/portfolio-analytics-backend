using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Portfolio.Api.Seed.Dto;

namespace Portfolio.Api.Seed
{
    /// <summary>
    /// Reads JSON seed files from SeedData/companies/{SYMBOL}.json and validates fields.
    /// </summary>
    public sealed class SeedFileService : ISeedFileService
    {
        private readonly ILogger<SeedFileService> _log;
        private readonly IHostEnvironment _env;

        private static readonly JsonSerializerOptions _json = new()
        {
            // English: resilient JSON parsing
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public SeedFileService(ILogger<SeedFileService> log, IHostEnvironment env)
        {
            _log = log;
            _env = env;
        }

        public async Task<SeedLoadResult<CompanySeedFile>> LoadCompanyAsync(string symbol)
        {
            var sym = (symbol ?? "").Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(sym))
                return SeedLoadResult<CompanySeedFile>.Fail("Symbol is empty.");

            try
            {
                var path = Path.Combine(_env.ContentRootPath, "SeedData", "companies", $"{sym}.json");
                if (!File.Exists(path))
                    return SeedLoadResult<CompanySeedFile>.Fail($"Seed file not found: {path}");

                await using var fs = File.OpenRead(path);
                var model = await JsonSerializer.DeserializeAsync<CompanySeedFile>(fs, _json);
                if (model is null)
                    return SeedLoadResult<CompanySeedFile>.Fail("Failed to parse JSON (null).");

                if (!string.Equals(model.Symbol?.Trim(), sym, StringComparison.OrdinalIgnoreCase))
                    return SeedLoadResult<CompanySeedFile>.Fail($"Symbol mismatch: file '{model.Symbol}', requested '{sym}'.");

                var errors = Validate(model);
                if (!string.IsNullOrEmpty(errors))
                    return SeedLoadResult<CompanySeedFile>.Fail(errors);

                return SeedLoadResult<CompanySeedFile>.Ok(model);
            }
            catch (JsonException jx)
            {
                return SeedLoadResult<CompanySeedFile>.Fail($"JSON error: {jx.Message}");
            }
            catch (Exception ex)
            {
                return SeedLoadResult<CompanySeedFile>.Fail($"Unexpected error: {ex.Message}");
            }
        }

        // English: basic field/date validation; return empty string if OK
        private static string Validate(CompanySeedFile m)
        {
            var errs = new List<string>();

            if (m.Profile is null) errs.Add("profile: missing object");
            else
            {
                if (string.IsNullOrWhiteSpace(m.Profile.Name)) errs.Add("profile.name is required");
                if (string.IsNullOrWhiteSpace(m.Profile.Sector)) errs.Add("profile.sector is required");
            }

            if (m.Quotes is null) errs.Add("quotes: missing object");
            else
            {
                if (string.IsNullOrWhiteSpace(m.Quotes.Currency)) errs.Add("quotes.currency is required");

                if (m.Quotes.Rows is null || m.Quotes.Rows.Count == 0)
                    errs.Add("quotes.rows must contain at least one row");
                else
                {
                    for (int i = 0; i < m.Quotes.Rows.Count; i++)
                    {
                        var r = m.Quotes.Rows[i];
                        var p = $"quotes.rows[{i}]";

                        if (string.IsNullOrWhiteSpace(r.Date)) errs.Add($"{p}.date is required (YYYY-MM-DD)");
                        else if (!DateOnly.TryParseExact(r.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                            errs.Add($"{p}.date invalid: '{r.Date}' (YYYY-MM-DD)");

                        if (r.Open <= 0 || r.High <= 0 || r.Low <= 0 || r.Close <= 0)
                            errs.Add($"{p}: OHLC must be > 0");
                        if (r.High < r.Low)
                            errs.Add($"{p}: high < low");
                        if (r.Volume < 0)
                            errs.Add($"{p}.volume must be >= 0");
                    }
                }
            }

            // --- Fundamentals (optional, annual rows) -----------------------------------
            if (m.Fundamentals is not null)
            {
                var f = m.Fundamentals;

                // English: require at least one annual row if the block exists
                if (f.Annual is null || f.Annual.Count == 0)
                {
                    errs.Add("fundamentals.annual must contain at least one row when fundamentals is present");
                }
                else
                {
                    for (int i = 0; i < f.Annual.Count; i++)
                    {
                        var a = f.Annual[i];
                        var p = $"fundamentals.annual[{i}]";

                        // English: year sanity range
                        if (a.Year < 1900 || a.Year > 2100)
                            errs.Add($"{p}.year out of range (1900..2100): {a.Year}");

                        // English: at least one metric should be provided
                        bool anyMetric =
                            a.Revenue.HasValue || a.NetIncome.HasValue || a.TotalAssets.HasValue ||
                            a.TotalLiabilities.HasValue || a.Equity.HasValue || a.Shares.HasValue ||
                            a.OperatingCashFlow.HasValue || a.CapitalExpenditures.HasValue;

                        if (!anyMetric)
                            errs.Add($"{p}: at least one metric required (revenue/netIncome/totalAssets/totalLiabilities/equity/shares)");

                        // English: non-negative checks
                        if (a.Revenue is < 0) errs.Add($"{p}.revenue < 0");
                        if (a.NetIncome is < 0) errs.Add($"{p}.netIncome < 0");
                        if (a.TotalAssets is < 0) errs.Add($"{p}.totalAssets < 0");
                        if (a.TotalLiabilities is < 0) errs.Add($"{p}.totalLiabilities < 0");
                        if (a.Equity is < 0) errs.Add($"{p}.equity < 0");
                        if (a.Shares is < 0) errs.Add($"{p}.shares < 0");
                    }
                }
            }

            return errs.Count == 0 ? string.Empty : string.Join(Environment.NewLine, errs);
        }
        
    }
    
    
}
