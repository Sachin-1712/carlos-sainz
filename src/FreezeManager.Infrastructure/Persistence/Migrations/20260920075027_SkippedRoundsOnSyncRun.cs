using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FreezeManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SkippedRoundsOnSyncRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SkippedRoundsJson",
                table: "CalendarSyncRuns",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SkippedRoundsJson",
                table: "CalendarSyncRuns");
        }
    }
}
