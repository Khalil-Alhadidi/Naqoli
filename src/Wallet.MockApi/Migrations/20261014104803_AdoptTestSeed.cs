using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wallet.MockApi.Migrations
{
    /// <inheritdoc />
    public partial class AdoptTestSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The rows of the old literal seed do not fit the new columns (codes, account phones). Delete every row,
            // in dependency order, before changing the schema; "migrate" then adds the seed from the test-set files.
            migrationBuilder.Sql("DELETE FROM [Orders]; DELETE FROM [Transactions]; DELETE FROM [BillAccounts]; DELETE FROM [Aliases]; DELETE FROM [Contacts]; DELETE FROM [Accounts]; DELETE FROM [Users]; DELETE FROM [Products]; DELETE FROM [Billers];");

            // Restart each identity at 1, as the reset does, so the seed's ids (user 1 is U1) hold on a database that
            // held the old seed. A table that never held a row is skipped: RESEED 0 there would make the next id 0.
            foreach (var table in new[] { "Orders", "Transactions", "BillAccounts", "Aliases", "Contacts", "Accounts", "Users", "Products", "Billers" })
            {
                migrationBuilder.Sql(
                    $"IF EXISTS (SELECT 1 FROM sys.identity_columns WHERE object_id = OBJECT_ID(N'[dbo].[{table}]') AND last_value IS NOT NULL) "
                    + $"DBCC CHECKIDENT ('[dbo].[{table}]', RESEED, 0) WITH NO_INFOMSGS;");
            }

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "BillAccounts");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "Products",
                newName: "ProviderNameEn");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Users",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "ConsentGiven",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AlsoCalled",
                table: "Products",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Products",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProviderNameAr",
                table: "Products",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Contacts",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NameEn",
                table: "Billers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "Label",
                table: "BillAccounts",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "BillAccounts",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "Text",
                table: "Aliases",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "Accounts",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Accounts",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "Accounts",
                type: "nvarchar(15)",
                maxLength: 15,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RegisteredName",
                table: "Accounts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Accounts",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Code",
                table: "Users",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_Code",
                table: "Products",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_Code",
                table: "Contacts",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillAccounts_Code",
                table: "BillAccounts",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_Phone",
                table: "Accounts",
                column: "Phone",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Code",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Products_Code",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Contacts_Code",
                table: "Contacts");

            migrationBuilder.DropIndex(
                name: "IX_BillAccounts_Code",
                table: "BillAccounts");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_Phone",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ConsentGiven",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AlsoCalled",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ProviderNameAr",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "NameEn",
                table: "Billers");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "BillAccounts");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "RegisteredName",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Accounts");

            migrationBuilder.RenameColumn(
                name: "ProviderNameEn",
                table: "Products",
                newName: "Name");

            migrationBuilder.AlterColumn<string>(
                name: "Label",
                table: "BillAccounts",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DueDate",
                table: "BillAccounts",
                type: "date",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Text",
                table: "Aliases",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "Accounts",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
