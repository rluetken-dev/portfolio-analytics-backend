using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Api.Data;
using Portfolio.Api.DTOs;
using Portfolio.Api.Exceptions;
using Portfolio.Api.Models;
using Portfolio.Api.Services;
using Portfolio.Api.Utils;

namespace Portfolio.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CompaniesController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly FmpClient _fmp;
        private readonly ILogger<CompaniesController> _logger;
        private readonly FallbackData _fallbackData;

        public record CompanySummaryDto
        {
            public string? Id { get; init; }
            public string? Symbol { get; init; }
            public string? Name { get; init; }
            public string? Sector { get; init; }
        }

        public CompaniesController(
            AppDbContext db,
            FmpClient fmp,
            ILogger<CompaniesController> logger,
            FallbackData fallbackData)
        {
            _db = db;
            _fmp = fmp;
            _logger = logger;
            _fallbackData = fallbackData;
        }

        /// <summary>
        /// Returns companies from the local database.
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<CompanySummaryDto>>> GetCompanies(
            [FromQuery] string? q,
            [FromQuery] int? limit,
            CancellationToken ct)
        {
            var query = _db.Set<Ticker>().AsQueryable();

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                query = query.Where(t =>
                    EF.Functions.Like(t.Symbol, $"%{term}%") ||
                    (t.Name != null && EF.Functions.Like(t.Name, $"%{term}%")));
            }

            var take = Math.Clamp(limit ?? 50, 1, 200);

            var rows = await query
                .OrderBy(ticker => ticker.Symbol)
                .Select(ticker => new CompanySearchResult
                {
                    Id = ticker.Id,
                    Symbol = ticker.Symbol ?? string.Empty,
                    Name = ticker.Name ?? string.Empty,
                    Sector = ticker.Sector ?? string.Empty,
                    IsInDatabase = true
                })
                .Take(take)
                .ToListAsync(ct);

            return Ok(rows);
        }

        /// <summary>
        /// Refreshes profile information for a single local company.
        /// </summary>
        [HttpPost("{symbol}/refresh-profile")]
        public async Task<IActionResult> RefreshProfile([FromRoute] string symbol, CancellationToken ct)
        {
            Guard.BadRequestIf(string.IsNullOrWhiteSpace(symbol), "Symbol required.");

            var normalizedSymbol = symbol.Trim().ToUpperInvariant();

            var ticker = await _db.Set<Ticker>()
                .FirstOrDefaultAsync(ticker => ticker.Symbol == normalizedSymbol, ct);

            if (ticker is null)
            {
                throw new NotFoundException($"Ticker '{normalizedSymbol}' not found.");
            }

            try
            {
                var profile = await _fmp.GetCompanyProfileAsync(normalizedSymbol, ct);

                if (profile is null)
                {
                    throw new NotFoundException($"No profile found at FMP for '{normalizedSymbol}'.");
                }

                if (!string.IsNullOrWhiteSpace(profile.Name))
                {
                    ticker.Name = profile.Name;
                }

                if (!string.IsNullOrWhiteSpace(profile.Sector))
                {
                    ticker.Sector = profile.Sector;
                }

                await _db.SaveChangesAsync(ct);

                return Ok(ToSummaryDto(ticker));
            }
            catch (ServiceUnavailableException ex)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    error = "External provider is not configured.",
                    detail = ex.Message
                });
            }
            catch (HttpRequestException ex) when (IsRateLimitError(ex))
            {
                if (TryApplyFallbackSector(ticker))
                {
                    await _db.SaveChangesAsync(ct);
                    return Ok(ToSummaryDto(ticker));
                }

                return StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    error = "FMP rate limit hit. Try again later."
                });
            }
            catch (HttpRequestException ex) when (IsForbiddenError(ex))
            {
                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    error = "FMP access denied. Check API key and provider plan.",
                    detail = "403 from FMP"
                });
            }
            catch (HttpRequestException ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    error = "Upstream FMP call failed.",
                    detail = ex.Message
                });
            }
            catch (OperationCanceledException)
            {
                return StatusCode(StatusCodes.Status408RequestTimeout, new
                {
                    error = "Request cancelled or timed out."
                });
            }
        }

        /// <summary>
        /// Refreshes profile information for local companies with missing data.
        /// </summary>
        [HttpPost("refresh-profiles")]
        public async Task<IActionResult> RefreshProfiles([FromQuery] int? limit, CancellationToken ct)
        {
            var take = Math.Clamp(limit ?? 25, 1, 100);

            var candidates = await _db.Set<Ticker>()
                .Where(ticker => ticker.Name == null || ticker.Sector == null)
                .OrderBy(ticker => ticker.Symbol)
                .Take(take)
                .ToListAsync(ct);

            var updated = new List<CompanySummaryDto>();
            var providerUnavailable = false;

            foreach (var ticker in candidates)
            {
                try
                {
                    var profile = await _fmp.GetCompanyProfileAsync(ticker.Symbol, ct);

                    if (profile is null)
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(profile.Name))
                    {
                        ticker.Name = profile.Name;
                    }

                    if (!string.IsNullOrWhiteSpace(profile.Sector))
                    {
                        ticker.Sector = profile.Sector;
                    }

                    updated.Add(ToSummaryDto(ticker));
                }
                catch (ServiceUnavailableException)
                {
                    providerUnavailable = true;

                    if (TryApplyFallbackSector(ticker))
                    {
                        updated.Add(ToSummaryDto(ticker));
                    }
                }
                catch (HttpRequestException ex) when (IsRateLimitError(ex))
                {
                    if (TryApplyFallbackSector(ticker))
                    {
                        updated.Add(ToSummaryDto(ticker));
                    }
                }
                finally
                {
                    await Task.Delay(350, ct);
                }
            }

            if (updated.Count > 0)
            {
                await _db.SaveChangesAsync(ct);
            }

            var remaining = await _db.Set<Ticker>()
                .CountAsync(ticker => ticker.Name == null || ticker.Sector == null, ct);

            return Ok(new
            {
                count = updated.Count,
                remaining,
                providerUnavailable,
                items = updated
            });
        }

        /// <summary>
        /// Searches companies by local fallback data and optional external provider data.
        /// </summary>
        [HttpGet("search")]
        public async Task<ActionResult<CompanySearchResponse>> SearchCompanies(
            [FromQuery] string? q,
            [FromQuery] int? limit,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            {
                throw new BadRequestException("Query must be at least 2 characters long.");
            }

            var query = q.Trim();
            var take = Math.Clamp(limit ?? 10, 1, 50);

            try
            {
                var searchResults = await _fmp.SearchCompaniesAsync(query, take, ct);

                var symbols = searchResults
                    .Select(result => result.Symbol.ToUpperInvariant())
                    .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                    .Distinct()
                    .ToList();

                var dbTickers = await _db.Tickers
                    .AsNoTracking()
                    .Where(ticker => symbols.Contains(ticker.Symbol.ToUpper()))
                    .Select(ticker => new
                    {
                        ticker.Id,
                        Symbol = ticker.Symbol.ToUpper(),
                        ticker.Sector
                    })
                    .ToDictionaryAsync(
                        ticker => ticker.Symbol,
                        ticker => new { ticker.Id, ticker.Sector },
                        ct);

                var userSymbols = await GetCurrentUserPortfolioSymbolsAsync(ct);

                var results = searchResults.Select(result =>
                {
                    var upperSymbol = result.Symbol.ToUpperInvariant();
                    var existsInDb = dbTickers.TryGetValue(upperSymbol, out var local);

                    return new CompanySearchResult
                    {
                        Id = local?.Id ?? 0,
                        Symbol = result.Symbol,
                        Name = result.Name,
                        Exchange = result.Exchange,
                        Sector = local?.Sector ?? result.Sector,
                        IsInDatabase = existsInDb,
                        IsInUserPortfolio = userSymbols.Contains(upperSymbol)
                    };
                }).ToList();

                return Ok(new CompanySearchResponse
                {
                    Query = query,
                    Results = results,
                    TotalFound = results.Count
                });
            }
            catch (HttpRequestException ex) when (IsRateLimitError(ex))
            {
                return StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    error = "API rate limit exceeded. Try again later."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SearchCompanies failed for query: {Query}", query);

                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    error = "Search failed. Please try again later."
                });
            }
        }

        /// <summary>
        /// Adds a single company to the local database.
        /// </summary>
        [HttpPost("add")]
        public async Task<ActionResult<CompanySummaryDto>> AddCompany(
            [FromBody] AddCompanyRequest request,
            CancellationToken ct)
        {
            Guard.BadRequestIf(string.IsNullOrWhiteSpace(request.Symbol), "Symbol is required.");

            var symbol = request.Symbol.Trim().ToUpperInvariant();

            var existing = await _db.Tickers
                .FirstOrDefaultAsync(ticker => ticker.Symbol == symbol, ct);

            if (existing != null)
            {
                return Conflict(new { error = $"Company {symbol} already exists." });
            }

            var fallbackCompany = _fallbackData.Companies
                .FirstOrDefault(company => string.Equals(company.Symbol, symbol, StringComparison.OrdinalIgnoreCase));

            if (fallbackCompany != null)
            {
                var fallbackTicker = new Ticker
                {
                    Symbol = symbol,
                    Name = fallbackCompany.Name,
                    Sector = fallbackCompany.Sector
                };

                _db.Tickers.Add(fallbackTicker);
                await _db.SaveChangesAsync(ct);

                return CreatedAtAction(
                    nameof(GetCompanies),
                    new { q = symbol },
                    ToSummaryDto(fallbackTicker));
            }

            try
            {
                var profile = await _fmp.GetCompanyProfileAsync(symbol, ct);

                if (profile == null)
                {
                    throw new NotFoundException($"Company {symbol} not found in external API.");
                }

                var ticker = new Ticker
                {
                    Symbol = symbol,
                    Name = profile.Name ?? symbol,
                    Sector = profile.Sector
                };

                _db.Tickers.Add(ticker);
                await _db.SaveChangesAsync(ct);

                return CreatedAtAction(
                    nameof(GetCompanies),
                    new { q = symbol },
                    ToSummaryDto(ticker));
            }
            catch (ServiceUnavailableException ex)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    error = "External provider is not configured.",
                    detail = ex.Message
                });
            }
            catch (HttpRequestException ex) when (IsRateLimitError(ex))
            {
                return StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    error = "API rate limit exceeded."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add company: {Symbol}", symbol);

                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    error = "Failed to add company."
                });
            }
        }

        /// <summary>
        /// Adds popular companies from predefined local lists.
        /// </summary>
        [HttpPost("add-popular")]
        public async Task<ActionResult<BulkAddResponse>> AddPopularCompanies(
            [FromBody] AddPopularRequest request,
            CancellationToken ct)
        {
            var popularStocks = GetPopularStocksList(request.Category);
            var added = new List<CompanySearchResult>();
            var existing = new List<CompanySearchResult>();
            var errors = new List<string>();

            foreach (var symbol in popularStocks.Take(request.Limit ?? 20))
            {
                try
                {
                    var ticker = await _db.Tickers
                        .FirstOrDefaultAsync(ticker => ticker.Symbol == symbol, ct);

                    if (ticker != null)
                    {
                        existing.Add(ToSearchResult(ticker));
                        continue;
                    }

                    ticker = new Ticker
                    {
                        Symbol = symbol,
                        Name = GetFallbackCompanyName(symbol),
                        Sector = GetFallbackSector(symbol)
                    };

                    _db.Tickers.Add(ticker);
                    await _db.SaveChangesAsync(ct);

                    added.Add(ToSearchResult(ticker));

                    await Task.Delay(100, ct);
                }
                catch (Exception ex)
                {
                    errors.Add($"{symbol}: {ex.Message}");
                }
            }

            return Ok(new BulkAddResponse
            {
                Added = added,
                Existing = existing,
                Errors = errors
            });
        }

        /// <summary>
        /// Removes a company if no related financial data exists.
        /// </summary>
        [HttpDelete("{symbol}")]
        public async Task<IActionResult> RemoveCompany([FromRoute] string symbol, CancellationToken ct)
        {
            Guard.BadRequestIf(string.IsNullOrWhiteSpace(symbol), "Symbol is required.");

            var normalizedSymbol = symbol.Trim().ToUpperInvariant();
            var ticker = await _db.Tickers
                .FirstOrDefaultAsync(ticker => ticker.Symbol == normalizedSymbol, ct);

            if (ticker == null)
            {
                throw new NotFoundException($"Company {normalizedSymbol} not found.");
            }

            var hasData =
                await _db.IncomeStatements.AnyAsync(statement => statement.Symbol == normalizedSymbol, ct) ||
                await _db.BalanceSheets.AnyAsync(statement => statement.Symbol == normalizedSymbol, ct) ||
                await _db.CashFlows.AnyAsync(statement => statement.Symbol == normalizedSymbol, ct) ||
                await _db.Prices.AnyAsync(price => price.TickerId == ticker.Id, ct);

            if (hasData)
            {
                return Conflict(new { error = $"Cannot delete {normalizedSymbol}: has financial data." });
            }

            _db.Tickers.Remove(ticker);
            await _db.SaveChangesAsync(ct);

            return Ok(new { message = $"Company {normalizedSymbol} removed successfully." });
        }

        private async Task<HashSet<string>> GetCurrentUserPortfolioSymbolsAsync(CancellationToken ct)
        {
            var userSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            if (userId == null || !int.TryParse(userId, out var parsedUserId))
            {
                return userSymbols;
            }

            var symbols = await _db.UserCompanies
                .Include(userCompany => userCompany.Ticker)
                .Where(userCompany => userCompany.UserId == parsedUserId)
                .Select(userCompany => userCompany.Ticker.Symbol.ToUpper())
                .ToListAsync(ct);

            return new HashSet<string>(symbols, StringComparer.OrdinalIgnoreCase);
        }

        private List<string> GetPopularStocksList(string? category = null)
        {
            if (string.IsNullOrEmpty(category))
            {
                return _fallbackData.PopularLists.GetValueOrDefault("default") ?? new List<string>();
            }

            var key = category.ToLowerInvariant();

            return _fallbackData.PopularLists.GetValueOrDefault(key) ?? new List<string>();
        }

        private string GetFallbackCompanyName(string symbol)
        {
            var company = _fallbackData.Companies
                .FirstOrDefault(company => string.Equals(company.Symbol, symbol, StringComparison.OrdinalIgnoreCase));

            return company?.Name ?? symbol;
        }

        private string? GetFallbackSector(string symbol)
        {
            var company = _fallbackData.Companies
                .FirstOrDefault(company => string.Equals(company.Symbol, symbol, StringComparison.OrdinalIgnoreCase));

            return company?.Sector;
        }

        private bool TryApplyFallbackSector(Ticker ticker)
        {
            if (!string.IsNullOrWhiteSpace(ticker.Sector))
            {
                return false;
            }

            var sector = GetFallbackSector(ticker.Symbol);

            if (string.IsNullOrWhiteSpace(sector))
            {
                return false;
            }

            ticker.Sector = sector;

            return true;
        }

        private static bool IsRateLimitError(HttpRequestException ex)
        {
            return ex.Message.Contains("429") ||
                ex.Message.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("Limit Reach", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsForbiddenError(HttpRequestException ex)
        {
            return ex.Message.Contains("403") ||
                ex.Message.Contains("Forbidden", StringComparison.OrdinalIgnoreCase);
        }

        private static CompanySummaryDto ToSummaryDto(Ticker ticker)
        {
            return new CompanySummaryDto
            {
                Id = ticker.Id.ToString(),
                Symbol = ticker.Symbol,
                Name = ticker.Name,
                Sector = ticker.Sector
            };
        }

        private static CompanySearchResult ToSearchResult(Ticker ticker)
        {
            return new CompanySearchResult
            {
                Id = ticker.Id,
                Symbol = ticker.Symbol ?? string.Empty,
                Name = ticker.Name ?? string.Empty,
                Sector = ticker.Sector ?? string.Empty,
                Exchange = null,
                IsInDatabase = true
            };
        }
    }
}
