using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Api.Data;
using Portfolio.Api.DTOs;
using Portfolio.Api.Exceptions;
using Portfolio.Api.Models;
using System.Security.Claims;

namespace Portfolio.Api.Controllers
{
    /// <summary>
    /// Provides API endpoints for recording and retrieving user stock transactions.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UserCompanyTransactionsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<UserCompanyTransactionsController> _logger;

        public UserCompanyTransactionsController(
            AppDbContext context,
            ILogger<UserCompanyTransactionsController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Records a new buy or sell transaction for the currently authenticated user.
        /// </summary>
        /// <remarks>
        /// Positive share quantities represent buys. Negative share quantities represent sells.
        /// The portfolio entry stores the latest transaction price as purchase price.
        /// </remarks>
        /// <param name="dto">The transaction details.</param>
        /// <param name="ct">Cancellation token for the request.</param>
        /// <response code="201">Transaction was recorded successfully.</response>
        /// <response code="400">Ticker symbol is invalid or the user tries to sell more shares than owned.</response>
        /// <response code="401">User is not authenticated.</response>
        [HttpPost]
        [ProducesResponseType(typeof(TransactionDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<TransactionDto>> AddTransaction(
            [FromBody] CreateUserCompanyTransactionDto dto,
            CancellationToken ct)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (userId == null)
            {
                return Unauthorized("User not authorized.");
            }

            if (!int.TryParse(userId, out var uid))
            {
                throw new BadRequestException("Invalid user identifier.");
            }

            _logger.LogInformation(
                "Recording transaction for user {UserId}, Symbol={Symbol}, Shares={Shares}, Price={Price}",
                uid,
                dto.Symbol,
                dto.Shares,
                dto.Price);

            var ticker = await _context.Tickers
                .FirstOrDefaultAsync(
                    ticker => ticker.Symbol.ToLower() == dto.Symbol.ToLower(),
                    ct);

            if (ticker == null)
            {
                throw new BadRequestException($"Invalid Symbol: {dto.Symbol}");
            }

            var transaction = new UserCompanyTransaction
            {
                UserId = uid,
                TickerId = ticker.Id,
                Shares = dto.Shares,
                Price = dto.Price,
                Notes = dto.Notes,
                CreatedAt = DateTime.UtcNow
            };

            _context.UserCompanyTransactions.Add(transaction);

            var userCompany = await _context.UserCompanies
                .FirstOrDefaultAsync(
                    userCompany => userCompany.UserId == uid && userCompany.TickerId == ticker.Id,
                    ct);

            if (userCompany != null && dto.Shares < 0 && userCompany.Shares + dto.Shares < 0)
            {
                _logger.LogWarning(
                    "User {UserId} attempted to sell {SellShares} shares of {TickerId}, but only owns {CurrentShares}",
                    uid,
                    Math.Abs(dto.Shares),
                    ticker.Id,
                    userCompany.Shares);

                throw new BadRequestException("Insufficient shares to sell. You cannot sell more shares than you currently own.");
            }

            if (userCompany != null)
            {
                userCompany.Shares += dto.Shares;
                userCompany.PurchasePrice = dto.Price;
                userCompany.Notes = AppendTransactionNote(userCompany.Notes, dto.Notes);
            }
            else
            {
                userCompany = new UserCompany
                {
                    UserId = uid,
                    TickerId = ticker.Id,
                    Shares = dto.Shares,
                    PurchasePrice = dto.Price,
                    Notes = FormatTransactionNote(dto.Notes)
                };

                _context.UserCompanies.Add(userCompany);
            }

            await _context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Transaction saved for user {UserId}: {Shares} shares at {Price}",
                uid,
                dto.Shares,
                dto.Price);

            var result = new TransactionDto
            {
                CreatedAt = transaction.CreatedAt,
                Shares = transaction.Shares,
                Price = transaction.Price,
                Notes = transaction.Notes
            };

            return Created($"/api/UserCompanyTransactions/{transaction.Id}", result);
        }

        /// <summary>
        /// Retrieves a specific transaction by ID.
        /// </summary>
        /// <param name="id">The transaction ID.</param>
        /// <param name="ct">Cancellation token for the request.</param>
        /// <response code="200">Transaction was found.</response>
        /// <response code="401">User is not authenticated.</response>
        /// <response code="404">Transaction was not found.</response>
        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(UserCompanyTransaction), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<UserCompanyTransaction>> GetTransactionById(
            int id,
            CancellationToken ct)
        {
            var transaction = await _context.UserCompanyTransactions
                .FindAsync(new object?[] { id }, ct);

            if (transaction == null)
            {
                return NotFound();
            }

            return transaction;
        }

        /// <summary>
        /// Retrieves all transactions for the currently authenticated user.
        /// </summary>
        /// <param name="ct">Cancellation token for the request.</param>
        /// <response code="200">Returns the current user's transactions.</response>
        /// <response code="401">User is not authenticated.</response>
        [HttpGet("mine")]
        [ProducesResponseType(typeof(IEnumerable<UserCompanyTransaction>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<IEnumerable<UserCompanyTransaction>>> GetUserTransactions(CancellationToken ct)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (userId == null)
            {
                return Unauthorized("User not authorized.");
            }

            var uid = int.Parse(userId);

            var transactions = await _context.UserCompanyTransactions
                .Include(transaction => transaction.Ticker)
                .Where(transaction => transaction.UserId == uid)
                .OrderByDescending(transaction => transaction.CreatedAt)
                .ToListAsync(ct);

            return transactions;
        }

        /// <summary>
        /// Retrieves all transactions for a company symbol belonging to the current user.
        /// </summary>
        /// <param name="symbol">The ticker symbol.</param>
        /// <response code="200">Returns transactions for the specified symbol.</response>
        /// <response code="401">User is not authenticated.</response>
        /// <response code="404">Ticker symbol was not found.</response>
        [HttpGet("by-symbol/{symbol}")]
        [ProducesResponseType(typeof(IEnumerable<TransactionDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<IEnumerable<TransactionDto>>> GetTransactionsBySymbol(string symbol)
        {
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (userIdString == null)
            {
                return Unauthorized();
            }

            var userId = int.Parse(userIdString);

            var ticker = await _context.Tickers
                .FirstOrDefaultAsync(ticker => ticker.Symbol.ToLower() == symbol.ToLower());

            if (ticker == null)
            {
                return NotFound($"Ticker '{symbol}' not found.");
            }

            var transactions = await _context.UserCompanyTransactions
                .Where(transaction => transaction.UserId == userId && transaction.TickerId == ticker.Id)
                .OrderByDescending(transaction => transaction.CreatedAt)
                .Select(transaction => new TransactionDto
                {
                    CreatedAt = transaction.CreatedAt,
                    Shares = transaction.Shares,
                    Price = transaction.Price ?? 0m,
                    Notes = transaction.Notes
                })
                .ToListAsync();

            return Ok(transactions);
        }

        private static string AppendTransactionNote(string? existingNotes, string? newNote)
        {
            var formattedNote = FormatTransactionNote(newNote);

            if (string.IsNullOrWhiteSpace(existingNotes))
            {
                return formattedNote;
            }

            return $"{existingNotes}{Environment.NewLine}{formattedNote}";
        }

        private static string FormatTransactionNote(string? note)
        {
            return $"[{DateTime.UtcNow:yyyy-MM-dd}] {note}";
        }
    }
}
