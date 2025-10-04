namespace Portfolio.Api.DTOs
{
    public class RegisterRequest
    {
        public string Username { get; set; } = string.Empty; // Chosen username
        public string Password { get; set; } = string.Empty; // Plain text password (will be hashed)
    }
}
