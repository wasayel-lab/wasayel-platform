using ACommerce.Kit.Payments;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ تَسريبُ مَسارِ المال — أَربَعَةُ عُيوبٍ، لِكُلٍّ اختِبارٌ سالِب ════
//
// **كُلُّ اختِبارٍ هُنا كُتِبَ أَحمَرَ قَبلَ حَرفٍ واحِدٍ مِن العِلاج**،
// واسمُه يَقول **الأَثَرَ الماليّ** لا اسمَ الدالَّة — فَمَن يَقرَأُ
// سَطرَ الفَشَلِ يَعرِف كَم يُكَلِّف قَبلَ أَن يَفتَحَ مِلَفّاً.
//
// ─── العُيوبُ الأَربَعَة، كَما قيسَت يَومَ ‏2026-08-30 ────────────────
//
// **١) `AddMockPayments()` سَطرٌ عارٍ في `Program.cs`** — بِلا شَرطِ
// بيئَة. و`MockPaymentProvider` يُرجِع `IsActive = true` **دائِماً**.
// وذلك **حَيٌّ في الإنتاج**: أُقلِعَ التَطبيقُ فِعلاً بِـ
// `ASPNETCORE_ENVIRONMENT=Production` فَرَدَّ ‏200 والمُحاكي مُسَجَّل.
//
// **٢) `/studio/billing/select` يَكتُب `u.Tier = tier` بَعدَ جَوابٍ
// ناجِحٍ دائِماً** — أَي تَرقِيَةٌ إلى `scale` (‏999 ريالاً) بِنَقرَةٍ
// وبِلا دَفع. والقَرارُ المُقابِلُ **مَوجودٌ في المُستَودَع مُنذُ
// ‏ADR-003**: ‏`PlanPurchasePolicy` تَرُدّ الباقَةَ بِسِعرٍ بِرَمزِ
// خَرقٍ مِن مَعجَمٍ مُغلَق. هذِه النُقطَةُ هي المَوضِعُ الوَحيدُ الَّذي
// لَم يُطَبَّق عَلَيه.
//
// **٣) `scale` تَفتَح `int.MaxValue` تَحليلاً في الشَهر** — أَي كُلفَةَ
// مِفتاحِ نَموذَجِ اللُغَةِ الخاصِّ بِالمالِك **بِلا سَقف**. فَالمُهاجِمُ
// لا يَسرِق اشتِراكاً فَحَسب، بَل يُنفِق رَصيدَ المالِك.
//
// **٤) `MockPaymentProvider.GetInvoiceAsync` تُلَفِّق رَقماً ضَريبيّاً**
// (`300000000000003`) ورابِطَ PDF إلى نُقطَةٍ مُعَلَّقٍ عَلَيها في الكودِ
// نَفسِه بِأَنَّها «مُستَقبَلِيَّة». ورَقمٌ ضَريبيٌّ مُلَفَّقٌ في وَثيقَةٍ
// تُعرَض على تاجِر **خَطَرٌ قانونيٌّ لا عَيبٌ تَجميليّ**. والجارَتانِ
// تَعرِفانِ الجَواب: ‏`NoonPaymentProvider` تُرجِع `null`،
// و`PayPalPaymentProvider` تُرجِع الرَقمَ الضَريبيَّ **فارِغاً** بِتَعليقٍ
// يَقول «لا يُخترَع رَقَمٌ على فاتورَة».
public class PaymentLeakTests
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static string Source(params string[] parts)
    {
        var path = Path.Combine(RepoRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياءُ بِلا طَرَفٍ مَقروء.");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 500, $"أَداة عَمياء: {path} طولُه {text.Length} مِحرَفاً — لَم يُقرَأ.");
        return text;
    }

    // ═══ ١) المُحاكي في الإنتاج — «اشتِراكٌ ناجِحٌ دائِماً» ═════════════

    /// <summary>سَطرُ تَسجيلٍ عارٍ عِندَ العَمودِ صِفر = خارِجَ أَيّ
    /// `switch` أَو `if` = المُحاكي هُوَ جَوابُ الإنتاج.</summary>
    [Fact]
    public void Production_must_not_register_the_always_succeeding_payment_provider()
    {
        var program = Source("apps", "V1.App", "Program.cs").Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.False(program.Contains("\nbuilder.Services.AddMockPayments();", StringComparison.Ordinal),
            "‏`AddMockPayments()` سَطرٌ عارٍ في `Program.cs` — فَمُزَوِّدٌ يَقول «نَجَحَ الدَفع» "
            + "دائِماً هُوَ مُزَوِّدُ الإنتاج. نَفسُ العَطَبِ الَّذي جَعَلَ رَمزَ الدُخولِ "
            + "‏`123456` ثابِتاً، ونَفسُ العِلاج: قَرارٌ بِالبيئَة وحارِسُ إقلاعٍ يَرمي.");
    }

    /// <summary>حارِسُ الإقلاعِ لِلدَفعِ — كَجارِه في قَنَواتِ
    /// الدُخول، ويَرمي **قَبلَ أَوَّلِ طَلَب** لا بَعدَ أَوَّلِ
    /// ضَحِيَّة.</summary>
    [Fact]
    public void Boot_must_refuse_to_start_when_a_payment_stub_is_registered_outside_development()
    {
        var program = Source("apps", "V1.App", "Program.cs");

        Assert.Contains("PaymentProviderSelection.AssertNoStubsOutsideDevelopment",
            program, StringComparison.Ordinal);
    }

    // ═══ ٢) التَرقِيَةُ الذاتِيَّةُ بِلا دَفع ═══════════════════════════

    /// <summary>
    /// <para><b>لا دَرَجَةَ بِسِعرٍ تُكتَب في وَثيقَةِ المُستَخدِمِ مِن
    /// نُقطَةِ الاختِيار.</b> والفَحصُ نَصِّيٌّ لِأَنّ العَطَبَ نَصِّيّ:
    /// جِسمُ النُقطَةِ كانَ يَكتُب `u.Tier = tier` بَعدَ نِداءٍ
    /// **ناجِحٍ دائِماً**.</para>
    /// </summary>
    [Fact]
    public void Selecting_a_priced_tier_must_not_upgrade_the_account_without_a_confirmed_payment()
    {
        var text = Source("libs", "templates", "ACommerce.Templates.Customer.Marketplace",
            "MarketplaceTemplateExtensions.cs");

        var start = text.IndexOf("/studio/billing/select", StringComparison.Ordinal);
        Assert.True(start > 0, "نُقطَةُ `/studio/billing/select` غَير مَوجودَة — الأَداةُ عَمياء.");
        var body = text.Substring(start, Math.Min(2400, text.Length - start));

        Assert.True(body.Contains("PlanPurchasePolicy", StringComparison.Ordinal),
            "نُقطَةُ اختِيارِ الدَرَجَةِ لا تَمُرّ بِـ`PlanPurchasePolicy` — وهي القَرارُ "
            + "القائِمُ في المُستودَع مُنذُ ‏ADR-003 لِنَفسِ السُؤالِ حَرفاً: «أَتُمنَح "
            + "باقَةٌ بِسِعرٍ ذاتِيّاً؟». أُنبوبٌ ثانٍ لِنَفسِ القَرارِ يَنجَرِف (القاعِدَة ٨).");

        Assert.False(body.Contains("payments.CreateSubscriptionAsync", StringComparison.Ordinal),
            "النُقطَةُ ما زالَت تُنادي مُزَوِّدَ الدَفع — وجَوابُه في المُحاكي «نَجَحَ» "
            + "دائِماً، فَالنِداءُ نَفسُه هُوَ الثَغرَة.");
    }

    /// <summary>ولا زِرَّ يُرسَم لِدَرَجَةٍ لا تُباع — مَدخَلٌ يَرُدُّ
    /// «لَيسَ بَعد» لَيسَ مَدخَلاً (القاعِدَة ١٢). نَفسُ ما فَعَلَته
    /// ‏`Plans.razor` بِـ`PlanPurchasePolicy.Visible`.</summary>
    [Fact]
    public void The_billing_screen_must_not_draw_a_buy_button_for_a_tier_it_cannot_sell()
    {
        var razor = Source("libs", "templates", "ACommerce.Templates.Customer.Marketplace",
            "Components", "Pages", "StudioBilling.razor");

        Assert.True(razor.Contains("StudioTierPurchase", StringComparison.Ordinal),
            "‏`StudioBilling.razor` ما زالَت تَرسُم زِرَّ «اختِيار» لِكُلّ دَرَجَة، "
            + "ولَو كانَت لا تُباع.");
    }

    /// <summary>والرَدُّ يُقرَأ. ‏`?err=` كانَ يُكتَب في العُنوانِ
    /// و**لا تَقرَؤُه الصَفحَة** — فَالمُستَخدِمُ يَرى الصَفحَةَ نَفسَها
    /// بِلا كَلِمَة. رَفضٌ لا يُرى = رَفضٌ مُبتلَع.</summary>
    [Fact]
    public void A_refused_tier_selection_must_say_so_on_screen_and_not_be_swallowed()
    {
        var razor = Source("libs", "templates", "ACommerce.Templates.Customer.Marketplace",
            "Components", "Pages", "StudioBilling.razor");

        Assert.True(razor.Contains("SupplyParameterFromQuery", StringComparison.Ordinal),
            "‏`StudioBilling.razor` لا تَقرَأ `?err=` إطلاقاً — والنُقطَةُ تُعيدُ التَوجيهَ "
            + "بِرَمزِ خَرقٍ لا يَراهُ أَحَد. نَفسُ شَكلِ `Plans.razor.ErrorMessage`.");
    }

    // ═══ ٣) الحَدُّ اللانِهائيّ — كُلفَةُ مِفتاحِ المالِك ═══════════════

    /// <summary>
    /// <para><b>‏`int.MaxValue` في حَدٍّ شَهريٍّ لَيسَ «كَرَماً» بَل
    /// فاتورَةً مَفتوحَة</b>: كُلُّ تَحليلٍ نِداءُ نَموذَجِ لُغَةٍ على
    /// مِفتاحِ المالِك، والعَدّادُ لا يَبلُغ الحَدَّ أَبَداً فَالبَوّابَةُ
    /// **لا تُغلَق قَطّ**.</para>
    /// </summary>
    [Fact]
    public void No_tier_may_grant_an_unbounded_number_of_owner_paid_model_calls()
    {
        var unbounded = TierCatalog.All.Values
            .SelectMany(t => new[]
            {
                (t.Tier, Field: nameof(t.AnalysesPerMonth), Value: t.AnalysesPerMonth),
                (t.Tier, Field: nameof(t.RefinesPerMonth),  Value: t.RefinesPerMonth),
                (t.Tier, Field: nameof(t.StoresMax),        Value: t.StoresMax),
            })
            .Where(x => x.Value == int.MaxValue)
            .Select(x => $"{x.Tier}.{x.Field}")
            .ToArray();

        Assert.True(TierCatalog.All.Count >= 4,
            $"أَداة عَمياء: فُحِصَت {TierCatalog.All.Count} دَرَجَة — والمَقيس أَربَع.");
        Assert.True(unbounded.Length == 0,
            "حُدودٌ بِلا سَقفٍ على مِفتاحِ المالِك:\n  " + string.Join("\n  ", unbounded));
    }

    /// <summary>والبَوّابَةُ تُغلَق فِعلاً عِندَ السَقف — قاعِدَةٌ
    /// نَقِيَّةٌ تُقاس بِلا قاعِدَةِ بَيانات.</summary>
    [Fact]
    public void The_quota_gate_closes_at_the_cap_for_every_tier()
    {
        var open = TierCatalog.All.Values
            .Where(t => !(t.AnalysesPerMonth >= 0 && t.AnalysesPerMonth < int.MaxValue))
            .Select(t => t.Tier).ToArray();
        Assert.True(open.Length == 0,
            "دَرَجاتٌ لا تُغلِق بَوّابَةَ التَحاليل: " + string.Join("، ", open));
    }

    // ═══ ٤) الفاتورَةُ المُلَفَّقَة ═════════════════════════════════════

    /// <summary>رَقَمٌ ضَريبيٌّ مُخترَعٌ في وَثيقَةٍ تُعرَض على
    /// تاجِر — لا يُصلَح بِرَقَمٍ آخَر، يُحذَف.</summary>
    [Fact]
    public void No_fabricated_vat_number_may_appear_anywhere_in_the_shipped_source()
    {
        var roots = new[]
        {
            Path.Combine(RepoRoot, "libs"),
            Path.Combine(RepoRoot, "apps"),
        };

        var scanned = 0;
        var hits = new List<string>();
        foreach (var root in roots)
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;
            scanned++;
            if (File.ReadAllText(file).Contains("300000000000003", StringComparison.Ordinal))
                hits.Add(Path.GetRelativePath(RepoRoot, file));
        }

        Assert.True(scanned > 200, $"أَداة عَمياء: فُحِصَ {scanned} مِلَفّاً — والمَقيس بِالمِئات.");
        Assert.True(hits.Count == 0,
            "رَقَمٌ ضَريبيٌّ مُلَفَّقٌ ما زالَ في المَصدَر:\n  " + string.Join("\n  ", hits));
    }

    /// <summary>والمُحاكي لا يُصدِر فاتورَةً أَصلاً — إمّا فاتورَةٌ
    /// حَقيقِيَّةٌ مِن المُزَوِّدِ أَو لا فاتورَة. نَفسُ جَوابِ
    /// ‏`NoonPaymentProvider` حَرفاً.</summary>
    [Fact]
    public async Task A_mock_payment_provider_must_not_issue_an_invoice_at_all()
    {
        var provider = new MockPaymentProvider();
        var paid = await provider.AuthorizeAsync(
            new PaymentRequest(AmountSar: 115m, Description: "اختِبار",
                CustomerId: "u1", CustomerPhone: "+966500000000"),
            idempotencyKey: "leak-test-1");

        Assert.Equal(PaymentStatus.Authorized, paid.Status);
        Assert.Null(await provider.GetInvoiceAsync(paid.PaymentId));
    }
}
