using System.Text.RegularExpressions;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>كُلُّ مَدخَلٍ يَصِلُ إلى نَموذَجِ لُغَةٍ يُعلِنُ بَوّابَةَ
/// حِصَّةٍ — أَو يُثَبَّتُ بِاسمِه وسَبَبِه.</b> هذا الفَحصُ الآليُّ
/// لِلحَدِّ الَّذي كَتَبَته ‏ADR-015، وقَد كُتِبَ <b>بَعدَ كِلفَتِه لا
/// قَبلَها</b>.</para>
///
/// <para><b>العِلَّةُ المَقيسَة (‏2026-08-30)</b>: مَوجَةُ ‏ADR-015
/// أَنهَت الحُدودَ اللانِهائِيَّة (‏<c>int.MaxValue</c> ← ‏40 · 200 ·
/// 40) وادَّعَت أَنّ «مِفتاحَ نَموذَجِ اللُغَةِ صارَ مَحدوداً».
/// <b>والدَعوى ساقِطَة</b>: ‏<c>POST /studio/s/{id}/analyze</c> كانَت
/// تُشَغِّلُ التَحليلَ <b>بِلا بَوّابَةِ حِصَّةٍ ولا عَدّاد</b> —
/// قائِمَةُ وُسَطاءِ اللامدا لا تَحوي <c>StudioTierService</c>
/// إطلاقاً، ولا <c>CheckAnalyzeAsync</c> ولا
/// <c>RecordAnalysisAsync</c> بَينَ فَحصِ المِلكِيَّةِ وإطلاقِ
/// <c>RunAnalysisAsync</c>. فَأَيُّ مُستَخدِمٍ — ولَو عَلى
/// <c>spark</c> بِحَدِّ تَحليلٍ واحِدٍ شَهرِيّاً — يُعيدُ النِداءَ بِلا
/// سَقف، وكُلُّ نِداءٍ <b>نِداءا</b> نَموذَجِ لُغَةٍ بِـ
/// <c>MaxTokens: 8000</c> عَلى <b>مِفتاحِ المالِك</b>.</para>
///
/// <para><b>ولِماذا لَم يُمسِكها شَيءٌ طَوالَ ذلك</b>: الحَدُّ كانَ
/// مَكتوباً في <b>كاتالوجِ الأَرقام</b> (‏<see cref="TierCatalog"/>)،
/// وفَحصُه كانَ يَسأَل «أَكُلُّ رَقَمٍ مُنتَهٍ؟» — وهُوَ سُؤالٌ عَن
/// <b>قيمَةِ</b> الحَدِّ لا عَن <b>وُصولِه</b>. ورَقَمٌ مُنتَهٍ في
/// جَدوَلٍ لا يَقرَؤُه أَحَدٌ عِندَ الإنفاقِ لَيسَ حَدّاً. وهذا
/// الفَحصُ يَقلِبُ السُؤال: <b>لا يَسأَلُ عَنِ الرَقَم، بَل عَن كُلِّ
/// بابٍ يُنفِقُ مِنه</b>.</para>
///
/// <para><b>يَفحَصُ التَوقيعَ لا النِيَّة</b> (القاعِدَة ٦): يَبحَثُ
/// عَن رَمزِ بَوّابَةٍ مَعروفٍ في جِسمِ المَدخَل، ويَتَحَقَّقُ
/// زِيادَةً أَنَّ البَوّابَةَ <b>تَسبِقُ الإطلاقَ</b> نَصّيّاً —
/// فَبَوّابَةٌ بَعدَ <c>Task.Run</c> لَيسَت بَوّابَة.</para>
///
/// <para><b>وحُدودُه مُعلَنَة، ولا يُدَّعى ما لا يَفعَل</b>: لا
/// يُثبِتُ أَنَّ البَوّابَةَ <b>تَرُدُّ</b> عِندَ الفَشَل، ولا
/// أَنَّها البَوّابَةُ الصَحيحَةُ لِهذا العَدّاد
/// (‏<c>CheckRefineAsync</c> عَلى نُقطَةِ تَحليلٍ تَمُرّ). تِلكَ
/// مُراجَعَةٌ لا فَحص. <b>وما يَفعَلُه هُوَ ما سَقَطَ فِعلاً</b>:
/// غِيابُ البَوّابَةِ رَأساً.</para>
/// </summary>
public class LanguageModelQuotaGateTests
{
    // ─── ١. المَعلومَةُ الأولى: أَينَ يُنادى نَموذَجُ اللُغَة؟ ────────

    /// <summary>
    /// <para>الدالَّاتُ الَّتي تَصِلُ إلى <c>IAgentBackend.CallAsync</c>
    /// — <b>مَقيسَةٌ لا مَحفوظَة</b>: تُشتَقُّ مِنَ المَصدَرِ بِمَسحِ
    /// أَجسامِ الدالَّاتِ الَّتي تَحوي النِداء، ثُمَّ تُغلَقُ
    /// بِمُنادِيها المُباشِر.</para>
    ///
    /// <para><b>ولِماذا قائِمَةٌ مُثَبَّتَةٌ فَوقَ المَسح</b>: المَسحُ
    /// يُجيبُ «مَن يُنادي اليَوم»، والقائِمَةُ تُجيبُ «ومَن كانَ
    /// يُنادي حينَ قيسَ» — فَاختِفاءُ اسمٍ مِنَ المَسحِ بِسَبَبِ
    /// إعادَةِ تَسمِيَةٍ صامِتَةٍ يُحمِرّ بَدَلَ أَن يُفرِغَ الفاحِصَ
    /// بِصَمت. (القاعِدَة ١٠: أَداةٌ تَفحَصُ صِفراً أَداةٌ عَمياء.)</para>
    /// </summary>
    internal static readonly string[] LlmReachingMethods =
    {
        // ‏AgentService — مُحادَثَةُ الوَكيل (‏MaxTokens: 2048).
        "AskAsync",
        "ContinueAfterToolAsync",
        // ‏FeasibilityAnalysisService — دِراسَةُ الجَدوى.
        "RunAnalysisAsync",     // ‏MaxTokens: 8000، ومُحاوَلَتان
        "RefineSectionAsync",   // ‏MaxTokens: 3000
    };

    /// <summary>
    /// <para><b>مَدخَلٌ يُنفِقُ نِداءَ نَموذَجِ لُغَةٍ</b> — نُقطَةُ
    /// <c>Map*</c> أَو صَفحَةُ <c>.razor</c> تَبلُغُ إحدى
    /// <see cref="LlmReachingMethods"/>.</summary>
    internal sealed record LlmEntry(string Where, string Body, string File);

    // ─── ٢. رُموزُ البَوّابات ─────────────────────────────────────────

    /// <summary>
    /// <para><b>بَوّابَةُ حِصَّةٍ حَقيقِيَّة</b> — عَدّادٌ يُفحَصُ
    /// ويُستَهلَك. ولا تُوَسَّعُ هذِه القائِمَةُ إلّا بِعَدّادٍ
    /// جَديدٍ في <see cref="StudioTierService"/>.</para>
    /// </summary>
    private static readonly string[] QuotaGates =
    {
        "CheckAnalyzeAsync", "CheckRefineAsync", "CheckBuildAsync",
    };

    /// <summary>
    /// <para><b>سُلطَةُ المالِكِ نَفسِه</b> — لا بَوّابَةَ حِصَّةٍ
    /// وهذا صَحيح: المِفتاحُ مِفتاحُه، والفاتورَةُ فاتورَتُه، ومَن
    /// يَعبُرُ هذا الحارِسَ هُوَ صاحِبُ المَنَصَّة.</para>
    ///
    /// <para><b>وهذا لَيسَ استِثناءً مَسكوتاً عَنه بَل تَصنيفٌ
    /// مُختَلِف</b>: الحِصَّةُ تَحرُسُ <b>مالَ المالِكِ مِن
    /// عُمَلائِه</b>، ولا مَعنى لِأَن تَحرُسَه مِن نَفسِه.</para>
    /// </summary>
    private static readonly string[] OwnerAuthorityGates =
    {
        "PlatformAdminGuard.EvaluateAsync", "RequirePlatformAdmin",
    };

    /// <summary>أَوَّلُ ما يُعَدُّ إنفاقاً — لِفَحصِ «البَوّابَةُ
    /// تَسبِق».</summary>
    private static readonly string[] SpendCalls = LlmReachingMethods;

    // ─── ٣. الدَينُ المُعلَن ──────────────────────────────────────────

    /// <summary>مَدخَلٌ يُنفِقُ نِداءً بِلا بَوّابَةِ حِصَّة، مُثَبَّتٌ
    /// بِسَبَبِه وبِشَرطِ سُقوطِه.</summary>
    internal sealed record UngatedSpender(string Where, string WhyAr);

    /// <summary>
    /// <para><b>واحِدٌ لا أَكثَر — ويُقالُ ولا يُبتلَع</b> (القاعِدَة
    /// ١٥).</para>
    ///
    /// <para><c>/studio/agent</c> صَفحَةُ مُحادَثَةٍ تَفاعُلِيَّة
    /// (‏<c>InteractiveServer</c>) يَبلُغُها أَيُّ صاحِبِ جَلسَةِ
    /// استوديو بِنَقرَةٍ مِن <c>/studio</c>
    /// (‏<c>StudioHome.razor</c>)، وكُلُّ رِسالَةٍ فيها نِداءُ نَموذَجِ
    /// لُغَةٍ عَلى مِفتاحِ المالِك — <b>بِلا عَدّادٍ واحِد</b>. وهي
    /// مِن صِنفِ العَطَبِ نَفسِه الَّذي أَغلَقَته هذِه المَوجَةُ في
    /// <c>/studio/s/{id}/analyze</c>.</para>
    ///
    /// <para><b>ولِماذا لَم تُغلَق في نَفسِ المَوجَة</b>: إغلاقُها
    /// يَلزَمُه <b>رَقَمٌ لا وُجودَ لَه</b> — لا حَدَّ لِرَسائِلِ
    /// الوَكيلِ في <see cref="TierLimits"/> ولا في أَيّ وَثيقَة،
    /// و<c>AnalysesPerMonth</c>/<c>RefinesPerMonth</c> عَدّادانِ
    /// لِفِعلَينِ آخَرَين. واختِراعُ الرَقَمِ خَرقُ القاعِدَة ١٦،
    /// و<b>عَدُّ رِسالَةِ وَكيلٍ تَحليلاً</b> يَجعَلُ لافِتَةَ
    /// التَرقِيَةِ تَكذِب.</para>
    ///
    /// <para><b>وشَرطُ السُقوط، مَكتوبٌ لا مُؤَجَّل</b>: يَومَ يُجيبُ
    /// المالِكُ بِعَدَدِ رَسائِلِ الوَكيلِ الشَهرِيَّةِ لِكُلِّ دَرَجَة،
    /// يُضافُ الحَقلُ إلى <see cref="TierLimits"/> و
    /// <c>CheckAgentMessageAsync</c> إلى <see cref="StudioTierService"/>،
    /// وتُرفَعُ هذِه الإدخالَة — <b>ويُحمِرُّ هذا الفاحِصُ مِن
    /// نَفسِه</b> إن لَم تُرفَع.</para>
    /// </summary>
    private static readonly UngatedSpender[] PinnedUngated =
    {
        new("libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages/StudioAgent.razor",
            "مُحادَثَةُ وَكيلِ التَطبيقات — تُنفِقُ نِداءً لِكُلِّ رِسالَة عَلى مِفتاحِ المالِك، " +
            "ولا عَدّادَ لَها في TierLimits. إغلاقُها يَلزَمُه رَقَمٌ مِنَ المالِك (القاعِدَة ١٦)، " +
            "وعَدُّ رِسالَةِ وَكيلٍ تَحليلاً يَجعَلُ لافِتَةَ التَرقِيَةِ تَكذِب. " +
            "تَسقُط يَومَ يوجَد حَدُّ رَسائِلِ الوَكيلِ في الدَرَجات."),
    };

    // ─── ٤. الفَحص ────────────────────────────────────────────────────

    /// <summary>
    /// <para><b>تَجاوُزُ الحِصَّةِ لا يُنفِقُ نِداءً.</b> كُلُّ مَدخَلٍ
    /// يَبلُغُ نَموذَجَ لُغَةٍ يُعلِنُ بَوّابَةَ حِصَّةٍ، أَو سُلطَةَ
    /// المالِكِ نَفسِه، أَو يُثَبَّتُ بِسَبَبِه.</para>
    /// </summary>
    [Fact]
    public void Exceeding_the_quota_spends_no_language_model_call()
    {
        var entries = LlmEntries().ToList();

        // عَدّاد: أَداةٌ تَفحَصُ صِفراً أَداةٌ عَمياء (القاعِدَة ١٠).
        Assert.True(entries.Count >= 9,
            $"أَداة عَمياء: وُجِدَ {entries.Count} مَدخَلاً يَبلُغُ نَموذَجَ لُغَة — والمَقيسُ ٩ فَأَكثَر.");

        var pinned = PinnedUngated.Select(p => p.Where).ToHashSet(StringComparer.Ordinal);

        var breaches = entries
            .Where(e => !HasQuotaGate(e.Body) && !HasOwnerAuthority(e.Body))
            .Where(e => !pinned.Contains(e.Where))
            .Select(e => $"{e.Where}   ({e.File})")
            .ToArray();

        Assert.True(breaches.Length == 0,
            $"مَدخَلٌ يُنفِقُ نِداءَ نَموذَجِ لُغَةٍ بِلا بَوّابَةِ حِصَّة ({entries.Count} مَفحوصاً):\n  " +
            string.Join("\n  ", breaches) +
            "\nإمّا `StudioTierService.Check…Async` في التَوقيعِ قَبلَ الإطلاق، " +
            "أَو حارِسُ مُشرِفِ المَنَصَّة، أَو تَثبيتٌ بِسَبَبِه في نَفسِ الكوميت.");
    }

    /// <summary>
    /// <para><b>والبَوّابَةُ تَسبِقُ الإنفاقَ نَصّيّاً</b> — وإلّا
    /// فَلَيسَت بَوّابَة (القاعِدَة ٦: التَخويلُ يَسبِق). بَوّابَةٌ
    /// تُفحَصُ بَعدَ <c>Task.Run</c> تُحاسِبُ عَلى نِداءٍ وَقَعَ.</para>
    /// </summary>
    [Fact]
    public void The_quota_gate_precedes_the_spend()
    {
        var late = new List<string>();
        var compared = 0;

        foreach (var e in LlmEntries())
        {
            var gateAt = FirstIndexOfAny(e.Body, QuotaGates);
            var spendAt = FirstIndexOfAny(e.Body, SpendCalls);
            if (gateAt < 0 || spendAt < 0) continue;

            compared++;
            if (gateAt > spendAt) late.Add($"{e.Where}   ({e.File})");
        }

        Assert.True(compared > 0, "أَداة عَمياء: لَم يُقارَن مَدخَلٌ واحِد.");
        Assert.True(late.Count == 0,
            "بَوّابَةُ حِصَّةٍ تَقَعُ بَعدَ الإنفاق — فَلا تَمنَعُه:\n  " + string.Join("\n  ", late));
    }

    /// <summary>
    /// <para><b>ونِصفُه الآخَر</b>: إدخالَةٌ مُثَبَّتَةٌ صارَت
    /// مَحروسَةً — أَو لَم يَعُد مَوضِعُها يُنفِق — تَحمَرُّ حَتّى
    /// تُرفَع. فَالقائِمَةُ دَينٌ مَوصوفٌ لا قائِمَةُ إسكات.</para>
    /// </summary>
    [Fact]
    public void No_pinned_ungated_spender_outlives_its_reason()
    {
        var entries = LlmEntries().ToList();
        var all = entries.Select(e => e.Where).ToHashSet(StringComparer.Ordinal);
        var gated = entries.Where(e => HasQuotaGate(e.Body) || HasOwnerAuthority(e.Body))
                           .Select(e => e.Where).ToHashSet(StringComparer.Ordinal);

        var gone = PinnedUngated.Where(p => !all.Contains(p.Where)).Select(p => p.Where).ToArray();
        var covered = PinnedUngated.Where(p => gated.Contains(p.Where)).Select(p => p.Where).ToArray();

        Assert.True(gone.Length == 0,
            "مَوضِعٌ مُثَبَّتٌ لَم يَعُد يُنفِقُ نِداءً — يُرفَع:\n  " + string.Join("\n  ", gone));
        Assert.True(covered.Length == 0,
            "مَوضِعٌ مُثَبَّتٌ صارَ يُعلِنُ بَوّابَتَه — يُرفَع مِنَ القائِمَة:\n  "
            + string.Join("\n  ", covered));

        foreach (var p in PinnedUngated)
            Assert.True(p.WhyAr.Length > 60, $"استِثناءٌ بِلا سَبَبٍ مَقروء: {p.Where}");

        Assert.Equal(PinnedUngated.Length,
            PinnedUngated.Select(p => p.Where).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// <para><b>الأَداةُ تُقاسُ قَبلَ أَن يُوثَقَ بِها</b> (القاعِدَة
    /// ١٠). و«صِفرُ مُخالَفَة» مِن ماسِحٍ لا يَرى شَيئاً لا يُمَيَّزُ
    /// عَن «صِفرُ مُخالَفَة» مِن ماسِحٍ يَرى كُلَّ شَيء — فَيُحقَنُ
    /// عَيبٌ مُصطَنَعٌ ويُشتَرَطُ أَن يُمسَك.</para>
    ///
    /// <para><b>والحَقنُ يَقَعُ عَلى المُصَنِّفِ نَفسِه</b> الَّذي
    /// يَحكُمُ عَلى المَداخِلِ الحَقيقِيَّة — لا عَلى نُسخَةٍ مِنه —
    /// فَلا يُقاسُ شَيءٌ غَيرُ الَّذي يُوثَقُ بِه.</para>
    /// </summary>
    [Fact]
    public void The_scanner_catches_an_injected_ungated_spender()
    {
        // ‏(أ) نُقطَةٌ مَحقونَةٌ تُطلِقُ التَحليلَ بِلا بَوّابَة —
        //     نَصُّ العَطَبِ الأَصليِّ حَرفاً.
        const string injectedUngated = """
            app.MapPost("/studio/s/{id:guid}/analyze-injected", async (
                Guid id, IServiceScopeFactory scopeFactory,
                Services.Incubator.StudioAuth auth,
                Services.Incubator.FeasibilityAnalysisService svc) =>
            {
                auth.Load();
                if (!auth.IsAuthenticated) return Results.Redirect("/studio/auth");
                _ = Task.Run(async () => { await svc.RunAnalysisAsync(id); });
                return Results.Redirect("/studio");
            }).DisableAntiforgery();
            """;

        Assert.True(MentionsLlm(injectedUngated),
            "الماسِحُ لا يَرى نُقطَةً تُطلِقُ التَحليل — عَمىً عَنِ المَوضوعِ نَفسِه.");
        Assert.False(HasQuotaGate(injectedUngated) || HasOwnerAuthority(injectedUngated),
            "المُصَنِّفُ عَدَّ نُقطَةً بِلا بَوّابَةٍ مَحروسَةً — فَهُوَ يُمَرِّرُ العَطَبَ الأَصليّ.");

        // ‏(ب) ونَظيرَتُها مَحروسَةً تَمُرّ — فَالماسِحُ لا يَتَّهِمُ
        //     كُلَّ شَيءٍ لِيَبدُوَ يَقِظاً.
        const string injectedGated = """
            app.MapPost("/studio/s/{id:guid}/analyze-injected", async (
                Guid id, IServiceScopeFactory scopeFactory,
                Services.Incubator.StudioAuth auth,
                Services.Incubator.StudioTierService tier,
                Services.Incubator.FeasibilityAnalysisService svc) =>
            {
                auth.Load();
                var gate = await tier.CheckAnalyzeAsync(auth.UserId!.Value);
                if (!gate.Allowed) return Results.Redirect("/studio");
                await tier.RecordAnalysisAsync(auth.UserId!.Value);
                _ = Task.Run(async () => { await svc.RunAnalysisAsync(id); });
                return Results.Redirect("/studio");
            }).DisableAntiforgery();
            """;

        Assert.True(HasQuotaGate(injectedGated),
            "المُصَنِّفُ لا يَرى بَوّابَةً مُعلَنَة — فَيَتَّهِمُ المَحروسَ.");
        Assert.True(FirstIndexOfAny(injectedGated, QuotaGates)
                    < FirstIndexOfAny(injectedGated, SpendCalls),
            "فَحصُ التَرتيبِ لا يَرى أَنّ البَوّابَةَ سَبَقَت.");

        // ‏(ج) وبَوّابَةٌ بَعدَ الإطلاقِ تُمسَكُ بِفَحصِ التَرتيب.
        const string injectedLateGate = """
            app.MapPost("/studio/s/{id:guid}/analyze-injected", async (
                Guid id, Services.Incubator.StudioTierService tier,
                Services.Incubator.FeasibilityAnalysisService svc) =>
            {
                _ = Task.Run(async () => { await svc.RunAnalysisAsync(id); });
                var gate = await tier.CheckAnalyzeAsync(Guid.Empty);
                return Results.Redirect("/studio");
            }).DisableAntiforgery();
            """;

        Assert.True(FirstIndexOfAny(injectedLateGate, QuotaGates)
                    > FirstIndexOfAny(injectedLateGate, SpendCalls),
            "بَوّابَةٌ بَعدَ الإنفاقِ عَبَرَت فَحصَ التَرتيب.");
    }

    // ─── ٤-ب. الحَجزُ والتَشغيلُ لا يَفتَرِقان ────────────────────────

    /// <summary>
    /// <para><b>إعادَةُ تَحليلٍ عَلى جَلسَةٍ قَيدَ التَشغيلِ لا تُنفِقُ
    /// نِداءً.</b> والبَوّابَةُ وَحدَها لا تَكفي: خَمسونَ طَلَباً
    /// مُتَوازِياً عَلى <b>نَفسِ المُعَرِّف</b> تَعبُرُ فَحصاً
    /// يَقرَأُ ثُمَّ يُطلِق — فَتَصيرُ خَمسينَ تَحليلاً (‏<b>مِئَةَ</b>
    /// نِداءٍ، لِأَنّ <c>RunAnalysisAsync</c> يُحاوِلُ مَرَّتَين).</para>
    ///
    /// <para><b>فَالمَقيسُ هُنا هُوَ الشَكل</b>: النُقطَةُ
    /// <b>تَحجُزُ</b> الجَلسَةَ بِعَمَلِيَّةٍ ذَرِّيَّةٍ واحِدَةٍ
    /// (‏<c>TryClaimAnalysisAsync</c>) وتَفحَصُ جَوابَها <b>قَبلَ</b>
    /// أَن تُطلِقَ شَيئاً — لا <c>MarkAnalyzingAsync</c> ثُمَّ
    /// <c>Task.Run</c>، فَذاكَ فَحصٌ ثُمَّ إطلاقٌ بِنافِذَةٍ
    /// بَينَهُما.</para>
    /// </summary>
    [Fact]
    public void A_repeat_on_a_running_session_spends_no_call()
    {
        var analyze = LlmEntries()
            .Where(e => e.Where.EndsWith("/analyze", StringComparison.Ordinal))
            .ToList();

        Assert.True(analyze.Count >= 2,
            $"أَداة عَمياء: وُجِدَت {analyze.Count} نُقطَةَ تَحليل — والمَقيسُ اثنَتان.");

        var breaches = new List<string>();
        foreach (var e in analyze)
        {
            var claimAt = e.Body.IndexOf(ClaimCall, StringComparison.Ordinal);
            var spendAt = FirstIndexOfAny(e.Body, SpendCalls);

            if (claimAt < 0)
            {
                breaches.Add($"{e.Where}: بِلا `{ClaimCall}` — فَحصٌ ثُمَّ إطلاقٌ بِنافِذَةِ سِباق.");
                continue;
            }
            if (spendAt >= 0 && claimAt > spendAt)
                breaches.Add($"{e.Where}: الحَجزُ بَعدَ الإطلاق — فَلا يَمنَعُ تَوازِياً.");
        }

        Assert.True(breaches.Count == 0,
            "نُقطَةُ تَحليلٍ تُطلِقُ بِلا حَجزٍ ذَرِّيّ:\n  " + string.Join("\n  ", breaches) +
            "\nالحَجزُ والتَشغيلُ لا يَفتَرِقان — نَفسُ نَمَطِ «مَرَّةٌ واحِدَةٌ في نَفسِ المُعامَلَة» في مَسارِ المال.");
    }

    /// <summary>
    /// <para><b>والحَجزُ نَفسُه مُعامَلَةٌ واحِدَة</b> — إدخالُ مِفتاحِ
    /// الفَرادَةِ وقَلبُ الحالَةِ يَقَعانِ في
    /// <c>SaveChangesAsync</c> <b>واحِدَة</b>. فَحَجزٌ يُحفَظُ عَلى
    /// دَفعَتَينِ يَترُكُ النافِذَةَ الَّتي فُتِحَ لِيُغلِقَها.</para>
    ///
    /// <para><b>ومِفتاحُ الفَرادَةِ هُوَ الحَكَم</b> — لا فَحصُ
    /// «أَمَوجودٌ؟» قَبلَ الإدخال: ذاكَ سِباقٌ آخَر. فَيُشتَرَطُ
    /// <c>Insert(</c> لا <c>Store(</c> لِلمِفتاح، لِأَنّ
    /// <c>Store</c> يَكتُبُ فَوقَ المَوجودِ فَلا يَصطَدِمُ بِأَحَد.</para>
    /// </summary>
    [Fact]
    public void The_claim_is_one_transaction_keyed_by_a_unique_id()
    {
        var body = MethodBodyIn(
            "libs/templates/ACommerce.Templates.Customer.Marketplace/Services/Incubator/FeasibilityAnalysisService.cs",
            "TryClaimAnalysisAsync");

        Assert.False(string.IsNullOrEmpty(body),
            $"أَداة عَمياء: لا وُجودَ لِـ`{ClaimCall}` في خِدمَةِ التَحليل.");

        var saves = Regex.Matches(body, @"\bSaveChangesAsync\s*\(").Count;
        Assert.True(saves == 1,
            $"الحَجزُ يُحفَظُ في {saves} مُعامَلَة — والذَرِّيَّةُ تَلزَمُها واحِدَة.");

        Assert.Contains(".Insert(", body, StringComparison.Ordinal);

        var insertAt = body.IndexOf(".Insert(", StringComparison.Ordinal);
        var saveAt = body.IndexOf("SaveChangesAsync", StringComparison.Ordinal);
        Assert.True(insertAt < saveAt,
            "مِفتاحُ الفَرادَةِ يُدخَلُ بَعدَ الحِفظ — فَلا يَصطَدِمُ بِسِباق.");
    }

    private const string ClaimCall = "TryClaimAnalysisAsync";

    /// <summary>جِسمُ دالَّةٍ بِاسمِها في مِلَفٍّ بِمَسارِه — بِلا
    /// تَعليقات، فَذِكرُ الاسمِ في شَرحٍ لَيسَ تَنفيذاً.</summary>
    private static string MethodBodyIn(string relPath, string methodName)
    {
        foreach (var (file, text) in EntitlementContractTests.SourceFiles())
        {
            if (Rel(file) != relPath) continue;
            var code = WriteEndpointGuardTests.StripComments(text);
            var m = Regex.Match(code,
                @"(?:public|private|internal)[^\n;{}]*\b" + Regex.Escape(methodName) + @"\s*\(");
            return m.Success ? BlockFrom(code, m.Index) : "";
        }
        return "";
    }

    // ─── ٥. الجَردُ المُثَبَّت ────────────────────────────────────────

    /// <summary>مَدخَلٌ يَبلُغُ نَموذَجَ لُغَةٍ — وحُكمُه.</summary>
    internal sealed record Verdict(string Where, string Gate);

    /// <summary>
    /// <para><b>الجَردُ الكامِل — تِسعَةُ مَداخِلَ لا عاشِرَ، وحُكمُ
    /// كُلِّ واحِدٍ مَكتوب.</b> وهذا هُوَ ما لَم يَكُن مَوجوداً:
    /// المَوجَةُ السابِقَةُ عَدَّتِ الأَرقامَ في الكاتالوجِ ولَم تَعُدَّ
    /// الأَبوابَ الَّتي تُنفِقُ مِنه، فَعَبَرَت نُقطَةٌ مَفتوحَةٌ مِن
    /// ‏1967 اختِباراً أَخضَرَ.</para>
    ///
    /// <para><b>وثُنائِيُّ الاتِّجاه</b>: مَدخَلٌ جَديدٌ لا يُذكَر هُنا
    /// يُحمِرّ، ومَذكورٌ زالَ يُحمِرّ، وحُكمٌ تَبَدَّلَ يُحمِرّ. فَلا
    /// يَنمو سَطحُ الإنفاقِ صامِتاً.</para>
    /// </summary>
    private static readonly Verdict[] PinnedInventory =
    {
        // ─── سُلطَةُ المالِك: مِفتاحُه يُنفِقُه بِنَفسِه ──────────────
        new("/admin/agent/ask",                   "PlatformAdminGuard"),
        new("/admin/agent/tool/{toolId}/apply",   "PlatformAdminGuard"),
        new("/admin/agent/tool/{toolId}/reject",  "PlatformAdminGuard"),
        new("/admin/incubator/{id:guid}/analyze", "PlatformAdminGuard"),
        new("libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages/Admin/AgentChatPanel.razor",
            "RequirePlatformAdmin"),

        // ─── بَوّابَةُ حِصَّة: عَميلٌ يُنفِقُ مِن مِفتاحِ المالِك ─────
        new("/studio/s/{id:guid}/refine",  "CheckRefineAsync"),
        new("ResumeStudioPromptAsync()",   "CheckAnalyzeAsync"),
        new("/studio/s/{id:guid}/analyze", "CheckAnalyzeAsync"),

        // ─── مَفتوحٌ ومُعلَن — انظُر PinnedUngated ────────────────────
        new("libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages/StudioAgent.razor",
            "(مَفتوح)"),
    };

    /// <summary><b>الجَردُ يُطابِقُ الواقِعَ حَرفاً</b> — لا مَدخَلَ
    /// زائِداً ولا ناقِصاً ولا حُكماً مُنجَرِفاً.</summary>
    [Fact]
    public void The_language_model_entry_inventory_matches_the_source()
    {
        var actual = LlmEntries()
            .ToDictionary(e => e.Where, e => GateOf(e.Body), StringComparer.Ordinal);

        Assert.True(actual.Count >= 9,
            $"أَداة عَمياء: وُجِدَ {actual.Count} مَدخَلاً — والمَقيسُ ٩ فَأَكثَر.");

        var expected = PinnedInventory.ToDictionary(v => v.Where, v => v.Gate, StringComparer.Ordinal);

        var added = actual.Keys.Except(expected.Keys, StringComparer.Ordinal).ToArray();
        var gone = expected.Keys.Except(actual.Keys, StringComparer.Ordinal).ToArray();
        var drifted = actual.Where(kv => expected.TryGetValue(kv.Key, out var g) && g != kv.Value)
                            .Select(kv => $"{kv.Key}: {expected[kv.Key]} ← {kv.Value}").ToArray();

        Assert.True(added.Length == 0,
            "بابُ إنفاقٍ جَديدٌ غَيرُ مَجرود — يُضافُ بِحُكمِه في نَفسِ الكوميت:\n  "
            + string.Join("\n  ", added));
        Assert.True(gone.Length == 0,
            "مَدخَلٌ مَجرودٌ لَم يَعُد يُنفِق — يُرفَع:\n  " + string.Join("\n  ", gone));
        Assert.True(drifted.Length == 0,
            "حُكمُ مَدخَلٍ تَبَدَّلَ ولَم يُحَدَّثِ الجَرد:\n  " + string.Join("\n  ", drifted));
    }

    /// <summary>حُكمُ جِسمٍ واحِد — أَوَّلُ رَمزِ بَوّابَةٍ يُطابِقُه،
    /// ‏<c>(مَفتوح)</c> إن لَم يُطابِقه شَيء.</summary>
    private static string GateOf(string body)
    {
        foreach (var g in QuotaGates) if (body.Contains(g, StringComparison.Ordinal)) return g;
        if (body.Contains("PlatformAdminGuard.EvaluateAsync", StringComparison.Ordinal))
            return "PlatformAdminGuard";
        if (body.Contains("RequirePlatformAdmin", StringComparison.Ordinal))
            return "RequirePlatformAdmin";
        return "(مَفتوح)";
    }

    // ─── ٦. الأَدَوات ─────────────────────────────────────────────────

    private static bool HasQuotaGate(string body) => ContainsAny(body, QuotaGates);
    private static bool HasOwnerAuthority(string body) => ContainsAny(body, OwnerAuthorityGates);
    private static bool MentionsLlm(string body) => ContainsAny(body, LlmReachingMethods);

    private static bool ContainsAny(string s, IEnumerable<string> needles) =>
        needles.Any(n => s.Contains(n, StringComparison.Ordinal));

    private static int FirstIndexOfAny(string s, IEnumerable<string> needles)
    {
        var best = -1;
        foreach (var n in needles)
        {
            var i = s.IndexOf(n, StringComparison.Ordinal);
            if (i >= 0 && (best < 0 || i < best)) best = i;
        }
        return best;
    }

    /// <summary>
    /// <para><b>كُلُّ مَدخَلٍ يَبلُغُ نَموذَجَ لُغَة</b> — صِنفانِ لا
    /// ثالِثَ لَهُما، وكِلاهُما وَقَعَ فِعلاً:</para>
    ///
    /// <list type="number">
    ///   <item><b>نُقطَةُ <c>Map*</c></b> تُنادي إحدى دالَّاتِ
    ///   الإنفاق — وهذا شَكلُ <c>/studio/s/{id}/analyze</c>.</item>
    ///   <item><b>صَفحَةُ <c>.razor</c> تَفاعُلِيَّة</b> تُنادي إحداها
    ///   مِن داخِلِ الـcircuit — وهذا شَكلُ <c>/studio/agent</c>، ولا
    ///   تَبلُغُها حِراسَةُ نِقاطِ الـ<c>POST</c> إطلاقاً لِأَنَّها
    ///   لا تَمُرُّ بِنُقطَة.</item>
    /// </list>
    ///
    /// <para><b>ولِماذا الصِنفُ الثاني ليس تَرَفاً</b>: ماسِحٌ يَقرَأُ
    /// <c>Map*</c> وَحدَها كانَ سَيُعطي «صِفرَ مُخالَفَة» بَعدَ
    /// إصلاحِ النُقطَة — <b>وصَفحَةُ الوَكيلِ مَفتوحَة</b>.</para>
    /// </summary>
    internal static IEnumerable<LlmEntry> LlmEntries()
    {
        foreach (var (file, text) in EntitlementContractTests.SourceFiles())
        {
            var rel = Rel(file);

            // مِلَفّاتُ الطَبَقَةِ نَفسِها — تُعَرِّفُ النِداءَ ولا
            // تَكونُ مَدخَلاً لَه.
            if (rel.EndsWith("/AgentService.cs", StringComparison.Ordinal) ||
                rel.EndsWith("/FeasibilityAnalysisService.cs", StringComparison.Ordinal))
                continue;

            var code = WriteEndpointGuardTests.StripComments(text);

            if (rel.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
            {
                var pageCode = StripMarkupComments(code);
                if (!MentionsLlm(pageCode)) continue;
                // الصَفحَةُ كُلُّها جِسمٌ واحِد: الحُكمُ يَقَعُ في
                // مُضيفِها أَو في جِسمِها، وكِلاهُما هُنا.
                yield return new LlmEntry(rel, pageCode + HostMarkupOf(rel), rel);
                continue;
            }

            foreach (Match m in MapAny.Matches(code))
            {
                var body = StatementFrom(code, m.Index);
                if (!MentionsLlm(body)) continue;
                yield return new LlmEntry(m.Groups["route"].Value, body, rel);
            }

            // ودالَّةٌ مُساعِدَةٌ تُطلِقُ النِداءَ خارِجَ جِسمِ نُقطَةٍ
            // هِيَ مَدخَلٌ أَيضاً — وهذا شَكلُ
            // ‏`ResumeStudioPromptAsync`: الجِسمُ استُخرِجَ مِنَ
            // النُقطَةِ فَلَو قُرِئَت النِقاطُ وَحدَها لاختَفى.
            foreach (var (name, body) in HelperMethodsThatSpend(code))
                yield return new LlmEntry(name, body, rel);
        }
    }

    /// <summary>مُضيفُ صَفحَةٍ تَفاعُلِيَّة — الحُكمُ قَد يَقَعُ فيه لا
    /// في الجَزيرَة (‏<c>AgentChat.razor</c> يَلُفُّ
    /// <c>AgentChatPanel</c> بِـ<c>RequirePlatformAdmin</c>). ولَولا
    /// هذا لَاتُّهِمَت الجَزيرَةُ المَحروسَةُ ظُلماً.</summary>
    private static string HostMarkupOf(string rel)
    {
        var name = Path.GetFileNameWithoutExtension(rel);
        var sb = new System.Text.StringBuilder();
        foreach (var (file, text) in EntitlementContractTests.SourceFiles())
        {
            if (!file.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)) continue;
            if (Rel(file) == rel) continue;
            if (!Regex.IsMatch(text, @"<\s*" + Regex.Escape(name) + @"[\s/>]")) continue;
            sb.Append(text);
        }
        return sb.ToString();
    }

    /// <summary>دالَّاتُ <c>private static</c> في مِلَفِّ نِقاطٍ
    /// تُطلِقُ نِداءً — تُقرَأُ بِأَجسامِها، فَلا يَختَفي مَدخَلٌ
    /// لِأَنّ جِسمَه استُخرِجَ مِن نُقطَةٍ إلى دالَّة.</summary>
    private static IEnumerable<(string Name, string Body)> HelperMethodsThatSpend(string code)
    {
        foreach (Match m in HelperMethod.Matches(code))
        {
            var body = BlockFrom(code, m.Index);
            if (!MentionsLlm(body)) continue;
            yield return (m.Groups["name"].Value + "()", body);
        }
    }

    private static readonly Regex HelperMethod =
        new(@"private\s+static\s+async\s+Task<[^>]*>\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(",
            RegexOptions.Compiled);

    private static readonly Regex MapAny =
        new(@"\.Map(?:Get|Post|Put|Delete|Patch)\s*\(\s*""(?<route>[^""]+)""", RegexOptions.Compiled);

    /// <summary>مِن مَوضِعِ البِدايَةِ حَتّى <c>;</c> الَّتي تُغلِقُ
    /// العِبارَة — نَفسُ عَقدِ <c>WriteEndpointGuardTests</c>.</summary>
    private static string StatementFrom(string code, int start)
    {
        int depth = 0, i = start;
        for (; i < code.Length; i++)
        {
            var c = code[i];
            if (c is '"' or '\'') { i = SkipLiteral(code, i); continue; }
            if (c is '(' or '{' or '[') depth++;
            else if (c is ')' or '}' or ']') depth--;
            else if (c == ';' && depth == 0) break;
        }
        return code[start..Math.Min(i + 1, code.Length)];
    }

    /// <summary>مِن تَوقيعِ الدالَّةِ حَتّى إغلاقِ كُتلَتِها.</summary>
    private static string BlockFrom(string code, int start)
    {
        var open = code.IndexOf('{', start);
        if (open < 0) return "";
        int depth = 0, i = open;
        for (; i < code.Length; i++)
        {
            var c = code[i];
            if (c is '"' or '\'') { i = SkipLiteral(code, i); continue; }
            if (c == '{') depth++;
            else if (c == '}') { depth--; if (depth == 0) break; }
        }
        return code[start..Math.Min(i + 1, code.Length)];
    }

    private static int SkipLiteral(string code, int i)
    {
        var quote = code[i];
        var verbatim = i > 0 && code[i - 1] == '@';
        for (var j = i + 1; j < code.Length; j++)
        {
            if (!verbatim && code[j] == '\\') { j++; continue; }
            if (code[j] != quote) continue;
            if (verbatim && j + 1 < code.Length && code[j + 1] == quote) { j++; continue; }
            return j;
        }
        return code.Length - 1;
    }

    private static string StripMarkupComments(string s) =>
        Regex.Replace(s, @"@\*.*?\*@", " ", RegexOptions.Singleline);

    private static string Rel(string path) =>
        Path.GetRelativePath(ThemeZeroEquivalenceTests.RepoRoot, path).Replace('\\', '/');
}
