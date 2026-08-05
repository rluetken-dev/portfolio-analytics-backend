using Microsoft.EntityFrameworkCore;
using Portfolio.Api.Data.Entities;
using Portfolio.Api.Models;

namespace Portfolio.Api.Data;

/// <summary>
/// Entity Framework Core database context for portfolio data, users, transactions, and cached market data.
/// </summary>
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }
    
    public DbSet<Ticker> Tickers => Set<Ticker>();
    public DbSet<Price> Prices => Set<Price>();
    public DbSet<IncomeStatementEntity> IncomeStatements => Set<IncomeStatementEntity>();
    public DbSet<BalanceSheetEntity> BalanceSheets => Set<BalanceSheetEntity>();
    public DbSet<CashFlowEntity> CashFlows => Set<CashFlowEntity>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<UserCompany> UserCompanies => Set<UserCompany>();
    public DbSet<UserCompanyTransaction> UserCompanyTransactions => Set<UserCompanyTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureTicker(modelBuilder);
        ConfigurePrice(modelBuilder);
        ConfigureIncomeStatement(modelBuilder);
        ConfigureBalanceSheet(modelBuilder);
        ConfigureCashFlow(modelBuilder);
        ConfigureUserCompany(modelBuilder);
    }

    private static void ConfigureTicker(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ticker>(entity =>
        {
            entity.HasIndex(ticker => ticker.Symbol)
                .IsUnique();

            entity.Property(ticker => ticker.Symbol)
                .IsRequired()
                .HasMaxLength(16);

            entity.Property(ticker => ticker.Name)
                .HasMaxLength(128);
        });
    }

    private static void ConfigurePrice(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Price>(entity =>
        {
            entity.HasIndex(price => new { price.TickerId, price.TradingDate })
                .IsUnique();

            entity.Property(price => price.Open)
                .HasColumnType("decimal(18,6)");

            entity.Property(price => price.High)
                .HasColumnType("decimal(18,6)");

            entity.Property(price => price.Low)
                .HasColumnType("decimal(18,6)");

            entity.Property(price => price.Close)
                .HasColumnType("decimal(18,6)");

            entity.Property(price => price.AdjustedClose)
                .HasColumnType("decimal(18,6)");

            entity.Property(price => price.Source)
                .IsRequired()
                .HasMaxLength(64);

            entity.Property(price => price.TradingDate)
                .HasConversion(
                    date => date.ToDateTime(TimeOnly.MinValue),
                    dateTime => DateOnly.FromDateTime(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)));
        });
    }

    private static void ConfigureIncomeStatement(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IncomeStatementEntity>(entity =>
        {
            entity.ToTable("income_statements");

            entity.HasKey(statement => statement.Id);

            entity.HasIndex(statement => new { statement.Symbol, statement.Date, statement.Frequency })
                .IsUnique();

            entity.Property(statement => statement.Symbol)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(statement => statement.Frequency)
                .IsRequired()
                .HasMaxLength(10);

            entity.Property(statement => statement.ReportedCurrency)
                .HasMaxLength(8);

            entity.Property(statement => statement.Date)
                .HasConversion(
                    date => date.ToDateTime(TimeOnly.MinValue),
                    dateTime => DateOnly.FromDateTime(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)));
        });
    }

    private static void ConfigureBalanceSheet(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BalanceSheetEntity>(entity =>
        {
            entity.ToTable("balance_sheets");

            entity.HasKey(statement => statement.Id);

            entity.HasIndex(statement => new { statement.Symbol, statement.Date, statement.Frequency })
                .IsUnique();

            entity.Property(statement => statement.Symbol)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(statement => statement.Frequency)
                .IsRequired()
                .HasMaxLength(10);

            entity.Property(statement => statement.ReportedCurrency)
                .HasMaxLength(8);

            entity.Property(statement => statement.Date)
                .HasConversion(
                    date => date.ToDateTime(TimeOnly.MinValue),
                    dateTime => DateOnly.FromDateTime(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)));
        });
    }
    
    private static void ConfigureCashFlow(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CashFlowEntity>(entity =>
        {
            entity.ToTable("cash_flows");

            entity.HasKey(statement => statement.Id);

            entity.HasIndex(statement => new { statement.Symbol, statement.Date, statement.Frequency })
                .IsUnique();

            entity.Property(statement => statement.Symbol)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(statement => statement.Frequency)
                .IsRequired()
                .HasMaxLength(10);

            entity.Property(statement => statement.ReportedCurrency)
                .HasMaxLength(8);

            entity.Property(statement => statement.Date)
                .HasConversion(
                    date => date.ToDateTime(TimeOnly.MinValue),
                    dateTime => DateOnly.FromDateTime(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)));
        });
    }

    private static void ConfigureUserCompany(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserCompany>(entity =>
        {
            entity.ToTable("user_companies");

            entity.HasKey(userCompany => userCompany.Id);

            entity.HasOne(userCompany => userCompany.User)
                .WithMany(user => user.UserCompanies)
                .HasForeignKey(userCompany => userCompany.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(userCompany => userCompany.Ticker)
                .WithMany(ticker => ticker.UserCompanies)
                .HasForeignKey(userCompany => userCompany.TickerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(userCompany => new { userCompany.UserId, userCompany.TickerId })
                .IsUnique();

            entity.Property(userCompany => userCompany.Shares)
                .HasColumnType("decimal(18,4)");

            entity.Property(userCompany => userCompany.PurchasePrice)
                .HasColumnType("decimal(18,4)");

            entity.Property(userCompany => userCompany.Notes)
                .HasMaxLength(500);
        });
    }
}
