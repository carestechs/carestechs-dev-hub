using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevHub.Modules.WorkItems.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "work_items");

            migrationBuilder.CreateTable(
                name: "checkpoint_signals",
                schema: "work_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    checkpoint_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    outcome = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: true),
                    signaled_by_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    signaled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    executor_response_status = table.Column<int>(type: "integer", nullable: true),
                    executor_response_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_checkpoint_signals", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "work_items",
                schema: "work_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    executor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    executor_correlation_marker = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    current_status = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    current_checkpoint_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    created_by_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_items", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_checkpoint_signals_work_item_id_idempotency_key",
                schema: "work_items",
                table: "checkpoint_signals",
                columns: new[] { "work_item_id", "idempotency_key" },
                unique: true,
                filter: "\"idempotency_key\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_checkpoint_signals_work_item_id_signaled_at",
                schema: "work_items",
                table: "checkpoint_signals",
                columns: new[] { "work_item_id", "signaled_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_work_items_executor_id_executor_correlation_marker",
                schema: "work_items",
                table: "work_items",
                columns: new[] { "executor_id", "executor_correlation_marker" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_items_project_id_current_status",
                schema: "work_items",
                table: "work_items",
                columns: new[] { "project_id", "current_status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "checkpoint_signals",
                schema: "work_items");

            migrationBuilder.DropTable(
                name: "work_items",
                schema: "work_items");
        }
    }
}
