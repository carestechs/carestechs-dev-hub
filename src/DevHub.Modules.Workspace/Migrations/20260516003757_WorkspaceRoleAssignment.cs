using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevHub.Modules.Workspace.Migrations
{
    /// <inheritdoc />
    public partial class WorkspaceRoleAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workspace_role_assignments",
                schema: "workspace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workspace_role_assignments", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_workspace_role_assignments_member_id_role_id",
                schema: "workspace",
                table: "workspace_role_assignments",
                columns: new[] { "member_id", "role_id" },
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workspace_role_assignments",
                schema: "workspace");
        }
    }
}
