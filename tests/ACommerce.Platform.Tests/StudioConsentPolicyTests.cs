using System.Text.RegularExpressions;
using ACommerce.Kit.Subscriptions;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ البَندُ الرابِعُ مِن مُوافَقَةِ الاستوديو كانَ يَقولُ «لا يَتِم تَحصيل» — والمَنصَّةُ تُحَصِّل ═══
//
// **المَقيسُ** (موجَزُ الفَجَوات P2-1، ‏2026-10-10): `studio.consent.item4_body` كانَ
// «الباقات لَها حُدود شَهريَّة. تُعاد كُلّ ٣٠ يَوم. الدَّفع مُسَجَّل كَ نِيَّة فَقَط في هذه
// المَرحَلَة (لا يَتِم تَحصيل).» — وهُوَ النَصُّ الوَحيدُ الَّذي يَقبَلُه رائِدُ الأَعمالِ
// **بِالنَقر** قَبلَ أَوَّلِ تَحليل. بَينَما مَسارُ التَحصيلِ قائِمٌ كوداً: `TenantPlanAdmin`
// يَضبُطُ باقَةَ المَتجَرِ بِسِعرٍ ومُدَّةٍ ومُهلَة، و`TenantPlan.Derive` يَشتَقُّ
// السَرَيانَ فَالسَماحَ فَالتَعليق، و`store_locked` يَمنَعُ الكِتابَةَ، وشُروطُ المَنصَّةِ
// المَنشورَةُ تَقولُ «تَعليقُ الخِدمَةِ … عِندَ عَدَمِ السَداد» (`platform.terms.s9_body`)
// و«يُخفى حَتّى التَجديد» (`s4_body`). فَالنَصُّ المَنقورُ يَنقُضُ «وَسايِل تُحَصِّلُ
// اشتِراكَها» ويُعطي المُستَأجِرَ حُجَّةً عِندَ أَوَّلِ فاتورَة.
//
// **القَرارُ المُنَفَّذُ تَحتَ التَفويضِ العامّ** (سِجِلُّ أَسئِلَةِ المالِك «أ»، والصِياغَةُ
// القانونِيَّةُ النِهائِيَّةُ تَنتَظِرُ نَصَّه في «ب»): البَندُ يَصِفُ ما يَفعَلُه الكودُ
// **بِلا رَقَمٍ مُختَرَع** (القاعِدَة ١٦) — الاشتِراكُ تَضبُطُه المَنصَّةُ لِكُلِّ مَتجَرٍ
// عَلى حِدَة (`platform.pricing.no_price` بِحَرفِه)، ويُعرَضُ في الاستوديو قَبلَ أَيِّ دَفع
// (لافِتَةُ `StudioHome` لِلمَتاجِرِ الخارِجَةِ عَن السَرَيانِ وَحدَها، ومَعَ كُلِّ رابِطِ
// دَفعٍ مَبلَغُه ومُدَّتُه)، وعِندَ الانتِهاءِ أَو عَدَمِ السَدادِ يَتَوَقَّفُ النَشرُ
// (`AllowsWrite(Grace) == false`) ثُمَّ يُخفى المَتجَرُ (`IsVisible(Suspended) == false`)
// وَفقَ شُروطِ المَنصَّة. والرَقَمُ الوَحيدُ الباقي «٣٠ يَوماً» مَقيسٌ في
// `StudioTierService.PeriodElapsed` (‏`StudioTierPeriodTests`). و`CurrentVersion` 1 → 2
// فَكُلُّ مَن قَبِلَ النَصَّ القَديمَ يُعادُ إلَيه النَصُّ الصادِقُ قَبلَ تَحليلِه التالي.
//
// **وشَرطُ القَبولِ صارَ مُسنَداً واحِداً** (`ConsentPolicy.AcceptedBy`) يَقرَؤُه بابُ الصَفحَةِ
// ونُقطَةُ الاستِئنافِ مَعاً (القاعِدَة ٨: كانا نُسخَتَينِ مُتَطابِقَتَينِ في مَوضِعَين) —
// وهُوَ ما يَجعَلُ «رَفعُ الإصدارِ يُجبِرُ القَبولَ» فَحصاً يُجَمَّعُ ويُقاسُ بِلا قاعِدَةِ
// بَيانات، لا دَعوى في تَعليق.

public class StudioConsentPolicyTests
{
    private const string Item4Key = "studio.consent.item4_body";

    /// <summary>النَصُّ القَديمُ بِحَرفِه (`ar.json:1784` حَتّى ‏2026-10-11) — لا يُعادُ
    /// تَوليدُه، يُقارَنُ بِه.</summary>
    private const string OldItem4Body =
        "الباقات لَها حُدود شَهريَّة. تُعاد كُلّ ٣٠ يَوم. الدَّفع مُسَجَّل كَ نِيَّة فَقَط في هذه المَرحَلَة (لا يَتِم تَحصيل).";

    private static string Ar(string key)
    {
        var v = LocaleCatalog.Find(LocaleCatalog.Arabic, key);
        Assert.False(string.IsNullOrWhiteSpace(v), $"«{key}» بِلا قيمَةٍ عَرَبِيَّة.");
        return v!;
    }

    // ─── النَصّ ─────────────────────────────────────────────────────────────

    /// <summary>
    /// <para><b>لَو عادَت عِبارَةُ «لا يَتِم تَحصيل» لَاحمَرَّ هذا الفَحصُ بِاسمِها</b> —
    /// وكَذا «نِيَّة فَقَط»، والنَصُّ القَديمُ بِحَرفِه. ثَلاثَةُ أَطرافٍ مُستَقِلَّة، فَتَعديلُ
    /// حَرفٍ في أَيٍّ مِنها لا يُفلِتُ مِن الآخَرَين.</para>
    /// </summary>
    [Fact]
    public void The_fourth_item_no_longer_says_payment_is_intent_only()
    {
        var body = Ar(Item4Key);
        Assert.NotEqual(OldItem4Body, body);
        Assert.DoesNotContain("لا يَتِم تَحصيل", body, StringComparison.Ordinal);
        Assert.DoesNotContain("نِيَّة فَقَط", body, StringComparison.Ordinal);

        // والنَصُّ القَديمُ لا يَعيشُ في أَيِّ مِفتاحٍ آخَرَ تَحتَ اسمٍ جَديد.
        var carriers = LocaleCatalog.Raw(LocaleCatalog.Arabic)
            .Where(e => e.Value.Contains("لا يَتِم تَحصيل", StringComparison.Ordinal))
            .Select(e => e.Key).ToArray();
        Assert.Empty(carriers);
    }

    /// <summary>
    /// <para><b>النَصُّ يَصِفُ ما يَفعَلُه الكودُ، ولا يَختَرِعُ رَقَماً</b>: الاشتِراكُ
    /// تَضبُطُه المَنصَّةُ لِكُلِّ مَتجَرٍ عَلى حِدَة ويُعرَضُ قَبلَ أَيِّ دَفع، والنَشرُ
    /// يَتَوَقَّفُ ثُمَّ يُخفى المَتجَرُ وَفقَ شُروطِ المَنصَّة — وكُلُّ جُملَةٍ مِنها لَها
    /// طَرَفٌ مَقيس: دالَّةٌ في <c>TenantPlan</c> أَو مِفتاحٌ مَنشورٌ في شُروطِ المَنصَّة.
    /// والرَقَمُ الوَحيدُ «٣٠» هُوَ دَوَرانُ حِصَّةِ الاستوديو المَقيس.</para>
    /// </summary>
    [Fact]
    public void The_fourth_item_describes_what_the_code_enforces_and_invents_no_number()
    {
        var body = Ar(Item4Key);

        Assert.Contains("تَضبُطُه المَنصَّةُ لِكُلِّ مَتجَرٍ عَلى حِدَة", body, StringComparison.Ordinal);
        Assert.Contains("قَبلَ أَيِّ دَفع", body, StringComparison.Ordinal);
        Assert.Contains("عَدَمِ السَداد", body, StringComparison.Ordinal);
        Assert.Contains("يَتَوَقَّفُ النَشرُ", body, StringComparison.Ordinal);
        Assert.Contains("يُخفى المَتجَرُ", body, StringComparison.Ordinal);
        Assert.Contains("شُروطِ المَنصَّة", body, StringComparison.Ordinal);

        // لا سِعرَ ولا عُملَةَ ولا نِسبَة — والرَقَمُ الوَحيدُ ٣٠.
        foreach (var forbidden in new[] { "ريال", "ر.س", "SAR", "$", "%", "مَجّان" })
            Assert.DoesNotContain(forbidden, body, StringComparison.Ordinal);
        var digits = Regex.Matches(body, "[0-9٠-٩]+").Select(m => m.Value).ToArray();
        Assert.Equal(new[] { "٣٠" }, digits);

        // الأَطرافُ المَقيسَة لِكُلِّ جُملَة:
        var now = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(StudioTierService.PeriodElapsed(now.AddDays(-30), now));      // «تُعادُ كُلَّ ٣٠ يَوماً»
        Assert.False(StudioTierService.PeriodElapsed(now.AddDays(-29), now));
        Assert.False(TenantPlanPolicy.AllowsWrite(TenantPlanState.Grace));         // «يَتَوَقَّفُ النَشرُ»
        Assert.True(TenantPlanPolicy.IsVisible(TenantPlanState.Grace));            // … ويَبقى ظاهِراً في السَماح
        Assert.False(TenantPlanPolicy.IsVisible(TenantPlanState.Suspended));       // «ثُمَّ يُخفى المَتجَرُ»
        Assert.Contains("عَدَمِ السَداد", Ar("platform.terms.s9_body"), StringComparison.Ordinal);
        Assert.Contains("يُخفى حَتّى التَجديد", Ar("platform.terms.s4_body"), StringComparison.Ordinal);
        Assert.Contains("يُحَدَّدُ لِكُلِّ مَتجَرٍ عَلى حِدَة", Ar("platform.pricing.no_price"), StringComparison.Ordinal);
    }

    // ─── الإصدارُ وشَرطُ القَبول ─────────────────────────────────────────────

    /// <summary>
    /// <para><b>رَفعُ الإصدارِ يُجبِرُ القَبولَ مَرَّةً أُخرى — مَقيساً بِالمُسنَدِ نَفسِه
    /// الَّذي يُتَرجِمُه Marten</b>: سِجِلُّ مَن قَبِلَ النَصَّ القَديمَ (الإصدار 1) لا يُرضي
    /// شَرطَ الإصدارِ الحاليّ، وسِجِلُّ الإصدارِ الحاليِّ يُرضيه، وسِجِلُّ غَيرِه لا،
    /// وإصدارٌ أَعلى لا — مُساواةٌ لا <c>&gt;=</c>، وهذا بِعَينِه ما يُعيدُ صاحِبَ
    /// الاستوديو إلى الصَفحَةِ قَبلَ تَحليلِه التالي.</para>
    /// </summary>
    [Fact]
    public void The_version_was_bumped_so_everyone_who_accepted_the_old_text_re_accepts()
    {
        Assert.Equal(2, ConsentPolicy.CurrentVersion);

        var me = Guid.NewGuid();
        var acceptedCurrent = ConsentPolicy.AcceptedBy(me, ConsentPolicy.CurrentVersion).Compile();

        Assert.False(acceptedCurrent(new ConsentRecord { UserId = me, Version = 1 }));            // قَبِلَ «لا يَتِم تَحصيل»
        Assert.True (acceptedCurrent(new ConsentRecord { UserId = me, Version = 2 }));
        Assert.False(acceptedCurrent(new ConsentRecord { UserId = Guid.NewGuid(), Version = 2 }));
        Assert.False(acceptedCurrent(new ConsentRecord { UserId = me, Version = 3 }));
    }

    /// <summary>
    /// <para><b>القارِئانِ يَسأَلانِ المُسنَدَ الواحِدَ، ولا نُسخَةَ مُضَمَّنَةً بَقِيَت</b>
    /// (القاعِدَة ٨): بابُ الصَفحَةِ في <c>StudioQueries</c> ونُقطَةُ الاستِئنافِ في
    /// <c>MarketplaceTemplateExtensions</c> يُمَرِّرانِ <c>ConsentPolicy.AcceptedBy</c> إلى
    /// <c>Where</c>، والمُسنَدُ مُعَرَّفٌ مَرَّةً واحِدَة — فَما يُقاسُ أَعلاهُ هُوَ ما
    /// يُنَفَّذُ عَلى القاعِدَة.</para>
    /// </summary>
    [Fact]
    public void Both_consent_readers_pass_the_single_predicate_and_no_inline_copy_remains()
    {
        var queries = StudioConsentCharacterizationTests.Queries();
        Assert.Contains(".Where(ConsentPolicy.AcceptedBy(userId, version))", queries, StringComparison.Ordinal);

        var endpoints = StudioConsentCharacterizationTests.Endpoints();
        Assert.Contains("Services.Incubator.ConsentPolicy.AcceptedBy(", endpoints, StringComparison.Ordinal);

        var inline = new Regex(@"c\.Version\s*==", RegexOptions.Compiled);
        Assert.False(inline.IsMatch(queries), "نُسخَةٌ مُضَمَّنَةٌ مِن شَرطِ الإصدارِ في StudioQueries.");
        Assert.False(inline.IsMatch(endpoints), "نُسخَةٌ مُضَمَّنَةٌ مِن شَرطِ الإصدارِ في نُقطَةِ الاستِئناف.");

        Assert.Equal(1, Regex.Matches(StudioConsentCharacterizationTests.Policy(),
            Regex.Escape("AcceptedBy(Guid userId, int version)")).Count);
    }

    // ─── اللَقطَةُ حُدِّثَت مُعلَناً ─────────────────────────────────────────

    /// <summary><b>ولَقطَةُ الأَساسِ لَحِقَت النَصَّ والإصدارَ مَعاً</b> — رَبطُها
    /// بِالقامُوسِ والثابِتِ في <c>StudioConsentCharacterizationTests</c>، وهُنا الطَرَفُ
    /// السالِب: لا أَثَرَ لِلنَصِّ القَديمِ ولا لِـ«إصدار 1» فيها.</summary>
    [Fact]
    public void The_baseline_no_longer_carries_the_old_text_or_the_old_version()
    {
        var baseline = StudioConsentCharacterizationTests.Baseline();
        Assert.DoesNotContain("لا يَتِم تَحصيل", baseline, StringComparison.Ordinal);
        Assert.DoesNotContain(">إصدار 1<", baseline, StringComparison.Ordinal);
        Assert.Contains($">إصدار {ConsentPolicy.CurrentVersion}<", baseline, StringComparison.Ordinal);
    }
}
