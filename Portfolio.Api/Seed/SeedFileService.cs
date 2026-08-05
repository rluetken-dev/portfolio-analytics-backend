using System.Globalization;
using System.Text.Json;
using Portfolio.Api.Seed.Dto;

namespace Portfolio.Api.Seed;

/// <summary>
/// Loads and validates company seed files from SeedData/companies.
/// </summary>
public sealed class SeedFileService : ISeedFileService
{
    private const string SeedDataDirectory = "SeedData";
    private const string CompaniesDirectory = "companies";
    private const string DateFormat = "yyyy-MM-dd";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly IHostEnvironment _environment;

    public SeedFileService(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public async Task<SeedLoadResult<CompanySeedFile>> LoadCompanyAsync(string symbol)
    {
        string normalizedSymbol = NormalizeSymbol(symbol);
        if (string.IsNullOrWhiteSpace(normalizedSymbol))
        {
            return SeedLoadResult<CompanySeedFile>.Fail("Symbol is required.");
        }

        string path = GetCompanySeedFilePath(normalizedSymbol);

        if (!File.Exists(path))
        {
            return SeedLoadResult<CompanySeedFile>.Fail($"Seed file not found: {path}");
        }

        try
        {
            await using FileStream stream = File.OpenRead(path);

            CompanySeedFile? model = await JsonSerializer.DeserializeAsync<CompanySeedFile>(
                stream,
                JsonOptions);

            if (model is null)
            {
                return SeedLoadResult<CompanySeedFile>.Fail("Seed file could not be parsed.");
            }

            if (!string.Equals(model.Symbol?.Trim(), normalizedSymbol, StringComparison.OrdinalIgnoreCase))
            {
                return SeedLoadResult<CompanySeedFile>.Fail(
                    $"Symbol mismatch: file '{model.Symbol}', requested '{normalizedSymbol}'.");
            }

            List<string> errors = Validate(model);
            if (errors.Count > 0)
            {
                return SeedLoadResult<CompanySeedFile>.Fail(string.Join(Environment.NewLine, errors));
            }

            return SeedLoadResult<CompanySeedFile>.Ok(model);
        }
        catch (JsonException ex)
        {
            return SeedLoadResult<CompanySeedFile>.Fail($"JSON error: {ex.Message}");
        }
        catch (IOException ex)
        {
            return SeedLoadResult<CompanySeedFile>.Fail($"File read error: {ex.Message}");
        }
    }

    private string GetCompanySeedFilePath(string symbol)
    {
        return Path.Combine(
            _environment.ContentRootPath,
            SeedDataDirectory,
            CompaniesDirectory,
            $"{symbol}.json");
    }

    private static List<string> Validate(CompanySeedFile model)
    {
        var errors = new List<string>();

        ValidateProfile(model, errors);
        ValidateQuotes(model, errors);
        ValidateFundamentals(model, errors);

        return errors;
    }

    private static void ValidateProfile(CompanySeedFile model, List<string> errors)
    {
        if (model.Profile is null)
        {
            errors.Add("profile is required.");
            return;
        }

        if (string.IsNullOrWhiteSpace(model.Profile.Name))
        {
            errors.Add("profile.name is required.");
        }

        if (string.IsNullOrWhiteSpace(model.Profile.Sector))
        {
            errors.Add("profile.sector is required.");
        }
    }

    private static void ValidateQuotes(CompanySeedFile model, List<string> errors)
    {
        if (model.Quotes is null)
        {
            errors.Add("quotes is required.");
            return;
        }

        if (string.IsNullOrWhiteSpace(model.Quotes.Currency))
        {
            errors.Add("quotes.currency is required.");
        }

        if (model.Quotes.Rows is null || model.Quotes.Rows.Count == 0)
        {
            errors.Add("quotes.rows must contain at least one row.");
            return;
        }

        for (int index = 0; index < model.Quotes.Rows.Count; index++)
        {
            ValidateQuoteRow(model.Quotes.Rows[index], index, errors);
        }
    }

    private static void ValidateQuoteRow(QuoteRow row, int index, List<string> errors)
    {
        string path = $"quotes.rows[{index}]";

        if (string.IsNullOrWhiteSpace(row.Date))
        {
            errors.Add($"{path}.date is required.");
        }
        else if (!DateOnly.TryParseExact(
            row.Date,
            DateFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _))
        {
            errors.Add($"{path}.date must use {DateFormat}.");
        }

        if (row.Open <= 0 || row.High <= 0 || row.Low <= 0 || row.Close <= 0)
        {
            errors.Add($"{path}.open/high/low/close must be greater than zero.");
        }

        if (row.High < row.Low)
        {
            errors.Add($"{path}.high must be greater than or equal to low.");
        }

        if (row.Volume < 0)
        {
            errors.Add($"{path}.volume must be non-negative.");
        }
    }

    private static void ValidateFundamentals(CompanySeedFile model, List<string> errors)
    {
        if (model.Fundamentals is null)
        {
            return;
        }

        if (model.Fundamentals.Annual is null || model.Fundamentals.Annual.Count == 0)
        {
            errors.Add("fundamentals.annual must contain at least one row when fundamentals is present.");
            return;
        }

        for (int index = 0; index < model.Fundamentals.Annual.Count; index++)
        {
            ValidateFundamentalsAnnualRow(model.Fundamentals.Annual[index], index, errors);
        }
    }

    private static void ValidateFundamentalsAnnualRow(
        FundamentalsAnnualRow row,
        int index,
        List<string> errors)
    {
        string path = $"fundamentals.annual[{index}]";

        if (row.Year is < 1900 or > 2100)
        {
            errors.Add($"{path}.year must be between 1900 and 2100.");
        }

        if (!HasAnyFundamentalMetric(row))
        {
            errors.Add($"{path} must contain at least one metric.");
        }

        if (row.Revenue is < 0)
        {
            errors.Add($"{path}.revenue must be non-negative.");
        }

        if (row.TotalAssets is < 0)
        {
            errors.Add($"{path}.totalAssets must be non-negative.");
        }

        if (row.TotalLiabilities is < 0)
        {
            errors.Add($"{path}.totalLiabilities must be non-negative.");
        }

        if (row.Equity is < 0)
        {
            errors.Add($"{path}.equity must be non-negative.");
        }

        if (row.Shares is < 0)
        {
            errors.Add($"{path}.shares must be non-negative.");
        }
    }

    private static bool HasAnyFundamentalMetric(FundamentalsAnnualRow row)
    {
        return row.Revenue.HasValue ||
               row.NetIncome.HasValue ||
               row.TotalAssets.HasValue ||
               row.TotalLiabilities.HasValue ||
               row.Equity.HasValue ||
               row.Shares.HasValue ||
               row.OperatingCashFlow.HasValue ||
               row.CapitalExpenditures.HasValue ||
               row.ChangeInWorkingCapital.HasValue;
    }

    private static string NormalizeSymbol(string symbol)
    {
        return string.IsNullOrWhiteSpace(symbol)
            ? string.Empty
            : symbol.Trim().ToUpperInvariant();
    }
}
