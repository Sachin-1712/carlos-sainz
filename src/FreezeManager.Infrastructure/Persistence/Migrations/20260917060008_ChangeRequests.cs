using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FreezeManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChangeRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChangeRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ReferenceYear = table.Column<int>(type: "INTEGER", nullable: false),
                    ReferenceSequence = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    RequestedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    ImplementationPlan = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    BackoutPlan = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Impact = table.Column<int>(type: "INTEGER", nullable: false),
                    Likelihood = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestedStartUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RequestedEndUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChangeRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChangeAffectedServices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ChangeRequestId = table.Column<int>(type: "INTEGER", nullable: false),
                    ServiceKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChangeAffectedServices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChangeAffectedServices_ChangeRequests_ChangeRequestId",
                        column: x => x.ChangeRequestId,
                        principalTable: "ChangeRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChangeAffectedServices_ChangeRequestId_ServiceKey",
                table: "ChangeAffectedServices",
                columns: new[] { "ChangeRequestId", "ServiceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChangeAffectedServices_ServiceKey",
                table: "ChangeAffectedServices",
                column: "ServiceKey");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_Reference",
                table: "ChangeRequests",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_ReferenceYear_ReferenceSequence",
                table: "ChangeRequests",
                columns: new[] { "ReferenceYear", "ReferenceSequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_State",
                table: "ChangeRequests",
                column: "State");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChangeAffectedServices");

            migrationBuilder.DropTable(
                name: "ChangeRequests");
        }
    }
}
