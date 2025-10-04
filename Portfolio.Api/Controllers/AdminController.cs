using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Portfolio.Api.Services;
using Portfolio.Api.Data;
using Microsoft.EntityFrameworkCore;
using Portfolio.Api.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Text.Json;
using Portfolio.Api.Seed;      // ISeedFileService
using Portfolio.Api.Seed.Dto;  // CompanySeedFile etc.
using Microsoft.AspNetCore.Authorization;
using Portfolio.Api.Extensions;
using Portfolio.Api.Exceptions;
using Portfolio.Api.Utils;

namespace Portfolio.Api.Controllers;

/// <summary>
/// Administrative endpoints for database housekeeping.
/// ⚠️ Important: Secure these endpoints (authentication/authorization) in production.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = "AdminOnly")] // ✅ only users with isAdmin=true in their JWT
public class AdminController : ControllerBase
{

    private readonly AppDbContext _db;
    private readonly MaintenanceService _maintenance;
    private readonly ISeedFileService _files;
    private readonly ISeedService _seed;

    /// <summary>Inject EF Core context and housekeeping service.</summary>
    public AdminController(AppDbContext db, MaintenanceService maintenance, ISeedFileService files, ISeedService seed)
    {
        _db = db;
        _maintenance = maintenance;
        _files = files;
        _seed = seed;
    }

    /// <summary>
    /// Prunes daily price rows only (does not touch fundamentals).
    /// English: Deletes old price rows based on maxAgeDays and/or caps rows per symbol.
    /// </summary>
    [HttpPost("prune")]
    [SwaggerOperation(
        Summary = "Prune old price data",
        Description = "Deletes outdated daily price rows based on retention settings. Does not affect fundamentals.",
        Tags = new[] { "Admin – Maintenance" }
    )]
    public async Task<IActionResult> Prune(
        [FromQuery] int? maxAgeDays = 3 * 365,
        [FromQuery] int? keepPerSymbol = null,
        CancellationToken ct = default)
    {
        if (maxAgeDays is < 0 || keepPerSymbol is < 0)
            throw new BadRequestException("Parameters must be non-negative.");

        var deleted = await _maintenance.PruneAsync(maxAgeDays, keepPerSymbol, ct);
        return Ok(new { ok = true, deleted });
    }

    /// <summary>
    /// Runs SQLite VACUUM and ANALYZE for the entire database file (not only Prices).
    /// English: Reclaims free space and refreshes query planner statistics.
    /// Should be run after large deletes/truncates.
    /// </summary>
    [HttpPost("vacuum")]
    [SwaggerOperation(
        Summary = "Run VACUUM + ANALYZE",
        Description = "Runs SQLite VACUUM and ANALYZE for the entire database file to reclaim space and refresh query planner stats.",
        Tags = new[] { "Admin – Maintenance" }
    )]
    public async Task<IActionResult> Vacuum(CancellationToken ct = default)
    {
        await _maintenance.VacuumAnalyzeAsync(ct);
        return Ok(new { ok = true });
    }

    /// <summary>
    /// Hard reset: deletes rows from one or more tables, controlled by the 'scope' query.
    /// English:
    /// - scope=prices        -> wipe only daily price rows
    /// - scope=fundamentals  -> wipe income, balance, cashflow rows
    /// - scope=tickers       -> wipe tickers (also implies prices due to FK)
    /// - scope=all (default) -> wipe everything in a safe order
    /// 
    /// WARNING: destructive. Keep behind DemoMode/Authorization in production.
    /// </summary>
    [HttpPost("truncate")]
    [SwaggerOperation(
        Summary = "Truncate database tables",
        Description = "Deletes all rows in selected tables (prices, fundamentals, tickers, or all). ⚠️ Destructive operation.",
        Tags = new[] { "Admin – Maintenance" }
    )]
    public async Task<IActionResult> Truncate(
        [FromQuery] string? scope,
        [FromServices] IConfiguration cfg,
        [FromServices] AppDbContext db,
        CancellationToken ct = default)
    {
        // Optional: protect with DemoMode like your seed endpoints
        if (!cfg.GetValue<bool>("DemoMode"))
            throw new NotFoundException("Resource not found.");

        // Normalize scope
        var s = (scope ?? "all").Trim().ToLowerInvariant();

        // English: we delete in an order that respects FK constraints.
        // Prices -> Fundamentals -> Tickers.
        var deleted = new Dictionary<string, int>();

        switch (s)
        {
            case "prices":
                {
                    // delete price rows only
                    deleted["prices"] = await db.Database.ExecuteSqlRawAsync("DELETE FROM Prices;", ct);
                    break;
                }
            case "fundamentals":
                {
                    // delete income, balance, cashflow rows
                    deleted["income_statements"] = await db.Database.ExecuteSqlRawAsync("DELETE FROM income_statements;", ct);
                    deleted["balance_sheets"] = await db.Database.ExecuteSqlRawAsync("DELETE FROM balance_sheets;", ct);
                    deleted["cash_flows"] = await db.Database.ExecuteSqlRawAsync("DELETE FROM cash_flows;", ct);
                    break;
                }
            case "tickers":
                {
                    // wipe dependent rows first, then tickers
                    deleted["prices"] = await db.Database.ExecuteSqlRawAsync("DELETE FROM Prices;", ct);
                    deleted["income_statements"] = await db.Database.ExecuteSqlRawAsync("DELETE FROM income_statements;", ct);
                    deleted["balance_sheets"] = await db.Database.ExecuteSqlRawAsync("DELETE FROM balance_sheets;", ct);
                    deleted["cash_flows"] = await db.Database.ExecuteSqlRawAsync("DELETE FROM cash_flows;", ct);
                    deleted["tickers"] = await db.Database.ExecuteSqlRawAsync("DELETE FROM Tickers;", ct);
                    break;
                }
            case "all":
            default:
                {
                    // full wipe in safe order
                    deleted["prices"] = await db.Database.ExecuteSqlRawAsync("DELETE FROM Prices;", ct);
                    deleted["income_statements"] = await db.Database.ExecuteSqlRawAsync("DELETE FROM income_statements;", ct);
                    deleted["balance_sheets"] = await db.Database.ExecuteSqlRawAsync("DELETE FROM balance_sheets;", ct);
                    deleted["cash_flows"] = await db.Database.ExecuteSqlRawAsync("DELETE FROM cash_flows;", ct);
                    deleted["tickers"] = await db.Database.ExecuteSqlRawAsync("DELETE FROM Tickers;", ct);
                    break;
                }
        }

        return Ok(new { ok = true, scope = s, deleted });
    }

    /// <summary>
    /// Diagnostics: shows DB provider, file path/connection string, and row counts per table.
    /// English: Use this to quickly inspect if tables are populated (Prices, Income, Balance, CashFlow, Tickers).
    /// Also includes a compact per-symbol fundamentals snapshot (annual) to see ROE-readiness at a glance.
    /// </summary>
    [HttpGet("info")]
    [Produces("application/json")]
    [SwaggerOperation(
        Summary = "Database diagnostics overview",
        Description = "Displays DB provider, file path, table row counts, and compact fundamentals summary per symbol.",
        Tags = new[] { "Admin – Maintenance" }
    )]
    public async Task<IActionResult> Info(
        [FromServices] AppDbContext db,
        CancellationToken ct = default)
    {
        var isSqlite = db.Database.IsSqlite();
        string dbPathOrCxn = "(unknown)";

        if (isSqlite)
        {
            var conn = Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetDbConnection(db.Database);
            dbPathOrCxn = conn?.DataSource ?? "(no data source)";
            try
            {
                if (!string.IsNullOrWhiteSpace(dbPathOrCxn) && !Path.IsPathRooted(dbPathOrCxn))
                    dbPathOrCxn = Path.GetFullPath(dbPathOrCxn, AppContext.BaseDirectory);
            }
            catch { /* ignore */ }
        }
        else
        {
            dbPathOrCxn = Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetConnectionString(db.Database)
                            ?? "(no connection string)";
        }

        // --- Row counts (quick overall health) -----------------------------------
        var totalPrices = await db.Prices.CountAsync(ct);
        var totalTickers = await db.Tickers.CountAsync(ct);
        var totalIncome = await db.IncomeStatements.CountAsync(ct);
        var totalBalance = await db.BalanceSheets.CountAsync(ct);
        var totalCashFlow = await db.CashFlows.CountAsync(ct);

        // --- Prices by symbol (range + density) ----------------------------------
        var pricesBySymbol = await db.Prices
            .GroupBy(p => p.Ticker.Symbol)
            .Select(g => new
            {
                symbol = g.Key,
                count = g.Count(),
                minDate = g.Min(x => x.TradingDate),
                maxDate = g.Max(x => x.TradingDate)
            })
            .OrderByDescending(x => x.count)
            .ToListAsync(ct);

        // --- Fundamentals snapshot (annual) --------------------------------------
        // English: For each symbol, show counts and latest period end for Income and Balance.
        // "roeReady" is true if latest annual dates exist on both sides and match (so ROE can be computed directly).
        var incomeAgg = await db.IncomeStatements.AsNoTracking()
            .Where(i => i.Frequency == "annual")
            .GroupBy(i => i.Symbol)
            .Select(g => new { symbol = g.Key, incomeCount = g.Count(), latestIncomeDate = g.Max(x => x.Date) })
            .ToListAsync(ct);

        var balanceAgg = await db.BalanceSheets.AsNoTracking()
            .Where(b => b.Frequency == "annual")
            .GroupBy(b => b.Symbol)
            .Select(g => new { symbol = g.Key, balanceCount = g.Count(), latestBalanceDate = g.Max(x => x.Date) })
            .ToListAsync(ct);

        var incomeMap = incomeAgg.ToDictionary(x => x.symbol, StringComparer.OrdinalIgnoreCase);
        var balanceMap = balanceAgg.ToDictionary(x => x.symbol, StringComparer.OrdinalIgnoreCase);
        var symbolsFund = incomeMap.Keys.Union(balanceMap.Keys, StringComparer.OrdinalIgnoreCase);

        var fundamentalsBySymbol = symbolsFund
            .OrderBy(s => s)
            .Select(s =>
            {
                incomeMap.TryGetValue(s, out var inc);
                balanceMap.TryGetValue(s, out var bal);

                var latestInc = inc?.latestIncomeDate;
                var latestBal = bal?.latestBalanceDate;
                var roeReady = latestInc.HasValue && latestBal.HasValue && latestInc.Value == latestBal.Value;

                return new
                {
                    symbol = s,
                    incomeCount = inc?.incomeCount ?? 0,
                    balanceCount = bal?.balanceCount ?? 0,
                    latestIncomeDate = latestInc,
                    latestBalanceDate = latestBal,
                    roeReady
                };
            })
            .ToList();

        return Ok(new
        {
            database = isSqlite ? "SQLite" : "Other",
            locationOrConnection = dbPathOrCxn,
            counts = new
            {
                tickers = totalTickers,
                prices = totalPrices,
                income = totalIncome,
                balance = totalBalance,
                cashflow = totalCashFlow
            },
            pricesBySymbol,
            fundamentalsBySymbol
        });
    }

    /// <summary>
    /// Ingests annual Income &amp; Balance from FMP and upserts into DB.
    /// English: real-data equivalent of our seed endpoints. Respects FMP free-tier limit.
    /// </summary>
    [HttpPost("ingest/fmp-annual")]
    [SwaggerOperation(
        Summary = "Ingest FMP annual fundamentals",
        Description = "Fetches annual Income & Balance data from FMP and upserts them into the database.",
        Tags = new[] { "Admin – Data Ingest" }
    )]
    public async Task<IActionResult> IngestFmpAnnual(
        [FromQuery] string symbol,
        [FromServices] IncomeIngestService incomeIngest,
        [FromServices] BalanceSheetIngestService balanceIngest,
        [FromQuery] int limit = 5,               // English: FMP low tiers allow up to 5 rows
        CancellationToken ct = default)
    {
        Guard.BadRequestIf(string.IsNullOrWhiteSpace(symbol), "Missing ?symbol=...");

        var ticker = symbol.Trim().ToUpperInvariant();

        try
        {
            // English: pass limit explicitly to avoid 402 on low-tier plans
            await incomeIngest.IngestAsync(ticker, "annual", limit, ct);
            await balanceIngest.IngestAsync(ticker, "annual", limit, ct);

            return Ok(new { ticker, limit, ok = true });
        }
        catch (HttpRequestException ex)
        {
            // English: surface upstream error without stack trace
            return StatusCode(502, new { ticker, error = "FMP request failed", detail = ex.Message });
        }
    }

    /// <summary>
    /// Ingests annual Cash Flow statements from FMP and upserts into DB.
    /// English: complements income &amp; balance so we can compute FCF.
    /// </summary>
    [HttpPost("ingest/fmp-cashflow-annual")]
    [SwaggerOperation(
        Summary = "Ingest FMP annual cash flow",
        Description = "Fetches annual Cash Flow data from FMP and upserts them into the database.",
        Tags = new[] { "Admin – Data Ingest" }
    )]
    public async Task<IActionResult> IngestFmpCashflowAnnual(
        [FromQuery] string symbol,
        [FromServices] CashFlowIngestService cashflowIngest,
        [FromQuery] int limit = 5,               // English: FMP low tiers allow up to ~5 rows
        CancellationToken ct = default)
    {
        Guard.BadRequestIf(string.IsNullOrWhiteSpace(symbol), "Missing ?symbol=...");

        var ticker = symbol.Trim().ToUpperInvariant();

        try
        {
            // English: ask service to fetch+upsert annual cashflows
            await cashflowIngest.IngestAsync(ticker, "annual", limit, ct);
            return Ok(new { ticker, limit, ok = true });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, new { ticker, error = "FMP request failed", detail = ex.Message });
        }
    }

    // Request shape for bulk upsert (symbol required; name/sector optional)
    public sealed class UpsertTickerDto
    {
        public string? Symbol { get; init; }
        public string? Name { get; init; }
        public string? Sector { get; init; }
    }

    /// <summary>
    /// Bulk upsert tickers. If overwrite=false (default), only fill missing fields;
    /// with overwrite=true, replace existing Name/Sector as well.
    /// </summary>
    [HttpPost("tickers/upsert")]
    [SwaggerOperation(
        Summary = "Bulk upsert tickers",
        Description = "Upserts multiple tickers; optionally overwrites existing Name and Sector values.",
        Tags = new[] { "Admin – Tickers" }
    )]
    public async Task<IActionResult> UpsertTickers(
        [FromQuery] bool overwrite,
        [FromBody] List<UpsertTickerDto> items,
        CancellationToken ct = default)
    {
        if (items is null || items.Count == 0)
            throw new BadRequestException("No items provided.");

        var set = _db.Set<Ticker>();
        var affected = 0;

        foreach (var row in items)
        {
            if (string.IsNullOrWhiteSpace(row.Symbol)) continue;

            var sym = row.Symbol.Trim().ToUpperInvariant();
            var name = string.IsNullOrWhiteSpace(row.Name) ? null : row.Name.Trim();
            var sector = string.IsNullOrWhiteSpace(row.Sector) ? null : row.Sector.Trim();

            var t = await set.FirstOrDefaultAsync(x => x.Symbol == sym, ct);
            if (t is null)
            {
                _db.Add(new Ticker { Symbol = sym, Name = name, Sector = sector });
                affected++;
                continue;
            }

            // Update only when overwriting OR target field is empty and incoming has value
            if (name is not null && (overwrite || string.IsNullOrWhiteSpace(t.Name))) t.Name = name;
            if (sector is not null && (overwrite || string.IsNullOrWhiteSpace(t.Sector))) t.Sector = sector;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { affected });
    }

    // AdminController.cs
    /// <summary>
    /// Admin-only: clears the <c>Sector</c> column for all tickers by setting it to <c>NULL</c>.
    /// Intended as a destructive reset before running a fresh profile refresh.
    /// </summary>
    /// <response code="200">Returns JSON like <c>{ cleared: N }</c>.</response>
    [HttpPost("tickers/clear-sectors")]
        [SwaggerOperation(
        Summary = "Clear all ticker sectors",
        Description = "Sets the Sector column to NULL for all tickers. ⚠️ Destructive reset before reclassification.",
        Tags = new[] { "Admin – Tickers" }
    )]
    public async Task<IActionResult> ClearTickerSectors(CancellationToken ct)
    {
        var n = await _maintenance.ClearAllTickerSectorsAsync(ct);
        return Ok(new { cleared = n });
    }


    /// <summary>
    /// Admin-only: deletes a company by ticker symbol, including prices and fundamentals.
    /// English: Hard-delete all data for {symbol} in one atomic operation.
    /// </summary>
    /// <response code="200">
    /// Returns JSON: { symbol, pricesDeleted, incomeDeleted, balanceDeleted, cashDeleted, tickerDeleted }
    /// </response>
    [HttpDelete("tickers/{symbol}")]
    [SwaggerOperation(
        Summary = "Delete company by symbol",
        Description = "Deletes a ticker and all related prices and fundamentals. Operation is idempotent.",
        Tags = new[] { "Admin – Tickers" }
    )]
    public async Task<IActionResult> DeleteTicker([FromRoute] string symbol, CancellationToken ct)
    {
        var result = await _maintenance.DeleteTickerAsync(symbol, ct);

        if (result is null || result.TickerDeleted == 0)
            throw new NotFoundException($"Ticker '{symbol}' not found in database.");

        return Ok(result); // English: idempotent — returns zeros if symbol not found
    }

    /// <summary>
    /// Lists all registered users (admin-only).
    /// </summary>
    [HttpGet("users")]    
    [ProducesResponseType(StatusCodes.Status200OK)]
    [SwaggerOperation(
        Summary = "List all registered users",
        Description = "Returns all user accounts including admin flag. Admin-only access.",
        Tags = new[] { "Admin – Users" }
    )]
    public async Task<ActionResult<IEnumerable<object>>> GetAllUsers()
    {
        var users = await _db.Users
            .Select(u => new
            {
                u.Id,
                u.Username,
                u.IsAdmin
            })
            .ToListAsync();

        return Ok(users);
    }

    /// <summary>
    /// Deletes a user by ID (admin-only).
    /// Prevents an admin from deleting their own account.
    /// </summary>
    [HttpDelete("users/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [SwaggerOperation(
        Summary = "Delete user account",
        Description = "Deletes a user by ID. Prevents deleting the last admin or self-deletion.",
        Tags = new[] { "Admin – Users" }
    )]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var currentUserId = User.GetUserId();

        if (currentUserId == id)
            throw new ForbiddenException("Prevent self-deletion.");

        var user = await _db.Users.FindAsync(id);
        if (user == null)
            throw new NotFoundException("Resource not found.");

        // 👇 Prevent deletion if this is the last remaining admin
        if (user.IsAdmin)
        {
            var otherAdminsExist = await _db.Users.AnyAsync(u => u.IsAdmin && u.Id != id);
            if (!otherAdminsExist)
                throw new ForbiddenException("Cannot delete the last remaining admin.");
        }

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Promotes or demotes a user (toggles admin rights).
    /// </summary>
    /// <param name="id">User ID</param>
    /// <param name="makeAdmin">true = promote to admin, false = demote</param>
    [HttpPut("users/{id:int}/promote")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [SwaggerOperation(
        Summary = "Promote or demote a user",
        Description = "Sets admin privileges for a user. Prevents demoting the last admin or self-demotion.",
        Tags = new[] { "Admin – Users" }
    )]
    public async Task<IActionResult> SetAdminStatus(int id, [FromQuery] bool makeAdmin)
    {
        var currentUserId = User.GetUserId();

        // Prevent an admin from demoting themselves
        if (currentUserId == id && !makeAdmin)
            throw new ForbiddenException("Operation not allowed.");

        var user = await _db.Users.FindAsync(id);
        if (user == null)
            throw new NotFoundException("Resource not found.");

        // Prevent demotion of the last remaining admin
        if (user.IsAdmin && !makeAdmin)
        {
            var otherAdminsExist = await _db.Users.AnyAsync(u => u.IsAdmin && u.Id != id);
            if (!otherAdminsExist)
                throw new ForbiddenException("Operation not allowed.");
        }

        user.IsAdmin = makeAdmin;
        await _db.SaveChangesAsync();

        return Ok(new
        {
            user.Id,
            user.Username,
            user.IsAdmin
        });
    }

















    /// <summary>
    /// Seed dry-run: parse &amp; validate seed JSON; no DB writes.
    /// English: Reads SeedData/companies/{SYMBOL}.json and returns a compact summary (symbol, profile, quotes range, fundamentals snapshot).
    /// </summary>
    [HttpPost("seed/company-file/{symbol}")]
    [SwaggerOperation(
        Summary = "Validate seed file (dry-run)",
        Description = "Parses SeedData/companies/{SYMBOL}.json and returns a compact summary without writing to the DB.",
        OperationId = "Seed_ValidateCompanyFile",
        Tags = new[] { "Admin – Seed Tools" }
    )]   
    #if !DEBUG
        [ApiExplorerSettings(IgnoreApi = true)]
    #endif
    public async Task<IActionResult> SeedCompanyFileDryRun([FromRoute] string symbol, CancellationToken ct)
    {
        // English: load + validate file (no DB writes)
        var res = await _files.LoadCompanyAsync(symbol);
        if (!res.Success || res.Data is null)
            throw new BadRequestException($"Seed validation failed: {res.Error}");


        var m = res.Data;

        // English: optional fundamentals summary
        var fundamentalsSummary = (m.Fundamentals is null || m.Fundamentals.Annual.Count == 0)
            ? null
            : new
            {
                count = m.Fundamentals.Annual.Count,
                firstYear = m.Fundamentals.Annual.Min(a => a.Year),
                lastYear = m.Fundamentals.Annual.Max(a => a.Year),
                currency = m.Fundamentals.Currency
            };

        // English: brief summary for caller
        var summary = new
        {
            symbol = m.Symbol,
            name = m.Profile.Name,
            sector = m.Profile.Sector,
            quotes = new
            {
                currency = m.Quotes.Currency,
                count = m.Quotes.Rows.Count,
                firstDate = m.Quotes.Rows.Min(r => r.Date),
                lastDate = m.Quotes.Rows.Max(r => r.Date)
            },
            fundamentals = fundamentalsSummary
        };

        return Ok(new { title = "Seed file OK (dry-run)", summary });
    }  

    [HttpPost("seed/company-file/{symbol}/apply")]
    [SwaggerOperation(
        Summary = "Apply seed file to database",
        Description = "Loads SeedData/companies/{symbol}.json and inserts all data (ticker, prices, fundamentals).",
        Tags = new[] { "Admin – Seed Tools" }
    )]
    #if !DEBUG
        [ApiExplorerSettings(IgnoreApi = true)]
    #endif
    public async Task<IActionResult> SeedCompanyFileApply([FromRoute] string symbol, CancellationToken ct)
    {
        // English: 1) load + validate JSON first
        var res = await _files.LoadCompanyAsync(symbol);
        if (!res.Success || res.Data is null)
            throw new BadRequestException($"Seed validation failed: {res.Error}");

        var m = res.Data;

        // English: 2) upsert ticker profile (name + sector)
        var (created, updated) = await _seed.SeedTickerProfileAsync(
            m.Symbol, m.Profile.Name, m.Profile.Sector, ct);

        // English: 3) upsert full OHLCV for each trading day
        int priceInsertedOrUpdated = 0;
        foreach (var r in m.Quotes.Rows)
        {
            if (!DateOnly.TryParseExact(
                    r.Date, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var d))
            {
                throw new BadRequestException($"Invalid date in quotes.rows: {r.Date}");
            }

            await _seed.SeedFullPriceAsync(
                m.Symbol, d,
                r.Open, r.High, r.Low, r.Close, r.Volume, // English: write full OHLCV
                ct
            );
            priceInsertedOrUpdated++;
        }

        // English: 4) optionally apply fundamentals (annual rows)
        int yearsTouched = 0;
        int annualPairs = 0;        // years where (NetIncome + Equity) were both set via SeedAnnualAsync
        int revenueCount = 0;
        int assetsCount = 0;
        int liabilitiesCount = 0;
        int sharesCount = 0;
        int cfoCount = 0;           // Operating Cash Flow rows written
        int capexCount = 0;         // Capital Expenditures rows written

        if (m.Fundamentals?.Annual is not null && m.Fundamentals.Annual.Count > 0)
        {
            foreach (var a in m.Fundamentals.Annual)
            {
                bool touchedThisYear = false;

                // English: set revenue if provided
                if (a.Revenue.HasValue)
                {
                    await _seed.SeedRevenueAsync(m.Symbol, a.Year, a.Revenue.Value, ct);
                    revenueCount++;
                    touchedThisYear = true;
                }

                // English: set assets if provided
                if (a.TotalAssets.HasValue)
                {
                    await _seed.SeedAssetsAsync(m.Symbol, a.Year, a.TotalAssets.Value, ct);
                    assetsCount++;
                    touchedThisYear = true;
                }

                // English: set liabilities if provided
                if (a.TotalLiabilities.HasValue)
                {
                    await _seed.SeedLiabilitiesAsync(m.Symbol, a.Year, a.TotalLiabilities.Value, ct);
                    liabilitiesCount++;
                    touchedThisYear = true;
                }

                // English: set shares if provided
                if (a.Shares.HasValue)
                {
                    await _seed.SeedSharesAsync(m.Symbol, a.Year, a.Shares.Value, ct);
                    sharesCount++;
                    touchedThisYear = true;
                }

                // English: set operating cash flow if provided (negative allowed)
                if (a.OperatingCashFlow.HasValue)
                {
                    await _seed.SeedOperatingCashFlowAsync(m.Symbol, a.Year, a.OperatingCashFlow.Value, ct);
                    cfoCount++;
                    touchedThisYear = true;
                }

                // English: set capital expenditures if provided (negative allowed)
                if (a.CapitalExpenditures.HasValue)
                {
                    await _seed.SeedCapitalExpendituresAsync(m.Symbol, a.Year, a.CapitalExpenditures.Value, ct);
                    capexCount++;
                    touchedThisYear = true;
                }

                // English: only set netIncome+equity together if both are present
                if (a.NetIncome.HasValue && a.Equity.HasValue)
                {
                    await _seed.SeedAnnualAsync(m.Symbol, a.Year, a.NetIncome.Value, a.Equity.Value, ct);
                    annualPairs++;
                    touchedThisYear = true;
                }

                if (touchedThisYear) yearsTouched++;
            }
        }

        // English: 5) summary response
        return Ok(new
        {
            title = "Seed applied",
            ticker = new { created, updated },
            prices = new { count = priceInsertedOrUpdated, currency = m.Quotes.Currency },
            range = new { firstDate = m.Quotes.Rows.Min(x => x.Date), lastDate = m.Quotes.Rows.Max(x => x.Date) },
            fundamentals = (m.Fundamentals?.Annual is null || m.Fundamentals.Annual.Count == 0)
                ? null
                : new
                {
                    yearsTouched,
                    annualPairs,   // number of years where (netIncome+equity) were set
                    revenue = revenueCount,
                    assets = assetsCount,
                    liabilities = liabilitiesCount,
                    shares = sharesCount,
                    cfo = cfoCount,
                    capex = capexCount,
                    currency = m.Fundamentals.Currency
                }
        });
    }



    [HttpGet("seed/inspect/{symbol}")]
    [SwaggerOperation(
        Summary = "Inspect existing ticker data",
        Description = "Shows ticker profile and basic price range information for a given symbol.",
        Tags = new[] { "Admin – Seed Tools" }
    )]
    #if !DEBUG
        [ApiExplorerSettings(IgnoreApi = true)]
    #endif
    public async Task<IActionResult> InspectSeed([FromRoute] string symbol, CancellationToken ct)
    {
        var s = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        Guard.BadRequestIf(string.IsNullOrWhiteSpace(s), "Invalid symbol.");

        var ticker = await _db.Tickers
            .Where(t => t.Symbol == s)
            .Select(t => new { t.Id, t.Symbol, t.Name, t.Sector })
            .FirstOrDefaultAsync(ct);

        if (ticker is null)
            throw new NotFoundException($"No annual income row for {ticker}.");

        var priceInfo = await _db.Prices
            .Where(p => p.TickerId == ticker.Id)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                count = g.Count(),
                firstDate = g.Min(x => x.TradingDate), // DateOnly (non-nullable)
                lastDate = g.Max(x => x.TradingDate)  // DateOnly (non-nullable)
            })
            .FirstOrDefaultAsync(ct);

        // English: build a single anonymous object; null-safe per field
        var prices = new
        {
            count = priceInfo?.count ?? 0,
            firstDate = priceInfo?.firstDate, // becomes DateOnly?
            lastDate = priceInfo?.lastDate   // becomes DateOnly?
        };

        return Ok(new { title = "Inspect OK", ticker, prices });
    }

    // POST /api/admin/seed/ticker
    [HttpPost("seed/ticker")]
    [SwaggerOperation(
        Summary = "Seed single ticker (demo)",
        Description = "Creates or updates a ticker record. DemoMode only.",
        Tags = new[] { "Admin – Seed Tools" }
    )]
    #if !DEBUG
        [ApiExplorerSettings(IgnoreApi = true)]
    #endif
    public async Task<IActionResult> SeedTicker(
        [FromQuery] string symbol,
        [FromQuery] string? name,
        [FromServices] IConfiguration cfg,
        [FromServices] ISeedService seeder,
        CancellationToken ct = default)
    {
        if (!cfg.GetValue<bool>("DemoMode")) throw new NotFoundException("Resource not found.");
        if (string.IsNullOrWhiteSpace(symbol)) 
            Guard.BadRequestIf(string.IsNullOrWhiteSpace(symbol), "Query ?symbol=... is required.");

        var (created, updated) = await seeder.SeedTickerAsync(symbol, name, ct);
        return Ok(new { ticker = symbol.Trim().ToUpperInvariant(), created, updated });
    }

    // POST /api/admin/seed/annual
    [HttpPost("seed/annual")]
    [SwaggerOperation(
        Summary = "Seed annual data (demo)",
        Description = "Sets net income and equity for one year. DemoMode only.",
        Tags = new[] { "Admin – Seed Tools" }
    )]
    #if !DEBUG
        [ApiExplorerSettings(IgnoreApi = true)]
    #endif
    public async Task<IActionResult> SeedAnnual(
        [FromQuery] string symbol,
        [FromQuery] int year,
        [FromQuery] long netIncome,
        [FromQuery] long equity,
        [FromServices] IConfiguration cfg,
        [FromServices] ISeedService seeder,
        CancellationToken ct = default)
    {
        if (year < 1900 || year > DateTime.UtcNow.Year)
            throw new BadRequestException($"Invalid year '{year}' — out of range.");


        if (string.IsNullOrWhiteSpace(symbol))
            Guard.BadRequestIf(string.IsNullOrWhiteSpace(symbol), "Missing ?symbol=...");

        if (!cfg.GetValue<bool>("DemoMode")) throw new NotFoundException("Resource not found.");
        if (string.IsNullOrWhiteSpace(symbol)) 
            Guard.BadRequestIf(string.IsNullOrWhiteSpace(symbol), "Missing ?symbol=...");

        await seeder.SeedAnnualAsync(symbol, year, netIncome, equity, ct);
        return Ok(new { ticker = symbol.Trim().ToUpperInvariant(), year, netIncome, equity });
    }

    // POST /api/admin/seed/liabilities
    [HttpPost("seed/liabilities")]
    [SwaggerOperation(
        Summary = "Seed liabilities (demo)",
        Description = "Adds or updates total liabilities for one year. DemoMode only.",
        Tags = new[] { "Admin – Seed Tools" }
    )]
    #if !DEBUG
        [ApiExplorerSettings(IgnoreApi = true)]
    #endif
    public async Task<IActionResult> SeedLiabilities(
        [FromQuery] string symbol,
        [FromQuery] int year,
        [FromQuery] long totalLiabilities,
        [FromServices] IConfiguration cfg,
        [FromServices] ISeedService seeder,
        CancellationToken ct = default)
    {
        if (!cfg.GetValue<bool>("DemoMode")) throw new NotFoundException("Resource not found.");
        if (string.IsNullOrWhiteSpace(symbol)) 
            Guard.BadRequestIf(string.IsNullOrWhiteSpace(symbol), "Missing ?symbol=...");

        await seeder.SeedLiabilitiesAsync(symbol, year, totalLiabilities, ct);
        return Ok(new { ticker = symbol.Trim().ToUpperInvariant(), year, totalLiabilities });
    }

    // POST /api/admin/seed/revenue
    [HttpPost("seed/revenue")]
    [SwaggerOperation(
        Summary = "Seed revenue (demo)",
        Description = "Adds or updates revenue for one year. DemoMode only.",
        Tags = new[] { "Admin – Seed Tools" }
    )]
    #if !DEBUG
        [ApiExplorerSettings(IgnoreApi = true)]
    #endif
    public async Task<IActionResult> SeedRevenue(
        [FromQuery] string symbol,
        [FromQuery] int year,
        [FromQuery] long revenue,
        [FromServices] IConfiguration cfg,
        [FromServices] ISeedService seeder,
        CancellationToken ct = default)
    {
        if (!cfg.GetValue<bool>("DemoMode")) throw new NotFoundException("Resource not found.");
        if (string.IsNullOrWhiteSpace(symbol)) 
            Guard.BadRequestIf(string.IsNullOrWhiteSpace(symbol), "Missing ?symbol=...");

        await seeder.SeedRevenueAsync(symbol, year, revenue, ct);
        return Ok(new { ticker = symbol.Trim().ToUpperInvariant(), year, revenue });
    }

    // POST /api/admin/seed/assets
    [HttpPost("seed/assets")]
    [SwaggerOperation(
        Summary = "Seed assets (demo)",
        Description = "Adds or updates total assets for one year. DemoMode only.",
        Tags = new[] { "Admin – Seed Tools" }
    )]
    #if !DEBUG
        [ApiExplorerSettings(IgnoreApi = true)]
    #endif
    public async Task<IActionResult> SeedAssets(
        [FromQuery] string symbol,
        [FromQuery] int year,
        [FromQuery] long totalAssets,
        [FromServices] IConfiguration cfg,
        [FromServices] ISeedService seeder,
        CancellationToken ct = default)
    {
        if (!cfg.GetValue<bool>("DemoMode")) throw new NotFoundException("Resource not found.");
        if (string.IsNullOrWhiteSpace(symbol)) 
            Guard.BadRequestIf(string.IsNullOrWhiteSpace(symbol), "Missing ?symbol=...");

        await seeder.SeedAssetsAsync(symbol, year, totalAssets, ct);
        return Ok(new { ticker = symbol.Trim().ToUpperInvariant(), year, totalAssets });
    }

    // POST /api/admin/seed/price
    [HttpPost("seed/price")]
    [SwaggerOperation(
        Summary = "Seed price (demo)",
        Description = "Sets a closing price for a specific date. DemoMode only.",
        Tags = new[] { "Admin – Seed Tools" }
    )]
    #if !DEBUG
        [ApiExplorerSettings(IgnoreApi = true)]
    #endif
    public async Task<IActionResult> SeedPrice(
        [FromQuery] string symbol,
        [FromQuery] DateOnly date,
        [FromQuery] decimal close,
        [FromServices] IConfiguration cfg,
        [FromServices] ISeedService seeder,
        CancellationToken ct = default)
    {
        if (!cfg.GetValue<bool>("DemoMode")) throw new NotFoundException("Resource not found.");
        if (string.IsNullOrWhiteSpace(symbol)) 
            Guard.BadRequestIf(string.IsNullOrWhiteSpace(symbol), "Missing ?symbol=...");

        await seeder.SeedPriceAsync(symbol, date, close, ct);
        return Ok(new { ticker = symbol.Trim().ToUpperInvariant(), date, close });
    }

    // POST /api/admin/seed/shares
    [HttpPost("seed/shares")]
    [SwaggerOperation(
        Summary = "Seed shares (demo)",
        Description = "Sets outstanding shares for a given year. DemoMode only.",
        Tags = new[] { "Admin – Seed Tools" }
    )]
    #if !DEBUG
        [ApiExplorerSettings(IgnoreApi = true)]
    #endif
    public async Task<IActionResult> SeedShares(
        [FromQuery] string symbol,
        [FromQuery] int year,
        [FromQuery] long shares,
        [FromServices] IConfiguration cfg,
        [FromServices] ISeedService seeder,
        CancellationToken ct = default)
    {
        if (!cfg.GetValue<bool>("DemoMode")) throw new NotFoundException("Resource not found.");
        if (string.IsNullOrWhiteSpace(symbol)) 
            Guard.BadRequestIf(string.IsNullOrWhiteSpace(symbol), "Missing ?symbol=...");

        await seeder.SeedSharesAsync(symbol, year, shares, ct);
        return Ok(new { ticker = symbol.Trim().ToUpperInvariant(), year, shares });
    }

}
