using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.DTOs;
using Portfolio.Api.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Portfolio.Api.Controllers;

/// <summary>
/// Provides aggregated portfolio-level analytics for the authenticated user.
/// Combines cash balance, holdings value, and total portfolio overview.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PortfolioAnalyticsController : ControllerBase
{
    private readonly PortfolioAnalyticsService _portfolioService;
    private readonly ILogger<PortfolioAnalyticsController> _logger;

    public PortfolioAnalyticsController(
        PortfolioAnalyticsService portfolioService,
        ILogger<PortfolioAnalyticsController> logger)
    {
        _portfolioService = portfolioService;
        _logger = logger;
    }

    /// <summary>
    /// Returns a summarized portfolio view for the currently authenticated user.
    /// </summary>
    /// <remarks>
    /// Example:
    /// <br/>GET <c>/api/PortfolioAnalytics/summary</c>
    /// <br/><br/>
    /// The response includes:
    /// <br/>• Cash balance (USD)
    /// <br/>• Portfolio value (USD)
    /// <br/>• Combined total value
    /// <br/>• Holdings list (symbol, name, shares, etc.)
    /// </remarks>
    [HttpGet("summary")]
    [Produces("application/json")]
    [SwaggerOperation(
        Summary = "Get aggregated portfolio summary",
        Description = "Returns cash balance, portfolio value, and detailed holdings for the logged-in user.")]
    [ProducesResponseType(typeof(PortfolioSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PortfolioSummaryDto>> GetPortfolioSummary()
    {
        try
        {
            // --- Extract userId from JWT ---
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                return Unauthorized(new ProblemDetails
                {
                    Status = 401,
                    Title = "Unauthorized",
                    Detail = "Invalid or missing user ID claim."
                });

            // --- Delegate to service ---
            var summary = await _portfolioService.GetPortfolioSummaryAsync(userId);
            if (summary == null)
                return NotFound(new ProblemDetails
                {
                    Status = 404,
                    Title = "User not found",
                    Detail = $"No portfolio data found for user ID {userId}."
                });

            return Ok(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving portfolio summary.");
            return StatusCode(500, new ProblemDetails
            {
                Status = 500,
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred while processing the request."
            });
        }
    }

    /// <summary>
    /// Returns all recorded buy/sell transactions for the authenticated user.
    /// </summary>
    /// <remarks>
    /// Example:
    /// <br/>GET <c>/api/PortfolioAnalytics/transactions</c>
    /// <br/><br/>
    /// Returns the user's complete transaction history with ticker info.
    /// </remarks>
    [HttpGet("transactions")]
    [Produces("application/json")]
    [SwaggerOperation(
        Summary = "Get all transactions for current user",
        Description = "Returns the full buy/sell history for the logged-in user with ticker info.")]
    [ProducesResponseType(typeof(List<TransactionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<TransactionDto>>> GetTransactions()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var transactions = await _portfolioService.GetTransactionsAsync(userId);
        return Ok(transactions);
    }

}
