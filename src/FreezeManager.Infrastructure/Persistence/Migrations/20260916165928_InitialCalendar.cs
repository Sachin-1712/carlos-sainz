using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FreezeManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCalendar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CalendarSyncRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Season = table.Column<int>(type: "INTEGER", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Succeeded = table.Column<bool>(type: "INTEGER", nullable: false),
                    EventsAdded = table.Column<int>(type: "INTEGER", nullable: false),
                    EventsUpdated = table.Column<int>(type: "INTEGER", nullable: false),
                    EventsSkipped = table.Column<int>(type: "INTEGER", nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarSyncRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RaceEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Season = table.Column<int>(type: "INTEGER", nullable: false),
                    Round = table.Column<int>(type: "INTEGER", nullable: false),
                    OfficialName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Circuit = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Country = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LocalTimeZoneId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Format = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    IsVerified = table.Column<bool>(type: "INTEGER", nullable: false),
                    PinnedByAdmin = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpstreamCircuitId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    SyncedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RaceEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Services",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Tier = table.Column<int>(type: "INTEGER", nullable: false),
                    Owner = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Services", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ParcFermeWindows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RaceEventId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    IsDerived = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParcFermeWindows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ParcFermeWindows_RaceEvents_RaceEventId",
                        column: x => x.RaceEventId,
                        principalTable: "RaceEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RaceEventId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    ScheduledStartUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ScheduledEndUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ActualStartUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ActualEndUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sessions_RaceEvents_RaceEventId",
                        column: x => x.RaceEventId,
                        principalTable: "RaceEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ParcFermeWindows_RaceEventId",
                table: "ParcFermeWindows",
                column: "RaceEventId");

            migrationBuilder.CreateIndex(
                name: "IX_RaceEvents_Season_Round",
                table: "RaceEvents",
                columns: new[] { "Season", "Round" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Services_Key",
                table: "Services",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_RaceEventId_Type",
                table: "Sessions",
                columns: new[] { "RaceEventId", "Type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CalendarSyncRuns");

            migrationBuilder.DropTable(
                name: "ParcFermeWindows");

            migrationBuilder.DropTable(
                name: "Services");

            migrationBuilder.DropTable(
                name: "Sessions");

            migrationBuilder.DropTable(
                name: "RaceEvents");
        }
    }
}
