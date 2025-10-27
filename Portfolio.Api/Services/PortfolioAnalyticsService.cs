using Microsoft.EntityFrameworkCore;
using Portfolio.Api.Data;
using Portfolio.Api.DTOs;

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
                Shares = uc.Shares ?? 0,
                PurchasePriceUSD = uc.PurchasePrice,
                CurrentPriceUSD = latestPrice,
            };
        }).ToList();

        // Step 5: Compute total portfolio value
        var portfolioValue = holdingDtos.Sum(h => h.CurrentValueUSD);

        // Step 6: Build summary DTO
        var summary = new PortfolioSummaryDto
        {
            CashBalance = user.CashBalance,
            PortfolioValue = portfolioValue,
            Holdings = holdingDtos
        };

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
