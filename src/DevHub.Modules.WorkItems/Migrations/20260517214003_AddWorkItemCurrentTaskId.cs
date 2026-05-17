using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevHub.Modules.WorkItems.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkItemCurrentTaskId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "current_task_id",
                schema: "work_items",
                table: "work_items",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "current_task_id",
                schema: "work_items",
                table: "work_items");
        }
    }
}
