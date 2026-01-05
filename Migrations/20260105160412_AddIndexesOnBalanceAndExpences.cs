using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace simpleShare_backend.Migrations
{
    /// <inheritdoc />
    public partial class AddIndexesOnBalanceAndExpences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_GroupMembers_GroupId_MemberId",
                table: "GroupMembers",
                columns: new[] { "GroupId", "MemberId" },
                filter: "[LeftAt] IS NULL AND [GroupMemberStatus] <> 0");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseSplits_OwedByMemberId_ExpenseId",
                table: "ExpenseSplits",
                columns: new[] { "OwedByMemberId", "ExpenseId" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_GroupId",
                table: "Expenses",
                column: "GroupId",
                filter: "[DeletedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ExpensePayments_PaidByMemberId_ExpenseId",
                table: "ExpensePayments",
                columns: new[] { "PaidByMemberId", "ExpenseId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GroupMembers_GroupId_MemberId",
                table: "GroupMembers");

            migrationBuilder.DropIndex(
                name: "IX_ExpenseSplits_OwedByMemberId_ExpenseId",
                table: "ExpenseSplits");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_GroupId",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_ExpensePayments_PaidByMemberId_ExpenseId",
                table: "ExpensePayments");
        }
    }
}
