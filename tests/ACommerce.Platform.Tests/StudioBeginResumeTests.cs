using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ «حَلِّل فِكرَتي» لِمَن دَخَلَ أَصلاً — الحَلقَةُ الَّتي لا تُغلَق ═══
//
// **العِلَّةُ المَقيسَةُ حَيّاً (‏2026-08-30، على الموقِعِ الحَيّ)**:
// حِسابٌ جَديدٌ أُنشِئَ بِالبَريد، ثُمَّ فُتِحَت `/studio/new` وكُتِبَت
// فِكرَةٌ ونُقِرَ «حَلِّل فِكرَتي». **النَتيجَة: لا شَيء.** رَجَعَت
// الصَفحَةُ إلى `/studio` وفيها «لا أَفكار بَعد»، **ولا رِسالَةَ خَطَإٍ
// واحِدَة**. والبُرهانُ قاطِعٌ: الكَعكَةُ `ac.studio.prompt` بَقِيَت في
// المُتَصَفِّحِ **مَملوءَةً بِنَصِّ الفِكرَةِ غَيرَ مُستَهلَكَة**.
//
// **المَسارُ بِحَرفِه**:
//   ‏`POST /studio/begin` → يَكتُبُ الكَعكَةَ → `Redirect("/studio/auth")`
//   ‏`/studio/auth` يَرى الجَلسَةَ قائِمَةً → `location.replace('/studio')`
//   ‏`/studio` صَفحَةُ عَرضٍ — **لا تَستَهلِكُ الكَعكَةَ أَبَداً**.
//
// **ولِماذا لَم يُكشَف قَبلَ اليَوم**: التَدَفُّقُ صُمِّمَ لِلزائِرِ
// **المَجهول** (هبوط ← مُطالَبَة ← دُخول ← استِئناف)، والاستِئنافُ
// مُعَلَّقٌ في `‎/studio/auth/verify` وَحدَها ونُقطَةِ قَبولِ الشُروط.
// فَالمَسارُ أَخضَرُ لِأَوَّلِ زيارَةٍ في العُمر، **وأَحمَرُ لِكُلِّ
// زيارَةٍ بَعدَها** — وهذا أَسوَأُ ما يَكون: يَعمَلُ في التَجرِبَةِ
// الأولى ويَموتُ لِكُلِّ مُستَخدِمٍ عائِد.
//
// **وهُوَ القاعِدَة ١٢ بِحَرفِها، بَل أَسوَأُ مِن «قَريباً»**:
// ‏`ResumeStudioPromptAsync` هُوَ **المُستَدعي الوَحيدُ** لِـ
// `FeasibilityAnalysisService.StartAsync` بِمُستَخدِمٍ حَقيقيّ في
// المُستَودَعِ كُلِّه (الآخَرانِ يُمَرِّرانِ `Guid.Empty`). فَالزِرُّ
// الرَئيسُ لِلمُنتَجِ كُلِّه — «حَلِّل فِكرَتي» — **نَقرَةٌ لا تُفضي
// إلى شَيء** لِكُلِّ مَن سَبَقَ لَه الدُخول.
public class StudioBeginResumeTests
{
    private const string TemplateRoot =
        "libs/templates/ACommerce.Templates.Customer.Marketplace";

    private static string Read(string relative)
    {
        var path = Path.Combine(
            ThemeZeroEquivalenceTests.RepoRoot,
            relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {relative} — الأَداةُ عَمياءُ بِلا طَرَفٍ مَقروء.");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 300, $"أَداةٌ عَمياء: {relative} طولُه {text.Length} مِحرَفاً.");
        return text;
    }

    private static string Endpoints() => Read($"{TemplateRoot}/MarketplaceTemplateExtensions.cs");

    /// <summary>جِسمُ <c>POST /studio/begin</c> وَحدَه — مَقصوصاً مِن
    /// تَسجيلِه حَتّى <c>DisableAntiforgery</c>. القَصُّ يَمنَعُ أَن
    /// يَخضَرَّ الفَحصُ بِسَبَبِ نُقطَةٍ مُجاوِرَةٍ تَذكُرُ الاسمَ
    /// نَفسَه — وتِلكَ أَداةٌ عَمياءُ لا فَحص.</summary>
    private static string BeginBody()
    {
        var all = Endpoints();
        const string marker = "MapPost(\"/studio/begin\"";
        var start = all.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "نُقطَةُ /studio/begin غَيرُ مَوجودَة — الأَداةُ تَقيسُ عَدَماً.");

        var end = all.IndexOf("DisableAntiforgery", start, StringComparison.Ordinal);
        Assert.True(end > start, "لَم يُغلَق جِسمُ /studio/begin — القَصُّ فاشِل.");

        var body = all[start..end];
        // الأَداةُ تُعلِنُ ما قاسَته (القاعِدَة ١٠): جِسمٌ أَقصَرُ مِن
        // ذلكَ يَعني أَنّ القَصَّ انزَلَق، لا أَنّ العَطبَ اختَفى.
        Assert.True(body.Length > 200,
            $"جِسمُ /studio/begin المَقصوصُ {body.Length} مِحرَفاً فَقَط — القَصُّ انزَلَق.");
        return body;
    }

    /// <summary>
    /// <para><b>مَن دَخَلَ أَصلاً يُستَأنَفُ فَوراً، ولا يُرسَلُ إلى
    /// بابٍ دَخَلَه.</b> بِلا هذا الفَرعِ تُكتَبُ الكَعكَةُ ولا
    /// يَستَهلِكُها أَحَد — وهُوَ ما قيسَ حَيّاً.</para>
    /// </summary>
    [Fact]
    public void Begin_resumes_immediately_for_an_already_signed_in_user()
    {
        var body = BeginBody();

        // يَقرَأُ الجَلسَةَ أَصلاً — بِلا قِراءَتِها لا يُمكِنُ التَفريق.
        Assert.Contains("auth.Load()", body, StringComparison.Ordinal);
        Assert.Contains("auth.IsAuthenticated", body, StringComparison.Ordinal);

        // ويَستَأنِفُ بِنَفسِ الدالَّةِ الَّتي يَستَأنِفُ بِها بابُ
        // الدُخول — لا بِنُسخَةٍ ثانِيَةٍ تَنجَرِف (القاعِدَة ٨).
        Assert.Contains("ResumeStudioPromptAsync", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// <para><b>ومَن لَم يَدخُل يَبقى مَسارُه كَما كان</b> — الكَعكَةُ
    /// ثُمَّ البابُ ثُمَّ الاستِئنافُ بَعدَ التَحَقُّق. الإصلاحُ
    /// يُضيفُ فَرعاً ولا يَهدِمُ التَدَفُّقَ الَّذي يَعمَل.</para>
    /// </summary>
    [Fact]
    public void Begin_still_sends_an_anonymous_visitor_to_the_door()
    {
        var body = BeginBody();
        Assert.Contains(StudioPromptCookieName, body, StringComparison.Ordinal);
        Assert.Contains("Results.Redirect(\"/studio/auth\")", body, StringComparison.Ordinal);
    }

    /// <summary>اسمُ الكَعكَةِ كَما هُوَ في الكود — مَوضِعٌ واحِدٌ
    /// يُقرَأُ مِنه، فَلا يَنجَرِفُ الفَحصُ عَنِ المَفحوص.</summary>
    private const string StudioPromptCookieName = "StudioPromptCookie";

    /// <summary>
    /// <para><b>والاستِئنافُ يَقبَلُ مُطالَبَةً مُمَرَّرَةً لا كَعكَةً
    /// فَقَط.</b> السَبَبُ مُلزِم: الكَعكَةُ المَكتوبَةُ على
    /// <c>Response</c> في هذا الطَلَبِ نَفسِه **لا تَظهَرُ في
    /// <c>Request.Cookies</c>** — فَلَو قَرَأَ الاستِئنافُ الطَلَبَ
    /// وَحدَه لَرَجَعَ <c>null</c> وسَكَتَ العَطَبُ كَما كان.</para>
    /// </summary>
    [Fact]
    public void Resume_accepts_a_prompt_handed_to_it_not_only_a_request_cookie()
    {
        var all = Endpoints();
        const string marker = "private static async Task<IResult?> ResumeStudioPromptAsync";
        var start = all.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "دالَّةُ الاستِئنافِ غَيرُ مَوجودَة — الأَداةُ تَقيسُ عَدَماً.");

        var body = all.Substring(start, Math.Min(2000, all.Length - start));
        Assert.Contains("promptOverride", body, StringComparison.Ordinal);
    }
}
