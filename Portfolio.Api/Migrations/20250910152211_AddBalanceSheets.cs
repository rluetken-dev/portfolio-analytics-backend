using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBalanceSheets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "balance_sheets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Symbol = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Frequency = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    ReportedCurrency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    TotalAssets = table.Column<long>(type: "INTEGER", nullable: true),
                    TotalLiabilities = table.Column<long>(type: "INTEGER", nullable: true),
                    TotalStockholdersEquity = table.Column<long>(type: "INTEGER", nullable: true),
                    CashAndCashEquivalents = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_balance_sheets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_balance_sheets_Symbol_Date_Frequency",
                table: "balance_sheets",
                columns: new[] { "Symbol", "Date", "Frequency" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "balance_sheets");
        }
    }
}
