using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReunionIdentityAndDeckStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReunionPseudo",
                table: "PlayerTournaments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReunionUserId",
                table: "PlayerTournaments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReunionPseudo",
                table: "PlayerGames",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReunionUserId",
                table: "PlayerGames",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerTournaments_ComputedAt_TournamentParentId_BgaUserId",
                table: "PlayerTournaments",
                columns: new[] { "ComputedAt", "TournamentParentId", "BgaUserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayerTournaments_ComputedAt_TournamentParentId_BgaUserId",
                table: "PlayerTournaments");

            migrationBuilder.DropColumn(
                name: "ReunionPseudo",
                table: "PlayerTournaments");

            migrationBuilder.DropColumn(
                name: "ReunionUserId",
                table: "PlayerTournaments");

            migrationBuilder.DropColumn(
                name: "ReunionPseudo",
                table: "PlayerGames");

            migrationBuilder.DropColumn(
                name: "ReunionUserId",
                table: "PlayerGames");
        }
    }
}
