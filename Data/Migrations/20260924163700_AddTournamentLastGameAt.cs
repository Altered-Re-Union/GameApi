using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTournamentLastGameAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastGameAt",
                table: "Tournaments",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            // Nothing will trigger a fresh RecomputeTournamentAsync for a
            // tournament that's over and getting no new games, so an
            // existing tournament's LastGameAt has to be backfilled directly
            // here rather than by waiting on the next consolidation pass --
            // same reasoning as BackfillTournamentParentName.
            migrationBuilder.Sql(
                """
                UPDATE "Tournaments" t
                SET "LastGameAt" = g."MaxReceivedAt"
                FROM (
                    SELECT "TournamentParentId", MAX("ReceivedAt") AS "MaxReceivedAt"
                    FROM "Games"
                    WHERE "TournamentParentId" IS NOT NULL
                    GROUP BY "TournamentParentId"
                ) g
                WHERE t."TournamentParentId" = g."TournamentParentId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastGameAt",
                table: "Tournaments");
        }
    }
}
