using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Data;
using Portfolio.Api.Models;
using Portfolio.Api.DTOs;
using Microsoft.EntityFrameworkCore;
using Portfolio.Api.Exceptions;
using Portfolio.Api.Services;

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
    private readonly FmpClient _fmp;
    private readonly ILogger<UserCompanyController> _logger;


    public UserCompanyController(AppDbContext context, FmpClient fmp, ILogger<UserCompanyController> logger)
    {
        _context = context;
        _fmp = fmp;
        _logger = logger;
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
            throw new UnauthorizedException("User is not authorized.");

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
                Sector = uc.Ticker.Sector,
                Shares = uc.Shares,
                PurchasePrice = uc.PurchasePrice,
                Notes = uc.Notes
            })
            .ToListAsync();

        return Ok(userCompanies);
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
            throw new UnauthorizedException("User is not authorized.");

        var uid = int.Parse(userId);

        // Find portfolio entry
        var userCompany = await _context.UserCompanies.FindAsync(id);
        if (userCompany == null)
            throw new NotFoundException("No annual income row found.");

        // Prevent deleting other users' entries
        if (userCompany.UserId != uid)
            throw new ForbiddenException("Operation not allowed.");

        _context.UserCompanies.Remove(userCompany);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Updates an existing portfolio entry for the current user.
    /// </summary>
    /// <param name="id">The ID of the portfolio entry to update.</param>
    /// <param name="dto">The updated data (shares, price, notes).</param>
    /// <response code="200">Portfolio entry updated successfully.</response>
    /// <response code="401">If user is not authorized.</response>
    /// <response code="403">If user tries to update an entry they do not own.</response>
    /// <response code="404">If entry not found.</response>
    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserCompanyDto>> UpdateUserCompany(int id, [FromBody] UpdateUserCompanyDto dto)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId == null)
            throw new UnauthorizedException("User is not authorized.");

        var uid = int.Parse(userId);

        var userCompany = await _context.UserCompanies
            .Include(uc => uc.Ticker)
            .FirstOrDefaultAsync(uc => uc.Id == id);

        if (userCompany == null)
            throw new NotFoundException("No annual income row found.");

        if (userCompany.UserId != uid)
            throw new ForbiddenException("Operation not allowed.");

        // Apply updates (only provided values)
        if (dto.Shares.HasValue) userCompany.Shares = dto.Shares;
        if (dto.PurchasePrice.HasValue) userCompany.PurchasePrice = dto.PurchasePrice;
        if (dto.Notes != null) userCompany.Notes = dto.Notes;

        await _context.SaveChangesAsync();

        var response = new UserCompanyDto
        {
            Id = userCompany.Id,
            TickerId = userCompany.TickerId,
            Symbol = userCompany.Ticker.Symbol,
            Name = userCompany.Ticker.Name,
            Shares = userCompany.Shares,
            PurchasePrice = userCompany.PurchasePrice,
            Notes = userCompany.Notes
        };

        return Ok(response);
    }

    /// <summary>
    /// Returns all UserCompany entries for all users (Admin only).
    /// </summary>
    /// <response code="200">List of all user-company mappings with user info.</response>
    /// <response code="403">If the current user is not an admin.</response>
    [Authorize(Roles = "Admin")]
    [HttpGet("/api/admin/usercompanies")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<object>>> GetAllUserCompaniesForAdmin()
    {
        var entries = await _context.UserCompanies
            .Include(uc => uc.Ticker)
            .Include(uc => uc.User)
            .Select(uc => new
            {
                uc.Id,
                Username = uc.User.Username,
                Ticker = new
                {
                    uc.Ticker.Symbol,
                    uc.Ticker.Name,
                    uc.Ticker.Sector
                },
                uc.Shares,
                uc.PurchasePrice,
                uc.Notes
            })
            .ToListAsync();

        return Ok(entries);
    }

    /// <summary>
    /// Deletes a company from the currently authenticated user's portfolio.
    /// </summary>
    /// <param name="id">The ID of the UserCompany entry to delete.</param>
    /// <param name="ct">Cancellation token for the request.</param>
    /// <response code="200">The company was successfully removed from the user's portfolio.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="404">If the portfolio entry does not exist or does not belong to the user.</response>
    [HttpDelete("remove/{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteUserCompany(int id, CancellationToken ct)
    {
        // ✅ Ensure the user is authenticated
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId == null)
            return Unauthorized(new { error = "User is not authenticated." });

        var uid = int.Parse(userId);

        // 🔍 Find the user-company entry that belongs to the current user
        var entry = await _context.UserCompanies
            .Include(uc => uc.Ticker)
            .FirstOrDefaultAsync(uc => uc.Id == id && uc.UserId == uid, ct);

        if (entry == null)
            return NotFound(new { error = "Company not found in your portfolio." });

        // 🗑️ Remove the user-company link, but keep the company in the global list
        _context.UserCompanies.Remove(entry);
        await _context.SaveChangesAsync(ct);

        // ✅ Return a success message
        return Ok(new
        {
            message = $"Company '{entry.Ticker?.Symbol}' removed from your portfolio.",
            tickerId = entry.TickerId
        });
    }

    /// <summary>
    /// Adds a new company (ticker) to the current user's portfolio.
    /// If the ticker does not exist globally, it will be created automatically.
    /// </summary>
    /// <param name="dto">The company and investment details.</param>
    /// <param name="ct">Cancellation token to cancel the request.</param>
    /// <response code="201">Portfolio entry created successfully.</response>
    /// <response code="400">Invalid input or ticker not found.</response>
    /// <response code="409">The ticker is already in the user's portfolio.</response>
    /// <response code="401">User not authorized.</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserCompanyDto>> AddUserCompany([FromBody] CreateUserCompanyDto dto, CancellationToken ct)
    {      
        _logger.LogInformation("AddUserCompany invoked for Symbol={Symbol}", dto.Symbol);
 
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId == null)
            throw new UnauthorizedException("User is not authorized.");

        var uid = int.Parse(userId);

        // 1️⃣ Check if ticker already exists in the global database
        var ticker = await _context.Tickers
            .FirstOrDefaultAsync(t => t.Id == dto.TickerId || t.Symbol.ToUpper() == dto.Symbol!.ToUpper(), ct);

        _logger.LogInformation("AddUserCompany: Searching for TickerId={TickerId}, Symbol={Symbol}", dto.TickerId, dto.Symbol);
         _logger.LogInformation("AddUserCompany: Found ticker? {Found}", ticker != null);


        // 2️⃣ If not found -> fetch from external API and create it
        if (ticker == null)
        {
            _logger.LogInformation("Attempting to fetch company profile for symbol {Symbol}", dto.Symbol);
            var profile = await _fmp.GetCompanyProfileAsync(dto.Symbol!, ct);
            _logger.LogInformation("Profile fetch completed for {Symbol}. Result is null? {IsNull}", dto.Symbol, profile == null);

            if (profile == null)
                throw new BadRequestException($"Ticker '{dto.Symbol}' not found in external API.");

            ticker = new Ticker
            {
                Symbol = dto.Symbol!.ToUpperInvariant(),
                Name = profile.Name ?? dto.Symbol,
                Sector = profile.Sector
            };

            _context.Tickers.Add(ticker);
            await _context.SaveChangesAsync(ct);
        }

        // 3️⃣ Check if user already owns this ticker
        var existing = await _context.UserCompanies
            .FirstOrDefaultAsync(uc => uc.UserId == uid && uc.TickerId == ticker.Id, ct);

        if (existing != null)
            return Conflict(new { message = $"Ticker '{ticker.Symbol}' is already in your portfolio." });

        // 4️⃣ Create new user-company entry
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

        // 5️⃣ Return the created DTO
        var response = new UserCompanyDto
        {
            Id = userCompany.Id,
            TickerId = ticker.Id,
            Symbol = ticker.Symbol,
            Name = ticker.Name,
            Shares = userCompany.Shares,
            PurchasePrice = userCompany.PurchasePrice,
            Notes = userCompany.Notes
        };

        return CreatedAtAction(nameof(GetUserCompanies), new { id = userCompany.Id }, response);
    }
    
}



