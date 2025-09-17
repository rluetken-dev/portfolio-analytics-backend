using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Portfolio.Api.Services;
using Portfolio.Api.Data;
using Microsoft.EntityFrameworkCore;
using Portfolio.Api.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace Portfolio.Api.Controllers;

/// <summary>
/// Administrative endpoints for database housekeeping.
/// ⚠️ Important: Secure these endpoints (authentication/authorization) in production.
/// </summary>
[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly MaintenanceService _maintenance;

    /// <summary>Inject EF Core context and housekeeping service.</summary>
    public AdminController(AppDbContext db, MaintenanceService maintenance)
    {
        _db = db;
        _maintenance = maintenance;
    }

    /// <summary>
    /// Prunes daily price rows only (does not touch fundamentals).
    /// English: Deletes old price rows based on maxAgeDays and/or caps rows per symbol.
    /// </summary>
    [HttpPost("prune")]
    public async Task<IActionResult> Prune(
        [FromQuery] int? maxAgeDays = 3 * 365,
        [FromQuery] int? keepPerSymbol = null,
        CancellationToken ct = default)
    {
        if (maxAgeDays is < 0 || keepPerSymbol is < 0)
            return BadRequest(new { error = "Parameters must be non-negative." });

        var deleted = await _maintenance.PruneAsync(maxAgeDays, keepPerSymbol, ct);
        return Ok(new { ok = true, deleted });
    }

    /// <summary>
    /// Runs SQLite VACUUM and ANALYZE for the entire database file (not only Prices).
    /// English: Reclaims free space and refreshes query planner statistics.
    /// Should be run after large deletes/truncates.
    /// </summary>
    [HttpPost("vacuum")]
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
    public async Task<IActionResult> Truncate(
        [FromQuery] string? scope,
        [FromServices] IConfiguration cfg,
        [FromServices] AppDbContext db,
        CancellationToken ct = default)
    {
        // Optional: protect with DemoMode like your seed endpoints
        if (!cfg.GetValue<bool>("DemoMode"))
            return NotFound();

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
    public async Task<IActionResult> IngestFmpAnnual(
        [FromQuery] string symbol,
        [FromServices] IncomeIngestService incomeIngest,
        [FromServices] BalanceSheetIngestService balanceIngest,
        [FromQuery] int limit = 5,               // English: FMP low tiers allow up to 5 rows
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return BadRequest(new { error = "Missing ?symbol=..." });

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
    public async Task<IActionResult> IngestFmpCashflowAnnual(
        [FromQuery] string symbol,
        [FromServices] CashFlowIngestService cashflowIngest,
        [FromQuery] int limit = 5,               // English: FMP low tiers allow up to ~5 rows
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return BadRequest(new { error = "Missing ?symbol=..." });

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
    public async Task<IActionResult> UpsertTickers(
        [FromQuery] bool overwrite,
        [FromBody] List<UpsertTickerDto> items,
        CancellationToken ct = default)
    {
        if (items is null || items.Count == 0) return BadRequest("No items.");

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
    public async Task<IActionResult> ClearTickerSectors(CancellationToken ct)
    {
        var n = await _maintenance.ClearAllTickerSectorsAsync(ct);
        return Ok(new { cleared = n });
    }


    // AdminController.cs (Ergänzung)
    /// <summary>
    /// Admin-only: deletes a company by ticker symbol, including prices and fundamentals.
    /// English: Hard-delete all data for {symbol} in one atomic operation.
    /// </summary>
    /// <response code="200">
    /// Returns JSON: { symbol, pricesDeleted, incomeDeleted, balanceDeleted, cashDeleted, tickerDeleted }
    /// </response>
    [HttpDelete("tickers/{symbol}")]
    public async Task<IActionResult> DeleteTicker([FromRoute] string symbol, CancellationToken ct)
    {
        var result = await _maintenance.DeleteTickerAsync(symbol, ct);
        return Ok(result); // English: idempotent — returns zeros if symbol not found
    }


















    // POST /api/admin/seed/ticker
    [HttpPost("seed/ticker")]
    public async Task<IActionResult> SeedTicker(
        [FromQuery] string symbol,
        [FromQuery] string? name,
        [FromServices] IConfiguration cfg,
        [FromServices] ISeedService seeder,
        CancellationToken ct = default)
    {
        if (!cfg.GetValue<bool>("DemoMode")) return NotFound();
        if (string.IsNullOrWhiteSpace(symbol)) return BadRequest(new { error = "Query ?symbol=... is required." });

        var (created, updated) = await seeder.SeedTickerAsync(symbol, name, ct);
        return Ok(new { ticker = symbol.Trim().ToUpperInvariant(), created, updated });
    }

    // POST /api/admin/seed/annual
    [HttpPost("seed/annual")]
    public async Task<IActionResult> SeedAnnual(
        [FromQuery] string symbol,
        [FromQuery] int year,
        [FromQuery] long netIncome,
        [FromQuery] long equity,
        [FromServices] IConfiguration cfg,
        [FromServices] ISeedService seeder,
        CancellationToken ct = default)
    {
        if (!cfg.GetValue<bool>("DemoMode")) return NotFound();
        if (string.IsNullOrWhiteSpace(symbol)) return BadRequest(new { error = "Missing ?symbol=..." });

        await seeder.SeedAnnualAsync(symbol, year, netIncome, equity, ct);
        return Ok(new { ticker = symbol.Trim().ToUpperInvariant(), year, netIncome, equity });
    }

    // POST /api/admin/seed/liabilities
    [HttpPost("seed/liabilities")]
    public async Task<IActionResult> SeedLiabilities(
        [FromQuery] string symbol,
        [FromQuery] int year,
        [FromQuery] long totalLiabilities,
        [FromServices] IConfiguration cfg,
        [FromServices] ISeedService seeder,
        CancellationToken ct = default)
    {
        if (!cfg.GetValue<bool>("DemoMode")) return NotFound();
        if (string.IsNullOrWhiteSpace(symbol)) return BadRequest(new { error = "Missing ?symbol=..." });

        await seeder.SeedLiabilitiesAsync(symbol, year, totalLiabilities, ct);
        return Ok(new { ticker = symbol.Trim().ToUpperInvariant(), year, totalLiabilities });
    }

    // POST /api/admin/seed/revenue
    [HttpPost("seed/revenue")]
    public async Task<IActionResult> SeedRevenue(
        [FromQuery] string symbol,
        [FromQuery] int year,
        [FromQuery] long revenue,
        [FromServices] IConfiguration cfg,
        [FromServices] ISeedService seeder,
        CancellationToken ct = default)
    {
        if (!cfg.GetValue<bool>("DemoMode")) return NotFound();
        if (string.IsNullOrWhiteSpace(symbol)) return BadRequest(new { error = "Missing ?symbol=..." });

        await seeder.SeedRevenueAsync(symbol, year, revenue, ct);
        return Ok(new { ticker = symbol.Trim().ToUpperInvariant(), year, revenue });
    }

    // POST /api/admin/seed/assets
    [HttpPost("seed/assets")]
    public async Task<IActionResult> SeedAssets(
        [FromQuery] string symbol,
        [FromQuery] int year,
        [FromQuery] long totalAssets,
        [FromServices] IConfiguration cfg,
        [FromServices] ISeedService seeder,
        CancellationToken ct = default)
    {
        if (!cfg.GetValue<bool>("DemoMode")) return NotFound();
        if (string.IsNullOrWhiteSpace(symbol)) return BadRequest(new { error = "Missing ?symbol=..." });

        await seeder.SeedAssetsAsync(symbol, year, totalAssets, ct);
        return Ok(new { ticker = symbol.Trim().ToUpperInvariant(), year, totalAssets });
    }

    // POST /api/admin/seed/price
    [HttpPost("seed/price")]
    public async Task<IActionResult> SeedPrice(
        [FromQuery] string symbol,
        [FromQuery] DateOnly date,
        [FromQuery] decimal close,
        [FromServices] IConfiguration cfg,
        [FromServices] ISeedService seeder,
        CancellationToken ct = default)
    {
        if (!cfg.GetValue<bool>("DemoMode")) return NotFound();
        if (string.IsNullOrWhiteSpace(symbol)) return BadRequest(new { error = "Missing ?symbol=..." });

        await seeder.SeedPriceAsync(symbol, date, close, ct);
        return Ok(new { ticker = symbol.Trim().ToUpperInvariant(), date, close });
    }

    // POST /api/admin/seed/shares
    [HttpPost("seed/shares")]
    public async Task<IActionResult> SeedShares(
        [FromQuery] string symbol,
        [FromQuery] int year,
        [FromQuery] long shares,
        [FromServices] IConfiguration cfg,
        [FromServices] ISeedService seeder,
        CancellationToken ct = default)
    {
        if (!cfg.GetValue<bool>("DemoMode")) return NotFound();
        if (string.IsNullOrWhiteSpace(symbol)) return BadRequest(new { error = "Missing ?symbol=..." });

        await seeder.SeedSharesAsync(symbol, year, shares, ct);
        return Ok(new { ticker = symbol.Trim().ToUpperInvariant(), year, shares });
    }

}
