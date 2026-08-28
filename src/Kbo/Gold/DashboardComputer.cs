using System.Globalization;
using System.Numerics;
using DuckDB.NET.Data;
using Kbo.Jobs;
using Kbo.Registry;
using Kbo.Schemas;
using Kbo.Silver;

namespace Kbo.Gold;

public static class DashboardComputer
{
    public const int DeadManThresholdDays = 3;
    public const int ThemeWindowDays = 60;
    public const int RepoListCap = 50;
    public const int RecentSessionCap = 30;
    public const int TopListCap = 15;
    public const int MirrorSnapshotWeeks = 8;

    public static DashboardGold Compute(string silverPath, KnowledgeRegistry registry, TimeProvider clock,
        ConstitutionFleetGold? constitutionFleet = null)
    {
        DateTimeOffset now = clock.GetUtcNow();
        using DuckDBConnection connection = SilverConnection.OpenReadOnly(silverPath);

        HashSet<string> touchedSessions = TouchedSessions(connection, registry);
        List<ThemeReadsRow> unusedThemes = ReadsByTheme(connection, registry, now);
        (List<ReuseRow> topReused, ReuseSummary reuseSummary) = NoteReuse(connection, registry, now);
        (List<WriteReadRow> topWriteRead, WriteReadSummary writeReadSummary) = WriteReadLoop(connection, registry, now);
        SddPanelGold sddPanel = SddPanel(connection, registry, now);
        return new DashboardGold(
            now,
            registry.Machine,
            DeadManThresholdDays,
            JobDeadMan.WeeklyThresholdDays,
            JobHealth(connection, now),
            LastSeen(connection, now),
            constitutionFleet,
            ServiceSessions(connection, now),
            sddPanel,
            FailedSearches(connection),
            Tokens(connection),
            unusedThemes,
            RecentSessions(connection, touchedSessions),
            TopFailedSearches(connection, now),
            topReused,
            reuseSummary,
            topWriteRead,
            writeReadSummary,
            PracticeMirror(connection, registry, now));
    }

    /// <summary>Practice mirror, calibrated (BL-033): six tiles judged against their own
    /// weekly-snapshot history — corridor p25–p75, robust-z acute breaks, OLS drift —
    /// with static goals. State is an emoji; only the acute ⚠️ may color amber.</summary>
    private static PracticeMirrorGold PracticeMirror(
        DuckDBConnection connection,
        KnowledgeRegistry registry,
        DateTimeOffset now)
    {
        DateTime nowUtc = now.UtcDateTime;
        DateTime currentWeekStart = StartOfIsoWeek(nowUtc);
        List<DateTime> grid = Enumerable.Range(1, MirrorSnapshotWeeks)
            .Select(weeksBack => currentWeekStart.AddDays(-7 * weeksBack))
            .OrderBy(snapshot => snapshot)
            .ToList();

        double? CacheAt(DateTime start, DateTime end)
        {
            foreach (object?[] row in Query(connection, """
                SELECT coalesce(sum(cache_read_tokens), 0),
                       coalesce(sum(input_tokens), 0)
                FROM sessions
                WHERE started_at >= $start AND started_at < $end
                  AND session NOT IN (SELECT session FROM service_sessions)
                """, ("start", start), ("end", end)))
            {
                long cacheRead = AsLong(row[0]);
                long total = cacheRead + AsLong(row[1]);
                return total == 0 ? null : (double)cacheRead / total;
            }
            return null;
        }

        double? BurnerAt(DateTime start, DateTime end)
        {
            foreach (object?[] row in Query(connection, """
                SELECT count(*),
                       count_if(input_tokens > cache_read_tokens AND input_tokens > 100000)
                FROM sessions
                WHERE started_at >= $start AND started_at < $end
                  AND session NOT IN (SELECT session FROM service_sessions)
                """, ("start", start), ("end", end)))
            {
                long sessions = AsLong(row[0]);
                return sessions == 0 ? null : (double)AsLong(row[1]) / sessions;
            }
            return null;
        }

        double? FailedAt(DateTime start, DateTime end)
        {
            foreach (object?[] row in Query(connection, """
                SELECT count(TRY_CAST(json_extract_string(data, '$.hits') AS BIGINT)),
                       count_if(TRY_CAST(json_extract_string(data, '$.hits') AS BIGINT) = 0)
                FROM practice_events
                WHERE type = 'knowledge.searched' AND time >= $start AND time < $end
                """, ("start", start), ("end", end)))
            {
                long searches = AsLong(row[0]);
                return searches == 0 ? null : (double)AsLong(row[1]) / searches;
            }
            return null;
        }

        double? LoopAt(DateTime start, DateTime end)
        {
            Dictionary<string, DateTime> firstWrite = new();
            foreach (object?[] row in Query(connection, """
                SELECT subject, min(time) AS first_write
                FROM practice_events
                WHERE type = 'knowledge.written' AND subject IS NOT NULL AND time >= $start AND time < $end
                GROUP BY subject
                """, ("start", start), ("end", end)))
            {
                string subject = (string)row[0]!;
                if (registry.Resolve(subject) is null || ContentKind.Of(subject) != ContentKind.Knowledge)
                {
                    continue;
                }
                firstWrite[subject] = (DateTime)row[1]!;
            }

            if (firstWrite.Count == 0)
            {
                return null;
            }

            int written = firstWrite.Count;
            int reused = 0;
            foreach (object?[] row in Query(connection, """
                SELECT subject, time
                FROM practice_events
                WHERE type = 'knowledge.read' AND subject IS NOT NULL AND time >= $start AND time < $end
                """, ("start", start), ("end", end)))
            {
                string subject = (string)row[0]!;
                if (firstWrite.TryGetValue(subject, out DateTime writtenAt) && (DateTime)row[1]! > writtenAt)
                {
                    reused++;
                    firstWrite.Remove(subject);
                }
            }
            return (double)reused / written;
        }

        double? SingleUseAt(DateTime start, DateTime end)
        {
            long notes = 0;
            long singleUse = 0;
            foreach (object?[] row in Query(connection, """
                SELECT subject, count(DISTINCT session) AS sessions
                FROM practice_events
                WHERE type = 'knowledge.read' AND subject IS NOT NULL AND time >= $start AND time < $end
                GROUP BY subject
                """, ("start", start), ("end", end)))
            {
                string subject = (string)row[0]!;
                if (registry.Resolve(subject) is null || ContentKind.Of(subject) != ContentKind.Knowledge)
                {
                    continue;
                }
                notes++;
                if (AsLong(row[1]) <= 1)
                {
                    singleUse++;
                }
            }
            return notes == 0 ? null : (double)singleUse / notes;
        }

        double? SddAt(DateTime start, DateTime end)
        {
            Dictionary<string, DateTime> firstSpec = new();
            foreach (object?[] row in Query(connection, """
                SELECT session, min(time) AS first_spec
                FROM practice_events
                WHERE session IS NOT NULL AND subject IS NOT NULL
                  AND type IN ('knowledge.read', 'knowledge.written')
                  AND (contains(subject, '/docs/superpowers/') OR contains(subject, '/docs/cases/'))
                  AND time >= $start AND time < $end
                GROUP BY session
                """, ("start", start), ("end", end)))
            {
                firstSpec[(string)row[0]!] = (DateTime)row[1]!;
            }

            Dictionary<string, DateTime> firstCode = new();
            foreach (object?[] row in Query(connection, """
                SELECT session, subject, time
                FROM practice_events
                WHERE type = 'knowledge.written' AND subject IS NOT NULL AND time >= $start AND time < $end
                """, ("start", start), ("end", end)))
            {
                if (ContentKind.Of((string)row[1]!) != ContentKind.Code)
                {
                    continue;
                }
                string session = (string)row[0]!;
                DateTime time = (DateTime)row[2]!;
                if (!firstCode.TryGetValue(session, out DateTime existing) || time < existing)
                {
                    firstCode[session] = time;
                }
            }

            if (firstCode.Count == 0)
            {
                return null;
            }

            long specFirst = firstCode.Count(entry =>
                firstSpec.TryGetValue(entry.Key, out DateTime spec) && spec < entry.Value);
            return (double)specFirst / firstCode.Count;
        }

        MirrorTile Tile(
            string label,
            Func<DateTime, DateTime, double?> valueAt,
            int windowDays,
            MirrorGoal? goal,
            bool trust,
            string stableHint,
            string chronicHint)
        {
            double? current = valueAt(nowUtc.AddDays(-windowDays), nowUtc);
            List<double> history = new();
            foreach (DateTime snapshotEnd in grid)
            {
                double? value = valueAt(snapshotEnd.AddDays(-windowDays), snapshotEnd);
                if (value.HasValue)
                {
                    history.Add(value.Value);
                }
            }

            if (current is null)
            {
                return new MirrorTile(label, "нет данных", "нет данных в окне", "нечего измерять — окно пустое",
                    MirrorCalibration.ClassWait, MirrorCalibration.StateWaiting, HistoryWeeks: history.Count);
            }

            MirrorVerdict calibration = MirrorCalibration.Evaluate(history, current.Value, goal, trust);
            return new MirrorTile(
                label,
                Pct(current.Value),
                TrendLine(calibration),
                HintFor(calibration, stableHint, chronicHint),
                calibration.StatusClass,
                calibration.State,
                GoalLine(calibration, goal, current.Value),
                calibration.CorridorLow,
                calibration.CorridorHigh,
                calibration.Median,
                calibration.Mad,
                calibration.HistoryWeeks);
        }

        return new PracticeMirrorGold(
        [
            Tile("Cache discipline · 14д", CacheAt, 14, goal: null, trust: true,
                "контекст переиспользуется — норма", ""),
            Tile("Burner sessions · 14д", BurnerAt, 14, goal: null, trust: true,
                "одноразовых задач мало — норма", ""),
            Tile("Write→read loop · 6 нед", LoopAt, 42,
                new MirrorGoal(0.30, MirrorDirection.UpIsBetter), trust: false,
                "записи окупаются — норма", "пиши короче, ссылочнее и в читаемый корень"),
            Tile("Single-use notes · 6 нед", SingleUseAt, 42,
                new MirrorGoal(0.55, MirrorDirection.DownIsBetter), trust: false,
                "фонд здоров — норма", "кандидаты на weeding — очередь предложений"),
            Tile("Failed-search · 14д", FailedAt, 14,
                new MirrorGoal(0.15, MirrorDirection.DownIsBetter), trust: false,
                "знание находится — норма", "линкуй заметки от слов, которыми ищешь"),
            Tile("Spec-before-code · 6 нед", SddAt, 42,
                new MirrorGoal(0.50, MirrorDirection.UpIsBetter), trust: false,
                "спека идёт перед кодом — норма", "сначала код — включи спека-скиллы в практику"),
        ]);

        static string Pct(double v) => v.ToString("0%", CultureInfo.InvariantCulture);

        static string TrendLine(MirrorVerdict calibration)
        {
            if (calibration.StatusClass == MirrorCalibration.ClassWait)
            {
                return FormattableString.Invariant(
                    $"история {calibration.HistoryWeeks}/{MirrorCalibration.RequiredHistoryWeeks} нед");
            }
            if (calibration.StatusClass == MirrorCalibration.ClassAcute)
            {
                return calibration.RobustZ.HasValue
                    ? FormattableString.Invariant($"острый выход: z = {calibration.RobustZ.Value:0.0}")
                    : "вне насыщенной нормы";
            }
            if (calibration.StatusClass == MirrorCalibration.ClassTrend && calibration.SlopePerWeek.HasValue)
            {
                double ppPerWeek = calibration.SlopePerWeek.Value * 100;
                string sign = ppPerWeek > 0 ? "+" : "−";
                return FormattableString.Invariant($"наклон {sign}{Math.Abs(ppPerWeek):0.0}пп/нед");
            }
            string low = calibration.CorridorLow?.ToString("0%", CultureInfo.InvariantCulture) ?? "—";
            string high = calibration.CorridorHigh?.ToString("0%", CultureInfo.InvariantCulture) ?? "—";
            return $"в коридоре {low}–{high}";
        }

        static string HintFor(MirrorVerdict calibration, string stableHint, string chronicHint)
        {
            return calibration.StatusClass switch
            {
                MirrorCalibration.ClassWait => "плитка ждёт достаточно своей истории",
                MirrorCalibration.ClassAcute => "требует внимания сейчас — острый слом против своей нормы",
                MirrorCalibration.ClassTrend => "устойчивый сдвиг — найди, что изменилось в практике",
                MirrorCalibration.ClassSick => chronicHint + " (хроника — кандидат в бэклог)",
                _ => stableHint,
            };
        }

        static string? GoalLine(MirrorVerdict calibration, MirrorGoal? goal, double current)
        {
            if (goal is null || calibration.StatusClass == MirrorCalibration.ClassWait)
            {
                return null;
            }
            string target = goal.Direction == MirrorDirection.UpIsBetter
                ? FormattableString.Invariant($"цель ≥{goal.Value:0%}")
                : FormattableString.Invariant($"цель ≤{goal.Value:0%}");
            double gapPp = goal.Direction == MirrorDirection.UpIsBetter
                ? (goal.Value - current) * 100
                : (current - goal.Value) * 100;
            return gapPp <= 0
                ? target + " · достигнута"
                : FormattableString.Invariant($"{target} · до цели −{gapPp:0}пп");
        }
    }

    /// <summary>
    /// SDD-practice panel (ADR-0040). Spec activity = subjects under the
    /// fleet spec homes (/docs/superpowers/, /docs/cases/); code write =
    /// knowledge.written with ContentKind code; writes under /docs/ai/
    /// are machine-managed and never count as documentation discipline.
    /// Practice sessions only (practice_events, ADR-0039).
    /// </summary>
    private static SddPanelGold SddPanel(DuckDBConnection connection, KnowledgeRegistry registry, DateTimeOffset now)
    {
        DateTime cutoff = now.AddDays(-ThemeWindowDays).UtcDateTime;

        // Session → repo (sessions view; '(unknown)' when absent —
        // same coalesce convention silver's sessions view uses).
        Dictionary<string, string> repoBySession = new();
        foreach (object?[] row in Query(connection, """
            SELECT session, coalesce(repo, '(unknown)') AS repo
            FROM sessions
            WHERE session IS NOT NULL
            GROUP BY session, repo
            """))
        {
            repoBySession[(string)row[0]!] = (string)row[1]!;
        }

        // Ordering: per session, earliest spec activity vs first code write.
        Dictionary<string, DateTime> firstSpec = new();
        foreach (object?[] row in Query(connection, """
            SELECT session, min(time) AS first_spec
            FROM practice_events
            WHERE session IS NOT NULL AND subject IS NOT NULL
              AND type IN ('knowledge.read', 'knowledge.written')
              AND (contains(subject, '/docs/superpowers/') OR contains(subject, '/docs/cases/'))
              AND time >= $cutoff
            GROUP BY session
            """, ("cutoff", cutoff)))
        {
            firstSpec[(string)row[0]!] = (DateTime)row[1]!;
        }

        Dictionary<string, DateTime> firstCodeWrite = new();
        long machineManagedWrites = 0;
        Dictionary<string, long> writesByKind = new();
        foreach (object?[] row in Query(connection, """
            SELECT session, subject, time
            FROM practice_events
            WHERE type = 'knowledge.written' AND subject IS NOT NULL AND time >= $cutoff
            """, ("cutoff", cutoff)))
        {
            string subject = (string)row[1]!;
            if (subject.Contains("/docs/ai/", StringComparison.Ordinal))
            {
                machineManagedWrites++;
                continue;
            }
            string kind = ContentKind.Of(subject);
            writesByKind[kind] = writesByKind.GetValueOrDefault(kind) + 1;
            if (kind != ContentKind.Code)
            {
                continue;
            }
            string session = (string)row[0]!;
            DateTime time = (DateTime)row[2]!;
            if (!firstCodeWrite.TryGetValue(session, out DateTime existing) || time < existing)
            {
                firstCodeWrite[session] = time;
            }
        }

        // Ordering rows per repo × ISO week of the first code write.
        Dictionary<(string Week, string Repo), long[]> ordering = new();
        long codeSessions = 0;
        long specFirst = 0;
        foreach (KeyValuePair<string, DateTime> entry in firstCodeWrite)
        {
            codeSessions++;
            bool isSpecFirst = firstSpec.TryGetValue(entry.Key, out DateTime spec) && spec < entry.Value;
            if (isSpecFirst)
            {
                specFirst++;
            }
            DateTime firstWrite = entry.Value;
            int weekYear = IsoWeekYear(firstWrite);
            int week = System.Globalization.ISOWeek.GetWeekOfYear(firstWrite);
            string key = FormattableString.Invariant($"{weekYear:D4}-W{week:D2}");
            string repo = repoBySession.GetValueOrDefault(entry.Key, "(unknown)");
            long[] slot = ordering.TryGetValue((key, repo), out long[]? existing)
                ? existing : ordering[(key, repo)] = new long[2];
            slot[0]++;
            if (isSpecFirst)
            {
                slot[1]++;
            }
        }

        List<SddOrderingRow> orderingRows = ordering
            .OrderByDescending(entry => entry.Key.Week, StringComparer.Ordinal)
            .ThenBy(entry => entry.Key.Repo, StringComparer.Ordinal)
            .Take(RepoListCap)
            .Select(entry => new SddOrderingRow(
                entry.Key.Week, entry.Key.Repo, entry.Value[0], entry.Value[1],
                entry.Value[0] == 0 ? 0 : (double)entry.Value[1] / entry.Value[0]))
            .ToList();
        SddOrderingSummary orderingSummary = new(
            codeSessions, specFirst, codeSessions == 0 ? 0 : (double)specFirst / codeSessions);

        List<SddWritesRow> writesRows = writesByKind
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new SddWritesRow(entry.Key, entry.Value))
            .ToList();

        // Skill rate: configured skill names only (ADR-0031 pattern);
        // an unconfigured block is stated, never silently omitted.
        List<SddSkillRateRow> skillRows = new();
        bool skillConfigured = registry.Sdd is not null;
        if (registry.Sdd is not null)
        {
            HashSet<string> skills = new(registry.Sdd.Skills, StringComparer.Ordinal);
            Dictionary<string, long> sessionsTotal = new();
            Dictionary<string, long> sessionsWithSddSkill = new();
            foreach (object?[] row in Query(connection, """
                SELECT session, json_extract_string(data, '$.skill') AS skill
                FROM practice_events
                WHERE type = 'skill.invoked' AND session IS NOT NULL
                  AND skill IS NOT NULL AND time >= $cutoff
                """, ("cutoff", cutoff)))
            {
                string session = (string)row[0]!;
                sessionsTotal.TryAdd(session, 0);
                if (skills.Contains((string)row[1]!))
                {
                    sessionsWithSddSkill.TryAdd(session, 0);
                }
            }
            foreach (object?[] row in Query(connection, """
                SELECT DISTINCT session FROM practice_events
                WHERE session IS NOT NULL AND time >= $cutoff
                """, ("cutoff", cutoff)))
            {
                sessionsTotal.TryAdd((string)row[0]!, 0);
            }

            Dictionary<string, long[]> byRepo = new();
            foreach (string session in sessionsTotal.Keys)
            {
                string repo = repoBySession.GetValueOrDefault(session, "(unknown)");
                long[] slot = byRepo.TryGetValue(repo, out long[]? existing)
                    ? existing : byRepo[repo] = new long[2];
                slot[0]++;
                if (sessionsWithSddSkill.ContainsKey(session))
                {
                    slot[1]++;
                }
            }
            skillRows = byRepo
                .OrderByDescending(entry => entry.Value[0])
                .ThenBy(entry => entry.Key, StringComparer.Ordinal)
                .Take(RepoListCap)
                .Select(entry => new SddSkillRateRow(
                    entry.Key, entry.Value[0], entry.Value[1],
                    entry.Value[0] == 0 ? 0 : (double)entry.Value[1] / entry.Value[0]))
                .ToList();
        }

        return new SddPanelGold(orderingRows, orderingSummary, writesRows, machineManagedWrites, skillRows, skillConfigured);
    }

    private static (List<WriteReadRow> Top, WriteReadSummary Summary) WriteReadLoop(DuckDBConnection connection, KnowledgeRegistry registry, DateTimeOffset now)
    {        DateTime cutoff = now.AddDays(-ThemeWindowDays).UtcDateTime;

        Dictionary<string, DateTime> firstWrite = new();
        foreach (object?[] row in Query(connection, """
            SELECT subject, min(time) AS first_write
            FROM practice_events
            WHERE type = 'knowledge.written' AND subject IS NOT NULL AND time >= $cutoff
            GROUP BY subject
            """, ("cutoff", cutoff)))
        {
            string subject = (string)row[0]!;
            if (registry.Resolve(subject) is null || ContentKind.Of(subject) != ContentKind.Knowledge)
            {
                continue;
            }
            firstWrite[subject] = (DateTime)row[1]!;
        }

        Dictionary<string, long> laterReads = new();
        foreach (object?[] row in Query(connection, """
            SELECT subject, time
            FROM practice_events
            WHERE type = 'knowledge.read' AND subject IS NOT NULL AND time >= $cutoff
            """, ("cutoff", cutoff)))
        {
            string subject = (string)row[0]!;
            if (firstWrite.TryGetValue(subject, out DateTime written) && (DateTime)row[1]! > written)
            {
                laterReads[subject] = laterReads.GetValueOrDefault(subject) + 1;
            }
        }

        List<WriteReadRow> top = laterReads
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Take(TopListCap)
            .Select(entry => new WriteReadRow(entry.Key, entry.Value))
            .ToList();
        long writtenCount = firstWrite.Count;
        return (top, new WriteReadSummary(writtenCount, laterReads.Count, writtenCount == 0 ? 0 : (double)laterReads.Count / writtenCount));
    }

    private static (List<ReuseRow> Top, ReuseSummary Summary) NoteReuse(DuckDBConnection connection, KnowledgeRegistry registry, DateTimeOffset now)
    {
        List<ReuseRow> notes = new();
        foreach (object?[] row in Query(connection, """
            SELECT subject, count(*) AS reads, count(DISTINCT session) AS sessions
            FROM practice_events
            WHERE type = 'knowledge.read' AND subject IS NOT NULL AND time >= $cutoff
            GROUP BY subject
            """, ("cutoff", now.AddDays(-ThemeWindowDays).UtcDateTime)))
        {
            string subject = (string)row[0]!;
            if (registry.Resolve(subject) is null || ContentKind.Of(subject) != ContentKind.Knowledge)
            {
                continue;
            }
            notes.Add(new ReuseRow(subject, AsLong(row[2]), AsLong(row[1])));
        }

        long singleUse = notes.Count(note => note.Sessions <= 1);
        List<ReuseRow> top = notes
            .OrderByDescending(note => note.Sessions)
            .ThenByDescending(note => note.Reads)
            .ThenBy(note => note.Path, StringComparer.Ordinal)
            .Take(TopListCap)
            .ToList();
        return (top, new ReuseSummary(notes.Count, singleUse, notes.Count == 0 ? 0 : (double)singleUse / notes.Count));
    }

    private static List<DayCount> TopFailedSearches(DuckDBConnection connection, DateTimeOffset now)
    {
        List<DayCount> rows = new();
        foreach (object?[] row in Query(connection, $"""
            SELECT subject, count(*)
            FROM practice_events
            WHERE type = 'knowledge.searched' AND time >= $cutoff
              AND subject IS NOT NULL
              AND TRY_CAST(json_extract_string(data, '$.hits') AS BIGINT) = 0
            GROUP BY subject
            ORDER BY count(*) DESC, subject
            LIMIT {TopListCap}
            """, ("cutoff", now.AddDays(-ThemeWindowDays).UtcDateTime)))
        {
            rows.Add(new DayCount((string)row[0]!, AsLong(row[1])));
        }
        return rows;
    }

    private static HashSet<string> TouchedSessions(DuckDBConnection connection, KnowledgeRegistry registry)
    {
        HashSet<string> registeredIds = registry.Sources.Select(source => source.Id).ToHashSet();
        HashSet<string> touchedSessions = new();
        foreach (object?[] row in Query(connection, """
            SELECT DISTINCT session, subject, kbroot
            FROM events_preferred
            WHERE session IS NOT NULL AND (subject IS NOT NULL OR kbroot IS NOT NULL)
            """))
        {
            string session = (string)row[0]!;
            if (touchedSessions.Contains(session))
            {
                continue;
            }
            bool resolvesNow = row[1] is string subject && registry.Resolve(subject) is not null;
            bool stampStillRegistered = row[2] is string kbroot && registeredIds.Contains(kbroot);
            if (resolvesNow || stampStillRegistered)
            {
                touchedSessions.Add(session);
            }
        }
        return touchedSessions;
    }

    private static List<RecentSessionRow> RecentSessions(DuckDBConnection connection, HashSet<string> touchedSessions)
    {
        Dictionary<string, long[]> countsBySession = new();
        foreach (object?[] row in Query(connection, """
            SELECT session, type, count(*)
            FROM events_preferred
            WHERE session IS NOT NULL
              AND type IN ('knowledge.read', 'knowledge.searched', 'skill.invoked', 'knowledge.written')
            GROUP BY session, type
            """))
        {
            string session = (string)row[0]!;
            long[] slot = countsBySession.TryGetValue(session, out long[]? existing) ? existing : countsBySession[session] = new long[4];
            int index = (string)row[1]! switch
            {
                EventTypes.KnowledgeRead => 0,
                EventTypes.KnowledgeSearched => 1,
                EventTypes.SkillInvoked => 2,
                EventTypes.KnowledgeWritten => 3,
                _ => -1,
            };
            if (index >= 0)
            {
                slot[index] = AsLong(row[2]);
            }
        }

        List<RecentSessionRow> rows = new();
        foreach (object?[] row in Query(connection, $"""
            SELECT session, agent, coalesce(repo, '(unknown)') AS repo, started_at,
                   coalesce(input_tokens, 0), coalesce(cache_read_tokens, 0)
            FROM sessions
            ORDER BY started_at DESC
            LIMIT {RecentSessionCap}
            """))
        {
            string session = (string)row[0]!;
            DateTime started = (DateTime)row[3]!;
            long[] counts = countsBySession.GetValueOrDefault(session) ?? new long[4];
            rows.Add(new RecentSessionRow(
                started.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                started.ToString("HH:mm", CultureInfo.InvariantCulture),
                (string)row[1]!, (string)row[2]!,
                counts[0], counts[1], counts[2], counts[3],
                touchedSessions.Contains(session), AsLong(row[4]), AsLong(row[5])));
        }
        return rows;
    }

    private static List<JobHealthTile> JobHealth(DuckDBConnection connection, DateTimeOffset now)
    {
        List<JobHealthTile> tiles = new();
        foreach (object?[] row in Query(connection, """
            SELECT machine, agent, subject, max(time)
            FROM events
            WHERE type = 'job.completed' AND subject IS NOT NULL
            GROUP BY machine, agent, subject
            ORDER BY machine, agent, subject
            """))
        {
            DateTimeOffset last = AsUtc(row[3]);
            string job = (string)row[2]!;
            double daysSilent = (now - last).TotalDays;
            tiles.Add(new JobHealthTile(
                (string)row[0]!, (string)row[1]!, job, last,
                Math.Round(daysSilent, 1),
                daysSilent > JobDeadMan.ThresholdDays(job) ? "red" : "ok"));
        }
        return tiles;
    }

    private static List<LastSeenTile> LastSeen(DuckDBConnection connection, DateTimeOffset now)
    {
        List<LastSeenTile> tiles = new();
        foreach (object?[] row in Query(connection, """
            SELECT machine, agent, max(time)
            FROM events
            GROUP BY machine, agent
            ORDER BY machine, agent
            """))
        {
            DateTimeOffset last = AsUtc(row[2]);
            double daysSilent = (now - last).TotalDays;
            tiles.Add(new LastSeenTile(
                (string)row[0]!, (string)row[1]!, last,
                Math.Round(daysSilent, 1),
                daysSilent > DeadManThresholdDays ? "red" : "ok"));
        }
        return tiles;
    }

    /// <summary>
    /// Service sessions in the window — excluded from the practice lenses,
    /// stated on the dashboard per the no-silent-caps rule (ADR-0039).
    /// </summary>
    private static ServiceSessionsSummary ServiceSessions(DuckDBConnection connection, DateTimeOffset now)
    {
        foreach (object?[] row in Query(connection, """
            SELECT count(DISTINCT session),
                   coalesce(string_agg(DISTINCT json_extract_string(data, '$.raw.agent_mode'), ', '), '')
            FROM events_preferred
            WHERE type = 'session.started' AND session IS NOT NULL
              AND json_extract_string(data, '$.raw.agent_mode') LIKE 'service-%'
              AND time >= $cutoff
            """, ("cutoff", now.AddDays(-ThemeWindowDays).UtcDateTime)))
        {
            return new ServiceSessionsSummary(AsLong(row[0]), (string)row[1]!);
        }
        return new ServiceSessionsSummary(0, "");
    }

    private static List<FailedSearchRow> FailedSearches(DuckDBConnection connection)
    {
        List<FailedSearchRow> rows = new();
        foreach (object?[] row in Query(connection, """
            SELECT strftime(date_trunc('day', time), '%Y-%m-%d') AS day,
                   count(*) AS searches,
                   count(*) FILTER (WHERE TRY_CAST(json_extract_string(data, '$.hits') AS BIGINT) = 0) AS zero_hits
            FROM practice_events
            WHERE type = 'knowledge.searched'
              AND TRY_CAST(json_extract_string(data, '$.hits') AS BIGINT) IS NOT NULL
            GROUP BY day
            ORDER BY day
            """))
        {
            long searches = AsLong(row[1]);
            long zeroHits = AsLong(row[2]);
            rows.Add(new FailedSearchRow((string)row[0]!, searches, zeroHits, searches == 0 ? 0 : (double)zeroHits / searches));
        }
        return rows;
    }

    private static List<TokensRow> Tokens(DuckDBConnection connection)
    {
        List<TokensRow> rows = new();
        foreach (object?[] row in Query(connection, """
            SELECT strftime(date_trunc('day', started_at), '%Y-%m-%d') AS day,
                   coalesce(sum(input_tokens), 0),
                   coalesce(sum(cache_read_tokens), 0)
            FROM sessions
            GROUP BY day
            ORDER BY day
            """))
        {
            rows.Add(new TokensRow((string)row[0]!, AsLong(row[1]), AsLong(row[2])));
        }
        return rows;
    }

    private static List<ThemeReadsRow> ReadsByTheme(
        DuckDBConnection connection, KnowledgeRegistry registry, DateTimeOffset now)
    {
        Dictionary<string, KnowledgeSource> sourcesById = registry.Sources.ToDictionary(source => source.Id);

        Dictionary<(string Source, string Theme), long> reads = new();
        foreach (object?[] row in Query(connection, """
            SELECT subject, count(*)
            FROM practice_events
            WHERE type IN ('knowledge.read', 'context.loaded')
              AND subject IS NOT NULL
              AND time >= $cutoff
            GROUP BY subject
            """, ("cutoff", now.AddDays(-ThemeWindowDays).UtcDateTime)))
        {
            string subject = (string)row[0]!;
            string? sourceId = registry.Resolve(subject);
            if (sourceId is null)
            {
                continue;
            }
            (string, string) key = (sourceId, ThemeOf(sourcesById[sourceId], subject));
            reads[key] = reads.GetValueOrDefault(key) + AsLong(row[1]);
        }

        Dictionary<(string Source, string Theme), long> notes = new();
        foreach (InventoryNote note in NoteInventory.Scan(registry))
        {
            (string, string) key = (note.SourceId, ThemeOf(sourcesById[note.SourceId], note.Path));
            notes[key] = notes.GetValueOrDefault(key) + 1;
        }

        List<ThemeReadsRow> rows = reads.Keys.Union(notes.Keys)
            .Select(key => new ThemeReadsRow(
                key.Theme.Length == 0 ? key.Source : $"{key.Source}/{key.Theme}",
                key.Source,
                reads.GetValueOrDefault(key),
                notes.GetValueOrDefault(key)))
            .ToList();
        return rows.Where(row => row.Reads == 0)
            .OrderByDescending(row => row.Notes).ThenBy(row => row.Theme, StringComparer.Ordinal)
            .ToList();
    }

    private static string ThemeOf(KnowledgeSource source, string path)
    {
        string relative = Path.GetRelativePath(source.Root, path);
        int separator = relative.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
        return separator < 0 ? "" : relative[..separator];
    }

    private static IEnumerable<object?[]> Query(DuckDBConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        using DuckDBCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.Add(new DuckDBParameter(name, value));
        }
        using DuckDBDataReader reader = (DuckDBDataReader)command.ExecuteReader();
        while (reader.Read())
        {
            object?[] values = new object?[reader.FieldCount];
            for (int index = 0; index < reader.FieldCount; index++)
            {
                values[index] = reader.IsDBNull(index) ? null : reader.GetValue(index);
            }
            yield return values;
        }
    }

    private static long AsLong(object? value)
    {
        if (value is BigInteger bigInteger)
        {
            return (long)bigInteger;
        }
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    /// <summary>The ISO week-year: the year of the week's Thursday — a
    /// Dec 29 can already belong to next year's W01.</summary>
    private static int IsoWeekYear(DateTime date)
    {
        int daysFromMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(3 - daysFromMonday).Year;
    }

    /// <summary>Monday 00:00 of the week the date falls in — the mirror's
    /// snapshot grid anchor (BL-033).</summary>
    private static DateTime StartOfIsoWeek(DateTime date)
    {
        int daysFromMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.Date.AddDays(-daysFromMonday);
    }

    private static DateTimeOffset AsUtc(object? value)
    {
        DateTime dateTime = (DateTime)value!;
        return new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
    }
}
