using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDecksJsonToPlayerTournament : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DecksJson",
                table: "PlayerTournaments",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DecksJson",
                table: "PlayerTournaments");
        }
    }
}
