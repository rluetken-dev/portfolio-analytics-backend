using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Api.Data;
using Portfolio.Api.DTOs;
using Portfolio.Api.Exceptions;
using Portfolio.Api.Models;
using Portfolio.Api.Services;

namespace Portfolio.Api.Controllers;

/// <summary>
/// API controller for managing the authenticated user's portfolio entries.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UserCompanyController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly FmpClient _fmp;
    private readonly ILogger<UserCompanyController> _logger;

    public UserCompanyController(
        AppDbContext context,
        FmpClient fmp,
        ILogger<UserCompanyController> logger)
    {
        _context = context;
        _fmp = fmp;
        _logger = logger;
    }

    /// <summary>
    /// Deletes a portfolio entry belonging to the current user.
    /// </summary>
    /// <param name="id">The ID of the portfolio entry to delete.</param>
    /// <response code="204">Portfolio entry deleted successfully.</response>
    /// <response code="401">User is not authorized.</response>
    /// <response code="403">Portfolio entry belongs to another user.</response>
    /// <response code="404">Portfolio entry was not found.</response>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteUserCompany(int id)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (userId == null)
        {
            throw new UnauthorizedException("User is not authorized.");
        }

        var uid = int.Parse(userId);

        var userCompany = await _context.UserCompanies.FindAsync(id);

        if (userCompany == null)
        {
            throw new NotFoundException("Portfolio entry not found.");
        }

        if (userCompany.UserId != uid)
        {
            throw new ForbiddenException("Operation not allowed.");
        }

        _context.UserCompanies.Remove(userCompany);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Updates an existing portfolio entry for the current user.
    /// </summary>
    /// <param name="id">The ID of the portfolio entry to update.</param>
    /// <param name="dto">The updated shares, purchase price, and notes.</param>
    /// <response code="200">Portfolio entry updated successfully.</response>
    /// <response code="401">User is not authorized.</response>
    /// <response code="403">Portfolio entry belongs to another user.</response>
    /// <response code="404">Portfolio entry was not found.</response>
    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserCompanyDto>> UpdateUserCompany(
        int id,
        [FromBody] UpdateUserCompanyDto dto)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (userId == null)
        {
            throw new UnauthorizedException("User is not authorized.");
        }

        var uid = int.Parse(userId);

        var userCompany = await _context.UserCompanies
            .Include(userCompany => userCompany.Ticker)
            .FirstOrDefaultAsync(userCompany => userCompany.Id == id);

        if (userCompany == null)
        {
            throw new NotFoundException("Portfolio entry not found.");
        }

        if (userCompany.UserId != uid)
        {
            throw new ForbiddenException("Operation not allowed.");
        }

        if (dto.Shares.HasValue)
        {
            userCompany.Shares = dto.Shares.Value;
        }

        if (dto.PurchasePrice.HasValue)
        {
            userCompany.PurchasePrice = dto.PurchasePrice;
        }

        if (dto.Notes != null)
        {
            userCompany.Notes = dto.Notes;
        }

        await _context.SaveChangesAsync();

        return Ok(ToDto(userCompany));
    }

    /// <summary>
    /// Returns all portfolio entries for all users. Admin access required.
    /// </summary>
    /// <response code="200">Returns all user-company mappings with user info.</response>
    /// <response code="403">Current user is not an admin.</response>
    [Authorize(Policy = "AdminOnly")]
    [HttpGet("/api/admin/usercompanies")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<object>>> GetAllUserCompaniesForAdmin()
    {
        var entries = await _context.UserCompanies
            .Include(userCompany => userCompany.Ticker)
            .Include(userCompany => userCompany.User)
            .Select(userCompany => new
            {
                userCompany.Id,
                Username = userCompany.User.Username,
                Ticker = new
                {
                    userCompany.Ticker.Symbol,
                    userCompany.Ticker.Name,
                    userCompany.Ticker.Sector
                },
                userCompany.Shares,
                userCompany.PurchasePrice,
                userCompany.Notes
            })
            .ToListAsync();

        return Ok(entries);
    }

    /// <summary>
    /// Removes a company from the current user's portfolio.
    /// </summary>
    /// <param name="id">The ID of the portfolio entry to remove.</param>
    /// <param name="ct">Cancellation token for the request.</param>
    /// <response code="200">Company was removed from the user's portfolio.</response>
    /// <response code="401">User is not authenticated.</response>
    /// <response code="404">Portfolio entry was not found for the current user.</response>
    [HttpDelete("remove/{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveUserCompany(int id, CancellationToken ct)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (userId == null)
        {
            return Unauthorized(new { error = "User is not authenticated." });
        }

        var uid = int.Parse(userId);

        var entry = await _context.UserCompanies
            .Include(userCompany => userCompany.Ticker)
            .FirstOrDefaultAsync(
                userCompany => userCompany.Id == id && userCompany.UserId == uid,
                ct);

        if (entry == null)
        {
            return NotFound(new { error = "Company not found in your portfolio." });
        }

        _context.UserCompanies.Remove(entry);
        await _context.SaveChangesAsync(ct);

        return Ok(new
        {
            message = $"Company '{entry.Ticker?.Symbol}' removed from your portfolio.",
            tickerId = entry.TickerId
        });
    }

    /// <summary>
    /// Adds a new company to the current user's portfolio.
    /// </summary>
    /// <param name="dto">The company and investment details.</param>
    /// <param name="ct">Cancellation token for the request.</param>
    /// <response code="201">Portfolio entry created successfully.</response>
    /// <response code="400">Invalid input or ticker was not found.</response>
    /// <response code="401">User is not authorized.</response>
    /// <response code="409">Ticker is already in the user's portfolio.</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserCompanyDto>> AddUserCompany(
        [FromBody] CreateUserCompanyDto dto,
        CancellationToken ct)
    {
        _logger.LogInformation("AddUserCompany invoked for Symbol={Symbol}", dto.Symbol);

        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (userId == null)
        {
            throw new UnauthorizedException("User is not authorized.");
        }

        var uid = int.Parse(userId);

        var ticker = await _context.Tickers
            .FirstOrDefaultAsync(
                ticker => ticker.Id == dto.TickerId ||
                    ticker.Symbol.ToUpper() == dto.Symbol!.ToUpper(),
                ct);

        if (ticker == null)
        {
            _logger.LogInformation("Attempting to fetch company profile for symbol {Symbol}", dto.Symbol);

            var profile = await _fmp.GetCompanyProfileAsync(dto.Symbol!, ct);

            if (profile == null)
            {
                throw new BadRequestException($"Ticker '{dto.Symbol}' not found in external API.");
            }

            ticker = new Ticker
            {
                Symbol = dto.Symbol!.ToUpperInvariant(),
                Name = profile.Name ?? dto.Symbol,
                Sector = profile.Sector
            };

            _context.Tickers.Add(ticker);
            await _context.SaveChangesAsync(ct);
        }

        var existing = await _context.UserCompanies
            .FirstOrDefaultAsync(
                userCompany => userCompany.UserId == uid && userCompany.TickerId == ticker.Id,
                ct);

        if (existing != null)
        {
            _logger.LogInformation(
                "User {UserId} already owns {Symbol}. Updating position instead.",
                uid,
                ticker.Symbol);

            var totalSharesBefore = existing.Shares;
            var totalCostBefore = existing.Shares * existing.PurchasePrice;
            var totalSharesAfter = totalSharesBefore + dto.Shares;
            var totalCostAfter = totalCostBefore + dto.Shares * dto.PurchasePrice;
            var newAveragePrice = totalSharesAfter > 0
                ? totalCostAfter / totalSharesAfter
                : existing.PurchasePrice;

            existing.Shares = totalSharesAfter;
            existing.PurchasePrice = newAveragePrice;

            if (!string.IsNullOrWhiteSpace(dto.Notes))
            {
                existing.Notes = string.Join(
                    " | ",
                    new[] { existing.Notes, dto.Notes }
                        .Where(note => !string.IsNullOrWhiteSpace(note)));
            }

            await _context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Updated position for {Symbol}: {Shares} shares at {Price}",
                ticker.Symbol,
                existing.Shares,
                existing.PurchasePrice);

            return Ok(new
            {
                message = $"Updated position for {ticker.Symbol}.",
                shares = existing.Shares,
                averagePrice = existing.PurchasePrice
            });
        }

        if (dto.PurchasePrice == null)
        {
            _logger.LogInformation(
                "User {UserId} added {Symbol} without specifying a purchase price. Using zero as transaction price.",
                uid,
                dto.Symbol);
        }

        var userCompany = new UserCompany
        {
            UserId = uid,
            TickerId = ticker.Id,
            Shares = dto.Shares,
            PurchasePrice = dto.PurchasePrice,
            Notes = dto.Notes
        };

        _context.UserCompanies.Add(userCompany);
        await _context.SaveChangesAsync(ct);

        var transaction = new UserCompanyTransaction
        {
            UserId = uid,
            TickerId = ticker.Id,
            Shares = dto.Shares,
            Price = dto.PurchasePrice ?? 0m,
            Notes = dto.Notes,
            CreatedAt = DateTime.UtcNow
        };

        _context.UserCompanyTransactions.Add(transaction);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Recorded transaction for user {UserId}: {Shares} shares of {Symbol} at {Price}",
            uid,
            dto.Shares,
            ticker.Symbol,
            dto.PurchasePrice);

        var response = ToDto(userCompany, ticker);

        return CreatedAtAction(nameof(GetUserCompanies), new { id = userCompany.Id }, response);
    }

    /// <summary>
    /// Returns all portfolio entries for the currently authenticated user.
    /// </summary>
    /// <param name="q">Optional search term for symbol or company name.</param>
    /// <param name="limit">Optional maximum number of results.</param>
    /// <response code="200">Returns a filtered list of portfolio entries.</response>
    /// <response code="401">User is not authorized.</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IEnumerable<UserCompanyDto>>> GetUserCompanies(
        [FromQuery] string? q,
        [FromQuery] int? limit,
        CancellationToken ct)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (userId == null)
        {
            throw new UnauthorizedException("User is not authorized.");
        }

        var uid = int.Parse(userId);

        var query = _context.UserCompanies
            .Include(userCompany => userCompany.Ticker)
            .Where(userCompany => userCompany.UserId == uid)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLowerInvariant();

            query = query.Where(userCompany =>
                userCompany.Ticker.Symbol.ToLower().Contains(term) ||
                (userCompany.Ticker.Name != null &&
                    userCompany.Ticker.Name.ToLower().Contains(term)));
        }

        if (limit.HasValue && limit.Value > 0)
        {
            query = query.Take(limit.Value);
        }

        var userCompanies = await query
            .OrderBy(userCompany => userCompany.Ticker.Symbol)
            .Select(userCompany => new UserCompanyDto
            {
                Id = userCompany.Id,
                TickerId = userCompany.TickerId,
                Symbol = userCompany.Ticker.Symbol,
                Name = userCompany.Ticker.Name,
                Sector = userCompany.Ticker.Sector,
                Shares = userCompany.Shares,
                PurchasePrice = userCompany.PurchasePrice,
                Notes = userCompany.Notes,
                LastPriceUpdate = userCompany.Ticker.Prices
                    .OrderByDescending(price => price.TradingDate)
                    .Select(price => price.TradingDate.ToDateTime(TimeOnly.MinValue))
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        return Ok(userCompanies);
    }

    private static UserCompanyDto ToDto(UserCompany userCompany)
    {
        return ToDto(userCompany, userCompany.Ticker);
    }

    private static UserCompanyDto ToDto(UserCompany userCompany, Ticker ticker)
    {
        return new UserCompanyDto
        {
            Id = userCompany.Id,
            TickerId = userCompany.TickerId,
            Symbol = ticker.Symbol,
            Name = ticker.Name,
            Sector = ticker.Sector,
            Shares = userCompany.Shares,
            PurchasePrice = userCompany.PurchasePrice,
            Notes = userCompany.Notes
        };
    }
}
