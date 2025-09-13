// Portfolio.Api/Controllers/CompaniesController.cs
using Microsoft.AspNetCore.Mvc;

namespace Portfolio.Api.Controllers
{
    /// <summary>
    /// Minimal controller to list companies (placeholder data).
    /// Replace the in-memory list with a DB query later.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class CompaniesController : ControllerBase
    {
        /// <summary>
        /// Returns a minimal list of companies (demo).
        /// GET /api/companies
        /// </summary>
        [HttpGet]
        public ActionResult<IEnumerable<CompanySummaryDto>> GetCompanies()
        {
            // TODO: Replace with real data from your EF Core DbContext later
            var demo = new List<CompanySummaryDto>
            {
                new() { Id = "1", Symbol = "AAPL", Name = "Apple Inc.",      Sector = "Technology" },
                new() { Id = "2", Symbol = "MSFT", Name = "Microsoft Corp.", Sector = "Technology" },
                new() { Id = "3", Symbol = "V",    Name = "Visa Inc.",       Sector = "Financials" },
            };

            return Ok(demo);
        }
    }

    /// <summary>
    /// Lightweight DTO used by the frontend Companies page.
    /// Extend fields over time as needed (e.g., market cap, country, etc.).
    /// </summary>
    public record CompanySummaryDto
    {
        public string? Id { get; init; }
        public string? Symbol { get; init; }
        public string? Name { get; init; }
        public string? Sector { get; init; }
    }
}
