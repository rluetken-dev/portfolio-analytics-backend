namespace Portfolio.Api.Models
{
    public class User
    {
        public int Id { get; set; } // Primary key (auto-increment)
        
        public string Username { get; set; } = string.Empty; // Username of the account
        
        public string PasswordHash { get; set; } = string.Empty; // Hashed password

         /// <summary>
        /// All user-company (portfolio) relationships linked to this user.
        /// </summary>
        public ICollection<UserCompany> UserCompanies { get; set; } = new List<UserCompany>();
    }
}
