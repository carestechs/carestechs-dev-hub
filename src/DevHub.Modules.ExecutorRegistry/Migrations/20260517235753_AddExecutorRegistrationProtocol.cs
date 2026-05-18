using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevHub.Modules.ExecutorRegistry.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutorRegistrationProtocol : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "protocol",
                schema: "executor_registry",
                table: "executors",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "devhub");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "protocol",
                schema: "executor_registry",
                table: "executors");
        }
    }
}
