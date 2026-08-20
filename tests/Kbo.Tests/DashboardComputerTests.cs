using System.Globalization;
using System.Text.Json.Nodes;
using Kbo.Bronze;
using Kbo.Gold;
using Kbo.Registry;
using Kbo.Silver;

namespace Kbo.Tests;

public class DashboardComputerTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-12T22:00:00Z", CultureInfo.InvariantCulture);

    private readonly string workspace;
    private readonly string silverPath;
    private readonly KnowledgeRegistry registry;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    public DashboardComputerTests()
    {
        workspace = Directory.CreateTempSubdirectory("kbo-dashboard-tests").FullName;
        silverPath = Path.Combine(workspace, "silver.duckdb");
        registry = KnowledgeRegistry.Parse($"""
            machine: test-machine
            sources:
              - id: vault
                layer: global
                root: {Path.Combine(workspace, "Knowledge")}
              - id: skills
                layer: skills
                root: {Path.Combine(workspace, "skills")}
            """);
    }

    public void Dispose()
    {
        Directory.Delete(workspace, recursive: true);
    }

    private static JsonObject Event(string id, string type, string time, string? kbroot = null, string? subject = null,
        string? session = "sess-1", string agent = "claude-code", JsonObject? data = null)
    {
        JsonObject eventData = data ?? new JsonObject();
        eventData["origin"] = "harvest";
        eventData["transcript"] = "t-" + id[^2..];
        return new JsonObject
        {
            ["id"] = id,
            ["type"] = type,
            ["time"] = time,
            ["subject"] = subject,
            ["machine"] = "test-machine",
            ["agent"] = agent,
            ["session"] = session,
            ["kbroot"] = kbroot,
            ["data"] = eventData,
        };
    }

    private DashboardGold Compute(params JsonObject[] events)
    {
        string eventsRepo = Path.Combine(workspace, "kb-events");
        new BronzeStore(eventsRepo).Append(events);
        SilverRebuilder.Rebuild(eventsRepo, silverPath);
        return DashboardComputer.Compute(silverPath, registry, new FixedTimeProvider(Now));
    }

    [Fact]
    public void DeadManTiles_RedAfterThreeDaysOfSilence()
    {
        DashboardGold gold = Compute(
            Event("01F00000000000000000000001", "job.completed", "2026-08-12T00:10:00Z", agent: "kbo", subject: "harvest",
                session: null, data: new JsonObject { ["job"] = "harvest", ["duration_ms"] = 5 }),
            Event("01F00000000000000000000002", "job.completed", "2026-08-08T00:10:00Z", agent: "kbo", subject: "backup",
                session: null, data: new JsonObject { ["job"] = "backup", ["duration_ms"] = 5 }));

        JobHealthTile harvest = gold.JobHealth.Single(tile => tile.Job == "harvest");
        JobHealthTile backup = gold.JobHealth.Single(tile => tile.Job == "backup");
        Assert.Equal("ok", harvest.Status);
        Assert.Equal("red", backup.Status);
        Assert.True(backup.DaysSilent > 3);
    }

    [Fact]
    public void DeadManTiles_WeeklyJobsRedOnlyPastTheWeeklyThreshold()
    {
        DashboardGold gold = Compute(
            Event("01F00000000000000000000090", "job.completed", "2026-08-08T22:00:00Z", agent: "kbo", subject: "audit",
                session: null, data: new JsonObject { ["job"] = "audit", ["duration_ms"] = 5 }),
            Event("01F00000000000000000000091", "job.completed", "2026-08-02T00:00:00Z", agent: "kbo", subject: "report",
                session: null, data: new JsonObject { ["job"] = "report", ["duration_ms"] = 5 }));

        JobHealthTile audit = gold.JobHealth.Single(tile => tile.Job == "audit");
        JobHealthTile report = gold.JobHealth.Single(tile => tile.Job == "report");
        Assert.Equal("ok", audit.Status);
        Assert.Equal("red", report.Status);
    }

    [Fact]
    public void DeadManTiles_UnknownJobDefaultsToTheDailyThreshold()
    {
        // Events from a retired job must err toward the cheap error: flag too
        // early (daily rule), never too late.
        DashboardGold gold = Compute(
            Event("01F00000000000000000000092", "job.completed", "2026-08-08T22:00:00Z", agent: "kbo", subject: "some-retired-job",
                session: null, data: new JsonObject { ["job"] = "some-retired-job", ["duration_ms"] = 5 }));

        Assert.Equal("red", gold.JobHealth.Single(tile => tile.Job == "some-retired-job").Status);
    }

    [Fact]
    public void DeadMan_CadenceMapDeclaresReportAndAuditWeekly()
    {
        Assert.Equal(Kbo.Jobs.JobCadence.Weekly, Kbo.Jobs.JobDeadMan.CadenceOf("report"));
        Assert.Equal(Kbo.Jobs.JobCadence.Weekly, Kbo.Jobs.JobDeadMan.CadenceOf("audit"));
        Assert.Equal(Kbo.Jobs.JobCadence.Daily, Kbo.Jobs.JobDeadMan.CadenceOf("harvest"));
        Assert.Equal(Kbo.Jobs.PulseRunner.WeeklyDueDays + Kbo.Jobs.JobDeadMan.GraceDays, Kbo.Jobs.JobDeadMan.WeeklyThresholdDays);
    }

    [Fact]
    public void ServiceSessions_AreExcludedFromPracticeLensesButCounted()
    {
        // A service session (agent_mode service-*) reads a registered note; a
        // practice session reads another. Practice lenses must only see the
        // practice read, the summary must count the service session, and
        // last-seen must still see the service agent's events (ADR-0039).
        DashboardGold gold = Compute(
            Event("01F00000000000000000000060", "session.started", "2026-08-12T09:00:00Z", session: "svc-1", agent: "opencode",
                data: new JsonObject { ["raw"] = new JsonObject { ["agent_mode"] = "service-fleet" } }),
            Event("01F00000000000000000000061", "knowledge.read", "2026-08-12T09:01:00Z", session: "svc-1", agent: "opencode",
                subject: Path.Combine(workspace, "Knowledge", "a.md"), kbroot: "vault"),
            Event("01F00000000000000000000062", "session.started", "2026-08-12T10:00:00Z", session: "prac-1",
                data: new JsonObject { ["raw"] = new JsonObject { ["agent_mode"] = "build" } }),
            Event("01F00000000000000000000063", "knowledge.read", "2026-08-12T10:01:00Z", session: "prac-1",
                subject: Path.Combine(workspace, "Knowledge", "b.md"), kbroot: "vault"));

        Assert.Equal(1, gold.ReadsByLayerDaily.Sum(row => row.Reads));
        KbTouchRow day = Assert.Single(gold.KbTouchDaily);
        Assert.Equal(1, day.Sessions);
        Assert.Equal(1, gold.ServiceSessions.Sessions);
        Assert.Equal("service-fleet", gold.ServiceSessions.Agents);
        Assert.Contains(gold.LastSeen, tile => tile.Agent == "opencode");
    }

    [Fact]
    public void ServiceSessions_ZeroWhenNoneMarked()
    {
        DashboardGold gold = Compute(
            Event("01F00000000000000000000064", "session.started", "2026-08-12T10:00:00Z", session: "prac-1",
                data: new JsonObject { ["raw"] = new JsonObject { ["agent_mode"] = "build" } }));

        Assert.Equal(0, gold.ServiceSessions.Sessions);
    }

    [Fact]
    public void LastSeenTiles_TrackNewestEventPerAgent()
    {
        DashboardGold gold = Compute(
            Event("01F00000000000000000000003", "knowledge.read", "2026-08-12T10:00:00Z", subject: "/x.md"),
            Event("01F00000000000000000000004", "knowledge.read", "2026-08-01T10:00:00Z", subject: "/y.md"));

        LastSeenTile claudeCode = gold.LastSeen.Single(tile => tile.Agent == "claude-code");
        Assert.Equal("test-machine", claudeCode.Machine);
        Assert.Equal("2026-08-12", claudeCode.LastEvent.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Assert.Equal("ok", claudeCode.Status);
    }

    [Fact]
    public void ReadsByLayer_ResolvesSubjectsThroughCurrentRegistry_IgnoringStaleStamps()
    {
        string vaultRoot = Path.Combine(workspace, "Knowledge");
        string skillsRoot = Path.Combine(workspace, "skills");
        DashboardGold gold = Compute(
            Event("01F00000000000000000000005", "knowledge.read", "2026-08-10T10:00:00Z", kbroot: "vault",
                subject: Path.Combine(vaultRoot, "a.md")),
            Event("01F00000000000000000000006", "knowledge.read", "2026-08-10T11:00:00Z", kbroot: null,
                subject: Path.Combine(vaultRoot, "b.md")),
            Event("01F00000000000000000000007", "knowledge.read", "2026-08-10T12:00:00Z", kbroot: null,
                subject: Path.Combine(skillsRoot, "s.md")),
            Event("01F00000000000000000000008", "knowledge.read", "2026-08-10T13:00:00Z", kbroot: "vault",
                subject: "/outside/registered/roots.md"));

        Assert.Contains(gold.ReadsByLayerDaily, row => row.Date == "2026-08-10" && row.Layer == "global" && row.Reads == 2);
        Assert.Contains(gold.ReadsByLayerDaily, row => row.Date == "2026-08-10" && row.Layer == "skills" && row.Reads == 1);
        Assert.DoesNotContain(gold.ReadsByLayerDaily, row => row.Layer == "local");
        long total = gold.ReadsByLayerDaily.Sum(row => row.Reads);
        Assert.Equal(3, total);
    }

    [Fact]
    public void KbTouchRate_HistoricalNullStampReadOfNowRegisteredPath_CountsAsTouched()
    {
        string vaultRoot = Path.Combine(workspace, "Knowledge");
        DashboardGold gold = Compute(
            Event("01F0000000000000000000001B", "session.started", "2026-08-10T09:00:00Z", session: "sess-late",
                subject: "sess-late", data: new JsonObject { ["branch"] = null, ["usage"] = null }),
            Event("01F0000000000000000000001C", "knowledge.read", "2026-08-10T09:05:00Z", kbroot: null,
                session: "sess-late", subject: Path.Combine(vaultRoot, "late.md")));

        KbTouchRow row = Assert.Single(gold.KbTouchDaily);
        Assert.Equal(1, row.Touched);
    }

    [Fact]
    public void FailedSearchRate_CountsZeroHitShareOfKnownHitSearches()
    {
        DashboardGold gold = Compute(
            Event("01F00000000000000000000009", "knowledge.searched", "2026-08-10T10:00:00Z", kbroot: "vault", subject: "q1",
                data: new JsonObject { ["hits"] = 0 }),
            Event("01F0000000000000000000000A", "knowledge.searched", "2026-08-10T11:00:00Z", kbroot: "vault", subject: "q2",
                data: new JsonObject { ["hits"] = 5 }),
            Event("01F0000000000000000000000B", "knowledge.searched", "2026-08-10T12:00:00Z", kbroot: "vault", subject: "q3",
                data: new JsonObject { ["hits"] = null }));

        FailedSearchRow row = Assert.Single(gold.FailedSearchDaily);
        Assert.Equal("2026-08-10", row.Date);
        Assert.Equal(2, row.Searches);
        Assert.Equal(1, row.ZeroHits);
        Assert.Equal(0.5, row.Rate, precision: 3);
    }

    [Fact]
    public void KbTouchRate_SharesSessionsTouchingRegisteredKnowledge()
    {
        DashboardGold gold = Compute(
            Event("01F0000000000000000000000C", "session.started", "2026-08-10T09:00:00Z", session: "sess-kb",
                subject: "sess-kb", data: new JsonObject { ["branch"] = null, ["usage"] = null }),
            Event("01F0000000000000000000000D", "knowledge.read", "2026-08-10T09:05:00Z", kbroot: "vault",
                session: "sess-kb", subject: "/a.md"),
            Event("01F0000000000000000000000E", "session.started", "2026-08-10T10:00:00Z", session: "sess-none",
                subject: "sess-none", data: new JsonObject { ["branch"] = null, ["usage"] = null }));

        KbTouchRow row = Assert.Single(gold.KbTouchDaily);
        Assert.Equal(2, row.Sessions);
        Assert.Equal(1, row.Touched);
        Assert.Equal(0.5, row.Rate, precision: 3);
    }

    private string Note(string relativePath)
    {
        string path = Path.Combine(workspace, "Knowledge", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "# note\n");
        return path;
    }

    [Fact]
    public void ReadsByTheme_GroupsReadsByTopLevelFolder_AndListsUnreadThemes()
    {
        string ritualsA = Note(Path.Combine("rituals", "a.md"));
        string ritualsB = Note(Path.Combine("rituals", "b.md"));
        string security = Note(Path.Combine("security", "s.md"));
        Note(Path.Combine("ideas", "i1.md"));
        Note(Path.Combine("ideas", "i2.md"));
        string inbox = Note("inbox.md");

        DashboardGold gold = Compute(
            Event("01F00000000000000000000011", "knowledge.read", "2026-08-10T10:00:00Z", kbroot: "vault", subject: ritualsA),
            Event("01F00000000000000000000012", "knowledge.read", "2026-08-10T11:00:00Z", kbroot: "vault", subject: ritualsB),
            Event("01F00000000000000000000013", "knowledge.read", "2026-08-11T10:00:00Z", kbroot: null, subject: security),
            Event("01F00000000000000000000014", "knowledge.read", "2026-08-11T11:00:00Z", kbroot: "vault", subject: inbox));

        ThemeReadsRow rituals = gold.ThemeReads.Single(row => row.Theme == "vault/rituals");
        Assert.Equal(2, rituals.Reads);
        Assert.Equal(2, rituals.Notes);
        Assert.Equal("vault/rituals", gold.ThemeReads[0].Theme);
        Assert.Contains(gold.ThemeReads, row => row.Theme == "vault/security" && row.Reads == 1);
        Assert.Contains(gold.ThemeReads, row => row.Theme == "vault" && row.Reads == 1);

        ThemeReadsRow ideas = Assert.Single(gold.UnusedThemes);
        Assert.Equal("vault/ideas", ideas.Theme);
        Assert.Equal(2, ideas.Notes);
        Assert.Equal(0, ideas.Reads);
    }

    [Fact]
    public void ReadsByTheme_ReadsOlderThanWindow_CountAsUnused()
    {
        string oldNote = Note(Path.Combine("archive", "old.md"));

        DashboardGold gold = Compute(
            Event("01F00000000000000000000015", "knowledge.read", "2026-05-01T10:00:00Z", kbroot: "vault", subject: oldNote));

        Assert.DoesNotContain(gold.ThemeReads, row => row.Theme == "vault/archive");
        Assert.Contains(gold.UnusedThemes, row => row.Theme == "vault/archive" && row.Notes == 1);
    }

    private static JsonObject Session(string id, string session, string time, string? repo)
    {
        JsonObject started = Event(id, "session.started", time, session: session, subject: session,
            data: new JsonObject { ["branch"] = null, ["usage"] = null });
        started["repo"] = repo;
        return started;
    }

    [Fact]
    public void SessionsByRepo_CountsSessionsPerRepoPath_NewestFirstByVolume()
    {
        DashboardGold gold = Compute(
            Session("01F00000000000000000000016", "s-1", "2026-08-10T09:00:00Z", "/home/u/Repository/RepoA"),
            Session("01F00000000000000000000017", "s-2", "2026-08-11T09:00:00Z", "/home/u/Repository/RepoA"),
            Session("01F00000000000000000000018", "s-3", "2026-08-11T10:00:00Z", "/home/u/Repository/RepoB"),
            Session("01F00000000000000000000019", "s-4", "2026-08-12T10:00:00Z", null));

        Assert.Equal(3, gold.SessionsByRepo.Count);
        RepoSessionsRow top = gold.SessionsByRepo[0];
        Assert.Equal("/home/u/Repository/RepoA", top.Repo);
        Assert.Equal(2, top.Sessions);
        Assert.Equal("claude-code", top.Agents);
        Assert.Equal("2026-08-11", top.LastStarted.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Assert.Contains(gold.SessionsByRepo, row => row.Repo == "/home/u/Repository/RepoB" && row.Sessions == 1);
        Assert.Contains(gold.SessionsByRepo, row => row.Repo == "(unknown)" && row.Sessions == 1);
    }

    [Fact]
    public void SessionsByRepo_SessionsOlderThanWindow_AreExcluded()
    {
        DashboardGold gold = Compute(
            Session("01F0000000000000000000001A", "s-old", "2026-05-01T09:00:00Z", "/home/u/Repository/Ancient"));

        Assert.DoesNotContain(gold.SessionsByRepo, row => row.Repo == "/home/u/Repository/Ancient");
    }

    [Fact]
    public void RecentSessions_NewestFirst_WithPerSessionCountsAndTouch()
    {
        string vaultRoot = Path.Combine(workspace, "Knowledge");
        DashboardGold gold = Compute(
            Session("01F00000000000000000000020", "s-old", "2026-08-10T09:00:00Z", "/home/u/RepoA"),
            Session("01F00000000000000000000021", "s-new", "2026-08-12T09:00:00Z", "/home/u/RepoB"),
            Event("01F00000000000000000000022", "knowledge.read", "2026-08-12T09:05:00Z", kbroot: null,
                session: "s-new", subject: Path.Combine(vaultRoot, "n.md")),
            Event("01F00000000000000000000023", "skill.invoked", "2026-08-12T09:06:00Z",
                session: "s-new", data: new JsonObject { ["skill"] = "tdd" }));

        RecentSessionRow newest = gold.RecentSessions[0];
        Assert.Equal("2026-08-12", newest.Date);
        Assert.Equal("/home/u/RepoB", newest.Repo);
        Assert.Equal(1, newest.Reads);
        Assert.Equal(1, newest.Skills);
        Assert.True(newest.TouchedKb);
        Assert.False(gold.RecentSessions[1].TouchedKb);
    }

    [Fact]
    public void WeekOverWeek_ComparesThisWeekToLastWeek_ForKeyMetrics()
    {
        // Now is 2026-08-12T22:00Z. This week = [08-05 22:00, now); last week = [07-29 22:00, 08-05 22:00).
        string vaultRoot = Path.Combine(workspace, "Knowledge");
        DashboardGold gold = Compute(
            // last week: 1 session, untouched
            Session("01F00000000000000000000070", "s-last", "2026-08-01T09:00:00Z", "/r"),
            // this week: 2 sessions, one touches a note
            Session("01F00000000000000000000071", "s-a", "2026-08-10T09:00:00Z", "/r"),
            Session("01F00000000000000000000072", "s-b", "2026-08-11T09:00:00Z", "/r"),
            Event("01F00000000000000000000073", "knowledge.read", "2026-08-10T09:05:00Z", kbroot: "vault",
                session: "s-a", subject: Path.Combine(vaultRoot, "n.md")));

        MetricDelta kbTouch = gold.WeekOverWeek.Single(metric => metric.Label.Contains("KB-touch"));
        Assert.Equal("percent", kbTouch.Format);
        Assert.True(kbTouch.HigherIsBetter);
        Assert.Equal(0.5, kbTouch.Current, precision: 3);   // this week 1/2
        Assert.Equal(0.0, kbTouch.Previous, precision: 3);  // last week 0/1
        Assert.Contains(gold.WeekOverWeek, metric => metric.Label.Contains("Failed-search") && !metric.HigherIsBetter);
        Assert.Contains(gold.WeekOverWeek, metric => metric.Label.Contains("Knowledge reads") && metric.Format == "count");
    }

    [Fact]
    public void WriteReadLoop_CountsAgentWrittenNotesLaterRead_NotesOnly()
    {
        string vaultRoot = Path.Combine(workspace, "Knowledge");
        string reusedNote = Path.Combine(vaultRoot, "written-then-read.md");
        string coldNote = Path.Combine(vaultRoot, "written-never-read.md");
        DashboardGold gold = Compute(
            Event("01F00000000000000000000060", "knowledge.written", "2026-08-11T09:00:00Z", kbroot: "vault",
                session: "s-1", subject: reusedNote),
            Event("01F00000000000000000000061", "knowledge.read", "2026-08-12T09:00:00Z", kbroot: "vault",
                session: "s-2", subject: reusedNote),
            Event("01F00000000000000000000062", "knowledge.read", "2026-08-12T10:00:00Z", kbroot: "vault",
                session: "s-3", subject: reusedNote),
            Event("01F00000000000000000000063", "knowledge.written", "2026-08-11T09:00:00Z", kbroot: "vault",
                session: "s-1", subject: coldNote),
            // a read BEFORE the write must not count as a later read
            Event("01F00000000000000000000064", "knowledge.read", "2026-08-10T09:00:00Z", kbroot: "vault",
                session: "s-9", subject: coldNote));

        Assert.Equal(2, gold.WriteReadLoop.Written);
        Assert.Equal(1, gold.WriteReadLoop.Reused);
        Assert.Equal(0.5, gold.WriteReadLoop.LoopRate, precision: 3);
        WriteReadRow top = Assert.Single(gold.TopWriteReadNotes);
        Assert.Equal(reusedNote, top.Path);
        Assert.Equal(2, top.LaterReads);
    }

    [Fact]
    public void NoteReuse_RanksNotesByDistinctSessionReach_NotesOnly_WithSingleUseRatio()
    {
        string vaultRoot = Path.Combine(workspace, "Knowledge");
        string reusedNote = Path.Combine(vaultRoot, "reused.md");
        string onceNote = Path.Combine(vaultRoot, "once.md");
        string codeFile = Path.Combine(vaultRoot, "Program.cs");
        DashboardGold gold = Compute(
            Event("01F00000000000000000000050", "knowledge.read", "2026-08-12T09:00:00Z", kbroot: "vault",
                session: "s-1", subject: reusedNote),
            Event("01F00000000000000000000051", "knowledge.read", "2026-08-12T10:00:00Z", kbroot: "vault",
                session: "s-2", subject: reusedNote),
            Event("01F00000000000000000000052", "knowledge.read", "2026-08-12T11:00:00Z", kbroot: "vault",
                session: "s-2", subject: reusedNote),
            Event("01F00000000000000000000053", "knowledge.read", "2026-08-12T12:00:00Z", kbroot: "vault",
                session: "s-1", subject: onceNote),
            Event("01F00000000000000000000054", "knowledge.read", "2026-08-12T13:00:00Z", kbroot: "vault",
                session: "s-3", subject: codeFile));

        ReuseRow top = gold.TopReusedNotes[0];
        Assert.Equal(reusedNote, top.Path);
        Assert.Equal(2, top.Sessions);
        Assert.Equal(3, top.Reads);
        Assert.DoesNotContain(gold.TopReusedNotes, note => note.Path == codeFile);   // code excluded
        Assert.Equal(2, gold.Reuse.Notes);          // reused.md + once.md
        Assert.Equal(1, gold.Reuse.SingleUse);      // once.md only
        Assert.Equal(0.5, gold.Reuse.SingleUseRate, precision: 3);
    }

    [Fact]
    public void ReadsByContentType_SeparatesKnowledgeFromCodeAmongRegisteredReads()
    {
        string vaultRoot = Path.Combine(workspace, "Knowledge");
        DashboardGold gold = Compute(
            Event("01F00000000000000000000040", "knowledge.read", "2026-08-12T09:00:00Z", kbroot: "vault",
                subject: Path.Combine(vaultRoot, "note.md")),
            Event("01F00000000000000000000041", "knowledge.read", "2026-08-12T09:01:00Z", kbroot: "vault",
                subject: Path.Combine(vaultRoot, "another.md")),
            Event("01F00000000000000000000042", "knowledge.read", "2026-08-12T09:02:00Z", kbroot: "vault",
                subject: Path.Combine(vaultRoot, "Program.cs")),
            Event("01F00000000000000000000043", "knowledge.read", "2026-08-12T09:03:00Z", kbroot: null,
                subject: "/outside/x.md"));

        Assert.Contains(gold.ReadsByContentType, kind => kind.Label == "knowledge" && kind.Count == 2);
        Assert.Contains(gold.ReadsByContentType, kind => kind.Label == "code" && kind.Count == 1);
        Assert.DoesNotContain(gold.ReadsByContentType, kind => kind.Count == 0);
        Assert.Equal(3, gold.ReadsByContentType.Sum(kind => kind.Count));
    }

    [Fact]
    public void TopSkillsAndFailedSearches_RankedByFrequencyOverWindow()
    {
        DashboardGold gold = Compute(
            Event("01F00000000000000000000030", "skill.invoked", "2026-08-10T09:00:00Z", session: "s",
                data: new JsonObject { ["skill"] = "tdd" }),
            Event("01F00000000000000000000031", "skill.invoked", "2026-08-10T09:05:00Z", session: "s",
                data: new JsonObject { ["skill"] = "tdd" }),
            Event("01F00000000000000000000032", "skill.invoked", "2026-08-10T09:06:00Z", session: "s",
                data: new JsonObject { ["skill"] = "grilling" }),
            Event("01F00000000000000000000033", "knowledge.searched", "2026-08-10T10:00:00Z", session: "s",
                subject: "ghost query", data: new JsonObject { ["hits"] = 0 }),
            Event("01F00000000000000000000034", "knowledge.searched", "2026-08-10T10:01:00Z", session: "s",
                subject: "ghost query", data: new JsonObject { ["hits"] = 0 }),
            Event("01F00000000000000000000035", "knowledge.searched", "2026-08-10T10:02:00Z", session: "s",
                subject: "found query", data: new JsonObject { ["hits"] = 3 }));

        Assert.Equal("tdd", gold.TopSkills[0].Label);
        Assert.Equal(2, gold.TopSkills[0].Count);
        Assert.Contains(gold.TopSkills, skill => skill.Label == "grilling" && skill.Count == 1);

        DayCount topMiss = Assert.Single(gold.TopFailedSearches);
        Assert.Equal("ghost query", topMiss.Label);
        Assert.Equal(2, topMiss.Count);
    }

    [Fact]
    public void TokensTrend_SumsSessionUsageByStartDay()
    {
        DashboardGold gold = Compute(
            Event("01F0000000000000000000000F", "session.started", "2026-08-10T09:00:00Z", session: "sess-a",
                subject: "sess-a", data: new JsonObject
                {
                    ["branch"] = null,
                    ["usage"] = new JsonObject { ["input_tokens"] = 100, ["cache_read_tokens"] = 5000, ["output_tokens"] = 10 },
                }),
            Event("01F00000000000000000000010", "session.started", "2026-08-10T11:00:00Z", session: "sess-b",
                subject: "sess-b", data: new JsonObject
                {
                    ["branch"] = null,
                    ["usage"] = new JsonObject { ["input_tokens"] = 50, ["cache_read_tokens"] = 2000, ["output_tokens"] = 5 },
                }));

        TokensRow row = Assert.Single(gold.TokensDaily);
        Assert.Equal("2026-08-10", row.Date);
        Assert.Equal(150, row.InputTokens);
        Assert.Equal(7000, row.CacheReadTokens);
    }

    [Fact]
    public void SddPanel_Ordering_ClassifiesSpecFirstSessions()
    {
        DashboardGold gold = Compute(
            // s-spec-first: spec read 09:00 → code write 10:00 → spec-first
            Session("01F000000000000000000000A0", "s-spec-first", "2026-08-10T08:00:00Z", "/home/u/RepoA"),
            Event("01F000000000000000000000A1", "knowledge.read", "2026-08-10T09:00:00Z", kbroot: "vault",
                session: "s-spec-first", subject: "/home/u/RepoA/docs/superpowers/specs/x-design.md"),
            Event("01F000000000000000000000A2", "knowledge.written", "2026-08-10T10:00:00Z", kbroot: "vault",
                session: "s-spec-first", subject: "/home/u/RepoA/src/Program.cs"),
            // s-code-first: code write 09:00 → spec read 10:00 → not spec-first
            Session("01F000000000000000000000A3", "s-code-first", "2026-08-10T08:00:00Z", "/home/u/RepoA"),
            Event("01F000000000000000000000A4", "knowledge.written", "2026-08-10T09:00:00Z", kbroot: "vault",
                session: "s-code-first", subject: "/home/u/RepoA/src/Program.cs"),
            Event("01F000000000000000000000A5", "knowledge.read", "2026-08-10T10:00:00Z", kbroot: "vault",
                session: "s-code-first", subject: "/home/u/RepoA/docs/superpowers/plans/x.md"),
            // s-code-only: code writes, never a spec → denominator only
            Session("01F000000000000000000000A6", "s-code-only", "2026-08-11T08:00:00Z", "/home/u/RepoA"),
            Event("01F000000000000000000000A7", "knowledge.written", "2026-08-11T09:00:00Z", kbroot: "vault",
                session: "s-code-only", subject: "/home/u/RepoA/src/Other.cs"),
            // s-spec-only: spec activity, no code write → outside the denominator
            Session("01F000000000000000000000A8", "s-spec-only", "2026-08-11T08:00:00Z", "/home/u/RepoA"),
            Event("01F000000000000000000000A9", "knowledge.read", "2026-08-11T09:00:00Z", kbroot: "vault",
                session: "s-spec-only", subject: "/home/u/RepoA/docs/superpowers/specs/y.md"));

        SddOrderingSummary summary = gold.SddPanel.OrderingSummary;
        Assert.Equal(3, summary.CodeSessions);
        Assert.Equal(1, summary.SpecFirstSessions);
        Assert.Equal(1.0 / 3, summary.Rate, precision: 3);

        SddOrderingRow week = Assert.Single(gold.SddPanel.Ordering);
        Assert.Equal("2026-W33", week.Week); // Aug 10–11, 2026 → ISO week 33
        Assert.Equal("/home/u/RepoA", week.Repo);
        Assert.Equal(3, week.CodeSessions);
        Assert.Equal(1, week.SpecFirstSessions);
    }

    [Fact]
    public void SddPanel_WritesByKind_ExcludesMachineManagedAndDisclosesThem()
    {
        DashboardGold gold = Compute(
            Session("01F000000000000000000000B0", "s-1", "2026-08-10T08:00:00Z", "/home/u/RepoA"),
            Event("01F000000000000000000000B1", "knowledge.written", "2026-08-10T09:00:00Z", kbroot: "vault",
                session: "s-1", subject: "/home/u/RepoA/docs/superpowers/specs/x.md"),
            Event("01F000000000000000000000B2", "knowledge.written", "2026-08-10T09:05:00Z", kbroot: "vault",
                session: "s-1", subject: "/home/u/RepoA/src/Program.cs"),
            Event("01F000000000000000000000B3", "knowledge.written", "2026-08-10T09:10:00Z", kbroot: "vault",
                session: "s-1", subject: "/home/u/RepoA/appsettings.json"),
            // machine-managed: constitution copy — excluded from kinds, disclosed
            Event("01F000000000000000000000B4", "knowledge.written", "2026-08-10T09:15:00Z", kbroot: "vault",
                session: "s-1", subject: "/home/u/RepoA/docs/ai/rules/core/okf.md"),
            // codebase-map.md is knowledge-kind but under docs/ai → still machine-managed
            Event("01F000000000000000000000B5", "knowledge.written", "2026-08-10T09:20:00Z", kbroot: "vault",
                session: "s-1", subject: "/home/u/RepoA/docs/ai/baseline.md"));

        Assert.Equal(2, gold.SddPanel.MachineManagedWrites);
        Dictionary<string, long> kinds = gold.SddPanel.WritesByKind.ToDictionary(row => row.Kind, row => row.Writes);
        Assert.Equal(1, kinds["knowledge"]);
        Assert.Equal(1, kinds["code"]);
        Assert.Equal(1, kinds["config"]);
        Assert.False(kinds.ContainsKey("other"));
    }

    [Fact]
    public void SddPanel_SkillRate_UsesConfiguredSkillsPerRepo()
    {
        KnowledgeRegistry sddRegistry = KnowledgeRegistry.Parse($"""
            machine: test-machine
            sdd:
              skills:
                - legislator
                - superpowers:brainstorm
            sources:
              - id: vault
                layer: global
                root: {Path.Combine(workspace, "Knowledge")}
            """);
        string eventsRepo = Path.Combine(workspace, "kb-events-sdd");
        JsonObject[] events =
        {
            Session("01F000000000000000000000C0", "s-a", "2026-08-10T08:00:00Z", "/home/u/RepoA"),
            Event("01F000000000000000000000C1", "skill.invoked", "2026-08-10T09:00:00Z",
                session: "s-a", data: new JsonObject { ["skill"] = "legislator" }),
            Session("01F000000000000000000000C2", "s-b", "2026-08-10T08:00:00Z", "/home/u/RepoA"),
            Event("01F000000000000000000000C3", "skill.invoked", "2026-08-10T09:00:00Z",
                session: "s-b", data: new JsonObject { ["skill"] = "dotnet-refactoring" }),
            Session("01F000000000000000000000C4", "s-c", "2026-08-10T08:00:00Z", "/home/u/RepoB"),
            // s-c invokes no skills at all — still a session in the denominator
        };
        new BronzeStore(eventsRepo).Append(events);
        SilverRebuilder.Rebuild(eventsRepo, silverPath);
        DashboardGold gold = DashboardComputer.Compute(silverPath, sddRegistry, new FixedTimeProvider(Now));

        Assert.True(gold.SddPanel.SkillConfigured);
        SddSkillRateRow repoA = gold.SddPanel.SkillRate.Single(row => row.Repo == "/home/u/RepoA");
        Assert.Equal(2, repoA.Sessions);
        Assert.Equal(1, repoA.SddSessions);
        Assert.Equal(0.5, repoA.Rate, precision: 3);
        SddSkillRateRow repoB = gold.SddPanel.SkillRate.Single(row => row.Repo == "/home/u/RepoB");
        Assert.Equal(1, repoB.Sessions);
        Assert.Equal(0, repoB.SddSessions);
    }

    [Fact]
    public void SddPanel_SkillRate_UnconfiguredIsFlaggedNotSilent()
    {
        DashboardGold gold = Compute(
            Session("01F000000000000000000000D0", "s-a", "2026-08-10T08:00:00Z", "/home/u/RepoA"));

        Assert.False(gold.SddPanel.SkillConfigured);
        Assert.Empty(gold.SddPanel.SkillRate);
    }
}
