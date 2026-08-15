using System.Text.RegularExpressions;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>كُلّ نُقطَة Wolverine إمّا تَحمِل <c>{slug}</c> في مَسارِها،
/// أَو تُعلِن <c>[NotTenanted]</c> بِاسمِها.</b> هذا هُوَ الحارِس
/// الَّذي يَجعَل حَصر المُستَأجِر <b>خاصِّيَّةً بِنيَوِيَّة</b> بَدَل
/// اتِّفاقٍ مَكتوب بِاليَد — ومَعَ
/// <c>opts.TenantId.AssertExists()</c> في <c>HostingExtensions</c>
/// تَرتَدّ المُخالِفَة ‏400 حَيَّةً؛ وهذا الفَحص يُقَدِّم اكتِشافَها
/// مِن الطَلَب إلى البِناء.</para>
///
/// <para><b>الكُلفَة الَّتي كَتَبَته</b>: قاسَت وَثيقَة القَرار
/// المِعماريّ <b>سِتّ مُعالِجات</b> تَحقُن <c>IDocumentSession</c>
/// وتَكتُب في مُستَأجِر <c>*DEFAULT*</c> لا في مُستَأجِر المَسار —
/// عَطَبٌ صامِت لا يُخطِئ ولا يُرى. والمُقابَلَة الَّتي تُلَخِّص
/// المَسأَلَة كُلَّها: الحَصر <b>بِاليَد</b> في مِلَفّ النِقاط كانَ
/// أَدَقّ (‏40 مِن 50) مِن الحَصر في المُعالِجات «الصَحيحَة»
/// (‏0 مِن 6). <b>فَالعَطَب لَم يَكُن اليَد، بَل أَنَّ الأَمرَ مَوكولٌ
/// إلَيها</b> — فَيَسقُط حَيثُ لا يَد.</para>
///
/// <para><b>ولِماذا مَسحُ مَصدَرٍ لا انعِكاس — وهذا قِياس لا ذَوق:</b>
/// النُسخَة الأُولى مِن هذا الفَحص قَرَأَت الأَوسِمَة بِالانعِكاس عَلى
/// التَجميعات المَنشورَة بِجِوار الاختِبار، فَوَجَدَت <b>ثَلاثاً مِن
/// أَربَع</b>: <c>ACommerce.Kit.Tenants.Server.dll</c> لا يُنشَر هُناك،
/// فَكانَت <c>/robots.txt</c> و<c>/sitemap.xml</c> — وهُما بِالضَبط
/// النُقطَتانِ اللَتانِ يَحرُسُهُما هذا المِلَفّ — <b>غَيرَ مَرئِيَّتَين
/// لِلأَداة</b>. أَداةٌ عَمياءُ عَن مَوضوعِها. والمَسح النَصّيّ يَرى
/// الشَجَرَة كُلَّها بِلا أَن يَتَعَلَّق بِرُسوم المَراجِع — وهُوَ
/// نَمَط <c>WriteEndpointGuardTests</c> القائِم.</para>
///
/// <para><b>وثُنائيّ الاتِّجاه</b>:</para>
/// <list type="bullet">
///   <item>نُقطَةٌ بِلا <c>{slug}</c> وبِلا <c>[NotTenanted]</c> ⇒
///   <b>تَحمَرّ</b>.</item>
///   <item>إعفاءٌ مُثَبَّت زالَت عِلَّتُه — زالَت النُقطَة، أَو صارَ
///   مَسارُها يَحمِل <c>{slug}</c>، أَو نُزِعَ الوَسم — ⇒
///   <b>يَحمَرّ</b>. فَالقائِمَة تَصِف الواقِع أَو تَرِثّ.</item>
/// </list>
///
/// <para><b>وحَدُّه مُعلَن</b>: الإعفاء يُقرَأ في كُتلَة أَوسِمَة
/// المِنهَج <b>قَبلَ</b> وَسم المَسار. ‏<c>[NotTenanted]</c> عَلى
/// <b>الصِنف</b> لا يُرى — فَتَحمَرّ النُقطَة. وهذا الاتِّجاه مَقصود:
/// أَحمَرُ كاذِبٌ يُصلَح بِسَطر، وأَخضَرُ كاذِبٌ يَمُرّ سَنَة.</para>
/// </summary>
public class WolverineTenancyContractTests
{
    /// <summary>نُقطَة بِلا <c>{slug}</c>، مُثَبَّتَة بِسَبَبِها.</summary>
    private sealed record Exempt(string Route, string WhyAr);

    /// <summary>
    /// <para><b>الإعفاءات المُعلَنَة</b> — وَثيقَتا زَحفٍ عَلى جَذر
    /// المَنصَّة. ونُموّ هذِه القائِمَة <b>قَرارٌ مَرئيّ في
    /// مُراجَعَة</b>.</para>
    /// </summary>
    private static readonly Exempt[] PinnedExempt =
    {
        new("/robots.txt",
            "وَثيقَة زَحف عَلى جَذر المَنصَّة — لا سلاج فيها بِطَبيعَتِها، ولا تَقرَأ Marten أَصلاً."),
        new("/sitemap.xml",
            "خَريطَة المَوقِع تَستَعرِض كُلّ المُستَأجِرين، ووَثيقَة Tenant مُسَجَّلَة SingleTenanted — فَالجَلسَة بِلا سلاج هي الصَحيحَة."),
    };

    // ─── الفَحص ───────────────────────────────────────────────────────

    [Fact]
    public void Every_wolverine_endpoint_is_tenanted_by_route_or_pinned_exempt()
    {
        var endpoints = Endpoints();

        // عَدّاد: أَداةٌ تَفحَص صِفراً أَداةٌ عَمياء (القاعِدَة ١٠).
        Assert.True(endpoints.Count >= 4,
            $"أَداة عَمياء: وُجِدَت {endpoints.Count} نُقطَة Wolverine — والمَقيس أَربَع.");

        var pinned = PinnedExempt.Select(e => e.Route).ToHashSet(StringComparer.Ordinal);

        var breaches = endpoints
            .Where(e => !e.Route.Contains("{slug}", StringComparison.Ordinal))
            .Where(e => !pinned.Contains(e.Route))
            .Select(e => $"{e.Route}   ({e.File})")
            .ToArray();

        Assert.True(breaches.Length == 0,
            "نُقطَة Wolverine بِلا {slug} وبِلا [NotTenanted] مُثَبَّت:\n  " +
            string.Join("\n  ", breaches) +
            "\nمَعَ AssertExists() تَرتَدّ ‏400 حَيَّةً. إمّا أَن تَنتَقِل تَحتَ /{slug}/…، " +
            "أَو تُعلِن [NotTenanted] وتُثَبَّت هُنا بِسَبَبِها في نَفس الكوميت.");
    }

    /// <summary><b>والنِصف الآخَر</b>: كُلّ إعفاء مُثَبَّت مَوجودٌ
    /// فِعلاً، وبِلا <c>{slug}</c>، ويَحمِل الوَسم. فَتَثبيتٌ في
    /// قائِمَةٍ بِلا وَسمٍ في الكود يَجعَل النُقطَة تَرتَدّ ‏400 حَيَّةً
    /// <b>والاختِبار أَخضَر</b> — وهذا أَسوَأ مِن غِياب الفَحص.</summary>
    [Fact]
    public void No_pinned_exemption_outlives_its_reason()
    {
        var endpoints = Endpoints();

        foreach (var ex in PinnedExempt)
        {
            var found = endpoints.FirstOrDefault(e => e.Route == ex.Route);

            Assert.True(found is not null,
                $"إعفاء مُثَبَّت لِنُقطَة لَم تَعُد مَوجودَة: «{ex.Route}» — اِرفَعه.");

            Assert.False(found!.Route.Contains("{slug}", StringComparison.Ordinal),
                $"«{ex.Route}» صارَ يَحمِل {{slug}} — اِرفَع الإعفاء.");

            Assert.True(found.OptedOut,
                $"«{ex.Route}» مُثَبَّت إعفاءً وبِلا [NotTenanted] في الكود — " +
                "يَرتَدّ ‏400 حَيّاً بَينَما القائِمَة تَقول إنَّه مَقصود.");
        }
    }

    /// <summary>كُلّ إعفاء يُعلِن سَبَبَه — فَالقائِمَة دَينٌ مَوصوف لا
    /// قائِمَةُ إسكات.</summary>
    [Fact]
    public void Every_pinned_exemption_declares_a_reason()
    {
        foreach (var e in PinnedExempt)
        {
            Assert.False(string.IsNullOrWhiteSpace(e.WhyAr), $"«{e.Route}» بِلا سَبَب.");
            Assert.True(e.WhyAr.Length > 40, $"سَبَب «{e.Route}» أَقصَر مِن أَن يَكون سَبَباً.");
        }

        Assert.Equal(
            PinnedExempt.Select(e => e.Route).Distinct(StringComparer.Ordinal).Count(),
            PinnedExempt.Length);
    }

    // ─── الأَدَوات ────────────────────────────────────────────────────

    private sealed record WolverineEndpoint(string Route, bool OptedOut, string File);

    /// <summary>البادِئَة المُؤَهَّلَة اختِيارِيَّة — نَفس عِلَّة
    /// <c>WriteEndpointGuardTests</c>: وَسمٌ يُكتَب
    /// <c>[Wolverine.Http.WolverineGet(…)]</c> يَعبُر النَمَط الساذِج
    /// بِلا أَن يُرى.</summary>
    private static readonly Regex RouteAttribute = new(
        @"\[(?:[A-Za-z_][A-Za-z0-9_]*\s*\.\s*)*Wolverine(?:Get|Post|Put|Delete|Patch)\s*\(\s*""(?<route>[^""]+)""",
        RegexOptions.Compiled);

    /// <summary>كُلّ وَسم مَسار Wolverine في شَجَرَة المَصدَر، مَعَ
    /// إجابَة «هَل أُعفِيَت؟» مَقروءَةً مِن كُتلَة أَوسِمَة المِنهَج
    /// وَحدَها — أَي مِن آخِر حَدّ عُضوٍ (<c>}</c>/<c>;</c>/<c>{</c>)
    /// حَتّى وَسم المَسار.</summary>
    private static IReadOnlyList<WolverineEndpoint> Endpoints()
    {
        var found = new List<WolverineEndpoint>();

        foreach (var (file, text) in EntitlementContractTests.SourceFiles())
        {
            // التَعليقات تُبَيَّض بِحِفظ الطول — فَذِكرُ [NotTenanted]
            // في تَعليقٍ شارِح لا يُعَدّ إعفاءً، والفَهارِس تَبقى صالِحَة.
            var code = WriteEndpointGuardTests.StripComments(text);

            foreach (Match m in RouteAttribute.Matches(code))
            {
                var before = code[..m.Index];
                var cut = before.LastIndexOfAny(new[] { '}', ';', '{' });
                var block = cut < 0 ? before : before[(cut + 1)..];

                found.Add(new WolverineEndpoint(
                    m.Groups["route"].Value,
                    block.Contains("NotTenanted", StringComparison.Ordinal),
                    Rel(file)));
            }
        }

        return found;
    }

    private static string Rel(string path) =>
        Path.GetRelativePath(ThemeZeroEquivalenceTests.RepoRoot, path).Replace('\\', '/');
}
