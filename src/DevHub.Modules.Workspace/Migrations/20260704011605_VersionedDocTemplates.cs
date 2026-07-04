using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevHub.Modules.Workspace.Migrations
{
    /// <inheritdoc />
    public partial class VersionedDocTemplates : Migration
    {
        // Seeded version 1 ID — referenced by ProjectService when creating new projects.
        private static readonly Guid SeedVersionId = new("84a1c665-2fa9-4212-b683-e56914b18f89");

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Drop the old flat project_docs table.
            migrationBuilder.DropTable(
                name: "project_docs",
                schema: "workspace");

            // 2. Create the new versioning tables before the FK column.
            migrationBuilder.CreateTable(
                name: "doc_template_versions",
                schema: "workspace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doc_template_versions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "doc_template_sections",
                schema: "workspace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    doc_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    section_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    hint = table.Column<string>(type: "text", nullable: true),
                    required = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doc_template_sections", x => x.id);
                    table.ForeignKey(
                        name: "fk_doc_template_sections_doc_template_versions_version_id",
                        column: x => x.version_id,
                        principalSchema: "workspace",
                        principalTable: "doc_template_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_doc_template_sections_version_id_doc_key_section_key",
                schema: "workspace",
                table: "doc_template_sections",
                columns: new[] { "version_id", "doc_key", "section_key" },
                unique: true);

            // 3. Seed version 1 (active) with 22 sections across 7 doc keys.
            var now = DateTimeOffset.UtcNow;
            migrationBuilder.Sql($@"
INSERT INTO workspace.doc_template_versions (id, version_number, is_active, notes, created_at, updated_at)
VALUES ('84a1c665-2fa9-4212-b683-e56914b18f89', 1, true, 'Initial version', '{now:O}', '{now:O}');

INSERT INTO workspace.doc_template_sections (id, version_id, doc_key, section_key, label, hint, required, display_order, created_at, updated_at) VALUES
-- stakeholder-definition (3)
('35bddade-1ce1-494a-aafe-496365e51947', '84a1c665-2fa9-4212-b683-e56914b18f89', 'stakeholder-definition', 'overview',    'Overview',              'Why this project exists and what problem it solves.',              true,  1, '{now:O}', '{now:O}'),
('493c4128-310f-438c-90b5-463610c55444', '84a1c665-2fa9-4212-b683-e56914b18f89', 'stakeholder-definition', 'personas',    'Personas & Stakeholders','Who benefits from the project and who owns it.',                  true,  2, '{now:O}', '{now:O}'),
('3b96de68-6aa5-4766-a636-858b281c6ea4', '84a1c665-2fa9-4212-b683-e56914b18f89', 'stakeholder-definition', 'out-of-scope','Out of Scope',           'Explicitly list what this project will not do.',                  false, 3, '{now:O}', '{now:O}'),
-- architecture (3)
('dec44c71-d1e9-4b62-8dd2-8b10dddaac19', '84a1c665-2fa9-4212-b683-e56914b18f89', 'architecture', 'system-overview', 'System Overview', 'High-level description of the system and its major components.',   true,  1, '{now:O}', '{now:O}'),
('b72f50b7-b0d0-4eae-94ce-9cfe8ce07b78', '84a1c665-2fa9-4212-b683-e56914b18f89', 'architecture', 'tech-stack',      'Tech Stack',       'Languages, frameworks, databases, and key infrastructure choices.', true,  2, '{now:O}', '{now:O}'),
('777f2ad6-d4dd-45fb-b5a6-44c2d21ac752', '84a1c665-2fa9-4212-b683-e56914b18f89', 'architecture', 'deployment',      'Deployment',       'How the system is deployed and what environments exist.',           false, 3, '{now:O}', '{now:O}'),
-- data-model (3)
('f9f7a0ee-84ac-4ea3-9dae-9aefd452c7da', '84a1c665-2fa9-4212-b683-e56914b18f89', 'data-model', 'entities',    'Entities & Relationships', 'Core entities, their fields, and how they relate.',          true,  1, '{now:O}', '{now:O}'),
('40ab42df-5a2e-4fb4-b9c8-fbe9566334a1', '84a1c665-2fa9-4212-b683-e56914b18f89', 'data-model', 'constraints', 'Business Constraints',     'Invariants, uniqueness rules, and validation requirements.',  false, 2, '{now:O}', '{now:O}'),
('c61e8350-3691-4e4b-b4e1-a8f1984c83f9', '84a1c665-2fa9-4212-b683-e56914b18f89', 'data-model', 'migrations',  'Migration Strategy',       'How schema changes are applied and rolled back.',             false, 3, '{now:O}', '{now:O}'),
-- api-spec (3)
('78f04edf-7b30-4381-8c49-2757373b787f', '84a1c665-2fa9-4212-b683-e56914b18f89', 'api-spec', 'endpoints',      'Endpoints',                    'Route list with request/response shapes and status codes.',   true,  1, '{now:O}', '{now:O}'),
('8de8aca9-c912-4fb5-b087-441f443d0bf1', '84a1c665-2fa9-4212-b683-e56914b18f89', 'api-spec', 'auth',           'Authentication & Authorization','How callers authenticate and what roles/scopes are checked.',  true,  2, '{now:O}', '{now:O}'),
('792d203a-70ad-4a0f-9cb1-a09ecaaba974', '84a1c665-2fa9-4212-b683-e56914b18f89', 'api-spec', 'error-handling', 'Error Handling',               'Error envelope format and common error codes.',               false, 3, '{now:O}', '{now:O}'),
-- ui-specification (4)
('1711c65b-b2f0-4408-8df6-7c85f65082bf', '84a1c665-2fa9-4212-b683-e56914b18f89', 'ui-specification', 'screens',       'Screens & Navigation', 'All screens, their URLs, and the navigation between them.', true,  1, '{now:O}', '{now:O}'),
('1c7829ee-f72f-42a2-8fb4-e65725a385e1', '84a1c665-2fa9-4212-b683-e56914b18f89', 'ui-specification', 'components',    'Shared Components',    'Reusable UI components and their variants.',                false, 2, '{now:O}', '{now:O}'),
('53e319d0-7609-4674-b6c6-130e3777cfca', '84a1c665-2fa9-4212-b683-e56914b18f89', 'ui-specification', 'design-tokens', 'Design Tokens',        'Colors, typography, and spacing scale.',                    false, 3, '{now:O}', '{now:O}'),
('86c214ad-7382-4b00-ad33-83bc598386d2', '84a1c665-2fa9-4212-b683-e56914b18f89', 'ui-specification', 'interactions',  'Interactions & States','Hover, focus, loading, empty, and error states per screen.', true,  4, '{now:O}', '{now:O}'),
-- primary-user-persona (3)
('656bfd84-b159-4d54-8d10-ff53eadd9339', '84a1c665-2fa9-4212-b683-e56914b18f89', 'primary-user-persona', 'profile',     'User Profile',    'Who the primary user is — role, context, and background.',          true,  1, '{now:O}', '{now:O}'),
('916de14a-c952-4f39-9fbb-8ffae2f4b73a', '84a1c665-2fa9-4212-b683-e56914b18f89', 'primary-user-persona', 'goals',       'Goals & Motivations','What the user wants to achieve and why.',                        true,  2, '{now:O}', '{now:O}'),
('4551e749-c4e7-469b-bd43-0ae94d607bb0', '84a1c665-2fa9-4212-b683-e56914b18f89', 'primary-user-persona', 'pain-points', 'Pain Points',        'Frustrations and obstacles the user faces without this product.', false, 3, '{now:O}', '{now:O}'),
-- claude-md (3)
('7df02321-0dac-4d54-af2f-5056e62b6a2f', '84a1c665-2fa9-4212-b683-e56914b18f89', 'claude-md', 'conventions', 'Code Conventions', 'Naming, file structure, and style rules for the project.',      true,  1, '{now:O}', '{now:O}'),
('3d4db5dc-2d38-493f-a7c0-e662d230b3a9', '84a1c665-2fa9-4212-b683-e56914b18f89', 'claude-md', 'patterns',    'Patterns to Follow','Architecture and design patterns enforced in this codebase.',   true,  2, '{now:O}', '{now:O}'),
('a3daa2b5-3a08-42a1-bb8f-9f663ed3ab9e', '84a1c665-2fa9-4212-b683-e56914b18f89', 'claude-md', 'commands',    'Common Commands',   'Build, test, and run commands for local development.',           false, 3, '{now:O}', '{now:O}');
");

            // 4. Wipe all existing projects — approved: no backfill needed for dev data.
            migrationBuilder.Sql("DELETE FROM workspace.projects;");

            // 5. Now safe to add the NOT NULL FK column (table is empty).
            migrationBuilder.AddColumn<Guid>(
                name: "doc_template_version_id",
                schema: "workspace",
                table: "projects",
                type: "uuid",
                nullable: false,
                defaultValue: SeedVersionId);

            // 6. Create project_doc_sections (depends on projects column existing).
            migrationBuilder.CreateTable(
                name: "project_doc_sections",
                schema: "workspace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    section_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "text", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_doc_sections", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_doc_sections_doc_template_sections_section_id",
                        column: x => x.section_id,
                        principalSchema: "workspace",
                        principalTable: "doc_template_sections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_doc_sections_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "workspace",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // 7. Remaining indexes and the projects FK.
            migrationBuilder.CreateIndex(
                name: "ix_projects_doc_template_version_id",
                schema: "workspace",
                table: "projects",
                column: "doc_template_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_doc_sections_project_id_section_id",
                schema: "workspace",
                table: "project_doc_sections",
                columns: new[] { "project_id", "section_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_doc_sections_section_id",
                schema: "workspace",
                table: "project_doc_sections",
                column: "section_id");

            migrationBuilder.AddForeignKey(
                name: "fk_projects_doc_template_versions_doc_template_version_id",
                schema: "workspace",
                table: "projects",
                column: "doc_template_version_id",
                principalSchema: "workspace",
                principalTable: "doc_template_versions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_projects_doc_template_versions_doc_template_version_id",
                schema: "workspace",
                table: "projects");

            migrationBuilder.DropTable(
                name: "project_doc_sections",
                schema: "workspace");

            migrationBuilder.DropTable(
                name: "doc_template_sections",
                schema: "workspace");

            migrationBuilder.DropTable(
                name: "doc_template_versions",
                schema: "workspace");

            migrationBuilder.DropIndex(
                name: "ix_projects_doc_template_version_id",
                schema: "workspace",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "doc_template_version_id",
                schema: "workspace",
                table: "projects");

            migrationBuilder.CreateTable(
                name: "project_docs",
                schema: "workspace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    doc_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_docs", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_docs_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "workspace",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_project_docs_project_id_doc_key",
                schema: "workspace",
                table: "project_docs",
                columns: new[] { "project_id", "doc_key" },
                unique: true);
        }
    }
}
