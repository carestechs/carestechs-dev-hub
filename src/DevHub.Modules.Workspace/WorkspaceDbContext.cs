using DevHub.Modules.Workspace.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Modules.Workspace;

public sealed class WorkspaceDbContext(DbContextOptions<WorkspaceDbContext> options) : DbContext(options)
{
    public const string SchemaName = "workspace";

    public DbSet<Member> Members => Set<Member>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMembership> ProjectMemberships => Set<ProjectMembership>();
    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();
    public DbSet<WorkspaceRoleAssignment> WorkspaceRoleAssignments => Set<WorkspaceRoleAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<Member>(b =>
        {
            b.Property(m => m.DisplayName).HasMaxLength(120).IsRequired();
            b.Property(m => m.Email).HasMaxLength(255).IsRequired();
            b.Property(m => m.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            b.HasIndex(m => m.Email).IsUnique().HasFilter("deleted_at IS NULL");
            b.HasQueryFilter(m => m.DeletedAt == null);
        });

        modelBuilder.Entity<Role>(b =>
        {
            b.Property(r => r.Key).HasMaxLength(60).IsRequired();
            b.Property(r => r.Name).HasMaxLength(120).IsRequired();
            b.HasIndex(r => r.Key).IsUnique();
        });

        modelBuilder.Entity<Team>(b =>
        {
            b.Property(t => t.Name).HasMaxLength(120).IsRequired();
            b.HasIndex(t => t.Name).IsUnique().HasFilter("deleted_at IS NULL");
            b.HasQueryFilter(t => t.DeletedAt == null);
        });

        modelBuilder.Entity<Project>(b =>
        {
            b.Property(p => p.Name).HasMaxLength(120).IsRequired();
            b.Property(p => p.Slug).HasMaxLength(60).IsRequired();
            b.Property(p => p.ProjectType).HasMaxLength(60).IsRequired();
            b.HasIndex(p => p.Slug).IsUnique().HasFilter("deleted_at IS NULL");
            b.HasIndex(p => p.Name).IsUnique().HasFilter("deleted_at IS NULL");
            b.HasIndex(p => p.OwningTeamId);
            b.HasIndex(p => p.ProjectType);
            b.HasQueryFilter(p => p.DeletedAt == null);
        });

        modelBuilder.Entity<ProjectMembership>(b =>
        {
            b.HasIndex(pm => new { pm.ProjectId, pm.MemberId })
             .IsUnique()
             .HasFilter("deleted_at IS NULL");
            b.HasQueryFilter(pm => pm.DeletedAt == null);
        });

        modelBuilder.Entity<RoleAssignment>(b =>
        {
            b.HasIndex(ra => new { ra.ProjectMembershipId, ra.RoleId })
             .IsUnique()
             .HasFilter("deleted_at IS NULL");
            b.HasQueryFilter(ra => ra.DeletedAt == null);
        });

        modelBuilder.Entity<WorkspaceRoleAssignment>(b =>
        {
            b.HasIndex(w => new { w.MemberId, w.RoleId })
             .IsUnique()
             .HasFilter("deleted_at IS NULL");
            b.HasQueryFilter(w => w.DeletedAt == null);
        });

        base.OnModelCreating(modelBuilder);
    }
}
