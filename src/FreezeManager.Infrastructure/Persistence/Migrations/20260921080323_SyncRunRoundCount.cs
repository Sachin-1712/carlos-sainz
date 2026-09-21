using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FreezeManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SyncRunRoundCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FromPublishedSource",
                table: "CalendarSyncRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "StoredRoundCount",
                table: "CalendarSyncRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FromPublishedSource",
                table: "CalendarSyncRuns");

            migrationBuilder.DropColumn(
                name: "StoredRoundCount",
                table: "CalendarSyncRuns");
        }
    }
}
