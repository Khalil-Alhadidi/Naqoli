using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agent.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AgentInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    PendingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Event = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FromState = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    ToState = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AmountIqd = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PendingTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AmountIqd = table.Column<long>(type: "bigint", nullable: false),
                    FeeIqd = table.Column<long>(type: "bigint", nullable: false),
                    TotalIqd = table.Column<long>(type: "bigint", nullable: false),
                    QuotedBalanceIqd = table.Column<long>(type: "bigint", nullable: false),
                    ToPhone = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ToAccountId = table.Column<int>(type: "int", nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RegisteredName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BillAccountId = table.Column<int>(type: "int", nullable: true),
                    BillCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProductId = table.Column<int>(type: "int", nullable: true),
                    ProductCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PinRequired = table.Column<bool>(type: "bit", nullable: false),
                    FirstTime = table.Column<bool>(type: "bit", nullable: false),
                    Warning = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BalanceAfterIqd = table.Column<long>(type: "bigint", nullable: true),
                    ResultCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PurchaseCodeLast4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingTransactions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_At",
                table: "AuditEntries",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_PendingId",
                table: "AuditEntries",
                column: "PendingId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingTransactions_IdempotencyKey",
                table: "PendingTransactions",
                column: "IdempotencyKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropTable(
                name: "PendingTransactions");
        }
    }
}
