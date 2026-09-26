using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTournamentModeAndLastGameAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastGameAt",
                table: "Tournaments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "Tournaments",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastGameAt",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "Tournaments");
        }
    }
}
