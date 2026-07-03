using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevHub.Modules.Workspace.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectDocs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "project_docs",
                schema: "workspace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    doc_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    content = table.Column<string>(type: "text", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_docs", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_docs_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "workspace",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_project_docs_project_id_doc_key",
                schema: "workspace",
                table: "project_docs",
                columns: new[] { "project_id", "doc_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project_docs",
                schema: "workspace");
        }
    }
}
