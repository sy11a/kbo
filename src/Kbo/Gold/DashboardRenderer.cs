using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kbo.Gold;

/// <summary>
/// DashboardGold + chart specs → static HTML. Renders what gold computed —
/// zero computation (P2). Charts are the owner-editable charts/*.vl.json
/// specs embedded at build; data is injected inline.
/// </summary>
public static class DashboardRenderer
{
    private static readonly JsonSerializerOptions DataJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static IReadOnlyDictionary<string, string> LoadEmbeddedChartSpecs()
    {
        Dictionary<string, string> specs = new();
        Assembly assembly = typeof(DashboardRenderer).Assembly;
        foreach (string resourceName in assembly.GetManifestResourceNames().Where(name => name.StartsWith("charts/", StringComparison.Ordinal)))
        {
            using Stream stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded chart resource '{resourceName}' could not be opened.");
            using StreamReader reader = new(stream);
            specs[Path.GetFileName(resourceName)] = reader.ReadToEnd();
        }
        return specs;
    }

    public static string Render(DashboardGold gold, IReadOnlyDictionary<string, string> chartSpecs, int? autoReloadSeconds = null)
    {
        StringBuilder html = new();
        html.AppendLine("<!doctype html>");
        html.AppendLine("<html lang=\"en\">");
        html.AppendLine("<head>");
        html.AppendLine("<meta charset=\"utf-8\">");
        if (autoReloadSeconds is int reloadSeconds)
        {
            html.AppendLine(CultureInfo.InvariantCulture, $"<meta http-equiv=\"refresh\" content=\"{reloadSeconds}\">");
        }
        html.AppendLine("<title>kbo dashboard</title>");
        html.AppendLine("""
            <script src="https://cdn.jsdelivr.net/npm/vega@5.30.0/build/vega.min.js" integrity="sha384-em7CHpJd+SsMugVFf6TY7AKQcLWMcbPhD84hmNK8o6WFDkK+2uHSUQRVQV1/w827" crossorigin="anonymous"></script>
            <script src="https://cdn.jsdelivr.net/npm/vega-lite@5.21.0/build/vega-lite.min.js" integrity="sha384-GhkD6ks9/zgY1m5EFOUZWz/vMVMUFF/92DL61RZc+B42J8osL+jNufKv68bNHHZ2" crossorigin="anonymous"></script>
            <script src="https://cdn.jsdelivr.net/npm/vega-embed@6.26.0/build/vega-embed.min.js" integrity="sha384-TqXb8su49m5OnEpKGO8m+VrgHesrUxyP22HgpXi4hnh1Hm43dXroiSYemNf5D8lv" crossorigin="anonymous"></script>
            <style>
              body { font-family: system-ui, sans-serif; margin: 2rem auto; max-width: 960px; padding: 0 1rem; background: #fcfcfb; color: #1a1a19; }
              h1 { margin-bottom: 0.2rem; }
              .generated-at { font-size: 1.05rem; color: #5f5e56; margin-bottom: 2rem; }
              .generated-at strong { color: #1a1a19; }
              .tiles { display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 12px; margin: 1rem 0 2rem; }
              .tile { border: 1px solid #e4e3db; border-radius: 8px; padding: 12px 14px; background: #ffffff; }
              .tile .name { font-weight: 600; }
              .tile .meta { color: #5f5e56; font-size: 0.85rem; margin-top: 4px; }
              .tile .status { margin-top: 6px; font-weight: 600; }
              .tile.ok .status { color: #008300; }
              .tile.red .status { color: #c22e2d; }
              .tile.red { border-color: #e34948; background: #fdf3f3; }
              .chart { margin: 2rem 0; }
              .chart h2 { font-size: 1.1rem; }
              figure { margin: 0; }
              .desc { color: #5f5e56; font-size: 0.92rem; margin: 0.2rem 0 0.8rem; max-width: 75ch; }
              .unused { margin: 0.5rem 0 2rem; padding-left: 1.2rem; }
              .unused li { margin: 3px 0; }
              .repos { border-collapse: collapse; width: 100%; margin: 0.5rem 0 2rem; font-size: 0.9rem; }
              .repos th, .repos td { text-align: left; padding: 6px 10px; border-bottom: 1px solid #e4e3db; }
              .repos th { color: #5f5e56; font-weight: 600; }
              .repos td.path { font-family: ui-monospace, monospace; word-break: break-all; }
              .wow { list-style: none; padding-left: 0; margin: 0.5rem 0 2rem; }
              .wow li { margin: 5px 0; }
              .wow .good { color: #008300; font-weight: 600; }
              .wow .bad { color: #c22e2d; font-weight: 600; }
              .wow .muted { color: #5f5e56; font-size: 0.9em; }
            </style>
            </head>
            <body>
            <h1>kbo dashboard</h1>
            """);

        html.AppendLine(CultureInfo.InvariantCulture,
            $"""<p class="generated-at">generated at <strong>{Timestamp(gold.GeneratedAt)}</strong> on <strong>{Html(gold.Machine)}</strong> — a stale dashboard must look stale</p>""");

        html.AppendLine(CultureInfo.InvariantCulture,
            $"<h2>Dead-man health — red past the job's cadence threshold (daily {gold.DeadManThresholdDays}d, weekly {gold.WeeklyDeadManThresholdDays}d)</h2>");
        AppendDescription(html, FormattableString.Invariant(
            $"Здоровье фоновых задач: плитка становится красной, если задача молчит дольше порога своей каденции — {gold.DeadManThresholdDays} дн. для ежедневных, {gold.WeeklyDeadManThresholdDays} дн. для еженедельных (report, audit). Если плитка красная — смотрите журнал: journalctl --user -u kbo-pulse.service."));
        html.AppendLine("""<div class="tiles">""");
        foreach (JobHealthTile tile in gold.JobHealth)
        {
            AppendTile(html, tile.Status, tile.Job, $"{tile.Machine} · {tile.Agent}",
                $"last completed {Timestamp(tile.LastCompleted)}", tile.DaysSilent);
        }
        html.AppendLine("</div>");

        html.AppendLine("<h2>Last seen in bronze — per machine × agent</h2>");
        AppendDescription(html,
            "Когда каждый агент последний раз записывал события. Если задачи выше зелёные, а агент давно молчит — сломан захват событий (хук или плагин этого агента).");
        html.AppendLine("""<div class="tiles">""");
        foreach (LastSeenTile tile in gold.LastSeen)
        {
            AppendTile(html, tile.Status, tile.Agent, tile.Machine,
                $"last event {Timestamp(tile.LastEvent)}", tile.DaysSilent);
        }
        html.AppendLine("</div>");

        AppendWeekOverWeek(html, gold.WeekOverWeek);
        AppendSessionsByRepo(html, gold.SessionsByRepo);
        AppendRecentSessions(html, gold.RecentSessions);
        AppendRankedList(html, FormattableString.Invariant($"Top skills used — last {DashboardComputer.ThemeWindowDays} days"),
            "Какие навыки (skills) агенты вызывали чаще всего за окно — из событий skill.invoked, добытых из транскриптов.",
            gold.TopSkills, "За окно не зафиксировано ни одного вызова навыка.", monospace: false);

        AppendChart(html, "reads-over-time", "Knowledge reads per day, by layer",
            chartSpecs["reads-over-time.vl.json"], gold.ReadsByLayerDaily);
        AppendRankedList(html, FormattableString.Invariant($"Reads by content type — last {DashboardComputer.ThemeWindowDays} days"),
            "Из чего состоят «чтения знаний»: knowledge — настоящие заметки (.md и т.п.), code/config — исходники и конфиги, попавшие под регистрацию целых репозиториев. Важно: остальные метрики (KB-touch, повторное использование, темы) считают ВСЕ зарегистрированные чтения, включая код — эта разбивка показывает, какая доля из них действительно про знания.",
            gold.ReadsByContentType, "За окно не было зарегистрированных чтений.", monospace: false);
        AppendChart(html, "reads-by-theme",
            FormattableString.Invariant($"Most-read knowledge themes — last {DashboardComputer.ThemeWindowDays} days"),
            chartSpecs["reads-by-theme.vl.json"], gold.ThemeReads);
        AppendUnusedThemes(html, gold.UnusedThemes);
        AppendReuse(html, gold.TopReusedNotes, gold.Reuse);
        AppendWriteReadLoop(html, gold.TopWriteReadNotes, gold.WriteReadLoop);
        AppendChart(html, "kb-touch-rate", "Share of sessions touching registered knowledge",
            chartSpecs["kb-touch-rate.vl.json"], gold.KbTouchDaily);
        AppendChart(html, "failed-search-rate", "Zero-hit share of knowledge searches",
            chartSpecs["failed-search-rate.vl.json"], gold.FailedSearchDaily);
        AppendRankedList(html, FormattableString.Invariant($"Top zero-hit searches — last {DashboardComputer.ThemeWindowDays} days"),
            "Запросы, которые чаще всего ничего не находили за окно. Каждая строка — кандидат на новую или переименованную заметку.",
            gold.TopFailedSearches, "За окно не было поисков без результата. ✓", monospace: true);
        AppendChart(html, "tokens-trend", "Cache-read vs fresh input tokens per day",
            chartSpecs["tokens-trend.vl.json"], gold.TokensDaily);

        html.AppendLine("</body>");
        html.AppendLine("</html>");
        return html.ToString();
    }

    private static void AppendTile(StringBuilder html, string status, string name, string scope, string lastLine, double daysSilent)
    {
        string symbol = status == "ok" ? "✓ ok" : "✗ SILENT";
        string statusClass = status == "ok" ? "ok" : "red";
        html.AppendLine(CultureInfo.InvariantCulture, $"""
            <div class="tile {statusClass}">
              <div class="name">{Html(name)}</div>
              <div class="meta">{Html(scope)}</div>
              <div class="meta">{Html(lastLine)}</div>
              <div class="status">{symbol} — {daysSilent.ToString("0.#", CultureInfo.InvariantCulture)}d silent</div>
            </div>
            """);
    }

    private static string Html(string value)
    {
        return WebUtility.HtmlEncode(value);
    }

    private static void AppendDescription(StringBuilder html, string text)
    {
        html.AppendLine(CultureInfo.InvariantCulture, $"""<p class="desc">{Html(text)}</p>""");
    }

    private static void AppendWeekOverWeek(StringBuilder html, IReadOnlyList<MetricDelta> metrics)
    {
        html.AppendLine("<h2>This week vs last week</h2>");
        AppendDescription(html,
            "Изменение ключевых метрик за последние 7 дней относительно предыдущих 7. Зелёное — практика улучшается, красное — ухудшается; pp — процентные пункты.");
        html.AppendLine("""<ul class="wow">""");
        foreach (MetricDelta metric in metrics)
        {
            double delta = metric.Current - metric.Previous;
            bool equal = Math.Abs(delta) < 1e-9;
            bool improved = !equal && (metric.HigherIsBetter ? delta > 0 : delta < 0);
            string arrow = equal ? "→" : (metric.Current > metric.Previous ? "↑" : "↓");
            string cssClass = equal ? "muted" : (improved ? "good" : "bad");
            bool percent = metric.Format == "percent";
            string current = percent ? Percent(metric.Current) : ((long)metric.Current).ToString("N0", CultureInfo.InvariantCulture);
            string previous = percent ? Percent(metric.Previous) : ((long)metric.Previous).ToString("N0", CultureInfo.InvariantCulture);
            string change = percent
                ? Signed(delta * 100) + "pp"
                : Signed(delta);
            html.AppendLine(CultureInfo.InvariantCulture,
                $"""<li>{Html(metric.Label)}: <strong>{current}</strong> <span class="{cssClass}">{arrow} {change}</span> <span class="muted">(было {previous})</span></li>""");
        }
        html.AppendLine("</ul>");
    }

    private static string Signed(double value)
    {
        string formatted = value.ToString("0.#", CultureInfo.InvariantCulture);
        return value >= 0 ? "+" + formatted : formatted;
    }

    private static string Percent(double rate)
    {
        return (rate * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
    }

    private static void AppendRecentSessions(StringBuilder html, IReadOnlyList<RecentSessionRow> sessions)
    {
        html.AppendLine(CultureInfo.InvariantCulture,
            $"<h2>Recent sessions — last {DashboardComputer.RecentSessionCap}</h2>");
        AppendDescription(html,
            "Последние сессии агентов: время, агент, папка, число операций (чтения · поиски · навыки · записи), затронута ли база знаний, токены. Куда смотреть: сессия с множеством операций, но без отметки в колонке KB — работа шла мимо зарегистрированных знаний.");
        if (sessions.Count == 0)
        {
            AppendDescription(html, "Пока нет ни одной сессии.");
            return;
        }
        html.AppendLine("""<table class="repos"><thead><tr><th>when</th><th>agent</th><th>repo</th><th>reads</th><th>searches</th><th>skills</th><th>writes</th><th>KB</th><th>tokens in/cache</th></tr></thead><tbody>""");
        foreach (RecentSessionRow session in sessions)
        {
            string touch = session.TouchedKb ? "✓" : "—";
            string tokens = FormattableString.Invariant($"{session.InputTokens / 1000}k/{session.CacheReadTokens / 1000}k");
            html.AppendLine(CultureInfo.InvariantCulture,
                $"<tr><td>{Html(session.Date)} {Html(session.Time)}</td><td>{Html(session.Agent)}</td><td class=\"path\">{Html(RepoLabel(session.Repo))}</td><td>{session.Reads}</td><td>{session.Searches}</td><td>{session.Skills}</td><td>{session.Writes}</td><td>{touch}</td><td>{Html(tokens)}</td></tr>");
        }
        html.AppendLine("</tbody></table>");
    }

    private static string RepoLabel(string repo)
    {
        string trimmed = repo.TrimEnd('/');
        int slash = trimmed.LastIndexOf('/');
        return slash >= 0 && slash < trimmed.Length - 1 ? trimmed[(slash + 1)..] : trimmed;
    }

    private static void AppendSessionsByRepo(StringBuilder html, IReadOnlyList<RepoSessionsRow> sessionsByRepo)
    {
        html.AppendLine(CultureInfo.InvariantCulture,
            $"<h2>Sessions by repository — last {DashboardComputer.ThemeWindowDays} days</h2>");
        AppendDescription(html, FormattableString.Invariant(
            $"Источник данных: рабочие папки (репозитории), из которых агенты запускали сессии за последние {DashboardComputer.ThemeWindowDays} дней — полный путь, число сессий, агенты и дата последней сессии. Куда смотреть: это карта того, где вы реально работаете; папка с множеством сессий, но без чтения знаний (см. графики ниже) — кандидат на регистрацию в реестре."));
        if (sessionsByRepo.Count == 0)
        {
            AppendDescription(html, "За это окно не найдено ни одной сессии.");
            return;
        }
        html.AppendLine("""<table class="repos"><thead><tr><th>repository / folder</th><th>sessions</th><th>agents</th><th>last session</th></tr></thead><tbody>""");
        foreach (RepoSessionsRow row in sessionsByRepo)
        {
            html.AppendLine(CultureInfo.InvariantCulture,
                $"<tr><td class=\"path\">{Html(row.Repo)}</td><td>{row.Sessions}</td><td>{Html(row.Agents)}</td><td>{Timestamp(row.LastStarted)}</td></tr>");
        }
        html.AppendLine("</tbody></table>");
    }

    private static void AppendWriteReadLoop(StringBuilder html, IReadOnlyList<WriteReadRow> topWriteRead, WriteReadSummary loop)
    {
        html.AppendLine(CultureInfo.InvariantCulture,
            $"<h2>Write → read loop — last {DashboardComputer.ThemeWindowDays} days</h2>");
        string loopPercent = (loop.LoopRate * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
        AppendDescription(html, FormattableString.Invariant(
            $"Замыкается ли петля знаний: из {loop.Written} заметок (.md), которые агенты СОЗДАЛИ или изменили за окно, {loop.Reused} ({loopPercent}) позже кто-то прочитал. Высокая доля — знания, произведённые в работе, реально переиспользуются; низкая — агенты пишут заметки, к которым потом не возвращаются."));
        if (topWriteRead.Count == 0)
        {
            AppendDescription(html, "За окно не было написанных и затем прочитанных заметок.");
            return;
        }
        html.AppendLine("""<table class="repos"><thead><tr><th>note (written, then read)</th><th>later reads</th></tr></thead><tbody>""");
        foreach (WriteReadRow note in topWriteRead)
        {
            html.AppendLine(CultureInfo.InvariantCulture,
                $"<tr><td class=\"path\">{Html(note.Path)}</td><td>{note.LaterReads}</td></tr>");
        }
        html.AppendLine("</tbody></table>");
    }

    private static void AppendReuse(StringBuilder html, IReadOnlyList<ReuseRow> topReused, ReuseSummary reuse)
    {
        html.AppendLine(CultureInfo.InvariantCulture,
            $"<h2>Most-reused knowledge notes — last {DashboardComputer.ThemeWindowDays} days</h2>");
        string singleUsePercent = (reuse.SingleUseRate * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
        AppendDescription(html, FormattableString.Invariant(
            $"Заметки (.md), отсортированные по охвату — в скольких РАЗНЫХ сессиях их читали (это надёжнее, чем общее число чтений). Из {reuse.Notes} прочитанных заметок {reuse.SingleUse} ({singleUsePercent}) читались лишь в одной сессии — разовые. Верх списка — несущее ядро базы знаний; кандидаты на продвижение и связывание. Разовые — кандидаты на пересмотр."));
        if (topReused.Count == 0)
        {
            AppendDescription(html, "За окно не было чтений заметок.");
            return;
        }
        html.AppendLine("""<table class="repos"><thead><tr><th>note</th><th>sessions</th><th>reads</th></tr></thead><tbody>""");
        foreach (ReuseRow note in topReused)
        {
            html.AppendLine(CultureInfo.InvariantCulture,
                $"<tr><td class=\"path\">{Html(note.Path)}</td><td>{note.Sessions}</td><td>{note.Reads}</td></tr>");
        }
        html.AppendLine("</tbody></table>");
    }

    private static void AppendRankedList(StringBuilder html, string heading, string ruDescription,
        IReadOnlyList<DayCount> items, string emptyMessage, bool monospace)
    {
        html.AppendLine(CultureInfo.InvariantCulture, $"<h2>{Html(heading)}</h2>");
        AppendDescription(html, ruDescription);
        if (items.Count == 0)
        {
            AppendDescription(html, emptyMessage);
            return;
        }
        html.AppendLine("""<ul class="unused">""");
        foreach (DayCount item in items)
        {
            string label = monospace ? $"<code>{Html(item.Label)}</code>" : $"<strong>{Html(item.Label)}</strong>";
            html.AppendLine(CultureInfo.InvariantCulture, $"<li>{label} — {item.Count}</li>");
        }
        html.AppendLine("</ul>");
    }

    private static void AppendUnusedThemes(StringBuilder html, IReadOnlyList<ThemeReadsRow> unusedThemes)
    {
        html.AppendLine(CultureInfo.InvariantCulture,
            $"<h2>Never-read themes — last {DashboardComputer.ThemeWindowDays} days</h2>");
        AppendDescription(html, FormattableString.Invariant(
            $"Разделы базы знаний, которые ни разу не читались за последние {DashboardComputer.ThemeWindowDays} дней. Это кандидаты на пересмотр: устарели, плохо названы или просто забыты."));
        if (unusedThemes.Count == 0)
        {
            AppendDescription(html, "Таких разделов нет — все разделы базы знаний читались в этом окне. ✓");
            return;
        }
        html.AppendLine("""<ul class="unused">""");
        foreach (ThemeReadsRow theme in unusedThemes)
        {
            html.AppendLine(CultureInfo.InvariantCulture,
                $"<li><strong>{Html(theme.Theme)}</strong> — заметок: {theme.Notes}</li>");
        }
        html.AppendLine("</ul>");
    }

    private static void AppendChart<T>(StringBuilder html, string id, string title, string specJson, IReadOnlyList<T> rows)
    {
        JsonNode spec = JsonNode.Parse(specJson)!;
        string? russianDescription = spec["usermeta"]?["kbo"]?["ru"]?.GetValue<string>();
        spec["data"] = new JsonObject
        {
            ["values"] = JsonNode.Parse(JsonSerializer.Serialize(rows, DataJsonOptions)),
        };

        html.AppendLine(CultureInfo.InvariantCulture, $$"""
            <div class="chart">
              <h2>{{Html(title)}}</h2>
            """);
        if (russianDescription is not null)
        {
            AppendDescription(html, russianDescription);
        }
        html.AppendLine(CultureInfo.InvariantCulture, $$"""
              <figure id="{{id}}" style="width:100%"></figure>
              <script>
                vegaEmbed("#{{id}}", {{spec.ToJsonString()}}, { actions: false });
              </script>
            </div>
            """);
    }

    private static string Timestamp(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }
}
