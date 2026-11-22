using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace simpleShare_backend.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupCurrencyAndExpenseSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "Groups",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OriginalAmount",
                table: "ExpenseSplits",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                table: "Expenses",
                type: "numeric(28,12)",
                precision: 28,
                scale: 12,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExchangeRateDate",
                table: "Expenses",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OriginalAmount",
                table: "Expenses",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalCurrency",
                table: "Expenses",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OriginalAmount",
                table: "ExpensePayments",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);
            // Backfill originals without touching any existing amount or timestamp.
            migrationBuilder.Sql("""
                UPDATE "Groups" SET "Currency" = 'BAM';
                UPDATE "Expenses" SET "OriginalAmount" = "TotalAmount", "OriginalCurrency" = 'BAM', "ExchangeRate" = 1;
                UPDATE "ExpensePayments" SET "OriginalAmount" = "Amount";
                UPDATE "ExpenseSplits" SET "OriginalAmount" = "Amount";
                ALTER TABLE "Groups" ALTER COLUMN "Currency" SET NOT NULL;
                ALTER TABLE "Expenses" ALTER COLUMN "OriginalAmount" SET NOT NULL, ALTER COLUMN "OriginalCurrency" SET NOT NULL, ALTER COLUMN "ExchangeRate" SET NOT NULL;
                ALTER TABLE "ExpensePayments" ALTER COLUMN "OriginalAmount" SET NOT NULL;
                ALTER TABLE "ExpenseSplits" ALTER COLUMN "OriginalAmount" SET NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Currency",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "OriginalAmount",
                table: "ExpenseSplits");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "ExchangeRateDate",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "OriginalAmount",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "OriginalCurrency",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "OriginalAmount",
                table: "ExpensePayments");
        }
    }
}
