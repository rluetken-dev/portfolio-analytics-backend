using System;

namespace Portfolio.Api.Models
{
    public class RefreshToken
    {
        public int Id { get; set; } // Primary Key

        public required string Token { get; set; } // Required: must always be set

        public DateTime ExpiresAt { get; set; } // Expiration date

        public DateTime? RevokedAt { get; set; } // Null if active, set when user logs out

        // Foreign Key to User
        public int UserId { get; set; }
        public required User User { get; set; } // Required: must always be set
    }
}
