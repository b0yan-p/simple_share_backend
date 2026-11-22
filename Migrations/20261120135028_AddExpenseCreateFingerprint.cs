using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace simpleShare_backend.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseCreateFingerprint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "CreateFingerprint",
                table: "Expenses",
                type: "bytea",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreateFingerprint",
                table: "Expenses");
        }
    }
}
