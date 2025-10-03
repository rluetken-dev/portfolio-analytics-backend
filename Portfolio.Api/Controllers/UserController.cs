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


        [HttpPost("refresh")]
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
