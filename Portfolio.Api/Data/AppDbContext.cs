using Microsoft.EntityFrameworkCore;
using Portfolio.Api.Models;
using Portfolio.Api.Data.Entities; // for IncomeStatementEntity

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
    /// Table of income statement observations (annual + quarterly).
    /// </summary>
    public DbSet<IncomeStatementEntity> IncomeStatements => Set<IncomeStatementEntity>();

    /// <summary>
    /// Table of balance sheet observations (annual + quarterly).
    /// </summary>
    public DbSet<Portfolio.Api.Data.Entities.BalanceSheetEntity> BalanceSheets => Set<Portfolio.Api.Data.Entities.BalanceSheetEntity>();

    /// <summary>
    /// Table of daily OHLCV price records.
    /// </summary>
    public DbSet<Price> Prices => Set<Price>();

    /// <summary>
    /// Table of cash flow observations (annual + quarterly).
    /// </summary>
    public DbSet<Portfolio.Api.Data.Entities.CashFlowEntity> CashFlows => Set<Portfolio.Api.Data.Entities.CashFlowEntity>();

    /// <summary>
    /// Users table for authentication and account management
    /// </summary>
    public DbSet<Portfolio.Api.Models.User> Users => Set<Portfolio.Api.Models.User>();


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

        // --- IncomeStatement configuration ---
        modelBuilder.Entity<IncomeStatementEntity>(e =>
        {
            // Table name: keep it simple and readable.
            e.ToTable("income_statements");

            // PK
            e.HasKey(x => x.Id);

            // Uniqueness: (Symbol, Date, Frequency) must be unique
            e.HasIndex(x => new { x.Symbol, x.Date, x.Frequency })
             .IsUnique();

            // Basic constraints
            e.Property(x => x.Symbol)
             .IsRequired()
             .HasMaxLength(20);

            e.Property(x => x.Frequency)
             .IsRequired()
             .HasMaxLength(10); // "annual" | "quarter"

            e.Property(x => x.ReportedCurrency)
             .HasMaxLength(8);

            // Map DateOnly <-> DateTime for SQLite
            e.Property(x => x.Date)
             .HasConversion(
                 d => d.ToDateTime(TimeOnly.MinValue),                           // store as UTC date
                 dt => DateOnly.FromDateTime(DateTime.SpecifyKind(dt, DateTimeKind.Utc))
             );
        });

        // --- BalanceSheet configuration ---
        modelBuilder.Entity<Portfolio.Api.Data.Entities.BalanceSheetEntity>(e =>
        {
            // Table name kept simple and readable.
            e.ToTable("balance_sheets");

            // PK
            e.HasKey(x => x.Id);

            // Uniqueness: (Symbol, Date, Frequency) prevents duplicates
            e.HasIndex(x => new { x.Symbol, x.Date, x.Frequency })
             .IsUnique();

            // Basic constraints
            e.Property(x => x.Symbol)
             .IsRequired()
             .HasMaxLength(20);

            e.Property(x => x.Frequency)
             .IsRequired()
             .HasMaxLength(10); // "annual" | "quarter"

            e.Property(x => x.ReportedCurrency)
             .HasMaxLength(8);

            // Map DateOnly <-> DateTime for SQLite
            e.Property(x => x.Date)
             .HasConversion(
                 d => d.ToDateTime(TimeOnly.MinValue),
                 dt => DateOnly.FromDateTime(DateTime.SpecifyKind(dt, DateTimeKind.Utc))
             );
        });

        // --- CashFlow configuration ---
        modelBuilder.Entity<Portfolio.Api.Data.Entities.CashFlowEntity>(e =>
        {
            // Table name kept simple and readable.
            e.ToTable("cash_flows");

            // PK
            e.HasKey(x => x.Id);

            // Uniqueness: (Symbol, Date, Frequency) prevents duplicates
            e.HasIndex(x => new { x.Symbol, x.Date, x.Frequency })
             .IsUnique();

            // Basic constraints
            e.Property(x => x.Symbol)
             .IsRequired()
             .HasMaxLength(20);

            e.Property(x => x.Frequency)
             .IsRequired()
             .HasMaxLength(10); // "annual" | "quarter"

            e.Property(x => x.ReportedCurrency)
             .HasMaxLength(8);

            // Map DateOnly <-> DateTime for SQLite
            e.Property(x => x.Date)
             .HasConversion(
                 d => d.ToDateTime(TimeOnly.MinValue),
                 dt => DateOnly.FromDateTime(DateTime.SpecifyKind(dt, DateTimeKind.Utc))
             );
        });
    }
}
