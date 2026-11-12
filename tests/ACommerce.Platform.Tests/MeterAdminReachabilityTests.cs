using System.Text.RegularExpressions;
using ACommerce.Platform.I18n;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ G11 — صَفحَتا العَدِّ تُبلَغانِ بِنَقرَةٍ ولا تُقرَآنِ إلّا لِمُشرِفِ المَنَصَّة ═════════════════════════════════════════════
// (‏2026-11-12، الموجَة ١٣ W13-6·٥ — ADR-051 §١٣؛ التَصميم §11 و§12 G11، القاعِدَتانِ ٦ و١٢). **شاشَةٌ مَبنِيَّةٌ لا يَصِلُ إلَيها المُستَخدِمُ غَيرُ مَوجودَة**:
// فَمَسارُ النَقرِ يُقاسُ بِطَرَفَيه — رابِطٌ في `AdminHome.razor` بِجِوارِ لَوحَةِ المُراقَبَة ← `/admin/meter` ← صَفُّ المَتجَرِ ← `/admin/meter/stores/{slug}`.
// **والحِراسَةُ في التَوقيعِ لا في الجِسم** (القاعِدَة ٦): الصَفحَتانِ مَلفوفَتانِ بِـ`<RequirePlatformAdmin>`، **والبَوّابَةُ قَبلَ القِراءَة** — طَلَبٌ مَجهولٌ لا يُقرَأُ
// لَه الدَفتَرُ أَصلاً (نَمَطُ `TenantEdit.razor`)، لا يُقرَأُ ثُمَّ يُحجَب. ولا جَلسَةَ Marten في الصَفحَة (الطَبَقَة ٨).
// والفاحِصُ دالَّةٌ نَقِيَّةٌ عَلى النُصوص (`Breaches`) فَيُقاسُ هُوَ نَفسُه بِنُسَخٍ مَحقونَة: كُلُّ عَيبٍ يُسَمّى بِرَمزِه (القاعِدَة ١٠).
// والحالاتُ الحَيَّةُ (الدَفتَرُ بِلا مُجَمِّع، والمَجموعُ مَعَ الذَيل، والمَجهولُ لا يُرسَم، و42P01، والعُطلُ الآخَرُ بِطاقَةٌ لا ‏500) في `LiveMeterAdminProofTests`.
public class MeterAdminReachabilityTests(ITestOutputHelper output)
{
    private const string TemplateRoot = "libs/templates/ACommerce.Templates.Customer.Marketplace";
    internal const string IndexRoute = "/admin/meter";
    internal const string StoreRoute = "/admin/meter/stores/{Slug}";

    private static string Read(string relative)
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {relative} — الأَداةُ عَمياءُ بِلا طَرَفٍ مَقروء.");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 300, $"أَداةٌ عَمياء: {relative} طولُه {text.Length} مِحرَفاً.");
        return text;
    }

    internal static string AdminHome() => Read($"{TemplateRoot}/Components/Pages/Admin/AdminHome.razor");
    internal static string IndexPage() => Read($"{TemplateRoot}/Components/Pages/Admin/MeterIndex.razor");
    internal static string StorePage() => Read($"{TemplateRoot}/Components/Pages/Admin/MeterStore.razor");

    /// <summary>رُموزُ الخَرقِ — مُعجَمٌ مُغلَق: <c>link_missing</c> (لا رابِطَ إلى الفِهرِسِ داخِلَ قِسمِ المُشرِفِ في <c>AdminHome</c> بَعدَ رابِطِ المُراقَبَة) ·
    /// <c>store_unlinked</c> (الفِهرِسُ لا يَربِطُ صَفحَةَ المَتجَر) · <c>route_missing</c> (لا <c>@page</c> بِالمَسارِ) · <c>page_unguarded</c> (المُحتَوى خارِجَ
    /// <c>&lt;RequirePlatformAdmin&gt;</c>) · <c>read_before_guard</c> (القِراءَةُ لا تَسبِقُها البَوّابَة) · <c>session_in_page</c> (جَلسَةُ Marten أَو مَخزَنُه في الصَفحَة).</summary>
    internal static readonly IReadOnlyList<string> Codes =
        ["link_missing", "store_unlinked", "route_missing", "page_unguarded", "read_before_guard", "session_in_page"];

    private static readonly string[] SessionTokens =
        ["IDocumentSession", "IQuerySession", "IDocumentStore", "LightweightSession", "QuerySession(", "SaveChangesAsync"];

    /// <summary>خُروقُ <c>AdminHome</c>: الرابِطُ داخِلَ <c>&lt;RequirePlatformAdmin&gt;</c> وبَعدَ رابِطِ <c>/admin/monitor</c> (بِجِوارِه — التَصميم §11).</summary>
    internal static IReadOnlyList<string> HomeBreaches(string adminHome)
    {
        var code = WriteEndpointGuardTests.StripMarkupComments(adminHome);
        var open = code.IndexOf("<RequirePlatformAdmin>", StringComparison.Ordinal);
        var close = code.IndexOf("</RequirePlatformAdmin>", StringComparison.Ordinal);
        var monitor = code.IndexOf("href=\"/admin/monitor\"", StringComparison.Ordinal);
        var meter = code.IndexOf($"href=\"{IndexRoute}\"", StringComparison.Ordinal);
        return open >= 0 && close > open && monitor > open && meter > monitor && meter < close ? [] : ["link_missing"];
    }

    /// <summary>
    /// <para>خُروقُ صَفحَة: <c>@page</c> بِمَسارِها، والمُحتَوى كُلُّه بَعدَ <c>&lt;RequirePlatformAdmin&gt;</c> وقَبلَ إغلاقِه، وفي <c>@code</c> سُؤالُ
    /// <c>PlatformGuard.EvaluateAsync()</c> قَبلَ أَوَّلِ <paramref name="read"/>، ولا رَمزَ جَلسَة. و<paramref name="storeLink"/> لِلفِهرِسِ وَحدَه.</para>
    /// </summary>
    internal static IReadOnlyList<string> PageBreaches(string page, string route, string read, string? storeLink = null)
    {
        var found = new List<string>();
        var code = WriteEndpointGuardTests.StripMarkupComments(page);
        if (!code.Contains($"@page \"{route}\"", StringComparison.Ordinal)) found.Add("route_missing");
        var open = code.IndexOf("<RequirePlatformAdmin>", StringComparison.Ordinal);
        var close = code.IndexOf("</RequirePlatformAdmin>", StringComparison.Ordinal);
        var body = code.IndexOf("@code", StringComparison.Ordinal);
        var firstMarkup = code.IndexOf("data-meter-state", StringComparison.Ordinal);
        var lastMarkup = code.LastIndexOf("data-meter-state", StringComparison.Ordinal);
        if (open < 0 || close < open || firstMarkup < open || lastMarkup > close || (body >= 0 && body < close)) found.Add("page_unguarded");
        var section = body >= 0 ? WriteEndpointGuardTests.StripComments(code[body..]) : "";
        var guard = section.IndexOf("PlatformGuard.EvaluateAsync()", StringComparison.Ordinal);
        var reading = section.IndexOf(read, StringComparison.Ordinal);
        if (guard < 0 || reading < 0 || reading < guard
            || !Regex.IsMatch(section[..Math.Max(reading, 0)], @"if\s*\(!\(await PlatformGuard\.EvaluateAsync\(\)\)\.Allowed\)\s*return;"))
            found.Add("read_before_guard");
        if (SessionTokens.Any(t => code.Contains(t, StringComparison.Ordinal))) found.Add("session_in_page");
        if (storeLink is not null && !code.Contains(storeLink, StringComparison.Ordinal)) found.Add("store_unlinked");
        return found;
    }

    internal static IReadOnlyList<string> Breaches(string adminHome, string index, string store) =>
        HomeBreaches(adminHome)
            .Concat(PageBreaches(index, IndexRoute, "Meter.TodayAsync", "href=\"/admin/meter/stores/@app\""))
            .Concat(PageBreaches(store, StoreRoute, "Meter.StoreAsync"))
            .Distinct().ToList();

    [Fact]
    public void The_meter_pages_are_one_click_from_AdminHome_behind_the_platform_admin_guard_and_ask_it_before_reading()
    {
        var home = AdminHome();
        var index = IndexPage();
        var store = StorePage();
        var links = Regex.Matches(WriteEndpointGuardTests.StripMarkupComments(home), Regex.Escape($"href=\"{IndexRoute}\"")).Count;
        var found = Breaches(home, index, store);
        output.WriteLine($"[G11] صَفَحاتٌ مَفحوصَة 2 · رَوابِطُ AdminHome إلى {IndexRoute}: {links} · خُروق {found.Count}"
                         + (found.Count == 0 ? "" : ": " + string.Join(", ", found)));
        Assert.Equal(1, links);
        Assert.Empty(found);
    }

    /// <summary><b>والفاحِصُ نَفسُه يَرى ما يَعُدُّ</b>: كُلُّ رَمزٍ يُنتِجُه عَيبٌ مَحقونٌ في نُسخَةٍ مِن المِلَفِّ الحَقيقِيّ — ورَمزٌ مُعلَنٌ بِلا حَقنٍ يُنتِجُه حارِسٌ أَعمى.</summary>
    [Fact]
    public void Each_breach_code_is_produced_by_an_injected_copy_of_the_real_files()
    {
        var home = AdminHome();
        var index = IndexPage();
        var store = StorePage();
        var injections = new (string Code, string Home, string Index, string Store)[]
        {
            ("link_missing", home.Replace($"href=\"{IndexRoute}\"", "href=\"/admin/elsewhere\"", StringComparison.Ordinal), index, store),
            ("store_unlinked", home, index.Replace("href=\"/admin/meter/stores/@app\"", "href=\"#\"", StringComparison.Ordinal), store),
            ("route_missing", home, index, store.Replace($"@page \"{StoreRoute}\"", "@page \"/admin/meter/store/{Slug}\"", StringComparison.Ordinal)),
            ("page_unguarded", home, index.Replace("<RequirePlatformAdmin>", "<div>", StringComparison.Ordinal)
                                          .Replace("</RequirePlatformAdmin>", "</div>", StringComparison.Ordinal), store),
            ("read_before_guard", home, index, Regex.Replace(store, @"if\s*\(!\(await PlatformGuard\.EvaluateAsync\(\)\)\.Allowed\)\s*return;", "")),
            ("session_in_page", home, index.Replace("@inject MeterQueries Meter", "@inject MeterQueries Meter\n@inject Marten.IDocumentStore Store", StringComparison.Ordinal), store),
        };
        var produced = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (codeName, h, i, s) in injections)
        {
            Assert.NotEqual(h + i + s, home + index + store);   // ‏الحَقنُ وَقَعَ فِعلاً — نَصٌّ لَم يَتَغَيَّر حَقنٌ أَعمى
            var got = Breaches(h, i, s);
            output.WriteLine($"[G11] حَقنُ {codeName} ⇐ {string.Join(", ", got)}");
            Assert.Contains(codeName, got);
            produced.Add(codeName);
        }
        output.WriteLine($"[G11] رُموزٌ مُعلَنَة {Codes.Count} · أَنتَجَها حَقنٌ {produced.Count}");
        Assert.Equal(Codes.OrderBy(c => c, StringComparer.Ordinal), produced);
    }

    /// <summary><b>كُلُّ نَصٍّ تَراه العَينُ مِفتاحٌ في القامُوس</b> (القاعِدَة ١١، عَرَبِيٌّ وَحدَه) — والمَفاتيحُ تُقرَأُ مِن الصَفحَتَينِ ورابِطِ <c>AdminHome</c>،
    /// ولا مِفتاحَ يُسَمّي سِعراً (القاعِدَة ١٦ — هذه المَوجَةُ عَدٌّ وَحدَه). ‏(‏2026-11-12، M6A-RULES-R3-1) <b>والقِيَمُ لا يَقرَؤُها هذا الصَفّ</b> — كانَ
    /// السَطرُ يَدَّعيها والفَحصُ عَلى أَسماءِ المَفاتيحِ بِمُعجَمٍ إنجليزِيٍّ وَحدَه؛ فَقِيَمُ مَفاتيحِ العَدِّ كُلِّها بِالمُعجَمَينِ في الصَفِّ التالي.</summary>
    [Fact]
    public void Every_visible_string_of_the_meter_pages_is_a_dictionary_key_and_none_names_a_price()
    {
        var keyRx = new Regex(@"\bL(?:\.Markup|\.Format)?(?:\(|\[)""(?<k>[a-z0-9_]+\.[a-z0-9_]+\.[a-z0-9_]+)""");
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var page in new[] { IndexPage(), StorePage() })
            foreach (Match m in keyRx.Matches(WriteEndpointGuardTests.StripMarkupComments(page))) keys.Add(m.Groups["k"].Value);
        keys.Add("admin.home.meter_cta");
        Assert.Contains("admin.home.meter_cta", WriteEndpointGuardTests.StripMarkupComments(AdminHome()), StringComparison.Ordinal);
        var missing = keys.Where(k => LocaleCatalog.Find(LocaleCatalog.Arabic, k) is null).ToList();
        var meterKeys = keys.Where(k => k.StartsWith("admin.meter.", StringComparison.Ordinal) || k == "admin.home.meter_cta" || k == "admin.nav.meter").ToList();
        var priced = meterKeys.Where(k => MeterUnitCatalogTests.PriceIsh.IsMatch(k)).ToList();
        output.WriteLine($"[G11] مَفاتيحُ الصَفحَتَينِ والرابِط {keys.Count} · مِنها لِلعَدّ {meterKeys.Count} · غائِبَةٌ عَن ar.json {missing.Count} · تُسَمّي سِعراً {priced.Count}");
        Assert.True(meterKeys.Count >= 10, $"أَداةٌ عَمياء: {meterKeys.Count} مِفتاحاً لِلعَدّ.");
        Assert.Empty(missing);
        Assert.Empty(priced);
    }

    /// <summary>مَفاتيحُ العَدِّ في القامُوس: <c>meter.*</c> و<c>admin.meter.*</c> و<c>admin.home.meter_cta</c> و<c>admin.nav.meter</c> — مِن مُعجَمِ العَرَبِيَّةِ
    /// كُلِّه لا مِمّا تَقرَؤُه الصَفحَتان (تَسمِيَةُ الوَحدَةِ تُبنى بِـ<c>MeterUnitCatalog.LabelKey</c> فَلا تَظهَرُ حَرفاً في صَفحَة).</summary>
    internal static bool IsMeterKey(string key) =>
        key.StartsWith("meter.", StringComparison.Ordinal) || key.StartsWith("admin.meter.", StringComparison.Ordinal)
        || key is "admin.home.meter_cta" or "admin.nav.meter";

    /// <summary>ما يُسَمّي سِعراً مِن مُدخَلاتِ القامُوس: <c>لُغَة:مِفتاح</c> — المِفتاحُ بِالمُعجَمِ الإنجليزِيّ، والقيمَةُ بِالمُعجَمَينِ بَعدَ نَزعِ التَشكيل.</summary>
    internal static IReadOnlyList<string> PricedEntries(IEnumerable<(string Lang, string Key, string Value)> entries) =>
        [.. entries.Where(e => MeterUnitCatalogTests.PriceIsh.IsMatch(e.Key) || MeterUnitCatalogTests.NamesAPrice(e.Value)).Select(e => $"{e.Lang}:{e.Key}")];

    /// <summary><b>ولا قيمَةَ تَراها العَينُ تُسَمّي سِعراً أَو مَبلَغاً أَو كُلفَةً أَو رَسماً أَو عُملَة</b> (القاعِدَة ١٦، والتَصميم §2) — في كُلِّ لُغَةٍ لَها مِلَفّ،
    /// بِالمُعجَمِ الإنجليزِيِّ والعَرَبِيّ. والفاحِصُ يُقاسُ أَوَّلاً: عُنوانُ الفِهرِسِ مَحقوناً بِـ«سِعرُ … بِالريال» يُمسَك.</summary>
    [Fact]
    public void No_key_or_value_of_the_meter_dictionary_names_a_price_in_either_lexicon()
    {
        var entries = LocaleCatalog.Languages
            .SelectMany(lang => LocaleCatalog.Raw(lang).Where(e => IsMeterKey(e.Key)).Select(e => (Lang: lang, e.Key, e.Value)))
            .ToList();
        var priced = PricedEntries(entries);
        output.WriteLine($"[G11] لُغاتٌ مَفحوصَة {LocaleCatalog.Languages.Count} · مُدخَلاتُ العَدّ "
                         + string.Join(" · ", LocaleCatalog.Languages.Select(l => $"{l} {entries.Count(e => e.Lang == l)}"))
                         + $" · تُسَمّي سِعراً {priced.Count}");
        Assert.True(entries.Count(e => e.Lang == LocaleCatalog.Arabic) >= 20, $"أَداةٌ عَمياء: {entries.Count} مُدخَلاً لِلعَدّ.");
        Assert.Contains(entries, e => e.Key == "admin.meter.title");

        var injected = entries.Select(e => e.Key == "admin.meter.title" ? e with { Value = "سِعرُ عَمَلِيّاتِ المَتاجِرِ اليَوم بِالريال" } : e).ToList();
        var caught = PricedEntries(injected);
        output.WriteLine($"[G11] حَقنُ admin.meter.title ⇐ {string.Join(", ", caught)}");
        Assert.Equal(["ar:admin.meter.title"], caught);

        Assert.True(priced.Count == 0, "قيمَةٌ أَو مِفتاحٌ يُسَمّي سِعراً في واجِهَةِ العَدّ: " + string.Join(", ", priced));
    }
}
