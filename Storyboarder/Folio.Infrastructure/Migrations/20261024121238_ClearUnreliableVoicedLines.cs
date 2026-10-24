using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Folio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ClearUnreliableVoicedLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data only, no schema change. Until now the desk's one-line note was composed knowing only the
            // stage's name, and wrote "Draft 1 is complete" over a book part-way through its reads. A stored
            // line is kept until the stage moves, so these would outlive the fix. They are cleared once, for
            // every book that has not finished (5 = ProjectStage.Completed); the next paint composes a line
            // from the facts. A finished book's line was composed knowing it was done, and stays.
            migrationBuilder.Sql(
                "UPDATE \"Novels\" SET \"VoicedLine\" = '', \"VoicedLineStage\" = NULL WHERE \"Stage\" <> 5;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
