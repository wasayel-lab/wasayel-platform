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
        // ‏(‏2026-10-25، W8-7 — بَندُ المُنَسِّق؛ يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه): كانَ هُنا `Time="@Culture.InZone(n.At).ToString("HH:mm")"` —
        // والتَنسيقُ صارَ في `FormatTime` لِتَغيبَ لَحظَةٌ لَم تُضبَط؛ والمِنطَقَةُ نَفسُها (`DisplayTimeAbsentTests`).
        Assert.Contains("Time=\"@Culture.FormatTime(n.At, \"HH:mm\")\"", razor, StringComparison.Ordinal);
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
        // ‏2026-10-29 (قَرارُ المالِك د53): ‏24 ← 25 — صَفحَةُ «بَياناتي» (`MyData.razor`) تَرسُمُ تَواريخَ الحِسابِ والصَفَقاتِ والرَسائِلِ بِـ`Culture.FormatTime`.
        // ‏2026-10-29 (قَرارُ المالِك د15): ‏25 ← 26 — `StudioStudy.razor` تُنَسِّقُ مَوعِدَ تَجَدُّدِ الحِصَّةِ لِلافِتَةِ الحَدِّ بِـ`Culture.FormatTime`.
        // ‏2026-10-29 (مُراجَعَةُ تَيّارِ S3 الجَولَة ٢، قَرارُ المالِك د3 (٢) «د»): ‏26 ← 27 — `StudioOwnerNotices.razor` تَرسُمُ وَقتَ تَنبيهِ صاحِبِ المَتجَرِ بِـ`Culture.FormatTime`.
        Assert.Equal(27, readers);
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
        // ‏(‏2026-10-24 — تَيّارُ S16، مُراجَعَةُ S15 الجَولَة ١) ما يَراه الماسِحُ بَعدَ أَن صارَ يَقرَأُ صيَغَ الإقحامِ والصيَغَ ذاتَ الحَرفِ الواحِد:
        ("Services/Export/TenantExportService.cs", "{content.GeneratedAtUtc:yyyyMMdd-HHmm}.zip",
            "اسمُ مِلَفِّ التَصديرِ بِلَحظَتِه UTC لا عَرض — والمُتَغَيِّرُ يُسَمّيها Utc بِحَرفِه."),
        ("Services/Export/TenantExportPackage.cs", "{content.GeneratedAtUtc:yyyy-MM-dd HH:mm} بِتَوقيتِ UTC",
            "مَوسومٌ «بِتَوقيتِ UTC» بِحَرفِه في وَثيقَةِ التَصدير."),
        ("Services/Export/TenantExportPackage.cs", "[\"generatedAtUtc\"] = content.GeneratedAtUtc.ToString(\"O\")",
            "حَقلٌ آلِيٌّ في بَيانِ التَصديرِ بِصيغَةِ ISO-8601 لِلذَهابِ والإياب (بِـZ) — تَقرَؤُه آلَة، ومِفتاحُه يُسَمّيه UTC."),
        ("Billing/PayPalOrderSurface.cs", "حَتّى {Expires:yyyy-MM-dd}: {Reason}\"",
            "قالَبُ سَطرِ سِجِلٍّ لِلمُشَغِّل لا عَرض، بِتاريخِ الباقَةِ التَقويميّ."),
        ("Billing/PayPalOrderSurface.cs", "expiresAt={d.NewExpiresAt:yyyy-MM-dd}; order=",
            "سَطرُ تَدقيقٍ مُخَزَّن (after:) بِتاريخِ الباقَةِ التَقويميّ — كَحَقلِها في TenantPlanAdmin."),
        ("Billing/PayPalSurface.cs", "حَتّى {Expires:yyyy-MM-dd}: {Reason}\"",
            "قالَبُ سِجِلٍّ لِلمُشَغِّل — نَفسُ السَبَب."),
        ("Billing/PayPalSurface.cs", "expiresAt={d.NewExpiresAt:yyyy-MM-dd}; event=",
            "سَطرُ تَدقيقٍ — نَفسُ السَبَب."),
        ("Billing/PaddleSurface.cs", "حَتّى {Expires:yyyy-MM-dd}: {Reason}\"",
            "قالَبُ سِجِلٍّ لِلمُشَغِّل — نَفسُ السَبَب."),
        ("Billing/PaddleSurface.cs", "expiresAt={d.NewExpiresAt:yyyy-MM-dd}; tx=",
            "سَطرُ تَدقيقٍ — نَفسُ السَبَب."),
    };

    /// <summary><b>أَراسِمُ وَقتٍ هذا السَطر؟</b> (‏2026-10-24 — تَيّارُ S16، مُراجَعَةُ S15 الجَولَة ١: كانَ الماسِحُ يَعرِفُ أَربَعَ صيَغٍ لِـ<c>ToString("…")</c>
    /// و<c>ToLocalTime()</c> وَحدَها، فَـ<c>$"{at:HH:mm}"</c> و<c>@at.ToString("t")</c> و<c>ToString("d MMM")</c> تُرسَمُ بِـUTC وتَمُرّ). يَقرَأُ الآن:
    /// تِلكَ الصيَغ، و<c>hh</c>/<c>yy</c>/<c>MMM</c>، وكُلَّ ثُقبِ إقحامٍ بِمُحَدِّدِ تاريخٍ أَو ساعَة، والصيَغَ القِياسِيَّةَ ذاتَ الحَرفِ الواحِدِ عَلى مُستَقبِلٍ
    /// اسمُه وَقت (<c>…At</c>/<c>…Utc</c>/<c>…Date</c>/<c>…Time</c>/<c>Expires…</c>/<c>Starts…</c> — فَـ<c>userId.ToString("D")</c> لِـGuid لا يُعَدّ)،
    /// و<c>To(Short|Long)(Date|Time)String()</c>. وسَطرُ التَعليقِ لا يُرسَم.</summary>
    internal static readonly Regex TimeRenderer = new(
        @"ToString\(""(HH:mm|hh:mm|yyyy|yy-|MM-dd|dd/MM|d MMM|dd MMM|MMM)|ToLocalTime\(\)"
        // ‏(‏2026-10-25، W8-7 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه): ودالَّةُ العَرضِ المُنَسِّقَةُ نَفسُها راسِمٌ — وحُكمُها «zoned» أَدناه.
        + @"|\bFormatTime\("
        + @"|\{[^{}""]*:(?:HH|hh|yyyy|yy|MM|dd|d MMM|MMM)[^{}""]*\}"
        + @"|\b\w*(?:At|Utc|Date|Time|Expires|Starts)\w*\)?\??\.ToString\(""[dDfFgGtTMmYyOoRrsu]""\)"
        + @"|\bTo(?:Short|Long)(?:Date|Time)String\(\)"
        // ‏(‏2026-10-25، مُراجَعَةُ تَيّارِ S16 الجَولَة ٢ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) وثَلاثَةُ أَشكالٍ فاتَته: الحَرفُ الواحِدُ في ثُقبِ إقحامٍ عَلى
        // مُستَقبِلٍ اسمُه وَقت (`{x.At:t}`)، والساعَةُ بِحَرفٍ واحِدٍ `H:mm`/`h:mm` (في ToString وفي الإقحام)، و`ToString(fmt, provider)`.
        + @"|\{[^{}""]*\b\w*(?:At|Utc|Date|Time|Expires|Starts)\w*\)?\??:[dDtTgGfFMmYy]\}"
        + @"|ToString\(""[Hh]:mm|\{[^{}""]*:[Hh]:mm[^{}""]*\}"
        + @"|\b\w*(?:At|Utc|Date|Time|Expires|Starts)\w*\)?\??\.ToString\(""[dDfFgGtTMmYyOoRrsu]""\s*,[^)]*\)"
        // ‏(‏2026-10-25، مُراجَعَةُ تَيّارِ W8-9 الجَولَة ١ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) وشَكلانِ فاتاه: صَنفُ الحَرفِ في الإقحامِ بِلا O/o/R/r/s/u الَّتي
        // يَعُدُّها فَرعُ ToString (`{x.At:O}`، `{x.At:u}`)، و`.Value` بَعدَ مُستَقبِلٍ اسمُه وَقتٌ في الفُروعِ الثَلاثَة (`{x.CreatedAt.Value:d}`،
        // `x.ExpiresAt.Value.ToString("d")` وبِمُزَوِّدٍ) — لَحَظاتٌ قابِلَةٌ لِلفَراغِ تُرسَمُ بِـUTC.
        + @"|\{[^{}""]*\b\w*(?:At|Utc|Date|Time|Expires|Starts)\w*\)?\??(?:\.Value)?:[dDtTgGfFMmYyOoRrsu]\}"
        + @"|\b\w*(?:At|Utc|Date|Time|Expires|Starts)\w*\)?\??\.Value\.ToString\(""[dDfFgGtTMmYyOoRrsu]""(?:\s*,[^)]*)?\)");

    /// <summary>حُكمُ سَطرٍ واحِد: <c>null</c> لَيسَ راسِماً، و«zoned»/«derived»/«pinned:N» مَقبول، و«bad» مُخالِف.</summary>
    internal static string? TimeRendererVerdict(string rel, string line)
    {
        var t = line.TrimStart();
        if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("*", StringComparison.Ordinal) || t.StartsWith("@*", StringComparison.Ordinal)) return null;
        if (!TimeRenderer.IsMatch(line)) return null;
        if (line.Contains("ToLocalTime()", StringComparison.Ordinal)) return "bad";
        if (line.Contains("InZone(", StringComparison.Ordinal)) return "zoned";
        // ‏(‏2026-10-25، W8-7): `FormatTime` تُحَوِّلُ بِـInZone ثُمَّ تُنَسِّق — ولا يُعَدُّ تَنسيقُ نَتيجَةِ InZone بِاليَدِ بَعدَها (`DisplayTimeAbsentTests`).
        if (line.Contains("FormatTime(", StringComparison.Ordinal)) return "zoned";
        // مُتَغَيِّرٌ حُوِّلَ قَبلَه في الدالَّةِ نَفسِها (`localAt`/`local` في المُحادَثات).
        if (Regex.IsMatch(line, @"\blocal(At)?\.ToString\(")) return "derived";
        var hit = Array.FindIndex(PinnedUnzoned, p => p.File == rel && line.Contains(p.Snippet, StringComparison.Ordinal));
        return hit >= 0 ? $"pinned:{hit}" : "bad";
    }

    [Fact]
    public void Every_time_renderer_passes_through_the_culture_zone_or_is_pinned()
    {
        var root = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace");
        // ‏(‏2026-10-24 — تَيّارُ S16) الحُكمُ في `TimeRendererVerdict` — الصيَغُ القَديمَةُ بِحَرفِها ومَعَها ما فاتَها؛ والعَدُّ والقائِمَةُ الثُنائِيَّةُ كَما كانا.
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
                switch (TimeRendererVerdict(rel, line))
                {
                    case null: continue;
                    case "zoned": zoned++; continue;
                    case "derived": derived++; continue;
                    case var v when v.StartsWith("pinned:", StringComparison.Ordinal): pinned++; used.Add(int.Parse(v[7..])); continue;
                    default: bad.Add($"{rel}:{i + 1} {line.Trim()}"); continue;
                }
            }
        }
        output.WriteLine($"مَفحوص: {files.Count} مِلَفّاً · بِـInZone: {zoned} · مُشتَقٌّ مِن مُحَوَّل: {derived} · مُثَبَّت: {pinned} · مُخالِف: {bad.Count}");
        Assert.True(zoned > 30, $"أَداةٌ عَمياء: {zoned} راسِماً بِـInZone فَقَط.");
        Assert.True(bad.Count == 0, "راسِمُ وَقتٍ بِلا مِنطَقَةِ الثَقافَة ولا تَثبيت:\n" + string.Join("\n", bad));
        var stale = PinnedUnzoned.Where((_, i) => !used.Contains(i)).Select(p => $"{p.File} «{p.Snippet}»").ToList();
        Assert.True(stale.Count == 0, "تَثبيتٌ لا يُطابِقُ شَيئاً — يُرفَعُ مِن القائِمَة:\n" + string.Join("\n", stale));
    }

    /// <summary><b>حَقنُ عَيب — ماسِحُ راسِمي الوَقتِ يَرى الصيَغَ الَّتي فاتَتهُ</b> (‏2026-10-24، S16): كُلُّ شَكلٍ مِنها في صَفحَةٍ بِلا <c>InZone</c> «bad»،
    /// وكانَ الماسِحُ قَبلَه لا يَراه؛ والشَكلُ نَفسُه داخِلَ <c>InZone</c> مَقبول، وGuid بِـ<c>"D"</c> وسَطرُ تَعليقٍ لَيسا راسِمَين.</summary>
    [Fact]
    public void The_time_renderer_scanner_sees_interpolation_and_single_letter_formats()
    {
        const string Page = "Components/Pages/Injected.razor";
        var old = new Regex(@"ToString\(""(HH:mm|yyyy|MM-dd|dd/MM)|ToLocalTime\(\)");
        var bad = new[]
        {
            "<span>@($\"{n.At:HH:mm}\")</span>",
            "<span>@n.At.ToString(\"t\")</span>",
            "<span>@n.CreatedAt.ToString(\"d MMM\")</span>",
            "<span>@n.At.ToShortDateString()</span>",
            "var label = $\"آخِرُ دُخول {user.LastLoginAt:dd/MM HH:mm}\";",
            "<td>@(plan?.ExpiresAt.ToString(\"D\"))</td>",
            // ‏(‏2026-10-25، مُراجَعَةُ S16 الجَولَة ٢) عَيِّنَةٌ لِكُلِّ شَكلٍ فاتَ الماسِحَ بَعدَ S16:
            "<span>@($\"{n.At:t}\")</span>",
            "<span>@($\"{x.CreatedAt:d}\")</span>",
            "<span>@($\"{x.At:g}\")</span>",
            "<span>@n.At.ToString(\"H:mm\")</span>",
            "var t = $\"{n.At:h:mm tt}\";",
            "<span>@n.At.ToString(\"t\", CultureInfo.InvariantCulture)</span>",
            // ‏(‏2026-10-25، مُراجَعَةُ W8-9 الجَولَة ١) عَيِّنَةٌ لِكُلِّ شَكلٍ فاتَ ماسِحَ S16 الجَولَة ٢:
            "<span>@($\"{x.At:O}\")</span>",
            "<span>@($\"{x.At:u}\")</span>",
            "<span>@($\"{x.CreatedAt.Value:d}\")</span>",
            "<span>@x.ExpiresAt.Value.ToString(\"d\")</span>",
            "<span>@x.ExpiresAt.Value.ToString(\"d\", CultureInfo.InvariantCulture)</span>",
        };
        var accepted = new (string Line, string? Verdict)[]
        {
            ("<span>@($\"{Culture.InZone(n.At):HH:mm}\")</span>", "zoned"),
            ("<span>@Culture.InZone(n.At).ToString(\"t\")</span>", "zoned"),
            ("_log.LogInformation(\"{Owner}\", owner.ToString(\"D\"));", null),
            ("// ‏مِثالٌ في تَعليق: $\"{at:HH:mm}\"", null),
            // ‏(‏2026-10-25) والأَشكالُ الجَديدَةُ داخِلَ InZone مَقبولَة، ومُستَقبِلٌ لَيسَ وَقتاً بِحَرفٍ واحِدٍ لا يُعَدّ:
            ("<span>@($\"{Culture.InZone(n.At):t}\")</span>", "zoned"),
            ("<span>@Culture.InZone(n.At).ToString(\"H:mm\")</span>", "zoned"),
            ("var s = $\"{count:d}\";", null),
            ("var id = userId.ToString(\"D\", CultureInfo.InvariantCulture);", null),
            // ‏(‏2026-10-25، W8-9 الجَولَة ١) والأَشكالُ الجَديدَةُ داخِلَ InZone مَقبولَة، و`.Value` عَلى مُستَقبِلٍ لَيسَ وَقتاً لا يُعَدّ:
            ("<span>@($\"{Culture.InZone(x.At):O}\")</span>", "zoned"),
            ("<span>@Culture.FormatTime(x.ExpiresAt.Value, \"d\")</span>", "zoned"),
            ("var s = $\"{count.Value:d}\";", null),
            ("var id = order.Value.ToString(\"D\");", null),
        };
        var wrong = bad.Where(l => TimeRendererVerdict(Page, l) != "bad").Select(l => $"لَم يُرَ: {l} ⇒ {TimeRendererVerdict(Page, l) ?? "لَيسَ راسِماً"}")
            .Concat(accepted.Where(a => TimeRendererVerdict(Page, a.Line) != a.Verdict)
                .Select(a => $"حُكمُه {TimeRendererVerdict(Page, a.Line) ?? "null"} لا {a.Verdict ?? "null"}: {a.Line}"))
            .ToList();
        var oldBlind = bad.Count(l => !old.IsMatch(l));
        // ‏(‏2026-10-25) وماسِحُ ما بَعدَ S16 (قَبلَ هذِه الأَشكالِ الثَلاثَة) بِحَرفِه — لا يَرى العَيِّناتِ السِتَّ الجَديدَة:
        var s16 = new Regex(
            @"ToString\(""(HH:mm|hh:mm|yyyy|yy-|MM-dd|dd/MM|d MMM|dd MMM|MMM)|ToLocalTime\(\)|\bFormatTime\("
            + @"|\{[^{}""]*:(?:HH|hh|yyyy|yy|MM|dd|d MMM|MMM)[^{}""]*\}"
            + @"|\b\w*(?:At|Utc|Date|Time|Expires|Starts)\w*\)?\??\.ToString\(""[dDfFgGtTMmYyOoRrsu]""\)"
            + @"|\bTo(?:Short|Long)(?:Date|Time)String\(\)");
        var s16Blind = bad.Count(l => !s16.IsMatch(l));
        // ‏(‏2026-10-25، W8-9 الجَولَة ١) وماسِحُ S16 الجَولَة ٢ بِحَرفِه (قَبلَ الشَكلَينِ الأَخيرَين) — لا يَرى العَيِّناتِ الخَمسَ الجَديدَة:
        var s16r2 = new Regex(s16.ToString()
            + @"|\{[^{}""]*\b\w*(?:At|Utc|Date|Time|Expires|Starts)\w*\)?\??:[dDtTgGfFMmYy]\}"
            + @"|ToString\(""[Hh]:mm|\{[^{}""]*:[Hh]:mm[^{}""]*\}"
            + @"|\b\w*(?:At|Utc|Date|Time|Expires|Starts)\w*\)?\??\.ToString\(""[dDfFgGtTMmYyOoRrsu]""\s*,[^)]*\)");
        var s16r2Blind = bad.Count(l => !s16r2.IsMatch(l));
        output.WriteLine($"حُقِنَ {bad.Length} راسِماً بِلا مِنطَقَة و{accepted.Length} مَقبولاً · كانَ الماسِحُ قَبلَ S16 لا يَرى {oldBlind} مِنها"
            + $"، وماسِحُ S16 لا يَرى {s16Blind}، وماسِحُ S16 الجَولَة ٢ لا يَرى {s16r2Blind}");
        // ‏(‏2026-10-25) سِتٌّ كانَت، وسِتٌّ جَديدَةٌ لا يَراها القَديمُ أَيضاً:
        // ‏(‏2026-10-25، W8-9 الجَولَة ١ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) وخَمسٌ أُخرى: القَديمُ 17، وS16 ‏11، وS16 الجَولَة ٢ ‏5.
        Assert.Equal(17, oldBlind);
        Assert.Equal(11, s16Blind);
        Assert.Equal(5, s16r2Blind);
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }
}
