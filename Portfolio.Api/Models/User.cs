namespace Portfolio.Api.Models
{
    public class User
    {
        public int Id { get; set; }

        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// Indicates whether this user has administrative privileges.
        /// </summary>
        public bool IsAdmin { get; set; } = false;

        public string PasswordHash { get; set; } = string.Empty;

        /// <summary>
        /// All user-company portfolio relationships linked to this user.
        /// </summary>
        public ICollection<UserCompany> UserCompanies { get; set; } = new List<UserCompany>();

        /// <summary>
        /// Current available cash balance for this user.
        /// </summary>
        public decimal CashBalance { get; set; } = 0m;
    }
}