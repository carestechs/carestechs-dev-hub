using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevHub.Modules.WorkItems.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkItemExecutorRunId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "executor_run_id",
                schema: "work_items",
                table: "work_items",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "executor_run_id",
                schema: "work_items",
                table: "work_items");
        }
    }
}
