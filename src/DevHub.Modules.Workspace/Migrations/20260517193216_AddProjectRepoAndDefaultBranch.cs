using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevHub.Modules.Workspace.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectRepoAndDefaultBranch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "default_branch",
                schema: "workspace",
                table: "projects",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "repo",
                schema: "workspace",
                table: "projects",
                type: "character varying(140)",
                maxLength: 140,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "default_branch",
                schema: "workspace",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "repo",
                schema: "workspace",
                table: "projects");
        }
    }
}
