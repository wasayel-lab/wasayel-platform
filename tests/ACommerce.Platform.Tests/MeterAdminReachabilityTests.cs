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
// ‏(‏2026-11-13، W13-6·٢١ — التَصميم §11؛ يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **وصَفحَةٌ ثالِثَة**: `/admin/meter/owners/{id}` بِالشُروطِ نَفسِها (مَسار، وحارِس، والبَوّابَةُ
// قَبلَ القِراءَة، ولا جَلسَة)، ومَدخَلُها خانَةُ رائِدِ الأَعمالِ في صَفحَةِ المَتجَر — رَمزُ خَرقٍ سابِعٌ `owner_unlinked` بِحَقنِه. وحالاتُها الحَيَّةُ (مَتجَرٌ مُتَبَنّى
// تَحتَ مالِكِه الآن وسُطورُ ما قَبلَ التَبَنّي `_unowned`، والمَجهولُ لا يُرسَمُ ولا يُقرَأُ لَه) في `LiveMeterOwnerProofTests`.
// ‏(‏2026-11-13، مُراجَعَةُ المَرحَلَةِ هـ الأولى · W13-6·E-R1·١ — SEC-E-R1-1 وM6E-RULES-R1-1؛ يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **وكُلُّ قِراءَةٍ في الصَفحَةِ بَعدَ البَوّابَة،
// لا قِراءَتُها المُسَمّاةُ وَحدَها**: البَندُ ٢٠ أَضافَ إلى صَفحَةِ المَتجَرِ قِراءَةً ثانِيَة (`Reconcile.StoreAsync` — المُطابَقَةُ العابِرَةُ لِلمَتاجِرِ بِلَقطَةِ REPEATABLE READ)
// والفاحِصُ يَسأَلُ عَن `Meter.StoreAsync` وَحدَه: مَقيسٌ عِندَ 41f2087 — نَقلُ سَطرِ المُطابَقَةِ فَوقَ البَوّابَةِ يُبقي الصُفوفَ الأَربَعَةَ خَضراءَ و«خُروق 0». فَصارَ
// `read_before_guard` يَعُدُّ القُرّاءَ مِن `@inject` نَفسِها (كُلُّ اسمٍ مَحقونٍ إلّا ما لا يَقرَأُ حالَةً مَخزونَة: البَوّابَةُ والقامُوسُ والثَقافَة) ويُلزِمُ كُلَّ ذِكرٍ لَها
// في `@code` أَن يَقَعَ بَعدَ سَطرِ البَوّابَةِ **وفي دالَّتِها** — ودالَّةُ دَورَةِ حَياةٍ أُخرى تَقرَأُ بِلا بَوّابَة. و`page_unguarded` يُلزِمُ سِماتِ `data-recon-` بِالحارِسِ
// كَـ`data-meter-state`. والحالَةُ الحَيَّةُ (المَجهولُ وغَيرُ المُشرِفِ لا تُقرَأُ لَهُما المُطابَقَة، والمُشرِفُ عَلى المُضيفِ نَفسِه تُقرَأُ لَه مَرَّة) في `LiveMeterAdminProofTests` (٣).
// ‏(‏2026-11-14، مُراجَعَةُ الاكتِمال · W13-6·Z-R1·١ — M6Z-1؛ يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **والفِهرِسُ يَبلُغُ كُلَّ مَتجَرٍ ورائِدَ أَعمالِه بِنَقرَة**: كانَ قائِمَةَ تَشغيلٍ
// ورائِدُ الأَعمالِ فيه نَصّ، فَالمَتجَرُ الخامِلُ (حُضورٌ وَحدَه) وصَفحَةُ مالِكِه لا يُبلَغانِ إلّا بِكِتابَةِ العُنوان — والفاحِصُ أَخضَرُ لِأَنّه يَسأَلُ عَن رابِطِ المَتجَرِ
// وَحدَه. فَرَمزانِ: `index_owner_unlinked` (خانَةُ رائِدِ الأَعمالِ في قِسمَيِ الفِهرِسِ لا تَربِطُ صَفحَتَه خَلفَ `Guid.TryParse`) و`presence_unlinked` (لا قِسمَ
// تِقنِيَةٍ يَقرَأُ `reading.PresenceApps()` ويَربِطُ صَفحَةَ المَتجَر)، و`data-meter-presence` سِمَةٌ مَحروسَةٌ ثالِثَة. والحَيُّ في `LiveMeterAdminProofTests` G11·F و(٧).
public class MeterAdminReachabilityTests(ITestOutputHelper output)
{
    private const string TemplateRoot = "libs/templates/ACommerce.Templates.Customer.Marketplace";
    internal const string IndexRoute = "/admin/meter";
    internal const string StoreRoute = "/admin/meter/stores/{Slug}";
    internal const string OwnerRoute = "/admin/meter/owners/{Id:guid}";

    /// <summary>‏(W13-6·٢١) رابِطُ خانَةِ رائِدِ الأَعمالِ في صَفحَةِ المَتجَر إلى صَفحَتِه.</summary>
    internal const string OwnerLink = "href=\"/admin/meter/owners/@value\"";

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
    internal static string OwnerPage() => Read($"{TemplateRoot}/Components/Pages/Admin/MeterOwner.razor");

    /// <summary>رُموزُ الخَرقِ — مُعجَمٌ مُغلَق: <c>link_missing</c> (لا رابِطَ إلى الفِهرِسِ داخِلَ قِسمِ المُشرِفِ في <c>AdminHome</c> بَعدَ رابِطِ المُراقَبَة) ·
    /// <c>store_unlinked</c> (الفِهرِسُ لا يَربِطُ صَفحَةَ المَتجَر) · <c>route_missing</c> (لا <c>@page</c> بِالمَسارِ) · <c>page_unguarded</c> (المُحتَوى خارِجَ
    /// <c>&lt;RequirePlatformAdmin&gt;</c>) · <c>read_before_guard</c> (القِراءَةُ لا تَسبِقُها البَوّابَة) · <c>session_in_page</c> (جَلسَةُ Marten أَو مَخزَنُه في الصَفحَة) ·
    /// ‏(W13-6·٢١) <c>owner_unlinked</c> (خانَةُ رائِدِ الأَعمالِ في صَفحَةِ المَتجَرِ لا تَربِطُ صَفحَتَه) ·
    /// ‏(W13-6·Z-R1·١) <c>index_owner_unlinked</c> (خانَةُ رائِدِ الأَعمالِ في أَحَدِ قِسمَيِ الفِهرِسِ لا تَربِطُ صَفحَتَه) · <c>presence_unlinked</c> (لا قِسمَ
    /// تِقنِيَةٍ في الفِهرِسِ يَربِطُ صَفحَةَ كُلِّ مَتجَرٍ حاضِر).</summary>
    internal static readonly IReadOnlyList<string> Codes =
        ["link_missing", "store_unlinked", "route_missing", "page_unguarded", "read_before_guard", "session_in_page", "owner_unlinked",
         "index_owner_unlinked", "presence_unlinked"];

    private static readonly string[] SessionTokens =
        ["IDocumentSession", "IQuerySession", "IDocumentStore", "LightweightSession", "QuerySession(", "SaveChangesAsync"];

    /// <summary>سَطرُ البَوّابَةِ في <c>@code</c> — نَمَطُ <c>TenantEdit.razor</c>.</summary>
    internal const string GateLine = @"if\s*\(!\(await PlatformGuard\.EvaluateAsync\(\)\)\.Allowed\)\s*return;";

    /// <summary>‏(W13-6·E-R1·١) <b>حُقَنُ الصَفحَةِ الَّتي لا تَقرَأُ حالَةً مَخزونَة</b> — مُعجَمٌ مُغلَق: البَوّابَةُ نَفسُها، والقامُوس، وثَقافَةُ الطَلَب.
    /// وكُلُّ اسمٍ تُعلِنُه <c>@inject</c> سِواها قارِئٌ (القاعِدَة ٧: المِحوَرُ «أَيَقرَأُ حالَةً مَخزونَة؟»).</summary>
    internal static readonly IReadOnlyList<string> NonReading = ["PlatformGuard", "L", "Culture"];

    /// <summary>‏(W13-6·E-R1·١) القُرّاءُ المَحقونونَ في الصَفحَة: أَسماءُ <c>@inject</c> ناقِصَ <see cref="NonReading"/>.</summary>
    internal static IReadOnlyList<string> Readers(string page) =>
        [.. Regex.Matches(WriteEndpointGuardTests.StripMarkupComments(page), @"^@inject[ \t]+\S+[ \t]+(?<n>[A-Za-z_]\w*)[ \t]*\r?$", RegexOptions.Multiline)
            .Select(m => m.Groups["n"].Value).Where(n => !NonReading.Contains(n)).Distinct()];

    /// <summary>‏(W13-6·E-R1·١) قِسمُ <c>@code</c> بِلا تَعليقات — بِمَواضِعِه (المُزيلُ يُبَيِّضُ ولا يَحذِف).</summary>
    private static string CodeSection(string page)
    {
        var code = WriteEndpointGuardTests.StripMarkupComments(page);
        var body = code.IndexOf("@code", StringComparison.Ordinal);
        return body >= 0 ? WriteEndpointGuardTests.StripComments(code[body..]) : "";
    }

    /// <summary>‏(W13-6·E-R1·١) <b>كُلُّ ذِكرٍ لِقارِئٍ مَحقونٍ في <c>@code</c></b> بِمَوضِعِه — نِداءً (<c>Reconcile.StoreAsync</c>) أَو اسماً مُجَرَّداً (تَمريرُه أَو
    /// تَسمِيَتُه بِغَيرِ اسمِه قِراءَةٌ مُؤَجَّلَة).</summary>
    internal static IReadOnlyList<(string Call, int At)> Reads(string page)
    {
        var readers = Readers(page);
        if (readers.Count == 0) return [];
        return [.. Regex.Matches(CodeSection(page), $@"\b(?<n>{string.Join("|", readers.Select(Regex.Escape))})\b(?:\s*\.\s*(?<m>[A-Za-z_]\w*))?")
            .Select(m => (m.Groups["m"].Success ? $"{m.Groups["n"].Value}.{m.Groups["m"].Value}" : m.Groups["n"].Value, m.Index))];
    }

    /// <summary>‏(W13-6·E-R1·١) حُدودُ الدالَّةِ الَّتي فيها سَطرُ البَوّابَة: ما بَعدَ السَطرِ حَتّى قَوسِ إغلاقِ دالَّتِه — أَو <c>null</c> إن غابَ السَطر.</summary>
    private static (int From, int To)? Gated(string section)
    {
        var gate = Regex.Match(section, GateLine);
        if (!gate.Success) return null;
        int open = -1, depth = 0;
        for (var i = gate.Index - 1; i >= 0 && open < 0; i--)
            if (section[i] == '}') depth++;
            else if (section[i] == '{') { if (depth == 0) open = i; else depth--; }
        if (open < 0) return null;
        depth = 0;
        for (var i = open + 1; i < section.Length; i++)
            if (section[i] == '{') depth++;
            else if (section[i] == '}') { if (depth == 0) return (gate.Index + gate.Length, i); depth--; }
        return null;
    }

    /// <summary>‏(W13-6·E-R1·١) سِماتُ الصَفحَةِ الَّتي لا تُرسَمُ إلّا خَلفَ الحارِس: الحالَةُ (لازِمَة) وقِسمُ المُطابَقَة (حَيثُ وُجِد).</summary>
    internal static readonly IReadOnlyList<string> GuardedMarkers = ["data-meter-state", "data-recon-", "data-meter-presence"];

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
    /// <c>PlatformGuard.EvaluateAsync()</c> قَبلَ أَوَّلِ <paramref name="reads"/>، ولا رَمزَ جَلسَة. و<paramref name="storeLink"/> لِلفِهرِسِ وَحدَه.</para>
    /// <para>‏(‏2026-11-13، W13-6·E-R1·١) <b>وكُلُّ قِراءَة، لا المُسَمّاةُ وَحدَها</b>: <paramref name="reads"/> ما يَجِبُ أَن يوجَدَ (فَالفاحِصُ لا يَعمى إن ضاعَت
    /// القِراءَة)، و<see cref="Reads"/> كُلُّ ذِكرٍ لِقارِئٍ مَحقونٍ — وكُلُّها بَعدَ سَطرِ البَوّابَةِ في دالَّتِه (<see cref="Gated"/>). و<see cref="GuardedMarkers"/>
    /// كُلُّها بَينَ طَرَفَيِ الحارِس.</para>
    /// </summary>
    internal static IReadOnlyList<string> PageBreaches(string page, string route, IReadOnlyList<string> reads, string? storeLink = null)
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
        else if (GuardedMarkers.Any(m => code.IndexOf(m, StringComparison.Ordinal) is var first and >= 0
                                         && (first < open || code.LastIndexOf(m, StringComparison.Ordinal) > close)))
            found.Add("page_unguarded");
        var calls = Reads(page);
        var gated = Gated(CodeSection(page));
        if (gated is not { } g || reads.Count == 0 || reads.Any(r => !calls.Any(c => c.Call == r))
            || calls.Any(c => c.At < g.From || c.At > g.To))
            found.Add("read_before_guard");
        if (SessionTokens.Any(t => code.Contains(t, StringComparison.Ordinal))) found.Add("session_in_page");
        if (storeLink is not null && !code.Contains(storeLink, StringComparison.Ordinal)) found.Add("store_unlinked");
        return found;
    }

    /// <summary>
    /// <para>‏(W13-6·٢١) <b>مَدخَلُ صَفحَةِ رائِدِ الأَعمال</b>: في صَفحَةِ المَتجَرِ، بَينَ حَلقَةِ رائِدِ الأَعمالِ (<c>reading.By(r =&gt; r.Entrepreneur, Slug)</c>) وعُنوانِ
    /// الدَور، رابِطٌ إلى <see cref="OwnerLink"/> — خَلفَ <c>Guid.TryParse</c> فَالحارِسُ لا يُربَط. وغِيابُ أَيٍّ مِنها <c>owner_unlinked</c>.</para>
    /// </summary>
    internal static IReadOnlyList<string> OwnerLinkBreaches(string store)
    {
        var code = WriteEndpointGuardTests.StripMarkupComments(store);
        var loop = code.IndexOf("reading.By(r => r.Entrepreneur, Slug)", StringComparison.Ordinal);
        var end = loop < 0 ? -1 : code.IndexOf("admin.meter.by_role", loop, StringComparison.Ordinal);
        var cell = loop >= 0 && end > loop ? code[loop..end] : "";
        var guard = cell.IndexOf("Guid.TryParse(value", StringComparison.Ordinal);
        var link = cell.IndexOf(OwnerLink, StringComparison.Ordinal);
        return guard >= 0 && link > guard ? [] : ["owner_unlinked"];
    }

    /// <summary>‏(W13-6·Z-R1·١) رابِطُ صَفحَةِ المَتجَرِ في صَفِّ الفِهرِس — في قِسمِ التَشغيلِ وفي قِسمِ التِقنِيَة.</summary>
    internal const string IndexStoreLink = "href=\"/admin/meter/stores/@app\"";

    /// <summary>‏(W13-6·Z-R1·١) قارِئا الفِهرِسِ اللَّذانِ تَسبِقانِ خانَةَ رائِدِ الأَعمالِ في قِسمَيه — ومَطلَعُ قِسمِ التِقنِيَة.</summary>
    internal const string OpsOwners = "reading.By(r => r.Entrepreneur, app)";
    internal const string PresenceOwners = "reading.PresenceEntrepreneurs(app)";
    internal const string PresenceList = "reading.PresenceApps()";

    /// <summary>
    /// <para>‏(‏2026-11-14، W13-6·Z-R1·١ — M6Z-1) <b>الفِهرِسُ يَبلُغُ كُلَّ مَتجَرٍ ورائِدَ أَعمالِه</b>: (أ) <c>presence_unlinked</c> — بَعدَ <see cref="PresenceList"/>
    /// صَفٌّ بِـ<c>data-meter-presence-app</c> ورابِطُ صَفحَةِ المَتجَر (<see cref="IndexStoreLink"/>)، قَبلَ <c>@code</c>؛ (ب) <c>index_owner_unlinked</c> — في كُلِّ قِسمٍ
    /// خانَةُ رائِدِ الأَعمالِ (مِن <see cref="OpsOwners"/> حَتّى سِمَةِ <c>data-meter-unresolved</c>، ومِن <see cref="PresenceOwners"/> حَتّى <c>reading.Presence(app)</c>)
    /// فيها <c>Guid.TryParse(value</c> ثُمَّ <see cref="OwnerLink"/> — فَالحارِسُ لا يُربَط.</para>
    /// </summary>
    internal static IReadOnlyList<string> IndexLinkBreaches(string index)
    {
        var found = new List<string>();
        var code = WriteEndpointGuardTests.StripMarkupComments(index);
        var body = code.IndexOf("@code", StringComparison.Ordinal);
        var markup = body < 0 ? code : code[..body];
        var list = markup.IndexOf(PresenceList, StringComparison.Ordinal);
        var tech = list < 0 ? "" : markup[list..];
        if (list < 0 || !tech.Contains("data-meter-presence-app=\"@app\"", StringComparison.Ordinal) || !tech.Contains(IndexStoreLink, StringComparison.Ordinal))
            found.Add("presence_unlinked");
        static bool Linked(string text, string from, string to)
        {
            var a = text.IndexOf(from, StringComparison.Ordinal);
            var b = a < 0 ? -1 : text.IndexOf(to, a, StringComparison.Ordinal);
            var cell = a >= 0 && b > a ? text[a..b] : "";
            var guard = cell.IndexOf("Guid.TryParse(value", StringComparison.Ordinal);
            return guard >= 0 && cell.IndexOf(OwnerLink, guard, StringComparison.Ordinal) > guard;
        }
        if (!Linked(markup, OpsOwners, "data-meter-unresolved") || !Linked(markup, PresenceOwners, "reading.Presence(app)"))
            found.Add("index_owner_unlinked");
        return found;
    }

    /// <summary>‏(W13-6·E-R1·١) قِراءاتُ كُلِّ صَفحَةٍ بِأَسمائِها — وصَفحَةُ المَتجَرِ اثنَتان مُنذُ البَند ٢٠: العَدُّ والمُطابَقَة.</summary>
    internal static readonly IReadOnlyList<string> IndexReads = ["Meter.TodayAsync"];
    internal static readonly IReadOnlyList<string> StoreReads = ["Meter.StoreAsync", "Reconcile.StoreAsync"];
    internal static readonly IReadOnlyList<string> OwnerReads = ["Meter.OwnerAsync"];

    internal static IReadOnlyList<string> Breaches(string adminHome, string index, string store, string owner) =>
        HomeBreaches(adminHome)
            .Concat(PageBreaches(index, IndexRoute, IndexReads, "href=\"/admin/meter/stores/@app\""))
            .Concat(PageBreaches(store, StoreRoute, StoreReads))
            .Concat(PageBreaches(owner, OwnerRoute, OwnerReads))
            .Concat(OwnerLinkBreaches(store))
            .Concat(IndexLinkBreaches(index))
            .Distinct().ToList();

    [Fact]
    public void The_meter_pages_are_one_click_from_AdminHome_behind_the_platform_admin_guard_and_ask_it_before_reading()
    {
        var home = AdminHome();
        var index = IndexPage();
        var store = StorePage();
        var owner = OwnerPage();
        var links = Regex.Matches(WriteEndpointGuardTests.StripMarkupComments(home), Regex.Escape($"href=\"{IndexRoute}\"")).Count;
        var ownerLinks = Regex.Matches(WriteEndpointGuardTests.StripMarkupComments(store), Regex.Escape(OwnerLink)).Count;
        var indexStoreLinks = Regex.Matches(WriteEndpointGuardTests.StripMarkupComments(index), Regex.Escape(IndexStoreLink)).Count;
        var indexOwnerLinks = Regex.Matches(WriteEndpointGuardTests.StripMarkupComments(index), Regex.Escape(OwnerLink)).Count;
        var found = Breaches(home, index, store, owner);
        output.WriteLine($"[G11] صَفَحاتٌ مَفحوصَة 3 · رَوابِطُ AdminHome إلى {IndexRoute}: {links} · رَوابِطُ صَفحَةِ المَتجَرِ إلى صَفحَةِ رائِدِ الأَعمال: {ownerLinks} · خُروق {found.Count}"
                         + (found.Count == 0 ? "" : ": " + string.Join(", ", found)));
        // ‏(W13-6·Z-R1·١) الفِهرِسُ بِقِسمَيه: رابِطُ صَفحَةِ المَتجَرِ ورابِطُ صَفحَةِ رائِدِ الأَعمالِ في كُلٍّ.
        output.WriteLine($"[G11] الفِهرِس: رَوابِطُ صَفحَةِ المَتجَر {indexStoreLinks} · رَوابِطُ صَفحَةِ رائِدِ الأَعمال {indexOwnerLinks} (قِسمُ التَشغيلِ وقِسمُ التِقنِيَة)");
        Assert.Equal(2, indexStoreLinks);
        Assert.Equal(2, indexOwnerLinks);
        // ‏(W13-6·E-R1·١) ما قَرَأَه الفاحِصُ لِكُلِّ صَفحَة: القُرّاءُ المَحقونونَ ونِداءاتُهم — وكُلُّها بَعدَ البَوّابَة؛ وسِماتُ المُطابَقَةِ داخِلَ الحارِس.
        var pages = new (string Name, string Page, IReadOnlyList<string> Named)[] { ("index", index, IndexReads), ("store", store, StoreReads), ("owner", owner, OwnerReads) };
        foreach (var (name, page, named) in pages)
            output.WriteLine($"[G11] {name}: قُرّاءٌ مَحقونون [{string.Join(", ", Readers(page))}] · قِراءاتٌ مَفحوصَة {Reads(page).Count} [{string.Join(", ", Reads(page).Select(r => r.Call))}] · "
                             + $"سِماتُ data-recon- {Regex.Matches(WriteEndpointGuardTests.StripMarkupComments(page), "data-recon-").Count}");
        Assert.Equal(1, links);
        Assert.Equal(1, ownerLinks);
        Assert.Empty(found);
        Assert.All(pages, p => Assert.Equal(p.Named.Order(StringComparer.Ordinal), Reads(p.Page).Select(r => r.Call).Distinct().Order(StringComparer.Ordinal)));
        Assert.True(Regex.Matches(WriteEndpointGuardTests.StripMarkupComments(store), "data-recon-").Count > 0, "أَداةٌ عَمياء: صَفحَةُ المَتجَرِ بِلا قِسمِ مُطابَقَة.");
    }

    /// <summary><b>والفاحِصُ نَفسُه يَرى ما يَعُدُّ</b>: كُلُّ رَمزٍ يُنتِجُه عَيبٌ مَحقونٌ في نُسخَةٍ مِن المِلَفِّ الحَقيقِيّ — ورَمزٌ مُعلَنٌ بِلا حَقنٍ يُنتِجُه حارِسٌ أَعمى.</summary>
    [Fact]
    public void Each_breach_code_is_produced_by_an_injected_copy_of_the_real_files()
    {
        var home = AdminHome();
        var index = IndexPage();
        var store = StorePage();
        var owner = OwnerPage();
        var guardLine = @"if\s*\(!\(await PlatformGuard\.EvaluateAsync\(\)\)\.Allowed\)\s*return;";

        // ‏(‏2026-11-13، W13-6·E-R1·١ — SEC-E-R1-1 وM6E-RULES-R1-1) حُقونُ القِراءَةِ الثانِيَةِ في صَفحَةِ المَتجَرِ وسِماتِ قِسمِها: كُلُّ حَقنٍ يَجِدُ نَصَّه
        // مَرَّةً واحِدَةً أَو يَحمَرّ (حَقنٌ لا يَقَعُ يُنتِجُ «أَخضَرَ» أَعمى).
        string Cut(string page, string pattern, out string line)
        {
            var hits = Regex.Matches(page, pattern);
            Assert.True(hits.Count == 1, $"حَقنٌ أَعمى: «{pattern}» ×{hits.Count}");
            line = hits[0].Value;
            return page.Remove(hits[0].Index, hits[0].Length);
        }
        string AboveGate(string page, string line)
        {
            var gate = Regex.Matches(page, @"[ \t]*" + GateLine);
            Assert.True(gate.Count == 1, $"حَقنٌ أَعمى: سَطرُ البَوّابَةِ ×{gate.Count}");
            return page.Insert(gate[0].Index, line);
        }
        var withoutRecon = Cut(store, @"[ \t]*recon = await Reconcile\.StoreAsync\(Slug\);\r?\n", out var reconLine);
        var reconAboveGate = AboveGate(withoutRecon, reconLine);                                     // ‏ما قاسَه المُراجِع: السَطرُ نَفسُه فَوقَ البَوّابَة
        var reconOutsideGated = withoutRecon.Insert(withoutRecon.LastIndexOf('}'),                    // ‏بَعدَ البَوّابَةِ نَصّاً وفي دالَّةِ دَورَةِ حَياةٍ بِلا بَوّابَة
            "\n    protected override async Task OnInitializedAsync()\n    {\n" + reconLine + "    }\n");
        Assert.Single(Regex.Matches(store, @"@inject MeterQueries Meter\r?\n"));
        var unnamedReader = AboveGate(Regex.Replace(store, @"(@inject MeterQueries Meter\r?\n)",                 // ‏قارِئٌ ثالِثٌ لا تُسَمّيه القائِمَة
                                          "$1@inject ACommerce.Templates.Customer.Marketplace.Services.Metering.MeterReconciliation Audit\n"),
                                      "        _ = await Audit.StoreAsync(Slug);\n");
        var reconMarkerOutside = store.Replace("<RequirePlatformAdmin>", "<div data-recon-state=\"error\"></div>\n<RequirePlatformAdmin>", StringComparison.Ordinal);

        var injections = new (string Code, string Home, string Index, string Store, string Owner)[]
        {
            ("link_missing", home.Replace($"href=\"{IndexRoute}\"", "href=\"/admin/elsewhere\"", StringComparison.Ordinal), index, store, owner),
            ("store_unlinked", home, index.Replace("href=\"/admin/meter/stores/@app\"", "href=\"#\"", StringComparison.Ordinal), store, owner),
            ("route_missing", home, index, store.Replace($"@page \"{StoreRoute}\"", "@page \"/admin/meter/store/{Slug}\"", StringComparison.Ordinal), owner),
            ("page_unguarded", home, index.Replace("<RequirePlatformAdmin>", "<div>", StringComparison.Ordinal)
                                          .Replace("</RequirePlatformAdmin>", "</div>", StringComparison.Ordinal), store, owner),
            ("read_before_guard", home, index, Regex.Replace(store, guardLine, ""), owner),
            ("session_in_page", home, index.Replace("@inject MeterQueries Meter", "@inject MeterQueries Meter\n@inject Marten.IDocumentStore Store", StringComparison.Ordinal), store, owner),
            // ‏(W13-6·٢١) صَفحَةُ رائِدِ الأَعمالِ بِحُقونِها، ومَدخَلُها مِن صَفحَةِ المَتجَر.
            ("owner_unlinked", home, index, store.Replace(OwnerLink, "href=\"#\"", StringComparison.Ordinal), owner),
            ("owner_unlinked", home, index, store.Replace("Guid.TryParse(value, out _)", "true", StringComparison.Ordinal), owner),
            ("route_missing", home, index, store, owner.Replace($"@page \"{OwnerRoute}\"", "@page \"/admin/meter/owner/{Id}\"", StringComparison.Ordinal)),
            ("page_unguarded", home, index, store, owner.Replace("<RequirePlatformAdmin>", "<div>", StringComparison.Ordinal)
                                                        .Replace("</RequirePlatformAdmin>", "</div>", StringComparison.Ordinal)),
            ("read_before_guard", home, index, store, Regex.Replace(owner, guardLine, "")),
            ("session_in_page", home, index, store, owner.Replace("@inject MeterQueries Meter", "@inject MeterQueries Meter\n@inject Marten.IQuerySession Session", StringComparison.Ordinal)),
            // ‏(W13-6·E-R1·١) كُلُّ قِراءَةٍ بَعدَ البَوّابَةِ وفي دالَّتِها — لا المُسَمّاةُ وَحدَها؛ وقِسمُ المُطابَقَةِ داخِلَ الحارِس.
            ("read_before_guard", home, index, reconAboveGate, owner),
            ("read_before_guard", home, index, reconOutsideGated, owner),
            ("read_before_guard", home, index, unnamedReader, owner),
            ("page_unguarded", home, index, reconMarkerOutside, owner),
            // ‏(W13-6·Z-R1·١) الفِهرِسُ بِقِسمَيه: كُلُّ رابِطٍ يُحقَنُ وَحدَه — الأَوَّلُ في قِسمِ التَشغيلِ والأَخيرُ في قِسمِ التِقنِيَة.
            ("index_owner_unlinked", home, ReplaceAt(index, OwnerLink, "href=\"#\"", last: false), store, owner),
            ("index_owner_unlinked", home, ReplaceAt(index, OwnerLink, "href=\"#\"", last: true), store, owner),
            ("index_owner_unlinked", home, index.Replace("Guid.TryParse(value, out _)", "true", StringComparison.Ordinal), store, owner),
            ("presence_unlinked", home, ReplaceAt(index, IndexStoreLink, "href=\"#\"", last: true), store, owner),
            ("presence_unlinked", home, index.Replace(PresenceList, "reading.Apps()", StringComparison.Ordinal), store, owner),
            ("page_unguarded", home, index.Replace("<RequirePlatformAdmin>", "<div data-meter-presence-apps=\"0\"></div>\n<RequirePlatformAdmin>", StringComparison.Ordinal), store, owner),
        };
        static string ReplaceAt(string text, string what, string with, bool last)
        {
            var at = last ? text.LastIndexOf(what, StringComparison.Ordinal) : text.IndexOf(what, StringComparison.Ordinal);
            Assert.True(at >= 0 && text.IndexOf(what, StringComparison.Ordinal) != text.LastIndexOf(what, StringComparison.Ordinal), $"حَقنٌ أَعمى: «{what}»");
            return text.Remove(at, what.Length).Insert(at, with);
        }
        var produced = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (codeName, h, i, s, o) in injections)
        {
            Assert.NotEqual(h + i + s + o, home + index + store + owner);   // ‏الحَقنُ وَقَعَ فِعلاً — نَصٌّ لَم يَتَغَيَّر حَقنٌ أَعمى
            var got = Breaches(h, i, s, o);
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
        foreach (var page in new[] { IndexPage(), StorePage(), OwnerPage() })
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
