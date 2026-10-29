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

    /// <summary>النَصُّ القَديمُ بِحَرفِه (قيمَةُ `studio.consent.item4_body` حَتّى ‏2026-10-11) — لا يُعادُ
    /// تَوليدُه، يُقارَنُ بِه. (‏2026-10-13: كانَ هُنا «`ar.json:1784`» والمِفتاحُ في ‏1785 يَومَ كُتِب، ثُمَّ انزاحَ إلى
    /// ‏1908 — فَيُسَمّى بِاسمِه لا بِسَطرِه.)</summary>
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
    /// <para><b>قُلِبَ بِاسمِه (‏2026-10-29، قَرارُ المالِك د18 «ج» — الصِياغَةُ مُسَوَّدَتُه بِحَرفِها)</b> — كانَ
    /// <c>The_fourth_item_describes_what_the_code_enforces_and_invents_no_number</c> عَلى نَصِّ الإصدارِ 2: «تَضبُطُه المَنصَّةُ لِكُلِّ مَتجَرٍ
    /// عَلى حِدَة … وَفقَ شُروطِ المَنصَّة»، والرَقَمُ الوَحيدُ «٣٠». <b>والآن</b> «الباقاتُ والاشتِراك»: كُلُّ جُملَةٍ لَها طَرَفٌ مَقيس — دالَّةٌ في
    /// <c>StudioTierService</c> أَو <c>TenantPlanPolicy</c>، أَو مِفتاحٌ مَنشورٌ في شُروطِ المَنصَّة — والأَرقامُ «٣٠» ورَقَما البَندَينِ اللَذَينِ يُحيلُ
    /// إلَيهِما، ويُقاسانِ بِعُنوانَيهِما.</para>
    /// </summary>
    [Fact]
    public void The_fourth_item_says_plans_and_subscription_as_the_code_and_the_terms_enforce_them()
    {
        Assert.Equal("٤. الباقاتُ والاشتِراك.", Ar("studio.consent.item4_title"));
        var body = Ar(Item4Key);

        foreach (var said in new[]
        {
            "التَحاليلُ والتَحسيناتُ تَتَجَدَّدُ كُلَّ ٣٠ يَوماً", "وعَدَدُ التَطبيقاتِ حَدٌّ ثابِت", "قَبلَ أَيِّ دَفع", "يَتَوَقَّفُ النَشرُ",
            "ويَبقى المَتجَرُ ظاهِراً خِلالَ مُهلَةِ السَماحِ", "ثُمَّ يُخفى حَتّى التَجديد", "تَعليقُه عِندَ عَدَمِ السَداد",
            "ولا يُحذَفُ شَيءٌ مِن بَياناتِه", "البَندُ ٤ (الباقاتُ والفَوتَرَةُ والتَجديد)", "البَندُ ٩ (التَعليقُ والإنهاء)", "وهي المَرجِعُ عِندَ الاختِلاف",
        })
            Assert.True(body.Contains(said, StringComparison.Ordinal), $"البَندُ الرابِعُ لا يَقولُ «{said}».");

        // لا سِعرَ ولا عُملَةَ ولا نِسبَة — والأَرقامُ ٣٠ ورَقَما البَندَين.
        foreach (var forbidden in new[] { "ريال", "ر.س", "SAR", "$", "%", "مَجّان" })
            Assert.DoesNotContain(forbidden, body, StringComparison.Ordinal);
        var digits = Regex.Matches(body, "[0-9٠-٩]+").Select(m => m.Value).ToArray();
        Assert.Equal(new[] { "٣٠", "٤", "٩" }, digits);

        // الأَطرافُ المَقيسَة لِكُلِّ جُملَة:
        var now = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(StudioTierService.PeriodElapsed(now.AddDays(-30), now));      // «تَتَجَدَّدُ كُلَّ ٣٠ يَوماً»
        Assert.False(StudioTierService.PeriodElapsed(now.AddDays(-29), now));
        var user = new StudioUser { PeriodStart = now.AddDays(-30), AnalysesUsed = 1, RefinesUsed = 2, StoresBuilt = 3 };
        Assert.True(StudioTierService.ApplyPeriodRollover(user, now));
        Assert.Equal((0, 0, 3), (user.AnalysesUsed, user.RefinesUsed, user.StoresBuilt));  // … و«عَدَدُ التَطبيقاتِ حَدٌّ ثابِت»
        Assert.False(TenantPlanPolicy.AllowsWrite(TenantPlanState.Grace));         // «يَتَوَقَّفُ النَشرُ»
        Assert.True(TenantPlanPolicy.IsVisible(TenantPlanState.Grace));            // «ويَبقى المَتجَرُ ظاهِراً خِلالَ مُهلَةِ السَماح»
        Assert.False(TenantPlanPolicy.IsVisible(TenantPlanState.Suspended));       // «ثُمَّ يُخفى حَتّى التَجديد»
        Assert.Contains("يُخفى حَتّى التَجديد", Ar("platform.terms.s4_body"), StringComparison.Ordinal);
        Assert.Contains("عَدَمِ السَداد", Ar("platform.terms.s9_body"), StringComparison.Ordinal);
        Assert.Contains("لا يُحذَفُ شَيءٌ مِن بَياناتِ المَتجَرِ", Ar("platform.terms.s4_note"), StringComparison.Ordinal);
        Assert.Equal("4. الباقاتُ والفَوتَرَةُ والتَجديد", Ar("platform.terms.s4_title"));
        Assert.Equal("9. التَعليقُ والإنهاء", Ar("platform.terms.s9_title"));
    }

    // ─── البَندُ الثالِث: لا «لا نُشارِكُها» والنَموذَجُ يُولِّدُ الدِراسَة ────────────────────────────

    /// <summary>النَصُّ القَديمُ بِحَرفِه (قيمَةُ <c>studio.consent.item3_body</c> حَتّى ‏2026-10-29) — يُقارَنُ بِه.</summary>
    private const string OldItem3Body = "نَحفَظ بَياناتِكَ (هاتِف، أَفكار، تَحاليل) لِاستِخدامِها في تَحسين الخِدمَة. لا نُشارِكها مَع أَطراف ثالِثَة.";

    /// <summary><b>البَندُ الثالِثُ كانَ يَنقُضُ الأَوَّل</b> (قَرارُ المالِك د20 «أ»، ‏2026-10-29): «لا نُشارِكها مَع أَطراف ثالِثَة» والبَندُ الأَوَّلُ
    /// يَقولُ إنّ نَموذَجَ ذَكاءٍ اصطِناعيٍّ يُولِّدُ الدِراسَة. والآن يُسَمّي مُزَوِّدَ النَموذَجِ ويُحيلُ إلى الجِهاتِ المُسَمّاةِ في سِياسَةِ الخُصوصِيَّة.</summary>
    [Fact]
    public void The_third_item_names_the_model_provider_and_the_privacy_policy_parties()
    {
        var body = Ar("studio.consent.item3_body");
        Assert.NotEqual(OldItem3Body, body);
        Assert.Contains("لا نَبيعُ بَياناتِك، ونُشارِكُها مَعَ مُزَوِّدِ نَموذَجِ اللُغَةِ لِتَوليدِ الدِراسَةِ ومَعَ الجِهاتِ المُسَمّاةِ في سِياسَةِ الخُصوصِيَّةِ فَقَط.",
            body, StringComparison.Ordinal);
        Assert.Contains("ذَكاء اصطِناعيّ", Ar("studio.consent.item1_body"), StringComparison.Ordinal);
        // والسِياسَةُ تُسَمّيه فِعلاً — السَطرُ الَّذي تُحيلُ إلَيه الموافَقَة.
        Assert.Contains("@doc.Markup(\"platform.privacy.share_llm\"", PrivacyShareInventoryTests.PrivacyPage(), StringComparison.Ordinal);
    }

    /// <summary>ولا بَندَ آخَرَ في مُوافَقَةِ الاستوديو يَحمِلُ الجُملَةَ القَديمَة. (‏شُروطُ المَتجَرِ <c>terms.privacy.body</c> تَقولُ «لا نُشارِكها مَع
    /// أَطراف ثالِثَة بِدون إذنِكَ الصَّريح» — نَصُّ المَتجَرِ لا الاستوديو، وخارِجَ د20؛ مَقيسٌ ومُبَلَّغٌ في تَقريرِ التَيّارِ لا مُبَدَّل.)</summary>
    [Fact]
    public void No_studio_consent_value_still_says_we_share_with_no_third_party()
    {
        var consent = LocaleCatalog.Raw(LocaleCatalog.Arabic)
            .Where(e => e.Key.StartsWith("studio.consent.", StringComparison.Ordinal)).ToList();
        Assert.True(consent.Count >= 10, $"أَداةٌ عَمياء: {consent.Count} مِفتاحاً لِلمُوافَقَة.");
        var carriers = consent
            .Where(e => AccountDeletionRetentionTests.Bare(e.Value).Contains("لا نشاركها مع أطراف ثالثة", StringComparison.Ordinal))
            .Select(e => e.Key).ToArray();
        Assert.Empty(carriers);
    }

    // ─── الإصدارُ وشَرطُ القَبول ─────────────────────────────────────────────

    /// <summary>
    /// <para><b>رَفعُ الإصدارِ يُجبِرُ القَبولَ مَرَّةً أُخرى — مَقيساً بِالمُسنَدِ نَفسِه
    /// الَّذي يُتَرجِمُه Marten</b>: سِجِلُّ مَن قَبِلَ النَصَّ القَديمَ (الإصدار 1) لا يُرضي
    /// شَرطَ الإصدارِ الحاليّ، وسِجِلُّ الإصدارِ الحاليِّ يُرضيه، وسِجِلُّ غَيرِه لا،
    /// وإصدارٌ أَعلى لا — مُساواةٌ لا <c>&gt;=</c>، وهذا بِعَينِه ما يُعيدُ صاحِبَ
    /// الاستوديو إلى الصَفحَةِ قَبلَ تَحليلِه التالي.</para>
    /// <para><b>‏(2026-10-29، قَرارا المالِك د20 ود18 — رَفعٌ واحِد) قُلِبَ بِاسمِه</b>: كانَ <c>The_version_was_bumped_so_everyone_who_accepted_the_old_text_re_accepts</c>
    /// عَلى الإصدارِ 2 (سِجِلُّ 2 يُرضي، و3 لا). والآن 3: مَن قَبِلَ «لا نُشارِكها مَع أَطراف ثالِثَة» و«الباقات.» القَديمَ (2) يُعادُ إلَيه النَصّ.</para>
    /// </summary>
    [Fact]
    public void The_version_was_bumped_to_three_so_everyone_who_accepted_version_two_re_accepts()
    {
        Assert.Equal(3, ConsentPolicy.CurrentVersion);

        var me = Guid.NewGuid();
        var acceptedCurrent = ConsentPolicy.AcceptedBy(me, ConsentPolicy.CurrentVersion).Compile();

        Assert.False(acceptedCurrent(new ConsentRecord { UserId = me, Version = 1 }));            // قَبِلَ «لا يَتِم تَحصيل»
        Assert.False(acceptedCurrent(new ConsentRecord { UserId = me, Version = 2 }));            // قَبِلَ «لا نُشارِكها مَع أَطراف ثالِثَة»
        Assert.True (acceptedCurrent(new ConsentRecord { UserId = me, Version = 3 }));
        Assert.False(acceptedCurrent(new ConsentRecord { UserId = Guid.NewGuid(), Version = 3 }));
        Assert.False(acceptedCurrent(new ConsentRecord { UserId = me, Version = 4 }));
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
