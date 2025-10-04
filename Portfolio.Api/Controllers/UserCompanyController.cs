using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Data;
using Portfolio.Api.Models;
using Portfolio.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace Portfolio.Api.Controllers;

/// <summary>
/// API controller for managing a user's portfolio (user-company relationships).
/// Requires authentication via JWT.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UserCompanyController : ControllerBase
{
    private readonly AppDbContext _context;

    public UserCompanyController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Returns all portfolio entries for the currently authenticated user.
    /// </summary>
    /// <response code="200">Returns a list of portfolio entries for the user.</response>
    /// <response code="401">If the user is not authorized.</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IEnumerable<UserCompanyDto>>> GetUserCompanies()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId == null)
            return Unauthorized();

        var uid = int.Parse(userId);

        var userCompanies = await _context.UserCompanies
            .Include(uc => uc.Ticker)
            .Where(uc => uc.UserId == uid)
            .Select(uc => new UserCompanyDto
            {
                Id = uc.Id,
                TickerId = uc.TickerId,
                Symbol = uc.Ticker.Symbol,
                Name = uc.Ticker.Name,
                Shares = uc.Shares,
                PurchasePrice = uc.PurchasePrice,
                Notes = uc.Notes
            })
            .ToListAsync();

        return Ok(userCompanies);
    }

    /// <summary>
    /// Adds a new company (ticker) to the current user's portfolio.
    /// </summary>
    /// <param name="dto">The company and investment details.</param>
    /// <response code="201">Portfolio entry created successfully.</response>
    /// <response code="400">Invalid input or ticker not found.</response>
    /// <response code="409">The ticker is already in the user's portfolio.</response>
    /// <response code="401">User not authorized.</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserCompanyDto>> AddUserCompany([FromBody] CreateUserCompanyDto dto)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId == null)
            return Unauthorized();

        var uid = int.Parse(userId);

        // Check if ticker exists
        var ticker = await _context.Tickers.FindAsync(dto.TickerId);
        if (ticker == null)
            return BadRequest(new { message = "Ticker not found." });

        // Check if this ticker is already in user's portfolio
        var existing = await _context.UserCompanies
            .FirstOrDefaultAsync(uc => uc.UserId == uid && uc.TickerId == dto.TickerId);

        if (existing != null)
            return Conflict(new { message = "This ticker is already in your portfolio." });

        // Create new entry
        var userCompany = new UserCompany
        {
            UserId = uid,
            TickerId = dto.TickerId,
            Shares = dto.Shares,
            PurchasePrice = dto.PurchasePrice,
            Notes = dto.Notes
        };

        _context.UserCompanies.Add(userCompany);
        await _context.SaveChangesAsync();

        // Return DTO response
        var response = new UserCompanyDto
        {
            Id = userCompany.Id,
            TickerId = userCompany.TickerId,
            Symbol = ticker.Symbol,
            Name = ticker.Name,
            Shares = userCompany.Shares,
            PurchasePrice = userCompany.PurchasePrice,
            Notes = userCompany.Notes
        };

        return CreatedAtAction(nameof(GetUserCompanies), new { id = userCompany.Id }, response);
    }

    /// <summary>
    /// Deletes a portfolio entry (UserCompany) belonging to the current user.
    /// </summary>
    /// <param name="id">The ID of the portfolio entry to delete.</param>
    /// <response code="204">Portfolio entry deleted successfully.</response>
    /// <response code="401">User not authorized.</response>
    /// <response code="403">Trying to delete an entry that belongs to another user.</response>
    /// <response code="404">Entry not found.</response>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteUserCompany(int id)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId == null)
            return Unauthorized();

        var uid = int.Parse(userId);

        // Find portfolio entry
        var userCompany = await _context.UserCompanies.FindAsync(id);
        if (userCompany == null)
            return NotFound(new { message = "Portfolio entry not found." });

        // Prevent deleting other users' entries
        if (userCompany.UserId != uid)
            return Forbid();

        _context.UserCompanies.Remove(userCompany);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}



