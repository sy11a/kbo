using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kbo.Jobs;

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
              .tile.amber .status { color: #a06b00; }
              .tile.amber { border-color: #d9a441; background: #fdf8ef; }
              .tile.wait { opacity: 0.65; }
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
              .repos td.good { color: #008300; font-weight: 600; }
              .repos td.bad { color: #c22e2d; font-weight: 600; }
              .healthline { font-weight: 600; margin: 0.5rem 0 1rem; }
              .healthline.ok { color: #008300; }
              .healthline.red { color: #c22e2d; }
              details { margin: 1rem 0 2rem; }
              details summary { cursor: pointer; font-weight: 600; color: #1a1a19; }
            </style>
            </head>
            <body>
            <h1>kbo dashboard</h1>
            """);

        html.AppendLine(CultureInfo.InvariantCulture,
            $"""<p class="generated-at">generated at <strong>{Timestamp(gold.GeneratedAt)}</strong> on <strong>{Html(gold.Machine)}</strong> — a stale dashboard must look stale</p>""");

        AppendServiceDisclosure(html, gold.ServiceSessions);

        AppendPracticeMirror(html, gold.Mirror);

        AppendDeadMan(html, gold);

        AppendLastSeen(html, gold.LastSeen);

        AppendSddPanel(html, gold.SddPanel);
        AppendReuse(html, gold.TopReusedNotes, gold.Reuse);
        AppendUnusedThemes(html, gold.UnusedThemes);
        AppendWriteReadLoop(html, gold.TopWriteReadNotes, gold.WriteReadLoop);
        AppendChart(html, "failed-search-rate", "Zero-hit share of knowledge searches",
            chartSpecs["failed-search-rate.vl.json"], gold.FailedSearchDaily);
        AppendRankedList(html, FormattableString.Invariant($"Top zero-hit searches — last {DashboardComputer.ThemeWindowDays} days"),
            "Запросы, которые чаще всего ничего не находили за окно. Каждая строка — кандидат на новую или переименованную заметку.",
            gold.TopFailedSearches, "За окно не было поисков без результата. ✓", monospace: true);
        AppendChart(html, "tokens-trend", "Cache-read vs fresh input tokens per day",
            chartSpecs["tokens-trend.vl.json"], gold.TokensDaily);
        AppendRecentSessions(html, gold.RecentSessions);

        html.AppendLine("</body>");
        html.AppendLine("</html>");
        return html.ToString();
    }

    /// <summary>Dead-man (ADR-0042 §5, ADR-0037): one strip line when every job
    /// is inside its cadence; a red job restores its full tile — silence is the
    /// signal, green tiles are wallpaper (BL-035 Q3).</summary>
    private static void AppendDeadMan(StringBuilder html, DashboardGold gold)
    {
        html.AppendLine(CultureInfo.InvariantCulture,
            $"<h2>Dead-man health — red past the job's cadence threshold (daily {gold.DeadManThresholdDays}d, weekly {gold.WeeklyDeadManThresholdDays}d)</h2>");
        AppendDescription(html, FormattableString.Invariant(
            $"Здоровье фоновых задач: работа становится красной, если молчит дольше порога своей каденции — {gold.DeadManThresholdDays} дн. для ежедневных, {gold.WeeklyDeadManThresholdDays} дн. для еженедельных (report, audit). Если работа красная — смотрите журнал: journalctl --user -u kbo-pulse.service."));
        if (gold.JobHealth.Count == 0)
        {
            AppendDescription(html, "Ни одной завершённой работы (job.completed) в бронзе пока нет.");
            return;
        }
        int okCount = gold.JobHealth.Count(tile => tile.Status == "ok");
        string tone = okCount == gold.JobHealth.Count ? "ok" : "red";
        JobHealthTile oldest = OldestJob(gold.JobHealth);
        html.AppendLine(CultureInfo.InvariantCulture,
            $"""<p class="healthline {tone}">Dead-man: {okCount}/{gold.JobHealth.Count} ok · oldest {Html(oldest.Job)} {oldest.DaysSilent.ToString("0.#", CultureInfo.InvariantCulture)}d / limit {JobDeadMan.ThresholdDays(oldest.Job).ToString("0.#", CultureInfo.InvariantCulture)}d</p>""");
        List<JobHealthTile> red = gold.JobHealth.Where(tile => tile.Status != "ok").ToList();
        if (red.Count > 0)
        {
            html.AppendLine("""<div class="tiles">""");
            foreach (JobHealthTile tile in red)
            {
                AppendTile(html, tile.Status, tile.Job, $"{tile.Machine} · {tile.Agent}",
                    $"last completed {Timestamp(tile.LastCompleted)}", tile.DaysSilent);
            }
            html.AppendLine("</div>");
        }
    }

    private static JobHealthTile OldestJob(IReadOnlyList<JobHealthTile> tiles)
    {
        JobHealthTile oldest = tiles[0];
        foreach (JobHealthTile tile in tiles)
        {
            if (tile.DaysSilent > oldest.DaysSilent)
            {
                oldest = tile;
            }
        }
        return oldest;
    }

    /// <summary>Last-seen tiles collapsed into a details element (BL-035):
    /// provenance detail, not a mirror question — one summary line when closed.</summary>
    private static void AppendLastSeen(StringBuilder html, IReadOnlyList<LastSeenTile> lastSeen)
    {
        if (lastSeen.Count == 0)
        {
            return;
        }
        double newest = lastSeen.Min(tile => tile.DaysSilent);
        html.AppendLine(CultureInfo.InvariantCulture,
            $"""<details><summary>Last seen in bronze — {lastSeen.Count} agent(s) · newest {newest.ToString("0.#", CultureInfo.InvariantCulture)}d ago</summary>""");
        AppendDescription(html,
            "Когда каждый агент последний раз записывал события. Если работы выше зелёные, а агент давно молчит — сломан захват событий (хук или плагин этого агента).");
        html.AppendLine("""<div class="tiles">""");
        foreach (LastSeenTile tile in lastSeen)
        {
            AppendTile(html, tile.Status, tile.Agent, tile.Machine,
                $"last event {Timestamp(tile.LastEvent)}", tile.DaysSilent);
        }
        html.AppendLine("</div></details>");
    }

    /// <summary>The ADR-0039 disclosure: a note, not a section (BL-035 Q5) —
    /// no-silent-caps requires the excluded count to stay visible.</summary>
    private static void AppendServiceDisclosure(StringBuilder html, ServiceSessionsSummary service)
    {
        if (service.Sessions == 0)
        {
            return;
        }
        AppendDescription(html, FormattableString.Invariant(
            $"Служебные сессии: {service.Sessions} за последние {DashboardComputer.ThemeWindowDays} дней ({service.Agents}) исключены из метрик практики ниже (ADR-0039). Dead-man и last-seen видят их как обычно."));
    }

    private static void AppendPracticeMirror(StringBuilder html, PracticeMirrorGold? mirror)
    {
        if (mirror is null || mirror.Tiles.Count == 0)
        {
            return;
        }

        html.AppendLine("<h2>Practice mirror — six numbers, three micro-decisions</h2>");
        AppendDescription(html,
            "Первый экран. Состояние = эмодзи, не цвет: 🟢 стабильно в норме · 🔴 хроника (путь — бэклог, не тревога) · 📈📉 устойчивый тренд · ⚠️ требует внимания сейчас — единственный янтарь · ⏳ мало истории. Каждая плитка сравнивается со своим коридором (p25–p75 собственной истории) и целью — пороги приезжают из gold json, рендер их не хранит (BL-033).");
        html.AppendLine("""<div class="tiles">""");
        foreach (MirrorTile tile in mirror.Tiles)
        {
            string statusClass = tile.Status switch
            {
                "ok" => "ok",
                "acute" => "amber",
                "wait" => "wait",
                "sick" => "sick",
                "trend" => "trend",
                _ => "",
            };
            string tileClass = statusClass.Length == 0 ? "tile" : $"tile {statusClass}";
            html.AppendLine(CultureInfo.InvariantCulture, $"""
                <div class="{tileClass}">
                  <div class="name">{Html(tile.State)} {Html(tile.Label)}</div>
                  <div class="meta">{Html(tile.Value)} · {Html(tile.Trend)}</div>
                  <div class="meta">{Html(tile.Goal ?? "")}</div>
                  <div class="meta">{Html(tile.Hint)}</div>
                </div>
                """);
        }

        html.AppendLine("</div>");
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

    private static string RatePercent(double rate)
    {
        return FormattableString.Invariant($"{rate * 100:F0}%");
    }

    private static string Html(string value)
    {
        return WebUtility.HtmlEncode(value);
    }

    private static void AppendDescription(StringBuilder html, string text)
    {
        html.AppendLine(CultureInfo.InvariantCulture, $"""<p class="desc">{Html(text)}</p>""");
    }

    private static void AppendSddPanel(StringBuilder html, SddPanelGold panel)
    {
        html.AppendLine("<h2>SDD practice — spec before code</h2>");
        AppendDescription(html, FormattableString.Invariant(
            $"Замер SDD-практики за {DashboardComputer.ThemeWindowDays} дней (ADR-0040). «Спек прежде кода»: доля сессий, где работа с спеками/планами (docs/superpowers/, docs/cases/) началась строго раньше первой правки кода — среди сессий, вообще правивших код, по репозиториям × неделям. Флот: {panel.OrderingSummary.SpecFirstSessions} из {panel.OrderingSummary.CodeSessions} ({RatePercent(panel.OrderingSummary.Rate)})."));
        if (panel.Ordering.Count > 0)
        {
            html.AppendLine("""<table class="repos"><thead><tr><th>week</th><th>repository</th><th>code sessions</th><th>spec-first</th><th>rate</th></tr></thead><tbody>""");
            foreach (SddOrderingRow row in panel.Ordering)
            {
                html.AppendLine(CultureInfo.InvariantCulture,
                    $"<tr><td>{Html(row.Week)}</td><td class=\"path\">{Html(row.Repo)}</td><td>{row.CodeSessions}</td><td>{row.SpecFirstSessions}</td><td>{RatePercent(row.Rate)}</td></tr>");
            }
            html.AppendLine("</tbody></table>");
        }
        else
        {
            AppendDescription(html, "Ни одной сессии с правками кода за окно — упорядочение не определено.");
        }

        AppendDescription(html, FormattableString.Invariant(
            $"Баланс записей по типам содержимого (ADR-0040): knowledge — рукописные доки, code — исходники, config/other — прочее. Машинные записи (docs/ai/ — копии конституции, сгенерированный baseline) исключены и посчитаны отдельно: {panel.MachineManagedWrites} — это не документационная дисциплина."));
        if (panel.WritesByKind.Count > 0)
        {
            html.AppendLine("""<table class="repos"><thead><tr><th>kind</th><th>writes</th></tr></thead><tbody>""");
            foreach (SddWritesRow row in panel.WritesByKind)
            {
                html.AppendLine(CultureInfo.InvariantCulture,
                    $"<tr><td>{Html(row.Kind)}</td><td>{row.Writes}</td></tr>");
            }
            html.AppendLine("</tbody></table>");
        }
        else
        {
            AppendDescription(html, "За окно не было записей (knowledge.written) в практических сессиях.");
        }

        if (!panel.SkillConfigured)
        {
            AppendDescription(html, "Доля SDD-скиллов не настроена: добавьте блок sdd: { skills: [...] } в реестр (ADR-0040) — публичный инструмент не знает чужих имён навыков.");
            return;
        }
        AppendDescription(html, "Доля сессий, вызвавших хотя бы один SDD-скилл из настроенного набора (реестр, блок sdd) — по репозиториям.");
        if (panel.SkillRate.Count > 0)
        {
            html.AppendLine("""<table class="repos"><thead><tr><th>repository</th><th>sessions</th><th>with SDD skill</th><th>rate</th></tr></thead><tbody>""");
            foreach (SddSkillRateRow row in panel.SkillRate)
            {
                html.AppendLine(CultureInfo.InvariantCulture,
                    $"<tr><td class=\"path\">{Html(row.Repo)}</td><td>{row.Sessions}</td><td>{row.SddSessions}</td><td>{RatePercent(row.Rate)}</td></tr>");
            }
            html.AppendLine("</tbody></table>");
        }
        else
        {
            AppendDescription(html, "За окно не было сессий.");
        }
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
