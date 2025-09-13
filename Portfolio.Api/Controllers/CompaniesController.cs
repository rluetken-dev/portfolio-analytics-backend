using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Api.Data;               
using Portfolio.Api.Models;          

namespace Portfolio.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CompaniesController : ControllerBase
    {
        private readonly AppDbContext _db;
        public CompaniesController(AppDbContext db) => _db = db;

        /// <summary>
        /// GET /api/companies?q=AAP&amp;limit=50
        /// Simple search (by Symbol/Name) + limit (default 50, max 200).
        /// </summary> 
        [HttpGet]
        public async Task<ActionResult<IEnumerable<CompanySummaryDto>>> GetCompanies(
            [FromQuery] string? q,
            [FromQuery] int? limit,
            CancellationToken ct)
        {
            // Use Set<T>() so we don't depend on a specific DbSet<Ticker> property name.
            var query = _db.Set<Ticker>().AsQueryable();

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                query = query.Where(t =>
                    EF.Functions.Like(t.Symbol, $"%{term}%") ||
                    (t.Name != null && EF.Functions.Like(t.Name, $"%{term}%"))
                );
            }

            var take = Math.Clamp(limit ?? 50, 1, 200);

            var rows = await query
                .OrderBy(t => t.Symbol)
                .Select(t => new CompanySummaryDto
                {
                    Id = t.Id.ToString(),
                    Symbol = t.Symbol,
                    Name = t.Name,
                    // Sector intentionally omitted (not in your Ticker model)
                })
                .Take(take)
                .ToListAsync(ct);

            return Ok(rows);
        }
    }

    /// <summary>
    /// Lightweight DTO for the frontend list.
    /// </summary>
    public record CompanySummaryDto
    {
        public string? Id { get; init; }
        public string? Symbol { get; init; }
        public string? Name { get; init; }
    }   
}
