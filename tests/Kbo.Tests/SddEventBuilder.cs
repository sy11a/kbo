using System.Globalization;
using System.Text.Json.Nodes;

namespace Kbo.Tests;

/// <summary>
/// Event set that makes every list and summary of the SDD panel non-empty (now = 2026-08-12, cutoff 2026-06-13).
/// </summary>
internal sealed class SddEventBuilder
{
    private const string RepoA = "/home/u/RepoA";
    private const string RepoB = "/home/u/RepoB";

    private readonly List<JsonObject> _events = [];
    private int _sequence;

    public List<JsonObject> Build()
    {
        AddSessions();
        AddSpecFirstAndCodeFirst();
        AddOtherWrites();
        AddSkills();
        return _events;
    }

    private void Add(string type, string time, string? subject, string session, JsonObject? data = null)
    {
        string id = string.Create(CultureInfo.InvariantCulture, $"01FS{_sequence++:D22}");
        _events.Add(DashboardTestEvents.Event(id, type, time, subject: subject, session: session, data: data));
    }

    private void Start(string session, string time, string repo)
    {
        Add("session.started", time, session, session, new JsonObject { ["branch"] = null, ["usage"] = null });
        _events[^1]["repo"] = repo;
    }

    private void AddSessions()
    {
        Start("s1", "2026-08-10T08:00:00Z", RepoA);
        Start("s2", "2026-08-10T08:00:00Z", RepoA);
        Start("s3", "2026-08-11T08:00:00Z", RepoA);
        Start("s4", "2026-08-03T08:00:00Z", RepoB);
        Start("s5", "2026-08-11T08:00:00Z", RepoB);
        Start("s9", "2026-06-01T08:00:00Z", RepoA);
    }

    private void AddSpecFirstAndCodeFirst()
    {
        Add("knowledge.read", "2026-08-10T09:00:00Z", RepoA + "/docs/superpowers/specs/x-design.md", "s1");
        Add("knowledge.written", "2026-08-10T10:00:00Z", RepoA + "/src/Program.cs", "s1");
        Add("knowledge.written", "2026-08-10T10:10:00Z", RepoA + "/docs/superpowers/plans/p.md", "s1");
        Add("knowledge.written", "2026-08-10T09:00:00Z", RepoA + "/src/Program.cs", "s2");
        Add("knowledge.written", "2026-08-10T10:00:00Z", RepoA + "/docs/cases/c1/case.md", "s2");
        Add("knowledge.read", "2026-08-03T09:00:00Z", RepoB + "/docs/cases/c2/case.md", "s4");
        Add("knowledge.written", "2026-08-03T10:00:00Z", RepoB + "/tools/run.py", "s4");
    }

    private void AddOtherWrites()
    {
        Add("knowledge.written", "2026-08-11T09:00:00Z", RepoA + "/src/A.cs", "s3");
        Add("knowledge.written", "2026-08-11T09:05:00Z", RepoA + "/web/app.ts", "s3");
        Add("knowledge.written", "2026-08-11T09:10:00Z", RepoA + "/appsettings.json", "s3");
        Add("knowledge.written", "2026-08-11T09:00:00Z", RepoB + "/src/B.cs", "s5");
        Add("knowledge.written", "2026-08-11T09:05:00Z", RepoB + "/ci/build.yaml", "s5");
        Add("knowledge.written", "2026-08-11T09:10:00Z", RepoB + "/data.bin", "s5");
        Add("knowledge.written", "2026-07-20T12:00:00Z", "/home/u/Unknown/main.go", "s6");
        Add("knowledge.written", "2026-08-10T10:30:00Z", RepoA + "/docs/ai/rules/core/okf.md", "s1");
        Add("knowledge.written", "2026-08-03T10:30:00Z", RepoB + "/docs/ai/baseline.md", "s4");
        Add("knowledge.written", "2026-06-01T09:00:00Z", RepoA + "/src/Old.cs", "s9");
    }

    private void AddSkills()
    {
        Add("skill.invoked", "2026-08-10T08:30:00Z", subject: null, "s1", new JsonObject { ["skill"] = "legislator" });
        Add("skill.invoked", "2026-08-10T08:30:00Z", subject: null, "s2", new JsonObject { ["skill"] = "tdd" });
        Add("skill.invoked", "2026-08-03T08:30:00Z", subject: null, "s4", new JsonObject { ["skill"] = "superpowers:brainstorm" });
        Add("skill.invoked", "2026-08-01T08:30:00Z", subject: null, "s7", new JsonObject { ["skill"] = "legislator" });
    }
}
