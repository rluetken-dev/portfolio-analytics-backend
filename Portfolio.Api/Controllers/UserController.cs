using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Data;
using Portfolio.Api.Models;
using Portfolio.Api.Services;
using Microsoft.EntityFrameworkCore;


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

            // Login successful
            return Ok("Login successful");
        }
    }
}
