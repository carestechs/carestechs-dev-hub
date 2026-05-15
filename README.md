# Project Scaffold

This scaffold provides a ready-to-copy project documentation structure for the AI Task Framework v2.

## How to Use

### Option A: New Project

1. Copy the entire `scaffold/` contents into your project root
2. Follow [`.ai-framework/guides/getting-started.md`](.ai-framework/guides/getting-started.md) — start with the **New Project** path
3. Fill system templates in order: Persona → Stakeholder → CLAUDE.md → Architecture → Data Model → API Spec → UI Spec
4. Write work items in `docs/work-items/` as needed (Feature Briefs, Bug Reports, Improvement Proposals)

### Option B: Existing Codebase

1. Copy the entire `scaffold/` contents into your project root
2. Follow [`.ai-framework/guides/getting-started.md`](.ai-framework/guides/getting-started.md) — start with the **Existing Codebase** path
3. Fill system templates in order: CLAUDE.md → Stakeholder → Architecture → Data Model → API Spec → UI Spec → Persona
4. Write work items in `docs/work-items/` as needed

## What You Get

```
your-project/
├── CLAUDE.md                          # Code conventions & project context (with AI framework routing)
├── .ai-framework/                     # Bundled framework reference (don't edit)
│   ├── VERSION                        # Framework version for upgrade tracking
│   ├── README.md                      # What this folder is and how to use it
│   ├── templates/                     # Full reference templates
│   ├── prompts/                       # Prompt templates for AI task generation
│   └── guides/                        # Workflow guides
└── docs/
    ├── personas/
    │   └── primary-user.md            # Target user definition
    ├── stakeholder-definition.md      # Product vision, scope & success criteria
    ├── ARCHITECTURE.md                # System structure & technical decisions
    ├── data-model.md                  # Domain entities, fields & relationships
    ├── api-spec.md                    # REST API endpoints & contracts
    ├── ui-specification.md            # Screen layouts, components & interactions
    └── work-items/                    # Work item documents
        ├── FEAT-001-example.md        # Feature brief template
        ├── BUG-001-example.md         # Bug report template
        └── IMP-001-example.md         # Improvement proposal template
```

## Scaffold vs Full Templates

| | Scaffold (this) | Full Templates (`.ai-framework/templates/`) |
|---|---|---|
| **Purpose** | Quick start — fill in the essentials | Reference — comprehensive coverage |
| **Time to fill** | 15-30 min per file | 1-2 hours per file |
| **Detail level** | Section headers + fill prompts | Full guidance, examples, edge cases |
| **When to use** | Starting a project | Deepening documentation later |

## Tips

- **Don't overthink it.** A rough first pass beats a perfect blank page. You can always refine later.
- **Use the full templates as reference.** If a scaffold section feels unclear, check the corresponding file in `.ai-framework/templates/` for detailed guidance.
- **CLAUDE.md is the most frequently used.** Even a minimal version helps AI generate better code immediately.
- **Generate specs from docs.** After filling Stakeholder + Architecture + CLAUDE.md, use `.ai-framework/prompts/spec-generation.md` to auto-generate Data Model and API Spec, then `.ai-framework/prompts/ui-spec-generation.md` for the UI Spec.
- **Update as you go.** These are living documents. Revisit them as your project evolves (see [`.ai-framework/guides/maintenance.md`](.ai-framework/guides/maintenance.md)).
- **Upgrading the framework.** Check `.ai-framework/VERSION` to see which version you have. To upgrade, replace the `.ai-framework/` folder with the latest version from the framework repo — your project docs are unaffected.
