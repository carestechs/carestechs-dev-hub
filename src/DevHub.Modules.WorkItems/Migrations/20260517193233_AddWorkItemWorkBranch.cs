using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevHub.Modules.WorkItems.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkItemWorkBranch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "work_branch",
                schema: "work_items",
                table: "work_items",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "work_branch",
                schema: "work_items",
                table: "work_items");
        }
    }
}
