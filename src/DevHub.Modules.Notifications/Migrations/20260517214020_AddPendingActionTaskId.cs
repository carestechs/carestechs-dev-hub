using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevHub.Modules.Notifications.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingActionTaskId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_pending_action_signals_member_id_work_item_id_checkpoint_key",
                schema: "notifications",
                table: "pending_action_signals");

            migrationBuilder.AddColumn<string>(
                name: "task_id",
                schema: "notifications",
                table: "pending_action_signals",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_pending_action_signals_member_id_work_item_id_checkpoint_key",
                schema: "notifications",
                table: "pending_action_signals",
                columns: new[] { "member_id", "work_item_id", "checkpoint_key" });

            // FEAT-009 / T-064: active-row uniqueness is per-task. The expression
            // COALESCE(task_id, '<root>') folds NULLs into a literal sentinel so two
            // rows with task_id=NULL (legacy / non-perTask) still collide as one,
            // while distinct task_ids coexist. Not expressible via EF's HasIndex
            // builder — raw SQL is the path.
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ""ux_pending_action_signals_active_per_task""
                    ON ""notifications"".""pending_action_signals"" (
                        ""member_id"",
                        ""work_item_id"",
                        ""checkpoint_key"",
                        COALESCE(""task_id"", '<root>')
                    )
                    WHERE ""dismissed_at"" IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""notifications"".""ux_pending_action_signals_active_per_task"";");

            migrationBuilder.DropIndex(
                name: "ix_pending_action_signals_member_id_work_item_id_checkpoint_key",
                schema: "notifications",
                table: "pending_action_signals");

            migrationBuilder.DropColumn(
                name: "task_id",
                schema: "notifications",
                table: "pending_action_signals");

            migrationBuilder.CreateIndex(
                name: "ix_pending_action_signals_member_id_work_item_id_checkpoint_key",
                schema: "notifications",
                table: "pending_action_signals",
                columns: new[] { "member_id", "work_item_id", "checkpoint_key" },
                unique: true,
                filter: "\"dismissed_at\" IS NULL");
        }
    }
}
