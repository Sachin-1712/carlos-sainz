using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FreezeManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ApprovalsOverridesAndAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Actor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Action = table.Column<int>(type: "INTEGER", nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Details = table.Column<string>(type: "TEXT", nullable: false),
                    PreviousHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    Hash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Sequence);
                });

            migrationBuilder.CreateTable(
                name: "ChangeApprovals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ChangeRequestId = table.Column<int>(type: "INTEGER", nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    Approver = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Decision = table.Column<int>(type: "INTEGER", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Comment = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChangeApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChangeApprovals_ChangeRequests_ChangeRequestId",
                        column: x => x.ChangeRequestId,
                        principalTable: "ChangeRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FreezeOverrides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ChangeReference = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IncidentReference = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Justification = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    RequestedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    GrantDurationMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    IsBreakGlass = table.Column<bool>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    GrantedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UsedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RetrospectiveDueAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RetrospectiveCompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RetrospectiveNotes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    ExpiryAudited = table.Column<bool>(type: "INTEGER", nullable: false),
                    RetrospectiveOverdueAudited = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FreezeOverrides", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OverrideApprovals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FreezeOverrideId = table.Column<int>(type: "INTEGER", nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    Approver = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Decision = table.Column<int>(type: "INTEGER", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Comment = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OverrideApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OverrideApprovals_FreezeOverrides_FreezeOverrideId",
                        column: x => x.FreezeOverrideId,
                        principalTable: "FreezeOverrides",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_OccurredAtUtc",
                table: "AuditEntries",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_Subject",
                table: "AuditEntries",
                column: "Subject");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeApprovals_ChangeRequestId_Role",
                table: "ChangeApprovals",
                columns: new[] { "ChangeRequestId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FreezeOverrides_ChangeReference",
                table: "FreezeOverrides",
                column: "ChangeReference");

            migrationBuilder.CreateIndex(
                name: "IX_FreezeOverrides_State",
                table: "FreezeOverrides",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_OverrideApprovals_FreezeOverrideId_Role",
                table: "OverrideApprovals",
                columns: new[] { "FreezeOverrideId", "Role" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropTable(
                name: "ChangeApprovals");

            migrationBuilder.DropTable(
                name: "OverrideApprovals");

            migrationBuilder.DropTable(
                name: "FreezeOverrides");
        }
    }
}
