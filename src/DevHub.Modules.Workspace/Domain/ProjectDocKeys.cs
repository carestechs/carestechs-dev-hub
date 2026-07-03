namespace DevHub.Modules.Workspace.Domain;

public static class ProjectDocKeys
{
    public const string StakeholderDefinition = "stakeholder-definition";
    public const string Architecture          = "architecture";
    public const string DataModel             = "data-model";
    public const string ApiSpec               = "api-spec";
    public const string UiSpecification       = "ui-specification";
    public const string PrimaryUserPersona    = "primary-user-persona";
    public const string ClaudeMd              = "claude-md";

    public static readonly IReadOnlyList<DocMeta> All =
    [
        new(StakeholderDefinition, "Stakeholder Definition",
            "Why this project exists, who it's for, and what is firmly out of scope.",
            "Executive summary · Value proposition · Core business problem · Success criteria · Scope lock · Guiding principles"),

        new(Architecture, "Architecture",
            "System structure — modules, services, and how data flows between them.",
            "Architectural style · Key modules and responsibilities · Data flow · External dependencies"),

        new(DataModel, "Data Model",
            "Entities, fields, relationships, and business constraints.",
            "Entity definitions · Fields and types · Relationships · Validation rules"),

        new(ApiSpec, "API Specification",
            "Endpoint contracts — routes, request/response shapes, and status codes.",
            "Endpoint list · Request DTOs · Response DTOs · Auth requirements · Error responses"),

        new(UiSpecification, "UI Specification",
            "Screens, components, interactions, and visual states.",
            "Screen inventory · Component hierarchy · Interactions · States · Design system"),

        new(PrimaryUserPersona, "Primary User Persona",
            "Who the primary user is — their goals, context, and pain points.",
            "Role and background · Goals · Pain points · Behaviours · Success criteria"),

        new(ClaudeMd, "CLAUDE.md",
            "Implementation conventions, patterns to follow, and anti-patterns to avoid.",
            "Tech stack · Code style · Naming conventions · Patterns · Anti-patterns · Testing conventions"),
    ];

    public static readonly IReadOnlySet<string> ValidKeys =
        new HashSet<string>(All.Select(d => d.Key), StringComparer.Ordinal);

    public static bool IsValid(string key) => ValidKeys.Contains(key);

    public sealed record DocMeta(string Key, string Label, string Description, string Hints);
}
