using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GameApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Games",
                columns: table => new
                {
                    TableId = table.Column<long>(type: "bigint", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Format = table.Column<string>(type: "text", nullable: true),
                    TournamentId = table.Column<long>(type: "bigint", nullable: true),
                    TournamentName = table.Column<string>(type: "text", nullable: true),
                    TournamentParentId = table.Column<long>(type: "bigint", nullable: true),
                    TournamentGroup = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Games", x => x.TableId);
                });

            migrationBuilder.CreateTable(
                name: "PlayerTournaments",
                columns: table => new
                {
                    TournamentParentId = table.Column<long>(type: "bigint", nullable: false),
                    BgaUserId = table.Column<string>(type: "text", nullable: false),
                    BgaName = table.Column<string>(type: "text", nullable: true),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    Losses = table.Column<int>(type: "integer", nullable: false),
                    DecksPlayed = table.Column<int>(type: "integer", nullable: false),
                    MainDeck = table.Column<string>(type: "text", nullable: true),
                    Faction = table.Column<string>(type: "text", nullable: true),
                    Hero = table.Column<string>(type: "text", nullable: true),
                    AdminWinsAdjustment = table.Column<int>(type: "integer", nullable: false),
                    AdminLossesAdjustment = table.Column<int>(type: "integer", nullable: false),
                    AdminAdjustmentNote = table.Column<string>(type: "text", nullable: true),
                    ComputedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RefreshedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerTournaments", x => new { x.TournamentParentId, x.BgaUserId });
                });

            migrationBuilder.CreateTable(
                name: "SyncCursors",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncCursors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tournaments",
                columns: table => new
                {
                    TournamentParentId = table.Column<long>(type: "bigint", nullable: false),
                    TournamentParentName = table.Column<string>(type: "text", nullable: true),
                    TotalGames = table.Column<int>(type: "integer", nullable: false),
                    TotalPlayers = table.Column<int>(type: "integer", nullable: false),
                    ComputedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RefreshedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tournaments", x => x.TournamentParentId);
                });

            migrationBuilder.CreateTable(
                name: "PlayerGames",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TableId = table.Column<long>(type: "bigint", nullable: false),
                    BgaUserId = table.Column<string>(type: "text", nullable: true),
                    BgaName = table.Column<string>(type: "text", nullable: true),
                    IsWinner = table.Column<bool>(type: "boolean", nullable: false),
                    Deck = table.Column<string>(type: "text", nullable: true),
                    PlayedCards = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerGames", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerGames_Games_TableId",
                        column: x => x.TableId,
                        principalTable: "Games",
                        principalColumn: "TableId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Games_ReceivedAt",
                table: "Games",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Games_TournamentParentId",
                table: "Games",
                column: "TournamentParentId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerGames_TableId_BgaUserId",
                table: "PlayerGames",
                columns: new[] { "TableId", "BgaUserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerGames");

            migrationBuilder.DropTable(
                name: "PlayerTournaments");

            migrationBuilder.DropTable(
                name: "SyncCursors");

            migrationBuilder.DropTable(
                name: "Tournaments");

            migrationBuilder.DropTable(
                name: "Games");
        }
    }
}
