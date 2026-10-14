using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agent.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AuditDetail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Detail",
                table: "AuditEntries",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Detail",
                table: "AuditEntries");
        }
    }
}
