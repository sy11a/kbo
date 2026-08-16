using Kbo.Gold;

namespace Kbo.Tests;

public class NoteRoleTests
{
    [Theory]
    [InlineData("/r/SomeApp/docs/superpowers/plans/2026-06-27-kanban-a.md")]
    [InlineData("/r/SomeApp/docs/superpowers/specs/feature-page.md")]
    [InlineData("/r/kbo/docs/journal/2026-08-11.md")]
    public void Executed_plans_specs_and_journals_are_lifecycle(string path)
    {
        Assert.Equal(NoteRole.Lifecycle, NoteRole.Of(path));
    }

    [Theory]
    [InlineData("/home/admin/Knowledge/homelab-sec/Glossary/Beacon.md")]
    [InlineData("/r/SomeApp/docs/okf/tenancy/tenant-resolution.md")]
    [InlineData("/r/SomeApp/docs/adr/0001-record-architecture-decisions.md")]
    [InlineData("/r/X/docs/planshet.md")]
    public void Reference_notes_including_adrs_are_reference(string path)
    {
        Assert.Equal(NoteRole.Reference, NoteRole.Of(path));
    }

    [Theory]
    [InlineData("/r/X/docs/ai/rules/core/okf.md")]
    [InlineData("/r/X/docs/ai/rules/stacks/dotnet/architecture.md")]
    [InlineData("/r/X/docs/adr/template.md")]
    public void Fleet_law_and_scaffolding_templates_are_machine_managed(string path)
    {
        Assert.Equal(NoteRole.MachineManaged, NoteRole.Of(path));
    }

    [Theory]
    [InlineData("/r/X/docs/aid/notes.md")]
    [InlineData("/r/X/docs/adr/template-usage-guide.md")]
    public void Near_miss_paths_stay_reference(string path)
    {
        Assert.Equal(NoteRole.Reference, NoteRole.Of(path));
    }
}
