using System.Globalization;
using System.Text.Json.Nodes;

namespace Kbo.Tests;

/// <summary>
/// Event factories shared by the dashboard computer tests.
/// </summary>
internal static class DashboardTestEvents
{
    public static JsonObject Event(string id, string type, string time, string? kbroot = null, string? subject = null,
        string? session = "sess-1", string agent = "claude-code", JsonObject? data = null)
    {
        JsonObject eventData = data ?? [];
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

    public static string Stamp(DateTime time) => time.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
