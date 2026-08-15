using Kbo.Gold;

namespace Kbo.Tests;

public class NoteRoleTests
{
    [Theory]
    [InlineData("/r/CareerPlatform/docs/superpowers/plans/2026-06-27-kanban-a.md")]
    [InlineData("/r/CareerPlatform/docs/superpowers/specs/career-page.md")]
    [InlineData("/r/kbo/docs/journal/2026-08-11.md")]
    public void Executed_plans_specs_and_journals_are_lifecycle(string path)
    {
        Assert.Equal(NoteRole.Lifecycle, NoteRole.Of(path));
    }

    [Theory]
    [InlineData("/home/admin/Knowledge/homelab-sec/Glossary/Beacon.md")]
    [InlineData("/r/CareerPlatform/docs/okf/tenancy/tenant-resolution.md")]
    [InlineData("/r/CareerPlatform/docs/adr/0001-record-architecture-decisions.md")]
    [InlineData("/r/X/docs/planshet.md")]
    public void Reference_notes_including_adrs_are_reference(string path)
    {
        Assert.Equal(NoteRole.Reference, NoteRole.Of(path));
    }
}
