using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIncomeStatements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "income_statements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Symbol = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Frequency = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    ReportedCurrency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    Revenue = table.Column<long>(type: "INTEGER", nullable: true),
                    NetIncome = table.Column<long>(type: "INTEGER", nullable: true),
                    Eps = table.Column<double>(type: "REAL", nullable: true),
                    EpsDiluted = table.Column<double>(type: "REAL", nullable: true),
                    WeightedAverageShsOut = table.Column<long>(type: "INTEGER", nullable: true),
                    WeightedAverageShsOutDil = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_income_statements", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_income_statements_Symbol_Date_Frequency",
                table: "income_statements",
                columns: new[] { "Symbol", "Date", "Frequency" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "income_statements");
        }
    }
}
