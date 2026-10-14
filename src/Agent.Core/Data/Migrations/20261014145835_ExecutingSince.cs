using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agent.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExecutingSince : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExecutingSince",
                table: "PendingTransactions",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExecutingSince",
                table: "PendingTransactions");
        }
    }
}
