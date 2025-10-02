using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Data;
using Portfolio.Api.Models;
using Portfolio.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;


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

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            // Check if username already exists
            if (_context.Users.Any(u => u.Username == request.Username))
            {
                return BadRequest("Username already taken");
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

            return Ok("User registered successfully");
        }

        [HttpPost("login")]
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

            // Return both tokens
            return Ok(new
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken.Token,
                RefreshTokenExpiresAt = refreshToken.ExpiresAt
            });
        }


        [HttpGet("me")]
        [Authorize]
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

        [HttpPost("logout")]
        [Authorize]
        public IActionResult Logout()
        {
            return Ok(new { message = "Logged out successfully" });
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] string refreshToken)
        {
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

            // Generate new access token for the user
            var newAccessToken = JwtService.GenerateToken(storedToken.User);

            // Optional: Rolling refresh tokens → revoke old and issue new one
            var newRefreshToken = await RefreshTokenService.GenerateAndSaveAsync(storedToken.User, _context);

            // Revoke old token
            storedToken.RevokedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new
            {
                accessToken = newAccessToken,
                refreshToken = newRefreshToken.Token,
                refreshTokenExpiresAt = newRefreshToken.ExpiresAt
            });
        }
    }
}
