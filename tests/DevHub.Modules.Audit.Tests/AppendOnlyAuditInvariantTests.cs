using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace DevHub.Modules.Audit.Tests;

/// FEAT-006 AC-5: <c>audit.audit_entries</c> is append-only. This test scans every <c>.cs</c>
/// file under <c>src/</c> and fails if any line mutates audit rows. If a future requirement
/// genuinely needs to update audit data, write an explicit migration with justification and
/// extend the allowlist below.
public class AppendOnlyAuditInvariantTests
{
    private static readonly Regex[] Forbidden =
    {
        // Tracked-entity mutations on the AuditEntries DbSet.
        new(@"AuditEntries\s*\.\s*(Update|Remove|RemoveRange|UpdateRange)\b", RegexOptions.Compiled),
        // Bulk-update / bulk-delete against the audit_entries table.
        new(@"audit_entries.*ExecuteUpdate|audit_entries.*ExecuteDelete", RegexOptions.Compiled | RegexOptions.IgnoreCase),
    };

    [Fact]
    public void No_source_file_mutates_audit_entries()
    {
        var srcRoot = LocateSrcRoot();
        var violations = new List<string>();

        foreach (var file in Directory.GetFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            // Skip EF-generated migrations — Initial only INSERTs against audit_entries.
            if (file.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
            // Skip bin/obj.
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;

            var text = File.ReadAllText(file);
            foreach (var re in Forbidden)
            {
                if (re.IsMatch(text)) violations.Add($"{file}: matched /{re}/");
            }
        }

        violations.Should().BeEmpty(
            "audit.audit_entries is append-only — only AuditWriter.Add(...) is permitted. " +
            "If you have a legitimate need to mutate audit rows, write a migration with explicit " +
            "justification and update the allowlist on this test.");
    }

    private static string LocateSrcRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }
        if (dir is null) throw new InvalidOperationException("Could not locate the repo's src/ directory from the test bin path.");
        return Path.Combine(dir.FullName, "src");
    }
}
