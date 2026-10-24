using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ كُلُّ رابِطٍ حَرفِيٍّ يَبلُغُ GET قائِماً، وكُلُّ استِمارَةِ POST تَبلُغُ MapPost قائِماً (تَدقيقُ الإطلاق lens-dead-doors-2) ═══
//
// **الكَلفَة** (مَقيسَةٌ حَيّاً عَلى نُسخَةٍ مَرمِيَّة عِندَ `dace7cd`): زِرّا «رَفضٌ وخُروج» في شُروطِ المَتجَرِ (`Terms.razor`) وفي مُوافَقَةِ
// الاستوديو (`StudioConsent.razor`) رابِطا `GET` إلى مَسارَي خُروجٍ لا يَقبَلانِ إلّا `POST` — `GET /order/logout` ⇒ ‏404،
// `GET /studio/logout` ⇒ ‏405، والمُستَخدِمُ يَبقى مُسَجَّلاً واستِمارَةُ القَبولِ مَرسومَة. والبابُ لا يُفضي إلى شَيءٍ (القاعِدَة ١٢)،
// ولا فاحِصَ يَعرِفُ أَنّ المَسارَ الَّذي يُسَمّيه الرابِطُ لا يَقبَلُ فِعلَه.
//
// **الفاحِص**: كُلُّ `href` وكُلُّ `action`/`formaction` في كُلِّ `.razor` في `libs/` و`apps/` قيمَتُه مَسارٌ حَرفِيٌّ (‏`/studio/logout`)،
// أَو `AuthSession.LinkFor(Slug, Role, "…")` بِمَسارٍ حَرفِيّ، أَو `$"/{Slug}/…"` لا يَحمِلُ غَيرَ المَتجَر — يُطابَقُ بِجَدوَلِ المَساراتِ
// المُعلَنَة: `@page` و`MapGet` و`[WolverineGet]` لِلرابِط، و`MapPost` و`[WolverinePost]` لِلاستِمارَة. والمَقطَعُ المُتَغَيِّرُ في القالِبِ
// (`{id:guid}`) يُطابِقُ أَيَّ مَقطَع، والمَتجَرُ في الرابِطِ لا يُطابِقُ إلّا مَقطَعاً مُتَغَيِّراً. و`LinkFor` بِـ`Role` يُطابَقُ بِشَكلَيه — بِدَورٍ
// وبِلا دَور — لِأَنّه يُعطي أَيَّهُما بِحَسَبِ الصَفحَة. **وحَدُّه يُقال**: ما يَحمِلُ مُتَغَيِّراً غَيرَ المَتجَرِ يُعَدُّ ولا يُفحَص (يُطبَعُ
// عَدَدُه)، والأُصولُ الساكِنَةُ (مِلَفٌّ بِامتِدادٍ، `_content`، `_framework`) لا تُفحَص.
//
// **يُثَبَّتُ قَبلَ العِلاجِ** (القاعِدَة ٣، ‏2026-10-24 عِندَ `38d1da9`): أَوَّلُ تَشغيلٍ عَلى المُستَودَعِ كُلِّه — ‏311 رابِطاً و‏114 استِمارَة —
// لا يَجِدُ مَيِّتاً إلّا البابَينِ المَوصوفَين (بِشَكلَي `LinkFor` في الشُروط). فَالصَفُّ الأَوَّلُ يُسَمّيهِما ويَنقَلِبُ بِاسمِه في كوميتِ
// العِلاج، والثاني (عَيِّنَةٌ تَحمَرُّ عَلى ما كُتِبَ لَه) يَبقى بِلا تَعديلِ حَرف.
public class RouteResolutionTests(ITestOutputHelper output)
{
    private static string Root => ThemeZeroEquivalenceTests.RepoRoot;

    private static IEnumerable<string> Under(string pattern) => new[] { "libs", "apps" }
        .SelectMany(d => Directory.EnumerateFiles(Path.Combine(Root, d), pattern, SearchOption.AllDirectories))
        .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                 && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
        .OrderBy(f => f, StringComparer.Ordinal);

    private static string Rel(string f) => Path.GetRelativePath(Root, f).Replace('\\', '/');

    private static string Clean(string text) =>
        WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(text));

    /// <summary>جَدوَلُ المَساراتِ المُعلَنَةِ لِفِعل: <c>GET</c> = <c>@page</c> و<c>MapGet</c> و<c>[WolverineGet]</c>؛
    /// <c>POST</c> = <c>MapPost</c> و<c>[WolverinePost]</c>.</summary>
    internal static IReadOnlyList<string> Declared(string verb)
    {
        var routes = new List<string>();
        foreach (var f in Under("*.cs"))
        {
            var code = WriteEndpointGuardTests.StripComments(File.ReadAllText(f));
            var map = verb == "GET" ? "Get" : "Post";
            routes.AddRange(Regex.Matches(code, $@"\.Map{map}\(\s*""(?<t>[^""]+)""").Select(m => m.Groups["t"].Value));
            routes.AddRange(Regex.Matches(code, $@"\[Wolverine{map}\(\s*""(?<t>[^""]+)""").Select(m => m.Groups["t"].Value));
        }
        if (verb == "GET")
            foreach (var f in Under("*.razor"))
                routes.AddRange(Regex.Matches(Clean(File.ReadAllText(f)), @"^@page\s+""(?<t>[^""]+)""", RegexOptions.Multiline)
                    .Select(m => m.Groups["t"].Value));
        return routes;
    }

    private static string[] Segments(string path) => path.Split('?', '#')[0].Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
    private static bool IsParam(string segment) => segment.StartsWith('{') && segment.EndsWith('}');

    /// <summary>هَل يَحُلُّ المَسارُ قالِباً مُعلَناً؟ المُتَغَيِّرُ في القالِبِ يُطابِقُ أَيَّ مَقطَع؛ والمُتَغَيِّرُ في الرابِطِ (المَتجَر) مُتَغَيِّراً
    /// وَحدَه؛ والحَرفِيّانِ بِلا حالَةِ أَحرُف.</summary>
    internal static bool Resolves(string path, IReadOnlyList<string> templates)
    {
        var link = Segments(path);
        foreach (var t in templates)
        {
            var tpl = Segments(t);
            if (tpl.Length != link.Length) continue;
            var ok = true;
            for (var i = 0; i < tpl.Length && ok; i++)
                ok = IsParam(tpl[i]) || (!IsParam(link[i]) && string.Equals(tpl[i], link[i], StringComparison.OrdinalIgnoreCase));
            if (ok) return true;
        }
        return false;
    }

    // ─── قِراءَةُ قيمَةِ الصِفَة — حَرفِيٌّ، أَو تَعبيرُ Razor مُتَوازِنُ الأَقواس ─────────────────────────────

    /// <summary>نِهايَةُ <c>(…)</c> أَو <c>{…}</c> المُتَوازِنَة مِن <paramref name="open"/> — والنُصوصُ داخِلَها تُقفَزُ بِعَلاماتِها.</summary>
    private static int Close(string s, int open)
    {
        var (o, c) = (s[open], s[open] == '(' ? ')' : '}');
        var depth = 0;
        for (var i = open; i < s.Length; i++)
        {
            if (s[i] == '"')
            {
                for (i++; i < s.Length && s[i] != '"'; i++)
                    if (s[i] == '{') i = Close(s, i);
                continue;
            }
            if (s[i] == o) depth++;
            else if (s[i] == c && --depth == 0) return i;
        }
        return -1;
    }

    /// <summary>قيمَةُ الصِفَةِ ابتِداءً مِن <paramref name="start"/> (بَعدَ <c>="</c>) حَتّى <c>"</c> الخاتِمَة — والتَعبيرُ
    /// <c>@(…)</c> أَو <c>@a.b(…)</c> يُقرَأُ بِأَقواسِه فَلا تَقطَعُه عَلامَةُ نَصٍّ داخِلَه.</summary>
    internal static string? AttributeValue(string code, int start)
    {
        var i = start;
        var sb = new System.Text.StringBuilder();
        while (i < code.Length && code[i] != '"')
        {
            if (code[i] == '@')
            {
                var j = i + 1;
                if (j < code.Length && code[j] == '(')
                {
                    var end = Close(code, j);
                    if (end < 0) return null;
                    sb.Append(code, i, end - i + 1);
                    i = end + 1;
                    continue;
                }
                while (j < code.Length && (char.IsLetterOrDigit(code[j]) || code[j] is '_' or '.')) j++;
                if (j < code.Length && code[j] == '(')
                {
                    var end = Close(code, j);
                    if (end < 0) return null;
                    j = end + 1;
                }
                sb.Append(code, i, j - i);
                i = j;
                continue;
            }
            sb.Append(code[i++]);
        }
        return i < code.Length ? sb.ToString() : null;
    }

    /// <summary>نَصٌّ مُستَوفى (<c>$"…"</c>) أَو حَرفِيٌّ (<c>"…"</c>) كامِلاً — كُلُّ ثُقبٍ <c>{…}</c> يَصيرُ <c>{x}</c>.</summary>
    private static string? StringLiteral(string expr)
    {
        expr = expr.Trim();
        var interpolated = expr.StartsWith("$\"", StringComparison.Ordinal);
        if (!interpolated && !expr.StartsWith('"')) return null;
        var body = expr[(interpolated ? 2 : 1)..];
        if (!body.EndsWith('"')) return null;
        body = body[..^1];
        if (!interpolated) return body.Contains('"') ? null : body;
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] == '{')
            {
                var end = Close(body, i);
                if (end < 0) return null;
                sb.Append("{x}");
                i = end;
                continue;
            }
            if (body[i] == '"') return null;
            sb.Append(body[i]);
        }
        return sb.ToString();
    }

    private static readonly Regex LinkForCall = new(
        @"^AuthSession\.LinkFor\(\s*Slug\s*,\s*(?<role>\w+)\s*,\s*(?<p>.+)\)$", RegexOptions.Singleline);

    /// <summary>المَساراتُ الَّتي تُعطيها قيمَةُ صِفَة — أَو <c>null</c> إن كانَت تَعبيراً لا يُقرَأُ هُنا (مُتَغَيِّرٌ أَو دالَّة). كُلُّ ثُقبٍ
    /// (‏<c>{…}</c> في نَصٍّ مُستَوفى أَو <c>@x</c> داخِلَ حَرفِيّ) يَصيرُ <c>{x}</c>، والمَتجَرُ مِنها.</summary>
    internal static IReadOnlyList<string>? PathsOf(string value)
    {
        value = value.Trim();
        if (value.Length == 0 || value.StartsWith('#')) return Array.Empty<string>();
        if (!value.StartsWith('@'))
        {
            // حَرفِيٌّ قَد يَحمِلُ `@x` أَو `@(x)` ثُقباً: `/studio/apps/@Slug/deals`.
            if (!value.StartsWith('/') || value.StartsWith("//")) return Array.Empty<string>();
            return new[] { Regex.Replace(value, @"@\([^)]*\)|@[\w.]+", "{x}") };
        }
        var expr = value.StartsWith("@(") && value.EndsWith(')') ? value[2..^1].Trim() : value[1..];
        var literal = StringLiteral(expr);
        if (literal is not null) return literal.StartsWith('/') ? new[] { literal } : Array.Empty<string>();
        var lf = LinkForCall.Match(expr);
        if (lf.Success && StringLiteral(lf.Groups["p"].Value) is { } tail)
        {
            tail = tail.TrimStart('/');
            var plain = "/{x}/" + tail;
            return lf.Groups["role"].Value == "null" ? new[] { plain } : new[] { plain, "/{x}/r/{x}/" + tail };
        }
        return null;
    }

    private static bool IsAsset(string path)
    {
        var segs = Segments(path);
        return segs.Any(s => s is "_content" or "_framework") || (segs.Length > 0 && segs[^1].Contains('.') && !IsParam(segs[^1]));
    }

    internal sealed record Door(string File, string Verb, string Value, string Path);

    /// <summary>أَبوابُ مِلَفِّ razor: <c>href</c> ⇒ GET؛ <c>action</c> في استِمارَةِ <c>post</c> و<c>formaction</c> ⇒ POST. والتَعبيرُ الَّذي لا
    /// يُقرَأُ يُعَدُّ في <paramref name="skipped"/>.</summary>
    internal static List<Door> DoorsOf(string file, string razor, ref int skipped)
    {
        var code = Clean(razor);
        var doors = new List<Door>();
        var skip = 0;
        void Add(string verb, int at)
        {
            var value = AttributeValue(code, at);
            var paths = value is null ? null : PathsOf(value);
            if (paths is null) { skip++; return; }
            foreach (var p in paths.Where(p => !IsAsset(p))) doors.Add(new Door(file, verb, value!, p));
        }
        foreach (Match m in Regex.Matches(code, @"\shref="""))
            Add("GET", m.Index + m.Length);
        foreach (Match form in Regex.Matches(code, @"<form\b", RegexOptions.IgnoreCase))
        {
            // صِفاتُ الوَسمِ حَتّى أَوَّلِ `>` خارِجَ قيمَة — والقيمَةُ تُقفَزُ بِتَعبيرِها (`@(a => b)` لا يُنهي الوَسم).
            var attrs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var i = form.Index + form.Length;
            while (i < code.Length && code[i] != '>')
            {
                var a = Regex.Match(code[i..Math.Min(code.Length, i + 64)], @"^\s*(?<n>[\w-]+)=""");
                if (!a.Success) { i++; continue; }
                var valueAt = i + a.Length;
                attrs[a.Groups["n"].Value] = valueAt;
                var v = AttributeValue(code, valueAt);
                if (v is null) break;
                // نِهايَةُ القيمَةِ في النَصِّ نَفسِه: نُعيدُ المَسحَ حَتّى `"` الخاتِمَةِ الَّتي وَقَفَ عِندَها القارِئ.
                var j = valueAt;
                while (j < code.Length && code[j] != '"')
                {
                    if (code[j] == '@' && j + 1 < code.Length && code[j + 1] == '(') { j = Close(code, j + 1) + 1; continue; }
                    if (code[j] == '@')
                    {
                        j++;
                        while (j < code.Length && (char.IsLetterOrDigit(code[j]) || code[j] is '_' or '.')) j++;
                        if (j < code.Length && code[j] == '(') j = Close(code, j) + 1;
                        continue;
                    }
                    j++;
                }
                i = j + 1;
            }
            if (!attrs.TryGetValue("method", out var m) || !code[m..].StartsWith("post\"", StringComparison.OrdinalIgnoreCase)) continue;
            if (attrs.TryGetValue("action", out var at)) Add("POST", at);
        }
        foreach (Match m in Regex.Matches(code, @"\sformaction="""))
            Add("POST", m.Index + m.Length);
        skipped += skip;
        return doors;
    }

    /// <summary>أَبوابُ كُلِّ صَفحَةٍ مَفحوصَةً بِجَدوَلِ المَسارات — ما لا يَحُلُّ، وسَطرُ العَدّ.</summary>
    internal (List<string> Dead, string Counted) Scan()
    {
        var gets = Declared("GET");
        var posts = Declared("POST");
        var files = Under("*.razor").ToList();
        var skipped = 0;
        var doors = new List<Door>();
        foreach (var f in files) doors.AddRange(DoorsOf(Rel(f), File.ReadAllText(f), ref skipped));

        var dead = doors.Where(d => !Resolves(d.Path, d.Verb == "GET" ? gets : posts))
            .Select(d => $"{d.File}: {d.Verb} {d.Path}").Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();

        // القاعِدَة ١٠: يُطبَعُ ما فُحِص، والصِفرُ عَمى.
        var counted = $"فُحِصَ {files.Count} مِلَفَّ razor بِجَدوَلٍ مِن {gets.Count} مَسارَ GET و{posts.Count} مَسارَ POST: " +
                      $"{doors.Count(d => d.Verb == "GET")} رابِطاً و{doors.Count(d => d.Verb == "POST")} استِمارَة، " +
                      $"وتُرِكَ {skipped} تَعبيراً لا يُقرَأُ هُنا";
        output.WriteLine(counted);
        Assert.True(files.Count > 100 && gets.Count > 100 && posts.Count > 50
                    && doors.Count(d => d.Verb == "GET") > 200 && doors.Count(d => d.Verb == "POST") > 80,
            "أَداةٌ عَمياء — " + counted);
        return (dead, counted);
    }

    private const string Pages = "libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages/";

    // ─── ١. «اليَوم» — بابا «رَفضٌ وخُروج» وَحدَهُما لا يَحُلّان ───────────────────────────────────────
    //
    // يَنقَلِبُ بِاسمِه في كوميتِ العِلاج إلى «لا بابَ إلّا ويَحُلّ».

    [Fact]
    public void Today_the_two_reject_doors_are_get_links_to_post_only_logout_routes_and_nothing_else_is_dead()
    {
        var (dead, counted) = Scan();
        Assert.True(dead.SequenceEqual(new[]
        {
            Pages + "StudioConsent.razor: GET /studio/logout",
            Pages + "Terms.razor: GET /{x}/logout",
            Pages + "Terms.razor: GET /{x}/r/{x}/logout",
        }), counted + $"\nولا يَحُلُّ {dead.Count}:\n  " + string.Join("\n  ", dead));
    }

    // ─── ٢. والفاحِصُ يَحمَرُّ عَلى ما كُتِبَ لَه — لا يَتَغَيَّرُ بِالعِلاج ─────────────────────────────

    [Fact]
    public void The_route_scanner_reads_every_door_shape_and_goes_red_on_a_verb_the_route_does_not_take()
    {
        var gets = new[] { "/{slug}", "/{slug}/r/{role}/me", "/{slug}/me", "/studio" };
        var posts = new[] { "/{slug}/auth/logout", "/studio/logout", "/{slug}/listings/{id:guid}/offers" };
        const string razor = """
            <a href="/studio/logout">x</a>
            <a href="@AuthSession.LinkFor(Slug, Role, "logout")">x</a>
            <a href="@AuthSession.LinkFor(Slug, Role, "me")">x</a>
            <a href="@($"/{Slug}")">x</a>
            <a href="/studio">x</a>
            <a href="@someVariable">x</a>
            <a href="https://example.com/out">x</a>
            <link href="/_content/x/site.css" />
            <form method="post" action="@($"/{Slug}/auth/logout")"><button>x</button></form>
            <form method="post" action="@($"/{Slug}/listings/{listing.Id}/offers")"><button>x</button></form>
            <form method="post" action="/studio/nowhere"><button>x</button></form>
            <form method="get" action="/studio/nowhere"><button>x</button></form>
            <form method="post" action="/studio/logout"><button formaction="@($"/{Slug}/auth/logout")">x</button></form>
            """;
        var skipped = 0;
        var doors = DoorsOf("probe.razor", razor, ref skipped);
        var dead = doors.Where(d => !Resolves(d.Path, d.Verb == "GET" ? gets : posts)).Select(d => $"{d.Verb} {d.Path}").ToList();

        Assert.Equal(1, skipped);
        Assert.Equal(7, doors.Count(d => d.Verb == "GET"));
        Assert.Equal(5, doors.Count(d => d.Verb == "POST"));
        Assert.Equal(new[] { "GET /studio/logout", "GET /{x}/logout", "GET /{x}/r/{x}/logout", "POST /studio/nowhere" }, dead);
    }
}
