using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.DTOs;
using Portfolio.Api.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Portfolio.Api.Controllers;

/// <summary>
/// Provides portfolio-level analytics for the authenticated user.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class PortfolioAnalyticsController : ControllerBase
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
    /// Returns a portfolio summary for the currently authenticated user.
    /// </summary>
    /// <remarks>
    /// The response includes cash balance, portfolio value, total value, and holdings.
    /// </remarks>
    [HttpGet("summary")]
    [Produces("application/json")]
    [SwaggerOperation(
        Summary = "Get portfolio summary",
        Description = "Returns cash balance, portfolio value, total value, and holdings for the logged-in user.")]
    [ProducesResponseType(typeof(PortfolioSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PortfolioSummaryDto>> GetPortfolioSummary()
    {
        if (!TryGetCurrentUserId(out int userId))
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Detail = "Invalid or missing user ID claim."
            });
        }

        try
        {
            PortfolioSummaryDto? summary = await _portfolioService.GetPortfolioSummaryAsync(userId);

            if (summary == null)
            {
                return NotFound(new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "User not found",
                    Detail = $"No portfolio data found for user ID {userId}."
                });
            }

            return Ok(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving portfolio summary for user {UserId}", userId);

            return Problem(
                title: "Portfolio summary failed",
                detail: "An unexpected error occurred while processing the request.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Returns all recorded buy and sell transactions for the authenticated user.
    /// </summary>
    [HttpGet("transactions")]
    [Produces("application/json")]
    [SwaggerOperation(
        Summary = "Get transactions",
        Description = "Returns the full buy and sell transaction history for the logged-in user.")]
    [ProducesResponseType(typeof(List<TransactionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<TransactionDto>>> GetTransactions()
    {
        if (!TryGetCurrentUserId(out int userId))
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Detail = "Invalid or missing user ID claim."
            });
        }

        try
        {
            List<TransactionDto> transactions = await _portfolioService.GetTransactionsAsync(userId);

            return Ok(transactions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving transactions for user {UserId}", userId);

            return Problem(
                title: "Transactions query failed",
                detail: "An unexpected error occurred while processing the request.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private bool TryGetCurrentUserId(out int userId)
    {
        string? userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return int.TryParse(userIdClaim, out userId);
    }
}
