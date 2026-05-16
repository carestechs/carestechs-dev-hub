using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevHub.Modules.Notifications.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notifications");

            migrationBuilder.CreateTable(
                name: "pending_action_signals",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    checkpoint_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    dismissed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pending_action_signals", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pending_action_signals_member_id_project_id",
                schema: "notifications",
                table: "pending_action_signals",
                columns: new[] { "member_id", "project_id" },
                filter: "\"dismissed_at\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pending_action_signals_member_id_work_item_id_checkpoint_key",
                schema: "notifications",
                table: "pending_action_signals",
                columns: new[] { "member_id", "work_item_id", "checkpoint_key" },
                unique: true,
                filter: "\"dismissed_at\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pending_action_signals",
                schema: "notifications");
        }
    }
}
