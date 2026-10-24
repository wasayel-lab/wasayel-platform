using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ الوَقتُ المَعروضُ بِتَوقيتِ مَن؟ — تَوصيفُ «اليَوم» قَبلَ التَبديل (القاعِدَة ٣) ═══
//
// **المَقيسُ** (RELEASE-READINESS §١ البَند ٦ و§٧·١): `Notification.At = DateTime.UtcNow` و`Notifications.razor` يَرسُمُ
// `n.At.ToString("HH:mm")` بِلا تَحويل — ثَلاثُ ساعاتٍ خَلفَ الرياض؛ و`Chats.razor`/`ChatRoom.razor` يُحَوِّلانِ بِـ`ToLocalTime()`
// أَي بِمِنطَقَةِ **الخادِم** (UTC في الحاوِيَة). والمُستَودَعُ فيه الاصطِلاحُ بِلا مُستَهلِكِ عَرض: `ICultureContext.TimeZone`
// = ‏"Asia/Riyadh" و`NowInZone`، مُسَجَّلٌ في `Program.cs` (‏`AddCultureContext`/`UseCultureContext`) ولا تَحقِنُه صَفحَةٌ واحِدَة.
// **والقيمَةُ المُخَزَّنَةُ مَقيسَة**: ‏Marten يَكتُبُها بِـ`Z` (‏`mt_doc_notification`/`message`/`deal`: «2026-10-24T05:08:46.671891Z»)،
// فَتَعودُ `DateTimeKind.Utc` — والبُرهانُ الحَيُّ في `LiveDisplayTimeKindProofTests`.
public class DisplayTimeZoneTests(ITestOutputHelper output)
{
    private const string Pages = "libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages";

    /// <summary>لَحظَةٌ ثابِتَةٌ عِندَ حافَّةِ اليَوم: ‏21:30 بِـUTC هي ‏00:30 مِن اليَومِ التالي في الرياض — فَيَظهَرُ فَرقُ الساعَةِ والتاريخِ مَعاً.</summary>
    internal static readonly DateTime FixedUtc = new(2026, 10, 24, 21, 30, 0, DateTimeKind.Utc);

    private static string Read(string relative)
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {relative}");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 300, $"أَداةٌ عَمياء: {relative} طولُه {text.Length}.");
        return text;
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-24، S15 B)</b>: الإشعارُ يُرسَمُ بِساعَةِ مِنطَقَةِ الثَقافَة — اللَحظَةُ الثابِتَةُ
    /// ‏«00:30» لا «21:30».</summary>
    [Fact]
    public void Notifications_render_the_clock_in_the_culture_zone()
    {
        var razor = Read($"{Pages}/Notifications.razor");
        Assert.Contains("Time=\"@Culture.InZone(n.At).ToString(\"HH:mm\")\"", razor, StringComparison.Ordinal);
        Assert.Equal("00:30", ACommerce.Kit.Culture.CultureTime.InZone(FixedUtc).ToString("HH:mm"));
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه</b>: المُحادَثاتُ تُحَوِّلُ بِمِنطَقَةِ الثَقافَةِ و«اليَوم» يَومُها — لا <c>ToLocalTime</c> ولا <c>DateTime.Now</c>.</summary>
    [Fact]
    public void Chats_convert_with_the_culture_zone_not_the_server()
    {
        foreach (var f in new[] { "Chats.razor", "ChatRoom.razor" })
        {
            var razor = Read($"{Pages}/{f}");
            Assert.DoesNotContain("ToLocalTime()", razor, StringComparison.Ordinal);
            Assert.DoesNotContain("DateTime.Now", razor, StringComparison.Ordinal);
            Assert.Contains("Culture.InZone(", razor, StringComparison.Ordinal);
            Assert.Contains("Culture.NowInZone.Date", razor, StringComparison.Ordinal);
        }
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه</b>: المِنطَقَةُ الافتِراضِيَّةُ ثابِتٌ واحِدٌ في العُدَّة (الرياض)، وصَفَحاتُ الوَقتِ تَحقِنُ السِياق.</summary>
    [Fact]
    public void The_culture_zone_has_one_default_and_the_time_pages_read_it()
    {
        var ctx = new ACommerce.Kit.Culture.MutableCultureContext();
        Assert.Equal("Asia/Riyadh", ACommerce.Kit.Culture.CultureTime.DefaultTimeZone);
        Assert.Equal(ACommerce.Kit.Culture.CultureTime.DefaultTimeZone, ctx.TimeZone);
        var root = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates");
        var razors = Directory.GetFiles(root, "*.razor", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();
        Assert.True(razors.Count > 100, $"أَداةٌ عَمياء: {razors.Count} مِلَفّ razor فَقَط.");
        var readers = razors.Count(f => File.ReadAllText(f).Contains("@inject ACommerce.Kit.Culture.ICultureContext Culture", StringComparison.Ordinal));
        output.WriteLine($"مِلَفّاتُ razor: {razors.Count} · تَحقِنُ ICultureContext: {readers}");
        Assert.Equal(24, readers);
    }

    // ─── الدالَّةُ النَقِيَّة — DateTimeKind صَراحَةً، والمِنطَقَةُ المَجهولَة ─────────────────────────

    [Fact]
    public void A_utc_instant_shows_in_riyadh_across_the_day_edge()
        => Assert.Equal("2026-10-25 00:30", ACommerce.Kit.Culture.CultureTime.InZone(FixedUtc).ToString("yyyy-MM-dd HH:mm"));

    /// <summary>‏<c>Unspecified</c> (ما يُعيدُه Marten لِقيمَةٍ بِلا Z) يُقرَأُ UTC — لا مِنطَقَةَ الخادِم.</summary>
    [Fact]
    public void An_unspecified_instant_is_read_as_utc()
    {
        var unspecified = DateTime.SpecifyKind(FixedUtc, DateTimeKind.Unspecified);
        Assert.Equal(ACommerce.Kit.Culture.CultureTime.InZone(FixedUtc), ACommerce.Kit.Culture.CultureTime.InZone(unspecified));
    }

    /// <summary>‏<c>Local</c> يُرَدُّ إلى UTC أَوَّلاً — فَلا يُحسَبُ فَرقُ مِنطَقَةِ الخادِمِ مَرَّتَين.</summary>
    [Fact]
    public void A_local_instant_is_brought_back_to_utc_first()
    {
        var local = FixedUtc.ToLocalTime();
        Assert.Equal(DateTimeKind.Local, local.Kind);
        Assert.Equal(ACommerce.Kit.Culture.CultureTime.InZone(FixedUtc), ACommerce.Kit.Culture.CultureTime.InZone(local));
    }

    /// <summary>مِنطَقَةٌ لا تُعرَف (رَأسُ <c>X-Timezone</c> مَكتوبٌ خَطَأً) تَسقُطُ إلى الرياض — لا إلى UTC كَما كانَ <c>TryGetTz</c>.</summary>
    [Fact]
    public void An_unknown_zone_falls_back_to_the_default_not_to_utc()
    {
        Assert.Equal("00:30", ACommerce.Kit.Culture.CultureTime.InZone(FixedUtc, "Mars/Olympus_Mons").ToString("HH:mm"));
        Assert.Equal("Asia/Riyadh", ACommerce.Kit.Culture.CultureTime.Zone(null).Id);
    }

    /// <summary>والسِياقُ يُحَوِّلُ بِمِنطَقَةِ طَلَبِه — مِنطَقَةٌ أُخرى تُعطي ساعَتَها (السالِب: لَيسَت الرياضَ مَكتوبَةً في الدالَّة).</summary>
    [Fact]
    public void The_context_converts_with_its_request_zone()
    {
        var ctx = new ACommerce.Kit.Culture.MutableCultureContext { TimeZone = "UTC" };
        Assert.Equal("21:30", ctx.InZone(FixedUtc).ToString("HH:mm"));
        ctx.TimeZone = "Asia/Riyadh";
        Assert.Equal("00:30", ctx.InZone(FixedUtc).ToString("HH:mm"));
    }

    /// <summary><b>ويَبقى بِحَرفِه</b>: مِنطَقَةُ الرياض تُحَلُّ عَلى هذا الجِهازِ وإزاحَتُها عِندَ اللَحظَةِ الثابِتَةِ ‏+03:00 — مِن
    /// بَياناتِ المِنطَقَةِ (tzdata) لا مِن رَقَمٍ مَكتوب.</summary>
    [Fact]
    public void The_riyadh_zone_resolves_here_with_its_offset_at_the_fixed_instant()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");
        var offset = tz.GetUtcOffset(FixedUtc);
        output.WriteLine($"Asia/Riyadh عِندَ {FixedUtc:O}: {offset}");
        Assert.Equal(TimeSpan.FromHours(3), offset);
        Assert.Equal("2026-10-25 00:30", TimeZoneInfo.ConvertTimeFromUtc(FixedUtc, tz).ToString("yyyy-MM-dd HH:mm"));
    }

    /// <summary><b>ويَبقى بِحَرفِه</b>: عَدَدُ راسِمي الوَقتِ في القَوالِبِ مَطبوعٌ لا مُفتَرَض (القاعِدَة ١٠) — الصيَغُ الثَلاث الَّتي يُقرَأُ بِها.</summary>
    [Fact]
    public void The_time_renderers_under_templates_are_counted()
    {
        var root = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates");
        var rx = new Regex(@"ToString\(""(HH:mm|yyyy|MM-dd|dd/MM)|ToLocalTime\(\)");
        var files = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".razor") || f.EndsWith(".cs"))
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
        var hits = files.Sum(f => rx.Matches(File.ReadAllText(f)).Count);
        output.WriteLine($"راسِمو الوَقتِ تَحتَ libs/templates: {hits}");
        Assert.True(hits > 0, "أَداةٌ عَمياء: صِفرُ راسِمٍ لِلوَقت.");
    }

    // ─── الحَدُّ مَقيسٌ آلِيّاً (القاعِدَة ٢): كُلُّ راسِمٍ يَمُرُّ بِـInZone أَو يُثَبَّتُ بِسَبَبِه ─────────────────────

    /// <summary>مَواضِعُ تُرسَمُ فيها اللَحظَةُ المُخَزَّنَةُ بِلا تَحويلٍ — كُلٌّ بِسَبَبِه، والقائِمَةُ ثُنائِيَّةُ الاتِّجاه.</summary>
    private static readonly (string File, string Snippet, string WhyAr)[] PinnedUnzoned =
    {
        ("Components/Pages/Admin/TenantRoles.razor", "@d.CreatedAt.ToString(\"yyyy-MM-dd HH:mm\") UTC",
            "مَوسومٌ «UTC» بِحَرفِه في الصَفحَة — صادِقٌ كَما هو؛ وتَحويلُه يُلزِمُ تَبديلَ الوَسم."),
        ("Components/Pages/Admin/TenantTheme.razor", "@d.CreatedAt.ToString(\"yyyy-MM-dd HH:mm\") UTC",
            "مَوسومٌ «UTC» — نَفسُ السَبَب."),
        ("Components/Pages/Admin/TenantTheme.razor", "@at.ToString(\"yyyy-MM-dd HH:mm:ss\") UTC",
            "مَوسومٌ «UTC» — نَفسُ السَبَب."),
        ("Services/Export/TenantExportPackage.cs", "content.GeneratedAtUtc.ToString(\"yyyy-MM-dd HH:mm\")",
            "خَليَّتُه بِجِوارِ «وَقتُ التَوليد (UTC):» — مَوسومَةٌ بِحَرفِها."),
        ("Components/Pages/Admin/TenantPlanAdmin.razor", "@plan.ExpiresAt.ToString(\"yyyy-MM-dd\")",
            "تاريخُ الباقَةِ التَقويميّ — يُدخَلُ بِاستِمارَةِ الصَفحَةِ نَفسِها تاريخاً ويُقرَأُ كَما أُدخِل؛ تَحويلُه يُخالِفُ حَقلَها."),
        ("Components/Pages/Admin/TenantPlanAdmin.razor", "(plan?.StartsAt ?? DateTime.UtcNow).ToString(\"yyyy-MM-dd\")",
            "قيمَةُ حَقلٍ في الاستِمارَة تُرسَلُ إلى نُقطَةِ الحِفظِ وتُقرَأُ تاريخاً — لا عَرض."),
        ("Components/Pages/Admin/TenantPlanAdmin.razor", "plan?.ExpiresAt.ToString(\"yyyy-MM-dd\")",
            "قيمَةُ حَقلٍ في الاستِمارَة — نَفسُ السَبَب."),
        ("Components/Pages/Admin/TenantPlanAdmin.razor", "TenantPlanPolicy.HiddenAt(plan)?.ToString(\"yyyy-MM-dd\")",
            "تاريخُ الإخفاءِ مُشتَقٌّ مِن تاريخِ الباقَةِ التَقويميّ أَعلاه — يَتبَعُه."),
        ("Components/Pages/StudioHome.razor", "p.ExpiresAt.ToString(\"yyyy-MM-dd\")",
            "تاريخُ الباقَةِ التَقويميّ نَفسُه في سَطرِ الاستوديو — يُطابِقُ ما في صَفحَةِ الإدارَة."),
        ("Components/Pages/StudioHome.razor", "TenantPlanPolicy.HiddenAt(p)?.ToString(\"yyyy-MM-dd\")",
            "مُشتَقٌّ مِن السابِق — يَتبَعُه."),
    };

    [Fact]
    public void Every_time_renderer_passes_through_the_culture_zone_or_is_pinned()
    {
        var root = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace");
        var rx = new Regex(@"ToString\(""(HH:mm|yyyy|MM-dd|dd/MM)|ToLocalTime\(\)");
        var files = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".razor") || f.EndsWith(".cs"))
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();
        int zoned = 0, pinned = 0, derived = 0;
        var used = new HashSet<int>();
        var bad = new List<string>();
        foreach (var f in files)
        {
            var rel = Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/');
            var lines = File.ReadAllLines(f);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (!rx.IsMatch(line)) continue;
                if (line.Contains("ToLocalTime()", StringComparison.Ordinal)) { bad.Add($"{rel}:{i + 1} ToLocalTime"); continue; }
                if (line.Contains("InZone(", StringComparison.Ordinal)) { zoned++; continue; }
                // مُتَغَيِّرٌ حُوِّلَ قَبلَه في الدالَّةِ نَفسِها (`localAt`/`local` في المُحادَثات).
                if (Regex.IsMatch(line, @"\blocal(At)?\.ToString\(")) { derived++; continue; }
                var hit = Array.FindIndex(PinnedUnzoned, p => p.File == rel && line.Contains(p.Snippet, StringComparison.Ordinal));
                if (hit >= 0) { pinned++; used.Add(hit); continue; }
                bad.Add($"{rel}:{i + 1} {line.Trim()}");
            }
        }
        output.WriteLine($"مَفحوص: {files.Count} مِلَفّاً · بِـInZone: {zoned} · مُشتَقٌّ مِن مُحَوَّل: {derived} · مُثَبَّت: {pinned} · مُخالِف: {bad.Count}");
        Assert.True(zoned > 30, $"أَداةٌ عَمياء: {zoned} راسِماً بِـInZone فَقَط.");
        Assert.True(bad.Count == 0, "راسِمُ وَقتٍ بِلا مِنطَقَةِ الثَقافَة ولا تَثبيت:\n" + string.Join("\n", bad));
        var stale = PinnedUnzoned.Where((_, i) => !used.Contains(i)).Select(p => $"{p.File} «{p.Snippet}»").ToList();
        Assert.True(stale.Count == 0, "تَثبيتٌ لا يُطابِقُ شَيئاً — يُرفَعُ مِن القائِمَة:\n" + string.Join("\n", stale));
    }
}
