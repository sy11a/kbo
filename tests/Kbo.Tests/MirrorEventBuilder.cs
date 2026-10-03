using System.Globalization;
using System.Text.Json.Nodes;

namespace Kbo.Tests;

/// <summary>
/// Builds one block of events per week so every weekly snapshot of the mirror has data.
/// </summary>
internal sealed class MirrorEventBuilder(string noteRoot)
{
    public const int WeekCount = 11;

    private const int SearchesPerWeek = 10;
    private const int BurnerInput = 150000;
    private const int SteadyInput = 40000;
    private const int SteadyCache = 400000;

    private static readonly DateTime _firstMonday = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly List<JsonObject> _events = [];
    private int _sequence;

    public List<JsonObject> Build(MirrorShape shape)
    {
        for (int week = 0; week < WeekCount; week++)
        {
            DateTime day = _firstMonday.AddDays((7 * week) + 1);
            AddSessions(week, day, shape.CacheA[week]);
            AddSearches(day, shape.ZeroSearches[week]);
            AddNotes(week, day, shape.Written[week], shape.Reused[week], shape.MultiRead[week]);
            AddCode(week, day, shape.CodeSessions[week], shape.SpecFirst[week]);
        }
        return _events;
    }

    private static string Name(FormattableString name) => FormattableString.Invariant(name);

    private void Add(string type, DateTime time, string? subject, string? session, JsonObject? data = null)
    {
        string id = string.Create(CultureInfo.InvariantCulture, $"01FM{_sequence++:D22}");
        _events.Add(DashboardTestEvents.Event(id, type, DashboardTestEvents.Stamp(time), subject: subject, session: session, data: data));
    }

    private void AddSessions(int week, DateTime day, int cacheA)
    {
        AddSession(Name($"sa{week}"), day.AddHours(8), BurnerInput, cacheA);
        AddSession(Name($"sb{week}"), day.AddMinutes(510), SteadyInput, SteadyCache);
    }

    private void AddSession(string session, DateTime time, int input, int cache)
    {
        JsonObject usage = new() { ["input_tokens"] = input, ["cache_read_tokens"] = cache };
        Add("session.started", time, session, session, new JsonObject { ["branch"] = null, ["usage"] = usage });
        _events[^1]["repo"] = "/home/u/RepoA";
    }

    private void AddSearches(DateTime day, int zero)
    {
        for (int search = 0; search < SearchesPerWeek; search++)
        {
            Add("knowledge.searched", day.AddHours(search), Name($"query {search}"), "sess-search",
                new JsonObject { ["query"] = "q", ["hits"] = search < zero ? 0 : 3 });
        }
    }

    private void AddNotes(int week, DateTime day, int written, int reused, int multiRead)
    {
        for (int note = 0; note < written; note++)
        {
            string subject = Path.Combine(noteRoot, Name($"w{week}"), Name($"n{note}.md"));
            Add("knowledge.written", day.AddHours(10), subject, Name($"w{week}-{note}"));
            if (note < reused)
            {
                Add("knowledge.read", day.AddHours(11), subject, Name($"r{week}-{note}"));
            }
            if (note < multiRead)
            {
                Add("knowledge.read", day.AddHours(12), subject, Name($"r{week}-{note}b"));
            }
        }
    }

    private void AddCode(int week, DateTime day, int codeSessions, int specFirst)
    {
        for (int index = 0; index < codeSessions; index++)
        {
            string session = Name($"c{week}-{index}");
            if (index < specFirst)
            {
                Add("knowledge.read", day.AddHours(9), "/home/u/RepoA/docs/superpowers/specs/x.md", session);
            }
            Add("knowledge.written", day.AddHours(12), "/home/u/RepoA/src/c.cs", session);
        }
    }
}
