using ACommerce.Kit.Payments;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ قَرارِ البِطاقَةِ في إتمامِ الشِراء — قَبلَ التَبديل ═══════════
//
// ‏ADR-032 §٢‑هـ الخُطوَة 1: «جَدوَلُ `CheckoutPaymentPolicy.Decide` ·
// `CollectsMoney`/`PaymentLink` لِلرابِط · `StoredValue.Explicit` تَرمي ·
// `Definitions.Count == 8`». الثَلاثَةُ الأَخيرَةُ مُثَبَّتَةٌ مُنذُ ‏2026-08-30 في
// `TenantProviderZeroEquivalenceTests` و`ProviderCatalogTests` فَلا تُكَرَّر؛ وهذا
// المِلَفُّ يُثَبِّتُ ما لَم يَكُن مُثَبَّتاً: **الجَدوَلَ كامِلاً**، و**أَنّ
// `CardOffered` اليَومَ هُوَ عَلَمُ المُستَأجِرِ وَحدَه** ولا يَسأَلُ مُزَوِّدَ
// الدَفعِ شَيئاً — وهذا هُوَ بِالضَبطِ الصَفُّ الَّذي سَيُقلِبُه §٢‑د في الكوميتِ
// التالي بِقَرارٍ مَرئِيّ. وما سِواهُ يَبقى أَخضَرَ بِلا تَعديلِ حَرف (القاعِدَة ٣).
//
// **المَدخَلُ الَّذي يَضُرُّ، مَقيساً** (موجَزُ الفَجَوات P0-1، ‏2026-10-10):
// رَبطُ `moyasar_hosted` بِرابِطٍ مَملوءٍ يَقلِبُ `Tenant.PaymentProviderConfigured`
// (‏`SyncPaymentProviderFlagAsync`)، فَتُعرَضُ البِطاقَةُ وتُنادي `IPaymentProvider`
// **العامَّ** — وهُوَ في الإنتاجِ `UnavailablePaymentProvider` يَرُدُّ `Failed`
// دائِماً. فَيُختَمُ `payment_status = Failed` عَلى الصَفقَةِ وتَبقى `Offered`
// ويُحالُ المُشتَري إلَيها بِلا كَلِمَة. **ولا اختِبارَ يُحمِرُّ إن عاد** — حَتّى
// هذا المِلَفّ، الَّذي يُثَبِّتُه أَوَّلاً كَما هُوَ.

public class CheckoutPaymentPolicyCharacterizationTests
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static string Source(params string[] parts)
    {
        var path = Path.Combine(RepoRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياءُ بِلا طَرَفٍ مَقروء.");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 500, $"أَداةٌ عَمياء: {path} طولُه {text.Length} مِحرَفاً — لَم يُقرَأ.");
        return text;
    }

    private static string CheckoutRazor() => Source(
        "libs", "templates", "ACommerce.Templates.Customer.Marketplace",
        "Components", "Pages", "CheckoutPage.razor");

    private static string Endpoints() => Source(
        "libs", "templates", "ACommerce.Templates.Customer.Marketplace",
        "MarketplaceTemplateExtensions.cs");

    /// <summary>جِسمُ نُقطَةِ إتمامِ الشِراءِ وَحدَه.</summary>
    private static string CheckoutSubmitBody()
    {
        var text = Endpoints();
        var start = text.IndexOf("/{slug}/checkout/submit", StringComparison.Ordinal);
        Assert.True(start > 0, "نُقطَةُ إتمامِ الشِراءِ غَيرُ مَوجودَة — الأَداةُ عَمياء.");
        var end = text.IndexOf(".RequireStoreWritable();", start, StringComparison.Ordinal);
        return text.Substring(start, end - start);
    }

    /// <summary>نَفسُ رَبطِ <c>TenantProviderZeroEquivalenceTests.Bound</c>
    /// حَرفاً — مُستَأجِرٌ لَصَقَ رابِطَ مُيَسِّر.</summary>
    private static TenantProviderBinding Bound(string link) => new()
    {
        Id = ProviderCapabilities.Payments,
        Slug = ProviderCapabilities.Payments,
        TenantSlug = "t",
        ProviderSlug = "moyasar_hosted",
        Status = TenantProviderBinding.StatusActive,
        Values = { ["invoice_url"] = new StoredValue { Kind = CredentialKinds.HostedLink, Plain = link } },
        BoundBy = "owner",
        BoundAt = new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc),
    };

    // ═══ ١) الجَدوَلُ كامِلاً — ذَهَبِيٌّ لا يُشتَقُّ مِن الكودِ المَفحوص ════

    /// <summary>كُلُّ خَلِيَّةٍ في <c>Decide</c>: ما يُطلَبُ × هَل يَقبِضُ
    /// المَتجَر ⇒ الطَريقَةُ ورَمزُ الرَفض. القِيَمُ مَكتوبَةٌ بِاليَد لا
    /// بِالثَوابِت — وإلّا صارَ الاختِبارُ دَورَةً فارِغَة.</summary>
    [Theory]
    [InlineData("card",  true,  "card", null)]
    [InlineData("card",  false, "cash", "pay_card_unavailable")]
    [InlineData(" card ", true, "card", null)]                   // قَصٌّ قَبلَ المُقارَنَة
    [InlineData("CARD",  true,  "cash", null)]                   // ‏Ordinal: حالَةُ الأَحرُفِ تَسقُطُ إلى النَقد
    [InlineData("cash",  true,  "cash", null)]
    [InlineData("cash",  false, "cash", null)]
    [InlineData("cod",   true,  "cash", null)]                   // الرَمزُ المَيِّت يُقرَأُ نَقداً
    [InlineData("cod",   false, "cash", null)]
    [InlineData("",      true,  "cash", null)]
    [InlineData("",      false, "cash", null)]
    [InlineData(null,    true,  "cash", null)]
    [InlineData(null,    false, "cash", null)]
    public void The_decision_table_today(string? requested, bool configured, string method, string? refusal)
    {
        var (m, r) = CheckoutPaymentPolicy.Decide(requested, configured);
        Assert.Equal(method, m);
        Assert.Equal(refusal, r);
    }

    /// <summary>والمُزَوِّدُ يُنادى لِلبِطاقَةِ وَحدَها — نَتيجَةُ الجَدوَلِ لا
    /// مُدخَلُه.</summary>
    [Fact]
    public void Only_a_card_outcome_ever_calls_the_provider()
    {
        Assert.True(CheckoutPaymentPolicy.CallsProvider(CheckoutPaymentPolicy.Decide("card", true).Method));
        Assert.False(CheckoutPaymentPolicy.CallsProvider(CheckoutPaymentPolicy.Decide("card", false).Method));
        Assert.False(CheckoutPaymentPolicy.CallsProvider(CheckoutPaymentPolicy.Decide("cash", true).Method));
    }

    // ═══ ٢) الصَفُّ الَّذي سَيَنقَلِبُ — `CardOffered` عَلَمُ المُستَأجِرِ وَحدَه ══

    /// <summary><b>اليَوم: <c>CardOffered(x) == x</c></b>، والدالَّةُ تَقبَلُ
    /// <c>bool</c> واحِداً — لا مُزَوِّدَ ولا قُدرَةَ قَبض. مَقيسٌ بِالانعِكاسِ
    /// لا بِالدَعوى، <b>وهذا هُوَ الصَفُّ الَّذي يُقلِبُه ADR-032 §٢‑د</b>:
    /// «لا يُشتَقُّ مِن `CollectsMoney` وَحدَه بَل مِن مُزَوِّدٍ يَستَطيعُ
    /// القَبضَ فِعلاً لِهذا المُستَأجِر».</summary>
    [Fact]
    public void CardOffered_today_mirrors_the_tenant_flag_and_asks_the_provider_nothing()
    {
        Assert.True(CheckoutPaymentPolicy.CardOffered(true));
        Assert.False(CheckoutPaymentPolicy.CardOffered(false));

        var offered = typeof(CheckoutPaymentPolicy).GetMethod(nameof(CheckoutPaymentPolicy.CardOffered))!;
        Assert.Equal(new[] { typeof(bool) }, offered.GetParameters().Select(p => p.ParameterType));

        var decide = typeof(CheckoutPaymentPolicy).GetMethod(nameof(CheckoutPaymentPolicy.Decide))!;
        Assert.Equal(new[] { typeof(string), typeof(bool) }, decide.GetParameters().Select(p => p.ParameterType));
    }

    /// <summary>والقارِئانِ — الشاشَةُ والنُقطَةُ — يُمَرِّرانِ عَلَمَ
    /// المُستَأجِرِ وَحدَه، ولا يَسأَلانِ <c>IPaymentProvider</c> عَن
    /// قُدرَتِه (الشاشَةُ تَسأَلُه عَن <b>التَجرِبَة</b> فَقَط).</summary>
    [Fact]
    public void Both_readers_pass_only_the_tenant_flag_today()
    {
        var razor = CheckoutRazor();
        Assert.Contains("CheckoutPaymentPolicy.CardOffered(tenant?.PaymentProviderConfigured ?? false)",
            razor, StringComparison.Ordinal);

        var body = CheckoutSubmitBody();
        Assert.Contains("CheckoutPaymentPolicy.Decide(", body, StringComparison.Ordinal);
        Assert.Contains("pay, tenant?.PaymentProviderConfigured ?? false);", body, StringComparison.Ordinal);
        Assert.DoesNotContain("CanCharge", body, StringComparison.Ordinal);
        Assert.DoesNotContain("CanCharge", razor, StringComparison.Ordinal);
    }

    // ═══ ٣) السِلسِلَةُ كامِلَةً — رابِطٌ مَلصوقٌ ⇒ بِطاقَةٌ تُعرَضُ ⇒ مُزَوِّدٌ يَفشَل ══

    /// <summary>عَلَمُ المُستَأجِرِ يُكتَبُ مِن <c>CollectsMoney</c> وَحدَها —
    /// أَي مِن رابِطٍ مَملوء — في <c>SyncPaymentProviderFlagAsync</c>.</summary>
    [Fact]
    public void The_tenant_flag_is_written_from_a_filled_hosted_link_alone()
    {
        var text = Endpoints();
        var at = text.IndexOf("static async Task SyncPaymentProviderFlagAsync(", StringComparison.Ordinal);
        Assert.True(at > 0, "‏`SyncPaymentProviderFlagAsync` غَيرُ مَوجودَة — الأَداةُ عَمياء.");
        var fn = text.Substring(at, 900);
        Assert.Contains("var collects = (await providers.ForAsync(slug)).CollectsMoney;", fn, StringComparison.Ordinal);
        Assert.Contains("t.PaymentProviderConfigured = collects;", fn, StringComparison.Ordinal);

        // والمَعنى النَقِيُّ لِـ`CollectsMoney`: رابِطٌ مَملوءٌ لا أَكثَر.
        var set = TenantProviderSet.FromDocuments("t", new[] { Bound("https://moyasar.com/i/inv_1") });
        Assert.True(set.CollectsMoney);
        Assert.Equal("https://moyasar.com/i/inv_1", set.For(ProviderCapabilities.Payments)!.PaymentLink);
        Assert.False(TenantProviderSet.FromDocuments("t", new[] { Bound("   ") }).CollectsMoney);
    }

    /// <summary><b>وفي الإنتاجِ المُزَوِّدُ خَلفَ البِطاقَةِ هُوَ الفَشَلُ
    /// المُغلَق</b>: بِلا `Payments__Provider` مَكتوبٍ يُختارُ
    /// <c>Unavailable</c>، وكُلُّ حَجزٍ عَلَيه <c>Failed</c> بِسَبَب.</summary>
    [Fact]
    public async Task In_production_the_provider_behind_the_card_cannot_charge()
    {
        Assert.Equal(PaymentProviderChoice.Unavailable, PaymentProviderSelection.Decide(isDevelopment: false, configured: null));
        Assert.Equal(PaymentProviderChoice.Unavailable, PaymentProviderSelection.Decide(isDevelopment: false, configured: ""));

        var auth = await new UnavailablePaymentProvider().AuthorizeAsync(
            new PaymentRequest(100m, "د", "u", "+966500000000"), "deal_x");
        Assert.Equal(PaymentStatus.Failed, auth.Status);
        Assert.Equal(UnavailablePaymentProvider.Reason, auth.FailureReason);
    }

    /// <summary>
    /// <para><b>P0-1 بِحَرفِه، مُثَبَّتاً كَما هُوَ اليَوم</b>: مُستَأجِرٌ لَصَقَ
    /// رابِطَ مُيَسِّر ⇒ يَقبِض ⇒ تُعرَضُ البِطاقَةُ ⇒ طَلَبُها يُقبَلُ
    /// ويُنادى المُزَوِّدُ — <b>وهُوَ في الإنتاجِ لا يَستَطيعُ القَبض</b>.
    /// فَالبابُ مَفتوحٌ عَلى فَشَلٍ مُؤَكَّد. هذا الاختِبارُ يَحمَرُّ يَومَ
    /// يُغلَقُ البابُ — وذاكَ مَقصود: يُقلَبُ في كوميتِ §٢‑د إلى
    /// نَقيضِه، لا يُحذَف.</para>
    /// </summary>
    [Fact]
    public void Today_a_pasted_link_offers_a_card_whose_provider_cannot_charge_in_production()
    {
        var collects = TenantProviderSet.FromDocuments("t", new[] { Bound("https://moyasar.com/i/inv_1") }).CollectsMoney;
        Assert.True(collects);
        Assert.True(CheckoutPaymentPolicy.CardOffered(collects));

        var (method, refusal) = CheckoutPaymentPolicy.Decide("card", collects);
        Assert.Equal(CheckoutPayMethods.Card, method);
        Assert.Null(refusal);
        Assert.True(CheckoutPaymentPolicy.CallsProvider(method));

        // والمُزَوِّدُ الَّذي سَيُنادى في الإنتاج:
        Assert.Equal(PaymentProviderChoice.Unavailable, PaymentProviderSelection.Decide(false, null));
    }

    /// <summary>والنَصُّ المَعروضُ عَلى الخِيارِ يُسَمّي بَوّابَتَينِ غَيرَ
    /// مُدمَجَتَين — <b>مُثَبَّتٌ كَما هُوَ</b> لِيُصَحَّحَ في كوميتِ §٢‑د
    /// نَفسِه (موجَزُ الفَجَوات P0-1: «وتَصحيحُ `ar.json:1276` في الكوميتِ
    /// نَفسِه»).</summary>
    [Fact]
    public void Today_the_card_label_names_gateways_that_are_not_integrated()
    {
        var ar = Source("libs", "core", "ACommerce.Platform.I18n", "Locales", "ar.json");
        Assert.Contains("\"checkout.payment.card\": \"بِطاقَة (Moyasar/Noon)\"", ar, StringComparison.Ordinal);

        // ولا تَكامُلَ لِأَيٍّ مِنهُما يَبلُغُ `IPaymentProvider`: صِفرُ صِنفٍ
        // يُنَفِّذُه بِاسمِهِما تَحتَ `libs/`.
        var impls = Directory.EnumerateFiles(Path.Combine(RepoRoot, "libs"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => File.ReadAllText(f))
            .Count(t => t.Contains(": IPaymentProvider", StringComparison.Ordinal)
                     && (t.Contains("class Moyasar", StringComparison.Ordinal) || t.Contains("class Noon", StringComparison.Ordinal)));
        Assert.Equal(0, impls);
    }
}
