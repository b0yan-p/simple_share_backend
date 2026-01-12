using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace simpleShare_backend.Migrations
{
    /// <inheritdoc />
    public partial class ExpenseSettleUpFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSettleUp",
                table: "Expenses",
                type: "bit",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_ModifiedByMemberId",
                table: "Expenses",
                column: "ModifiedByMemberId");

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_Members_ModifiedByMemberId",
                table: "Expenses",
                column: "ModifiedByMemberId",
                principalTable: "Members",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_Members_ModifiedByMemberId",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_ModifiedByMemberId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "IsSettleUp",
                table: "Expenses");
        }
    }
}
