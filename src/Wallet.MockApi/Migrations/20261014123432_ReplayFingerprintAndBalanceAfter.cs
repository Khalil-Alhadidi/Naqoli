using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wallet.MockApi.Migrations
{
    /// <inheritdoc />
    public partial class ReplayFingerprintAndBalanceAfter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "BalanceAfterIqd",
                table: "Transactions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestFingerprint",
                table: "Transactions",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BalanceAfterIqd",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "RequestFingerprint",
                table: "Transactions");
        }
    }
}
