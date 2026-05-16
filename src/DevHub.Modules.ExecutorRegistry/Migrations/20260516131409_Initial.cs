using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevHub.Modules.ExecutorRegistry.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "executor_registry");

            migrationBuilder.CreateTable(
                name: "bindings",
                schema: "executor_registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    executor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bindings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "executors",
                schema: "executor_registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    display_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    base_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    credentials_ref = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_executors", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "checkpoint_contracts",
                schema: "executor_registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    executor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    checkpoint_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    display_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    required_role_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    allowed_outcomes_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_checkpoint_contracts", x => x.id);
                    table.ForeignKey(
                        name: "fk_checkpoint_contracts_executors_executor_id",
                        column: x => x.executor_id,
                        principalSchema: "executor_registry",
                        principalTable: "executors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bindings_executor_id",
                schema: "executor_registry",
                table: "bindings",
                column: "executor_id");

            migrationBuilder.CreateIndex(
                name: "ix_bindings_project_type",
                schema: "executor_registry",
                table: "bindings",
                column: "project_type",
                unique: true,
                filter: "\"deleted_at\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_checkpoint_contracts_executor_id_checkpoint_key",
                schema: "executor_registry",
                table: "checkpoint_contracts",
                columns: new[] { "executor_id", "checkpoint_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_executors_key",
                schema: "executor_registry",
                table: "executors",
                column: "key",
                unique: true,
                filter: "\"deleted_at\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bindings",
                schema: "executor_registry");

            migrationBuilder.DropTable(
                name: "checkpoint_contracts",
                schema: "executor_registry");

            migrationBuilder.DropTable(
                name: "executors",
                schema: "executor_registry");
        }
    }
}
