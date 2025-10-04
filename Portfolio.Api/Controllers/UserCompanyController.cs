using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Data;
using Portfolio.Api.Models;
using Portfolio.Api.DTOs;

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
    /// Returns all companies (tickers) linked to the currently authenticated user.
    /// </summary>
    [HttpGet]
    public IActionResult GetUserCompanies()
    {
        // TODO: Implement in next step
        return Ok(new { message = "Endpoint ready – logic pending." });
    }
}
