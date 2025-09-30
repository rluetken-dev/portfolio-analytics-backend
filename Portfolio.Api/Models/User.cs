namespace Portfolio.Api.Models
{
    public class User
    {
        public int Id { get; set; } // Primary key (auto-increment)
        
        public string Username { get; set; } = string.Empty; // Username of the account
        
        public string PasswordHash { get; set; } = string.Empty; // Hashed password
    }
}
