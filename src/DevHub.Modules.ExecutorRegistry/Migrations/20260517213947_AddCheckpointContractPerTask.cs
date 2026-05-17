using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevHub.Modules.ExecutorRegistry.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckpointContractPerTask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "per_task",
                schema: "executor_registry",
                table: "checkpoint_contracts",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "per_task",
                schema: "executor_registry",
                table: "checkpoint_contracts");
        }
    }
}
