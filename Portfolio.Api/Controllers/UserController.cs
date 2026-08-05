using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Api.Data;
using Portfolio.Api.DTOs;
using Portfolio.Api.Exceptions;
using Portfolio.Api.Models;
using Portfolio.Api.Services;
using Portfolio.Api.Utils;

namespace Portfolio.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly JwtService _jwtService;

        public UserController(AppDbContext context, JwtService jwtService)
        {
            _context = context;
            _jwtService = jwtService;
        }

        /// <summary>
        /// Registers a new user account.
        /// </summary>
        /// <remarks>
        /// Creates a new user, hashes the password, and issues both an access token and a refresh token.
        /// </remarks>
        /// <param name="request">The registration data.</param>
        /// <returns>Returns tokens and basic user info.</returns>
        /// <response code="200">Registration successful.</response>
        /// <response code="400">Username already taken or invalid input.</response>
        [HttpPost("register")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (_context.Users.Any(user => user.Username == request.Username))
            {
                throw new BadRequestException("Username already taken.");
            }

            var passwordHash = PasswordHasher.HashPassword(request.Password);

            var user = new User
            {
                Username = request.Username,
                PasswordHash = passwordHash
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var accessToken = _jwtService.GenerateToken(user);
            var refreshToken = await RefreshTokenService.GenerateAndSaveAsync(user, _context);

            Response.Cookies.Append("refreshToken", refreshToken.Token, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Expires = refreshToken.ExpiresAt
            });

            return Ok(new
            {
                accessToken,
                refreshToken = refreshToken.Token,
                refreshTokenExpiresAt = refreshToken.ExpiresAt,
                user = new
                {
                    id = user.Id,
                    username = user.Username
                }
            });
        }

        /// <summary>
        /// Logs an existing user in.
        /// </summary>
        /// <remarks>
        /// Validates username and password, then issues a new JWT access token and refresh token.
        /// </remarks>
        /// <param name="request">Login credentials.</param>
        /// <returns>Access token and user info.</returns>
        /// <response code="200">Login successful.</response>
        /// <response code="401">Invalid username or password.</response>
        [HttpPost("login")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(user => user.Username == request.Username);

            if (user == null)
            {
                throw new UnauthorizedException("Invalid username or password.");
            }

            if (!PasswordHasher.VerifyPassword(request.Password, user.PasswordHash))
            {
                throw new UnauthorizedException("Invalid username or password.");
            }

            var accessToken = _jwtService.GenerateToken(user);
            var refreshToken = await RefreshTokenService.GenerateAndSaveAsync(user, _context);

            Response.Cookies.Append("refreshToken", refreshToken.Token, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Expires = refreshToken.ExpiresAt
            });

            return Ok(new
            {
                AccessToken = accessToken,
                User = new
                {
                    Id = user.Id,
                    Username = user.Username
                }
            });
        }

        /// <summary>
        /// Returns information about the currently authenticated user.
        /// </summary>
        /// <remarks>
        /// Requires a valid JWT access token in the Authorization header.
        /// </remarks>
        /// <response code="200">Returns user ID and username.</response>
        /// <response code="401">Request is not authorized.</response>
        [HttpGet("me")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public IActionResult Me()
        {
            var username = User.Identity?.Name;
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            return Ok(new
            {
                UserId = userId,
                Username = username
            });
        }

        /// <summary>
        /// Logs the current user out and revokes their refresh token.
        /// </summary>
        /// <response code="200">User successfully logged out.</response>
        [HttpPost("logout")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Logout()
        {
            var refreshToken = Request.Cookies["refreshToken"];

            if (!string.IsNullOrEmpty(refreshToken))
            {
                var storedToken = await _context.RefreshTokens
                    .FirstOrDefaultAsync(token => token.Token == refreshToken);

                if (storedToken != null && storedToken.RevokedAt == null)
                {
                    storedToken.RevokedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }
            }

            Response.Cookies.Append("refreshToken", string.Empty, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Expires = DateTime.UtcNow.AddDays(-1)
            });

            return Ok(new { message = "Logged out." });
        }

        /// <summary>
        /// Refreshes the access token using a valid refresh token cookie.
        /// </summary>
        /// <response code="200">New access token issued.</response>
        /// <response code="401">Refresh token is missing, invalid, expired, or revoked.</response>
        [HttpPost("refresh")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Refresh()
        {
            var refreshToken = Request.Cookies["refreshToken"];

            if (string.IsNullOrEmpty(refreshToken))
            {
                throw new UnauthorizedException("Refresh token missing.");
            }

            var storedToken = await _context.RefreshTokens
                .Include(token => token.User)
                .FirstOrDefaultAsync(token => token.Token == refreshToken);

            if (storedToken == null)
            {
                throw new UnauthorizedException("Invalid refresh token.");
            }

            if (storedToken.ExpiresAt <= DateTime.UtcNow || storedToken.RevokedAt != null)
            {
                throw new UnauthorizedException("Refresh token expired or revoked.");
            }

            var newAccessToken = _jwtService.GenerateToken(storedToken.User);

            storedToken.RevokedAt = DateTime.UtcNow;

            var newRefreshToken = await RefreshTokenService.GenerateAndSaveAsync(storedToken.User, _context);
            await _context.SaveChangesAsync();

            Response.Cookies.Append("refreshToken", newRefreshToken.Token, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Expires = newRefreshToken.ExpiresAt
            });

            return Ok(new
            {
                AccessToken = newAccessToken
            });
        }

        /// <summary>
        /// Returns the current user's cash balance.
        /// </summary>
        /// <remarks>
        /// Requires a valid JWT access token.
        /// </remarks>
        /// <response code="200">Returns the user's balance.</response>
        /// <response code="401">Request is unauthorized.</response>
        [HttpGet("balance")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetBalance()
        {
            var username = User.Identity?.Name;

            if (string.IsNullOrEmpty(username))
            {
                return Unauthorized("User not authenticated.");
            }

            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(user => user.Username == username);

            if (user == null)
            {
                return NotFound("User not found.");
            }

            return Ok(new
            {
                username = user.Username,
                cashBalance = user.CashBalance
            });
        }

        /// <summary>
        /// Adds money to the current user's account balance.
        /// </summary>
        /// <remarks>
        /// Requires a valid JWT access token.
        /// </remarks>
        /// <param name="amount">Deposit amount. Must be greater than zero.</param>
        /// <response code="200">Deposit successful, returns new balance.</response>
        /// <response code="400">Deposit amount is invalid.</response>
        /// <response code="401">Request is unauthorized.</response>
        [HttpPost("deposit")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Deposit([FromBody] decimal amount)
        {
            if (amount <= 0)
            {
                return BadRequest("Deposit amount must be greater than zero.");
            }

            var username = User.Identity?.Name;

            if (string.IsNullOrEmpty(username))
            {
                return Unauthorized("User not authenticated.");
            }

            var user = await _context.Users
                .FirstOrDefaultAsync(user => user.Username == username);

            if (user == null)
            {
                return NotFound("User not found.");
            }

            user.CashBalance += amount;
            await _context.SaveChangesAsync();

            return Ok(new
            {
                username = user.Username,
                newBalance = user.CashBalance
            });
        }

        /// <summary>
        /// Withdraws money from the current user's balance.
        /// </summary>
        /// <remarks>
        /// Requires a valid JWT access token. Fails if the user's balance is insufficient.
        /// </remarks>
        /// <param name="amount">Withdrawal amount. Must be greater than zero.</param>
        /// <response code="200">Withdrawal successful, returns new balance.</response>
        /// <response code="400">Withdrawal amount is invalid or balance is insufficient.</response>
        /// <response code="401">Request is unauthorized.</response>
        [HttpPost("withdraw")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Withdraw([FromBody] decimal amount)
        {
            if (amount <= 0)
            {
                return BadRequest("Withdrawal amount must be greater than zero.");
            }

            var username = User.Identity?.Name;

            if (string.IsNullOrEmpty(username))
            {
                return Unauthorized("User not authenticated.");
            }

            var user = await _context.Users
                .FirstOrDefaultAsync(user => user.Username == username);

            if (user == null)
            {
                return NotFound("User not found.");
            }

            if (user.CashBalance < amount)
            {
                return BadRequest("Insufficient funds.");
            }

            user.CashBalance -= amount;
            await _context.SaveChangesAsync();

            return Ok(new
            {
                username = user.Username,
                newBalance = user.CashBalance
            });
        }
    }
}