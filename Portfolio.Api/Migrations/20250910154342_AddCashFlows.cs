using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCashFlows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cash_flows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Symbol = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Frequency = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    ReportedCurrency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    OperatingCashFlow = table.Column<long>(type: "INTEGER", nullable: true),
                    CapitalExpenditure = table.Column<long>(type: "INTEGER", nullable: true),
                    FreeCashFlow = table.Column<long>(type: "INTEGER", nullable: true),
                    NetIncome = table.Column<long>(type: "INTEGER", nullable: true),
                    DepreciationAndAmortization = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_flows", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cash_flows_Symbol_Date_Frequency",
                table: "cash_flows",
                columns: new[] { "Symbol", "Date", "Frequency" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cash_flows");
        }
    }
}
