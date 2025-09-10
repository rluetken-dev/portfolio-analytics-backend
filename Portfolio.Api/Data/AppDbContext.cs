using Microsoft.EntityFrameworkCore;
using Portfolio.Api.Models;

namespace Portfolio.Api.Data
{
    /// <summary>
    /// Entity Framework Core DbContext:
    /// - Acts as the application's gateway to the database.
    /// - Exposes DbSet&lt;T&gt; for each aggregate/table we want to persist.
    /// - Configures schema details (indexes, precision, constraints) in OnModelCreating.
    ///
    /// Why a dedicated DbContext?
    /// - Keeps database concerns centralized and explicit.
    /// - Enables migrations (schema versioning) and testability (swap providers, e.g., InMemory).
    /// </summary>
    public class AppDbContext : DbContext
    {
        /// <summary>
        /// Standard constructor used by ASP.NET Core's dependency injection.
        /// Options (provider, connection string, etc.) are configured in Program.cs.
        /// </summary>
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        /// <summary>
        /// Table for end-of-day prices.
        /// </summary>
        public DbSet<Price> Prices => Set<Price>();

        /// <summary>
        /// Fluent model configuration:
        /// - Unique index on (Symbol, AsOfDate) to prevent duplicates (idempotent imports).
        /// - Precision for monetary values (Close) as decimal(18,6).
        /// - Optional: string length hints to keep the schema tight and queries fast.
        /// 
        /// Notes on SQLite:
        /// - SQLite is type-flexible; EF will still honor precision/scale as a convention.
        /// - DateOnly is stored as TEXT by default; EF handles conversion behind the scenes.
        /// </summary>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var price = modelBuilder.Entity<Price>();

            // Ensure Symbol + AsOfDate is unique (same symbol/day must not be inserted twice).
            price
                .HasIndex(p => new { p.Symbol, p.AsOfDate })
                .IsUnique();

            // Monetary precision: 18 total digits, 6 after the decimal point.
            // This is a common choice for currency-like values (avoids floating errors).
            price
                .Property(p => p.Close)
                .HasColumnType("decimal(18,6)");

            // Keep symbols reasonably short to aid indexing and reduce storage.
            price
                .Property(p => p.Symbol)
                .IsRequired()
                .HasMaxLength(16);

            // Source is optional to change but has a sensible default ("alpha_vantage").
            price
                .Property(p => p.Source)
                .IsRequired()
                .HasMaxLength(64);

            // RetrievedAt should always be present; default is set in the entity.
            price
                .Property(p => p.RetrievedAt)
                .IsRequired();

            base.OnModelCreating(modelBuilder);
        }
    }
}
