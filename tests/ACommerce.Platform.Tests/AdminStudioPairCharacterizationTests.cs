using System.Text.RegularExpressions;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>سِتّ عَمَلِيّات مَكتوبَة مَرَّتَين — والتَوصيف يُثَبِّت
/// الانحِراف لا الصِحَّة.</b> هذا المِلَفّ خَرقٌ <b>مُعلَن</b>
/// لِلقاعِدَة ٣ («التَوصيف يَخضَرّ قَبلَ التَبديل ولا يُمَسّ بَعدَه»)،
/// وبِمُبَرِّرٍ لا يَقبَل غَيرَه: السُلوكانِ المُوَصَّفانِ هُنا
/// <b>مُتَبايِنان</b>، والتَوحيد يَقتُل أَحَدَهُما بِالضَرورَة —
/// فَلا سَبيلَ إلى إبقاء التَوصيفَين مَعاً. وما يُمكِن، وهُوَ ما
/// يَفعَلُه هذا المِلَفّ، أَن <b>يُسَمّى الفَرقُ صَراحَةً قَبلَ أَن
/// يُحسَم</b>، فَيَصير الحَسمُ تَعديلَ سَطرٍ مَرئيّ في كوميت يَقول
/// أَيّ سُلوكٍ غَلَبَ ولِماذا — لا انجِرافاً صامِتاً.</para>
///
/// <para><b>ولِماذا فاحِصٌ نَصّيّ لا نِداءٌ حَيّ</b>: النِقاط الاثنَتا
/// عَشرَة لامداتٌ داخِلَ مِلَفٍّ واحِد، لا تُنادى مِن اختِبارٍ إلّا
/// بِإقلاع مُضيفٍ وقاعِدَةِ بَيانات — وذلك عَقد
/// <c>WASAYEL_LIVE_PROOF</c>، أَي <b>خارِج</b> البَوّابَة. فَالمَقيس
/// هُنا <b>مَوضِعٌ ونَصّ</b>، تَماماً كَما في
/// <c>WriteEndpointGuardTests</c> و<c>EndpointBodyBleedTests</c>،
/// وبِنَفس ماسِحِهِما المَقيس بِحَقن عَيب (القاعِدَة ٨: لا ماسِحَ
/// ثانِياً). والبُرهان الحَيّ يَقَع بِـ<c>curl</c> على المَسارَين
/// بَعدَ التَوحيد — لا بَدَلاً عَن هذا بَل فَوقَه.</para>
///
/// <para><b>وما لا يَدَّعيه</b>: لا يَقيس أَنّ الطَرَفَين
/// <i>يَتَصَرَّفانِ</i> كَما يَقول نَصُّهُما. يَقيس أَنّ النَصَّين
/// يَختَلِفانِ حَيثُ قيلَ إنَّهُما يَختَلِفان، ويَتَّفِقانِ حَيثُ
/// قيلَ إنَّهُما وُحِّدا.</para>
/// </summary>
public class AdminStudioPairCharacterizationTests
{
    /// <summary>حالَة الزَوج: <c>Diverged</c> = اثنانِ يَختَلِفان،
    /// <c>Unified</c> = اثنانِ يُنادِيانِ خِدمَةً واحِدَة.</summary>
    private enum PairState { Diverged, Unified }

    /// <summary>الحَقائِق المَقيسَة لِطَرَفٍ واحِد مِن الزَوج.</summary>
    private sealed record Side(
        string Route,
        bool WritesAudit,
        string Denial,
        string[] ErrorCodes,
        string[] Markers);

    private sealed record Pair(string Operation, PairState State, Side Admin, Side Studio, string WhyAr);

    // ─── ما هُوَ مَقيسٌ اليَوم ─────────────────────────────────────────

    /// <summary>
    /// <para><b>الجَدوَل المُثَبَّت.</b> كُلّ قيمَة هُنا قُرِئَت مِن
    /// أَجسام النِقاط نَفسِها يَوم ‏2026-08-15، لا مِن وَثيقَة. ومَتى
    /// وُحِّدَت عَمَلِيَّة تُقلَب حالَتُها إلى <c>Unified</c> <b>في
    /// نَفس كوميتِها</b>، ويُكتَب في <c>WhyAr</c> أَيّ سُلوكٍ غَلَبَ
    /// ولِماذا.</para>
    ///
    /// <para><b>والانحِرافات الخَمسَة الَّتي قاسَتها وَثيقَة القَرار
    /// المِعماريّ</b> كُلُّها ظاهِرَةٌ في هذا الجَدوَل بِأَعمِدَتِه:
    /// التَدقيق (<c>WritesAudit</c>)، وردّ الرَفض (<c>Denial</c>)،
    /// ورُموز الخَطَأ (<c>ErrorCodes</c>)، والأَيقونَة الافتِراضِيَّة
    /// و<c>AuthChannel</c> (<c>Markers</c>).</para>
    /// </summary>
    private static readonly Pair[] Pinned =
    {
        new("branding", PairState.Unified,
            new Side("/admin/tenants/{slug}/branding/save", true,  "403",
                     Array.Empty<string>(), new[] { "service" }),
            new Side("/studio/apps/{slug}/branding/save",   true,  "302",
                     Array.Empty<string>(), new[] { "service" }),
            "غَلَبَ /admin في مِحوَرَين بِتَوصِيَةِ المالِك: التَدقيق يُكتَب دائِماً (أَمنٌ وامتِثال، " +
            "وغِيابُه في مَسارٍ كانَ عَطَباً لا خِياراً)، و‏AuthChannel يُدرَج (الأَكمَل يَغلِب) — " +
            "لَكِنّ غِيابَه مِن الطَلَب يَعني «لا تُغَيِّر» لا «أَعِدهُ إلى هاتِف»، وإلّا مَحا " +
            "حِفظُ الاسمِ مِن الاستوديو قَناةَ مُستَأجِرٍ على نَفاذ — وصَفحَةُ الاستوديو لا تُدير " +
            "القَناة أَصلاً. ورُموزُ الخَطَأ غَلَبَت فيها صيغَةُ /admin لِأَنَّها تَقول العِلَّة " +
            "(name_required) لا الحَقل (name)."),

        new("categories", PairState.Diverged,
            new Side("/admin/tenants/{slug}/categories/save", true,  "403",
                     new[] { "bad_categories", "no_categories" }, new[] { "icon:U+1F3E0" }),
            new Side("/studio/apps/{slug}/categories/save",   false, "302",
                     new[] { "empty", "format" }, new[] { "icon:U+1F3F7,U+FE0F" }),
            ""),

        new("roles", PairState.Diverged,
            new Side("/admin/tenants/{slug}/roles/save", true,  "403",
                     Array.Empty<string>(), Array.Empty<string>()),
            new Side("/studio/apps/{slug}/roles/save",   false, "302",
                     Array.Empty<string>(), Array.Empty<string>()),
            ""),

        new("regions", PairState.Diverged,
            new Side("/admin/tenants/{slug}/regions/save", true,  "403",
                     new[] { "bad_format", "empty" }, Array.Empty<string>()),
            new Side("/studio/apps/{slug}/regions/save",   false, "302",
                     new[] { "empty", "format" }, Array.Empty<string>()),
            ""),

        new("pwa", PairState.Diverged,
            new Side("/admin/tenants/{slug}/pwa/save", true,  "403",
                     new[] { "icon_bad_type", "icon_too_large" }, Array.Empty<string>()),
            new Side("/studio/apps/{slug}/pwa/save",   false, "302",
                     new[] { "icon_bad_type", "icon_too_large" }, Array.Empty<string>()),
            ""),

        new("attributes", PairState.Diverged,
            new Side("/admin/tenants/{slug}/attributes/save", true,  "403",
                     new[] { "bad_format", "no_scope" }, Array.Empty<string>()),
            new Side("/studio/apps/{slug}/attributes/save",   false, "302",
                     new[] { "bad_format", "no_scope" }, Array.Empty<string>()),
            ""),
    };

    // ─── الفَحص ───────────────────────────────────────────────────────

    /// <summary>عَدّاد العَمى (القاعِدَة ١٠): الاثنَتا عَشرَة نُقطَةً
    /// تُوجَد فِعلاً. أَداةٌ لا تَجِد مَوضوعَها تَفشَل ولا تُبَلِّغ
    /// صِفراً.</summary>
    [Fact]
    public void All_twelve_endpoints_of_the_six_pairs_are_found()
    {
        var bodies = Bodies();

        foreach (var p in Pinned)
        {
            Assert.True(bodies.ContainsKey(p.Admin.Route),
                $"أَداة عَمياء: لَم تُوجَد النُقطَة {p.Admin.Route}.");
            Assert.True(bodies.ContainsKey(p.Studio.Route),
                $"أَداة عَمياء: لَم تُوجَد النُقطَة {p.Studio.Route}.");
        }

        Assert.Equal(12, bodies.Count);
    }

    /// <summary>كُلّ طَرَفٍ يُطابِق حَقائِقَه المُثَبَّتَة — وأَيّ
    /// انجِرافٍ عَن الجَدوَل يَحمَرّ بِاسم المِحوَر.</summary>
    [Fact]
    public void Each_side_matches_its_pinned_facts()
    {
        var bodies = Bodies();
        var breaches = new List<string>();

        foreach (var p in Pinned)
        foreach (var side in new[] { p.Admin, p.Studio })
        {
            var m = MeasureSide(side.Route, bodies[side.Route]);

            if (m.WritesAudit != side.WritesAudit)
                breaches.Add($"{side.Route}: تَدقيق — مُثَبَّت {side.WritesAudit}، مَقيس {m.WritesAudit}");
            if (m.Denial != side.Denial)
                breaches.Add($"{side.Route}: ردّ الرَفض — مُثَبَّت {side.Denial}، مَقيس {m.Denial}");
            if (!m.ErrorCodes.SequenceEqual(side.ErrorCodes, StringComparer.Ordinal))
                breaches.Add($"{side.Route}: رُموز الخَطَأ — مُثَبَّت [{string.Join(',', side.ErrorCodes)}]، " +
                             $"مَقيس [{string.Join(',', m.ErrorCodes)}]");
            if (!m.Markers.SequenceEqual(side.Markers, StringComparer.Ordinal))
                breaches.Add($"{side.Route}: العَلامات — مُثَبَّت [{string.Join(',', side.Markers)}]، " +
                             $"مَقيس [{string.Join(',', m.Markers)}]");
        }

        Assert.True(breaches.Count == 0,
            "انجِرافٌ عَن الجَدوَل المُثَبَّت — إمّا أَنّ الكود تَغَيَّر بِلا تَحديث الجَدوَل، " +
            "أَو أَنّ الجَدوَل يَصِف ماضِياً:\n  " + string.Join("\n  ", breaches));
    }

    /// <summary>
    /// <para><b>وهذا هُوَ الانحِراف مَقيساً لا مَوصوفاً</b>: زَوجٌ
    /// حالَتُه <c>Diverged</c> <b>يَجِب</b> أَن يَختَلِف طَرَفاه في
    /// مِحوَرٍ واحِد على الأَقَلّ. فَإن تَطابَقا وحالَتُهُما لَم
    /// تُقلَب، فَالجَدوَل يَصِف ماضِياً — وذلك يَحمَرّ. (النِصف
    /// الثاني مِن ثُنائيّ الاتِّجاه: الادِّعاءُ يَموت مَع
    /// سَبَبِه.)</para>
    /// </summary>
    [Fact]
    public void A_pair_still_marked_diverged_really_does_diverge()
    {
        var bodies = Bodies();
        var stale = new List<string>();
        var checkedPairs = 0;

        foreach (var p in Pinned.Where(x => x.State == PairState.Diverged))
        {
            checkedPairs++;
            var a = MeasureSide(p.Admin.Route, bodies[p.Admin.Route]);
            var s = MeasureSide(p.Studio.Route, bodies[p.Studio.Route]);

            var same =
                a.WritesAudit == s.WritesAudit &&
                a.Denial == s.Denial &&
                a.ErrorCodes.SequenceEqual(s.ErrorCodes, StringComparer.Ordinal) &&
                a.Markers.SequenceEqual(s.Markers, StringComparer.Ordinal);

            if (same)
                stale.Add($"{p.Operation}: مُعلَنٌ مُنحَرِفاً والطَرَفانِ مُتَّفِقان — اقلِب الحالَة.");
        }

        Assert.True(checkedPairs > 0, "أَداة عَمياء: لا زَوجَ مُنحَرِفاً لِيُفحَص.");
        Assert.True(stale.Count == 0, string.Join("\n  ", stale));
    }

    /// <summary>
    /// <para><b>الزَوج المُوَحَّد يَتَّفِق على أَربَعَة مَحاوِر
    /// ويَختَلِف على واحِد — بِقَرار.</b> يَتَّفِقانِ على: المَنطِق
    /// (خِدمَةٌ واحِدَة يُنادِيانِها)، والتَدقيق (يُكتَب في
    /// الطَرَفَين)، ورُموز الخَطَأ (لَم تَعُد في الجِسم أَصلاً —
    /// خَرَجَت إلى مُعجَم الخِدمَة المُغلَق)، والعَلامات.
    /// ويَختَلِفانِ على <b>ردّ الرَفض</b> وَحدَه: ‏403 لِلإدارَة
    /// و‏302 لِلاستوديو.</para>
    ///
    /// <para><b>ولِماذا هذا الفَرق يَبقى</b>: القَرار واحِد
    /// (مَسموح/مَمنوع + سَبَب)، وتَحويلُه شَأنُ السَطح — نَموذَجُ
    /// مُتَصَفِّحٍ يَحتاج إعادَةَ تَوجيه، وعَميلٌ آليّ يَحتاج رَمزاً.
    /// فَالمُوَحَّد هُوَ القَرار، والمَعروض يَبقى لِجُمهورِه.</para>
    /// </summary>
    [Fact]
    public void A_unified_pair_shares_one_service_and_differs_only_in_presentation()
    {
        var bodies = Bodies();
        var breaches = new List<string>();
        var unified = Pinned.Where(x => x.State == PairState.Unified).ToArray();

        foreach (var p in unified)
        {
            var a = MeasureSide(p.Admin.Route, bodies[p.Admin.Route]);
            var s = MeasureSide(p.Studio.Route, bodies[p.Studio.Route]);

            var adminService  = ServiceOf(bodies[p.Admin.Route]);
            var studioService = ServiceOf(bodies[p.Studio.Route]);

            if (adminService is null || studioService is null)
                breaches.Add($"{p.Operation}: زَوجٌ مُعلَنٌ مُوَحَّداً وأَحَدُ طَرَفَيه لا يُنادي خِدمَة " +
                             $"({adminService ?? "—"} / {studioService ?? "—"})");
            else if (!string.Equals(adminService, studioService, StringComparison.Ordinal))
                breaches.Add($"{p.Operation}: خِدمَتانِ لا واحِدَة — {adminService} مُقابِل {studioService}");

            if (!(a.WritesAudit && s.WritesAudit))
                breaches.Add($"{p.Operation}: التَدقيق لا يُكتَب في الطَرَفَين — " +
                             $"admin={a.WritesAudit}, studio={s.WritesAudit}");

            if (!a.ErrorCodes.SequenceEqual(s.ErrorCodes, StringComparer.Ordinal))
                breaches.Add($"{p.Operation}: رُموز الخَطَأ ما زالَت في الأَجسام ومُختَلِفَة — " +
                             $"[{string.Join(',', a.ErrorCodes)}] مُقابِل [{string.Join(',', s.ErrorCodes)}]");

            if (a.Denial != "403" || s.Denial != "302")
                breaches.Add($"{p.Operation}: ردّ الرَفض غَيرُ المُقَرَّر — " +
                             $"admin={a.Denial} (يَجِب 403)، studio={s.Denial} (يَجِب 302)");
        }

        Assert.True(breaches.Count == 0,
            "زَوجٌ مُعلَنٌ مُوَحَّداً وهُوَ لَيسَ كَذلك:\n  " + string.Join("\n  ", breaches));
    }

    /// <summary>كُلّ زَوجٍ مُوَحَّد يَقول أَيّ سُلوكٍ غَلَبَ ولِماذا —
    /// فَالتَوحيد قَرارٌ مَكتوب لا نَتيجَةُ نَسخ. والزَوج المُنحَرِف
    /// لا يُبَرَّر: هُوَ الحالَةُ الَّتي تُصلَح.</summary>
    [Fact]
    public void Every_unified_pair_declares_which_behaviour_won()
    {
        foreach (var p in Pinned.Where(x => x.State == PairState.Unified))
        {
            Assert.False(string.IsNullOrWhiteSpace(p.WhyAr), $"«{p.Operation}» وُحِّدَت بِلا سَبَب.");
            Assert.True(p.WhyAr.Length > 60,
                $"سَبَبُ تَوحيد «{p.Operation}» أَقصَرُ مِن أَن يَكون سَبَباً.");
        }

        Assert.Equal(
            Pinned.Select(p => p.Operation).Distinct(StringComparer.Ordinal).Count(),
            Pinned.Length);
    }

    // ─── الأَدَوات ────────────────────────────────────────────────────

    /// <summary>رَمزُ خَطَأٍ حَرفيّ في جِسم النُقطَة — بِصيغَتَيه
    /// المُستَعمَلَتَين: <c>?err=code</c> مُباشَرَةً، و
    /// <c>Back("code")</c> عَبرَ الدالَّة المَحَلِّيَّة. وقياسُ
    /// الأُولى وَحدَها كانَ يَبتَلِع رُموزَ الفِئات والخَصائِص كُلَّها
    /// (‏<c>?err={err}</c> لَيسَ رَمزاً).</summary>
    private static readonly Regex ErrCodeInline = new(@"\?err=(?<c>[a-z_]+)", RegexOptions.Compiled);
    private static readonly Regex ErrCodeViaBack = new(@"\bBack\(\s*""(?<c>[a-z_]+)""", RegexOptions.Compiled);

    /// <summary>الأَيقونَة الافتِراضِيَّة: القيمَة بَعدَ <c>:</c> في
    /// الشَرط الثُلاثيّ المُسنَد إلى <c>Icon</c>.</summary>
    private static readonly Regex DefaultIcon =
        new(@"Icon\s*=[^,;]*?:\s*""(?<i>[^""]+)""", RegexOptions.Compiled);

    private static readonly Regex ServiceCall =
        new(@"\b(?<s>[A-Z][A-Za-z]*SaveService)\s*\.", RegexOptions.Compiled);

    /// <summary>أَجسام النِقاط الاثنَتَي عَشرَة — مِن الماسِح المَقيس
    /// نَفسِه الَّذي تَستَعمِلُه بَقِيَّة الفَواحِص (القاعِدَة ٨).</summary>
    private static IReadOnlyDictionary<string, string> Bodies()
    {
        var wanted = Pinned
            .SelectMany(p => new[] { p.Admin.Route, p.Studio.Route })
            .ToHashSet(StringComparer.Ordinal);

        return WriteEndpointGuardTests.AllMinimalApiEndpoints()
            .Where(e => wanted.Contains(e.Route))
            .ToDictionary(e => e.Route, e => e.Body, StringComparer.Ordinal);
    }

    private static Side MeasureSide(string route, string body)
    {
        var denial =
            body.Contains("return Forbidden()", StringComparison.Ordinal) ? "403"
            : body.Contains("Results.Redirect(\"/studio\")", StringComparison.Ordinal) ? "302"
            : "—";

        var codes = ErrCodeInline.Matches(body).Select(m => m.Groups["c"].Value)
            .Concat(ErrCodeViaBack.Matches(body).Select(m => m.Groups["c"].Value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        var markers = new List<string>();
        foreach (Match m in DefaultIcon.Matches(body))
            markers.Add("icon:" + CodePoints(m.Groups["i"].Value));
        if (body.Contains("AuthChannel", StringComparison.Ordinal)) markers.Add("AuthChannel");
        if (ServiceCall.IsMatch(body)) markers.Add("service");

        return new Side(route,
            body.Contains("LogTenantConfigChangeAsync", StringComparison.Ordinal),
            denial, codes, markers.ToArray());
    }

    /// <summary>الأَيقونَة تُثَبَّت بِنِقاطِها الرَمزِيَّة لا
    /// بِمِحرَفِها: مُقارَنَةُ رُمَيزٍ تَعبيريّ عَبرَ مِلَفَّين
    /// تَتَعَلَّق بِمُحَدِّد الشَكل (‏U+FE0F) وبِتَرميز المِلَفّ،
    /// و<c>U+1F3F7,U+FE0F</c> تَقول ما تَقيسُه بِلا لَبس.</summary>
    private static string CodePoints(string s)
    {
        var parts = new List<string>();
        for (var i = 0; i < s.Length; i++)
        {
            int cp = char.IsHighSurrogate(s[i]) && i + 1 < s.Length
                ? char.ConvertToUtf32(s[i], s[++i])
                : s[i];
            parts.Add($"U+{cp:X4}");
        }
        return string.Join(',', parts);
    }

    private static string? ServiceOf(string body)
    {
        var m = ServiceCall.Match(body);
        return m.Success ? m.Groups["s"].Value : null;
    }
}
