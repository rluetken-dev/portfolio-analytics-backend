using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Data;
using Portfolio.Api.Models;
using Portfolio.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Portfolio.Api.DTOs;

namespace Portfolio.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserController : ControllerBase
    {
        private readonly AppDbContext _context;

        public UserController(AppDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Registers a new user account.
        /// </summary>
        /// <remarks>
        /// Creates a new user, hashes the password, and issues both an access token and a refresh token (HttpOnly cookie).
        /// </remarks>
        /// <param name="request">The registration data (username + password).</param>
        /// <returns>Returns tokens and basic user info.</returns>
        /// <response code="200">Registration successful.</response>
        /// <response code="400">Username already taken or invalid input.</response>
        [HttpPost("register")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            // Check if username already exists
            if (_context.Users.Any(u => u.Username == request.Username))
            {
                return BadRequest(new { message = "Username already taken" });
            }

            // Hash the password
            var passwordHash = PasswordHasher.HashPassword(request.Password);

            // Create new user
            var user = new User
            {
                Username = request.Username,
                PasswordHash = passwordHash
            };

            // Save to database
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // Generate JWT access token
            var accessToken = JwtService.GenerateToken(user);

            // Generate and save refresh token
            var refreshToken = await RefreshTokenService.GenerateAndSaveAsync(user, _context);

            // Set refresh token as HttpOnly cookie
            Response.Cookies.Append("refreshToken", refreshToken.Token, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Expires = refreshToken.ExpiresAt
            });

            // Return tokens and user info
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
        /// Validates username and password, issues new JWT access and refresh tokens.
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
            // Find user by username
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == request.Username);

            if (user == null)
            {
                return Unauthorized("Invalid username or password");
            }

            // Verify password
            if (!PasswordHasher.VerifyPassword(request.Password, user.PasswordHash))
            {
                return Unauthorized("Invalid username or password");
            }

            // Generate JWT access token
            var accessToken = JwtService.GenerateToken(user);

            // Generate and save refresh token
            var refreshToken = await RefreshTokenService.GenerateAndSaveAsync(user, _context);

            // Set refresh token as HttpOnly Secure cookie
            Response.Cookies.Append("refreshToken", refreshToken.Token, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Expires = refreshToken.ExpiresAt
            });

            // Return only the access token + user info
            return Ok(new
            {
                AccessToken = accessToken,
                User = new { Id = user.Id, Username = user.Username }
            });
        }

        /// <summary>
        /// Returns information about the currently authenticated user.
        /// </summary>
        /// <remarks>
        /// Requires a valid JWT access token in the Authorization header.
        /// </remarks>
        /// <response code="200">Returns user ID and username.</response>
        /// <response code="401">If the request is not authorized.</response>
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
                // Suche RefreshToken in DB
                var storedToken = await _context.RefreshTokens
                    .FirstOrDefaultAsync(rt => rt.Token == refreshToken);

                if (storedToken != null && storedToken.RevokedAt == null)
                {
                    storedToken.RevokedAt = DateTime.UtcNow; // Ungültig machen
                    await _context.SaveChangesAsync();
                }
            }

            // Cookie löschen (überschreiben + sofort ablaufen lassen)
            Response.Cookies.Append("refreshToken", "",
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.None,
                    Expires = DateTime.UtcNow.AddDays(-1) // abgelaufen
                });

            return Ok(new { message = "Logged out" });
        }


        /// <summary>
        /// Refreshes the access token using a valid refresh token (from cookie).
        /// </summary>
        /// <response code="200">New access token issued.</response>
        /// <response code="401">Invalid or expired refresh token.</response>
        [HttpPost("refresh")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Refresh()
        {
            // Get refresh token from cookies
            var refreshToken = Request.Cookies["refreshToken"];

            if (string.IsNullOrEmpty(refreshToken))
            {
                return Unauthorized(new { message = "Refresh token missing" });
            }

            // Find token in DB
            var storedToken = await _context.RefreshTokens
                .Include(rt => rt.User)
                .FirstOrDefaultAsync(rt => rt.Token == refreshToken);

            if (storedToken == null)
            {
                return Unauthorized(new { message = "Invalid refresh token" });
            }

            // Check if token is expired or revoked
            if (storedToken.ExpiresAt <= DateTime.UtcNow || storedToken.RevokedAt != null)
            {
                return Unauthorized(new { message = "Refresh token expired or revoked" });
            }

            // Generate new access token
            var newAccessToken = JwtService.GenerateToken(storedToken.User);

            // Optional: rolling refresh → revoke old + set new cookie
            storedToken.RevokedAt = DateTime.UtcNow;
            var newRefreshToken = await RefreshTokenService.GenerateAndSaveAsync(storedToken.User, _context);
            await _context.SaveChangesAsync();

            // Set new refresh token as cookie
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

    }
}
