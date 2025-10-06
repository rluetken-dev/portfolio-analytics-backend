using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Api.Models;
using System.Security.Claims;
using Portfolio.Api.Data;

namespace Portfolio.Api.Controllers
{
    /// <summary>
    /// Provides API endpoints for recording and retrieving user stock transactions.
    /// Each record represents a single buy or sell operation performed by an authenticated user.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UserCompanyTransactionsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<UserCompanyTransactionsController> _logger;

        public UserCompanyTransactionsController(AppDbContext context, ILogger<UserCompanyTransactionsController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Records a new buy or sell transaction for the currently authenticated user.
        /// Positive share quantities represent a "buy" action, while negative values indicate a "sell".
        /// </summary>
        /// <param name="dto">The transaction object containing TickerId, Shares, Price, and optional Notes.</param>
        /// <param name="ct">Cancellation token for the request.</param>
        /// <response code="201">The transaction was successfully recorded and created.</response>
        /// <response code="400">If the TickerId is invalid or request data is malformed.</response>
        /// <response code="401">If the user is not authenticated.</response>
        [HttpPost]
        [ProducesResponseType(typeof(UserCompanyTransaction), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<UserCompanyTransaction>> AddTransaction(
            [FromBody] UserCompanyTransaction dto,
            CancellationToken ct)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId == null)
                return Unauthorized("User not authorized.");

            var uid = int.Parse(userId);

            _logger.LogInformation("Recording transaction for user {UserId}, SymbolId {TickerId}, Shares={Shares}, Price={Price}",
                uid, dto.TickerId, dto.Shares, dto.Price);

            // Validate ticker
            var ticker = await _context.Tickers.FirstOrDefaultAsync(t => t.Id == dto.TickerId, ct);
            if (ticker == null)
                return BadRequest($"Invalid TickerId: {dto.TickerId}");

            // Create transaction record
            var transaction = new UserCompanyTransaction
            {
                UserId = uid,
                TickerId = dto.TickerId,
                Shares = dto.Shares,
                Price = dto.Price,
                Notes = dto.Notes,
                CreatedAt = DateTime.UtcNow
            };

            _context.UserCompanyTransactions.Add(transaction);
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation("Transaction saved for user {UserId}: {Shares} shares @ {Price}", uid, dto.Shares, dto.Price);

            return CreatedAtAction(nameof(GetTransactionById), new { id = transaction.Id }, transaction);
        }

        /// <summary>
        /// Retrieves a specific transaction by its unique ID.
        /// </summary>
        /// <param name="id">The unique transaction ID.</param>
        /// <param name="ct">Cancellation token for the request.</param>
        /// <response code="200">The requested transaction was found and returned.</response>
        /// <response code="401">If the user is not authenticated.</response>
        /// <response code="404">If the transaction with the specified ID does not exist.</response>
        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(UserCompanyTransaction), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<UserCompanyTransaction>> GetTransactionById(int id, CancellationToken ct)
        {
            var transaction = await _context.UserCompanyTransactions.FindAsync(new object?[] { id }, ct);
            if (transaction == null)
                return NotFound();

            return transaction;
        }

        /// <summary>
        /// Retrieves all recorded transactions for the currently authenticated user.
        /// Transactions are returned in descending order by creation date.
        /// </summary>
        /// <param name="ct">Cancellation token for the request.</param>
        /// <response code="200">Returns a list of transactions associated with the current user.</response>
        /// <response code="401">If the user is not authenticated.</response>
        [HttpGet("mine")]
        [ProducesResponseType(typeof(IEnumerable<UserCompanyTransaction>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<IEnumerable<UserCompanyTransaction>>> GetUserTransactions(CancellationToken ct)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId == null)
                return Unauthorized("User not authorized.");

            var uid = int.Parse(userId);

            var transactions = await _context.UserCompanyTransactions
                .Include(t => t.Ticker)
                .Where(t => t.UserId == uid)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync(ct);

            return transactions;
        }
    }
}
