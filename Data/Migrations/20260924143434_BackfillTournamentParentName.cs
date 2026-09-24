using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameApi.Data.Migrations
{
    /// <summary>
    /// Data migration, no schema change: fills TournamentParentName for the
    /// same two groupings altered-bga-api's own BackfillTournamentParentId
    /// backfills there (597924, 602986). Their games are already mirrored
    /// locally and already have a Tournament aggregate -- built by the old
    /// shared-prefix TournamentNameResolver, which is exactly what this
    /// replaces -- but nothing will trigger a fresh RecomputeTournamentAsync
    /// for tournaments that are over and getting no new games, so the
    /// aggregate has to be corrected directly here rather than by waiting on
    /// the next consolidation pass.
    /// </summary>
    public partial class BackfillTournamentParentName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "Tournaments"
                SET "TournamentParentName" = 'ROC Worlds Qualifier 1'
                WHERE "TournamentParentId" = 597924;
                """);

            migrationBuilder.Sql(
                """
                UPDATE "Tournaments"
                SET "TournamentParentName" = 'Monday Night Topcut - Season 1 - Week 3'
                WHERE "TournamentParentId" = 602986;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "Tournaments"
                SET "TournamentParentName" = NULL
                WHERE "TournamentParentId" = 597924 AND "TournamentParentName" = 'ROC Worlds Qualifier 1';
                """);

            migrationBuilder.Sql(
                """
                UPDATE "Tournaments"
                SET "TournamentParentName" = NULL
                WHERE "TournamentParentId" = 602986 AND "TournamentParentName" = 'Monday Night Topcut - Season 1 - Week 3';
                """);
        }
    }
}
