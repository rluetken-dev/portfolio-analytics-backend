using Microsoft.EntityFrameworkCore;
using Portfolio.Api.Data;
using Portfolio.Api.DTOs;
using Portfolio.Api.Services.Analytics;

namespace Portfolio.Api.Services;

/// <summary>
/// Provides business logic for aggregating and analyzing
/// user portfolio data such as holdings, cash, and performance.
/// </summary>
public class PortfolioAnalyticsService
{
    private readonly AppDbContext _context;
    private readonly ILogger<PortfolioAnalyticsService> _logger;

    public PortfolioAnalyticsService(AppDbContext context, ILogger<PortfolioAnalyticsService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Builds a complete portfolio summary for the specified user.
    /// Includes cash balance, holdings with latest prices, and total portfolio value.
    /// </summary>
    /// <param name="userId">The ID of the user whose portfolio should be summarized.</param>
    /// <returns>A populated <see cref="PortfolioSummaryDto"/> with current portfolio data.</returns>
    public async Task<PortfolioSummaryDto?> GetPortfolioSummaryAsync(int userId)
    {
        // Step 1: Load user (cash balance)
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            _logger.LogWarning("User {UserId} not found when building portfolio summary.", userId);
            return null;
        }

        // Step 2: Load holdings (with ticker info)
        var holdings = await _context.UserCompanies
            .AsNoTracking()
            .Include(uc => uc.Ticker)
            .Where(uc => uc.UserId == userId)
            .ToListAsync();

        // Step 3: Fetch latest adjusted close for all tickers
        var tickerIds = holdings.Select(h => h.TickerId).ToList();

        var latestPrices = await _context.Prices
            .AsNoTracking()
            .Where(p => tickerIds.Contains(p.TickerId))
            .GroupBy(p => p.TickerId)
            .Select(g => g.OrderByDescending(p => p.TradingDate).FirstOrDefault())
            .ToListAsync();

        // Step 4: Map holdings into DTOs
        var holdingDtos = holdings.Select(uc =>
        {
            var tickerId = uc.TickerId;
            var price = latestPrices
                .Where(p => p != null)
                .FirstOrDefault(p => p!.TickerId == uc.TickerId);
            var latestPrice = price?.AdjustedClose ?? 0m;

            return new HoldingDto
            {
                TickerId = uc.TickerId,
                Symbol = uc.Ticker.Symbol,
                CompanyName = uc.Ticker.Name ?? string.Empty,
                Shares = uc.Shares,
                PurchasePriceUSD = uc.PurchasePrice,
                CurrentPriceUSD = latestPrice,
            };
        }).ToList();

        // Step 5: Enrich holdings with average buy price and unrealized P/L
        foreach (var holding in holdingDtos)
        {
            var txs = await _context.UserCompanyTransactions
                .AsNoTracking()
                .Where(t => t.UserId == userId && t.TickerId == holding.TickerId)
                .ToListAsync();

            if (!txs.Any() || holding.Shares <= 0)
                continue;

            // Compute average buy price (USD)
            var avgBuy = FinanceMath.CalculateAverageBuyPrice(txs);
            holding.AvgBuyPriceUSD = avgBuy;

            // Compute unrealized profit/loss (USD + %)
            if (holding.CurrentPriceUSD.HasValue)
            {
                holding.UnrealizedPLUSD = FinanceMath.CalculateUnrealizedPLUSD(
                    holding.CurrentPriceUSD,
                    avgBuy,
                    holding.Shares
                );

                holding.UnrealizedPLPercent = FinanceMath.CalculateUnrealizedPLPercent(
                    holding.CurrentPriceUSD.Value,
                    avgBuy
                );
            }
        }

        // Step 6: Calculate realized profit/loss using FIFO
        foreach (var holding in holdingDtos)
        {
            var txs = await _context.UserCompanyTransactions
                .AsNoTracking()
                .Where(t => t.UserId == userId && t.TickerId == holding.TickerId)
                .OrderBy(t => t.CreatedAt)
                .ToListAsync();

            if (!txs.Any())
                continue;

            // Compute realized P/L using FIFO logic
            var realizedPL = FinanceMath.CalculateRealizedPLFIFO(txs);
            holding.RealizedPLUSD = realizedPL;
        }

        // Step 7: Calculate realized P/L percentage
        foreach (var holding in holdingDtos)
        {
            var txs = await _context.UserCompanyTransactions
                .AsNoTracking()
                .Where(t => t.UserId == userId && t.TickerId == holding.TickerId)
                .OrderBy(t => t.CreatedAt)
                .ToListAsync();

            if (!txs.Any())
                continue;

            // Compute realized P/L using FIFO logic
            var realizedPL = FinanceMath.CalculateRealizedPLFIFO(txs);
            holding.RealizedPLUSD = realizedPL;

            // Calculate realized P/L percentage
            var realizedPercent = FinanceMath.CalculateRealizedPLPercentFIFO(txs);
            holding.RealizedPLPercent = realizedPercent;
        }

        // Step 8: Compute total portfolio value
        var portfolioValue = holdingDtos.Sum(h => h.CurrentValueUSD ?? 0m);

        // Step 9: Build summary DTO
        var summary = new PortfolioSummaryDto
        {
            CashBalance = user.CashBalance,
            PortfolioValue = portfolioValue,
            Holdings = holdingDtos
        };

        // Step 10: Calculate aggregated profit/loss metrics
        var realizedTotal = holdingDtos
            .Where(h => h.RealizedPLUSD.HasValue)
            .Sum(h => h.RealizedPLUSD ?? 0m);

        var unrealizedTotal = holdingDtos
            .Where(h => h.UnrealizedPLUSD.HasValue)
            .Sum(h => h.UnrealizedPLUSD ?? 0m);

        var totalPL = realizedTotal + unrealizedTotal;

        // Estimate total invested capital (sum of all buys)
        var totalInvested = await _context.UserCompanyTransactions
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.Shares > 0 && t.Price.HasValue)
            .SumAsync(t => (decimal)t.Shares * (t.Price ?? 0m));

        decimal? totalPLPercent = totalInvested > 0
            ? (totalPL / totalInvested) * 100
            : null;

        // Assign to summary DTO
        summary.RealizedPLTotalUSD = realizedTotal;
        summary.UnrealizedPLTotalUSD = unrealizedTotal;
        summary.TotalProfitLossUSD = totalPL;
        summary.TotalProfitLossPercent = totalPLPercent;

        _logger.LogInformation("Built portfolio summary for user {UserId}. Total value: {TotalUSD}",
            userId, summary.TotalValue);

        return summary;
    }

    /// <summary>
    /// Retrieves all transactions for the specified user,
    /// including ticker information for each record.
    /// </summary>
    /// <param name="userId">The ID of the authenticated user.</param>
    /// <returns>A list of TransactionDto entries.</returns>
    public async Task<List<TransactionDto>> GetTransactionsAsync(int userId)
    {
        try
        {
            // Step 1: Load all transactions (joined with ticker info)
            var transactions = await _context.UserCompanyTransactions
                .AsNoTracking()
                .Include(t => t.Ticker)
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            // Step 2: Map entities to DTOs
            var dtos = transactions.Select(t => new TransactionDto
            {
                CreatedAt = t.CreatedAt,
                Shares = t.Shares,
                Price = t.Price,
                Notes = t.Notes,
                Symbol = t.Ticker?.Symbol,
                CompanyName = t.Ticker?.Name
            }).ToList();

            _logger.LogInformation("Fetched {Count} transactions for user {UserId}.", dtos.Count, userId);
            return dtos;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching transactions for user {UserId}.", userId);
            return new List<TransactionDto>();
        }
    }

}
