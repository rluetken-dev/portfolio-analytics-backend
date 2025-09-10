using Microsoft.EntityFrameworkCore;
using Portfolio.Api.Models;

namespace Portfolio.Api.Data;

/// <summary>
/// Entity Framework Core DbContext:
/// - Manages access to the database.
/// - Exposes DbSet&lt;T&gt; for each table (aggregate root).
/// - Configures schema (indexes, constraints, conversions) in OnModelCreating.
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    /// <summary>
    /// Table of tradable instruments (tickers).
    /// </summary>
    public DbSet<Ticker> Tickers => Set<Ticker>();

    /// <summary>
    /// Table of daily OHLCV price records.
    /// </summary>
    public DbSet<Price> Prices => Set<Price>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // --- Ticker configuration ---
        modelBuilder.Entity<Ticker>()
            .HasIndex(t => t.Symbol)
            .IsUnique(); // Prevent duplicate ticker symbols

        modelBuilder.Entity<Ticker>()
            .Property(t => t.Symbol)
            .IsRequired()
            .HasMaxLength(16);

        modelBuilder.Entity<Ticker>()
            .Property(t => t.Name)
            .HasMaxLength(128);

        // --- Price configuration ---
        modelBuilder.Entity<Price>()
            .HasIndex(p => new { p.TickerId, p.TradingDate })
            .IsUnique(); // Only one record per ticker per day

        // Monetary precision: 18 digits total, 6 decimals
        modelBuilder.Entity<Price>()
            .Property(p => p.Close)
            .HasColumnType("decimal(18,6)");

        modelBuilder.Entity<Price>()
            .Property(p => p.AdjustedClose)
            .HasColumnType("decimal(18,6)");

        modelBuilder.Entity<Price>()
            .Property(p => p.Open)
            .HasColumnType("decimal(18,6)");

        modelBuilder.Entity<Price>()
            .Property(p => p.High)
            .HasColumnType("decimal(18,6)");

        modelBuilder.Entity<Price>()
            .Property(p => p.Low)
            .HasColumnType("decimal(18,6)");

        // Provider/source info
        modelBuilder.Entity<Price>()
            .Property(p => p.Source)
            .IsRequired()
            .HasMaxLength(64);

        // Map DateOnly <-> DateTime for SQLite
        modelBuilder.Entity<Price>()
            .Property(p => p.TradingDate)
            .HasConversion(
                d => d.ToDateTime(TimeOnly.MinValue),
                dt => DateOnly.FromDateTime(DateTime.SpecifyKind(dt, DateTimeKind.Utc))
            );
    }
}
