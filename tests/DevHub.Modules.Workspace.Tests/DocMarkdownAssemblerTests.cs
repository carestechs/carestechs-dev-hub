using DevHub.Modules.Workspace.Services;
using FluentAssertions;
using Xunit;

namespace DevHub.Modules.Workspace.Tests;

/// <summary>
/// Unit tests for ProjectDocsService.AssembleDocMarkdown — no DB, no HTTP.
/// </summary>
public class DocMarkdownAssemblerTests
{
    [Fact]
    public void Single_section_with_content_renders_correctly()
    {
        var result = ProjectDocsService.AssembleDocMarkdown(
            "Architecture",
            [("System Overview", "This is the overview.")]);

        result.Should().StartWith("# Architecture");
        result.Should().Contain("## System Overview");
        result.Should().Contain("This is the overview.");
    }

    [Fact]
    public void Section_with_null_content_renders_heading_only()
    {
        var result = ProjectDocsService.AssembleDocMarkdown(
            "Data Model",
            [("Entities", null)]);

        result.Should().Contain("## Entities");
        // No content block — just the heading.
        var lines = result.Split('\n').Select(l => l.TrimEnd()).ToList();
        var entityLineIdx = lines.FindIndex(l => l == "## Entities");
        entityLineIdx.Should().BeGreaterThan(0);
        // All lines after the heading should be blank or end of string.
        lines.Skip(entityLineIdx + 1).Where(l => l.Length > 0).Should().BeEmpty();
    }

    [Fact]
    public void Section_with_whitespace_content_renders_heading_only()
    {
        var result = ProjectDocsService.AssembleDocMarkdown(
            "API Spec",
            [("Endpoints", "   ")]);

        result.Should().Contain("## Endpoints");
        result.Should().NotContain("   ");
    }

    [Fact]
    public void Multiple_sections_rendered_in_input_order()
    {
        var result = ProjectDocsService.AssembleDocMarkdown(
            "CLAUDE.md",
            [("Conventions", "content A"), ("Patterns", "content B"), ("Anti-Patterns", "content C")]);

        var convIdx = result.IndexOf("## Conventions", StringComparison.Ordinal);
        var pattIdx = result.IndexOf("## Patterns", StringComparison.Ordinal);
        var antiIdx = result.IndexOf("## Anti-Patterns", StringComparison.Ordinal);

        convIdx.Should().BeLessThan(pattIdx);
        pattIdx.Should().BeLessThan(antiIdx);
    }

    [Fact]
    public void Output_ends_with_trailing_newline()
    {
        var result = ProjectDocsService.AssembleDocMarkdown(
            "Any Doc",
            [("Section", "content")]);

        result.Should().EndWith("\n");
    }

    [Fact]
    public void Empty_sections_list_renders_only_title()
    {
        var result = ProjectDocsService.AssembleDocMarkdown("Title Only", []);

        result.Should().StartWith("# Title Only");
        result.Should().NotContain("##");
    }
}
