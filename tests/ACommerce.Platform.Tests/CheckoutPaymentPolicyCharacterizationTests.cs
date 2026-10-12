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
//
// **ما مُسَّ في كوميتِ §٢‑د (‏2026-10-11)، بِقَرارٍ مَرئِيّ**: الصُفوفُ الَّتي
// كانَت تُثَبِّتُ «`CardOffered` عَلَمُ المُستَأجِرِ وَحدَه» و«القارِئانِ لا
// يَسأَلانِ المُزَوِّد» و«رابِطٌ مَلصوقٌ يَعرِضُ بِطاقَةً عَلى مُزَوِّدٍ لا
// يَقبِض» و«النَصُّ يُسَمّي بَوّابَتَينِ غَيرَ مُدمَجَتَين» — انقَلَبَت إلى
// نَقائِضِها. وجَدوَلُ `Decide` بَقِيَ بِخَلاياهُ الاثنَتَي عَشرَةَ حَرفاً
// مَعَ وَسيطٍ ثالِثٍ `providerCanCharge: true` فَرَضَه التَوقيعُ لا المَعنى،
// وأُضيفَ إلَيه عَمودُ «المُزَوِّدُ لا يَقبِض».

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
        var (m, r) = CheckoutPaymentPolicy.Decide(requested, configured, providerCanCharge: true);
        Assert.Equal(method, m);
        Assert.Equal(refusal, r);
    }

    /// <summary><b>العَمودُ الَّذي أَضافَه §٢‑د</b>: حينَ لا يَستَطيعُ المُزَوِّدُ
    /// القَبضَ، البِطاقَةُ تُرَدُّ بِرَمزٍ في كُلِّ مَتجَرٍ — ولا شَيءَ غَيرُها
    /// يَتَغَيَّر.</summary>
    [Theory]
    [InlineData("card",  true,  "cash", "pay_card_unavailable")]   // P0-1 بِعَينِه: رابِطٌ مَلصوقٌ ومُزَوِّدٌ لا يَقبِض
    [InlineData("card",  false, "cash", "pay_card_unavailable")]
    [InlineData("cash",  true,  "cash", null)]
    [InlineData("cod",   true,  "cash", null)]
    [InlineData(null,    true,  "cash", null)]
    public void The_decision_table_when_the_provider_cannot_charge(string? requested, bool configured, string method, string? refusal)
    {
        var (m, r) = CheckoutPaymentPolicy.Decide(requested, configured, providerCanCharge: false);
        Assert.Equal(method, m);
        Assert.Equal(refusal, r);
    }

    /// <summary>والمُزَوِّدُ يُنادى لِلبِطاقَةِ وَحدَها — نَتيجَةُ الجَدوَلِ لا
    /// مُدخَلُه.</summary>
    [Fact]
    public void Only_a_card_outcome_ever_calls_the_provider()
    {
        Assert.True(CheckoutPaymentPolicy.CallsProvider(CheckoutPaymentPolicy.Decide("card", true, true).Method));
        Assert.False(CheckoutPaymentPolicy.CallsProvider(CheckoutPaymentPolicy.Decide("card", false, true).Method));
        Assert.False(CheckoutPaymentPolicy.CallsProvider(CheckoutPaymentPolicy.Decide("card", true, false).Method));
        Assert.False(CheckoutPaymentPolicy.CallsProvider(CheckoutPaymentPolicy.Decide("cash", true, true).Method));
    }

    // ═══ ٢) الصَفُّ الَّذي انقَلَب — `CardOffered` يَطلُبُ العَلَمَ وقُدرَةَ المُزَوِّدِ مَعاً ══

    /// <summary><b>كانَ <c>CardOffered(x) == x</c> بِـ<c>bool</c> واحِد</b>
    /// (كوميتُ التَوصيف ‏3d6c255)؛ وصارَ بِـ<c>bool</c>َين لا حِملَ
    /// واحِدِيَّ الوَسيطِ بِجِوارِهِما — فَلا سَبيلَ إلى الجَوابِ «نَعَم»
    /// بِلا سُؤالِ المُزَوِّد. مَقيسٌ بِالانعِكاسِ لا بِالدَعوى (‏ADR-032
    /// §٢‑د).</summary>
    [Fact]
    public void CardOffered_now_demands_the_tenant_flag_and_a_provider_that_can_charge()
    {
        Assert.True(CheckoutPaymentPolicy.CardOffered(true, true));
        Assert.False(CheckoutPaymentPolicy.CardOffered(true, false));
        Assert.False(CheckoutPaymentPolicy.CardOffered(false, true));

        var offered = typeof(CheckoutPaymentPolicy).GetMethods().Where(m => m.Name == nameof(CheckoutPaymentPolicy.CardOffered)).ToArray();
        Assert.Single(offered);
        Assert.Equal(new[] { typeof(bool), typeof(bool) }, offered[0].GetParameters().Select(p => p.ParameterType));

        var decide = typeof(CheckoutPaymentPolicy).GetMethods().Where(m => m.Name == nameof(CheckoutPaymentPolicy.Decide)).ToArray();
        Assert.Single(decide);
        Assert.Equal(new[] { typeof(string), typeof(bool), typeof(bool) }, decide[0].GetParameters().Select(p => p.ParameterType));
    }

    /// <summary>والقارِئانِ — الشاشَةُ والنُقطَةُ — يُمَرِّرانِ عَلَمَ
    /// المُستَأجِرِ <b>وقُدرَةَ المُزَوِّدِ</b> مِن المُسنَدِ نَفسِه
    /// (<c>PaymentChargeSurface.CanCharge</c>)، فَلا يَنجَرِفُ طَرَفان.
    /// <para><b>قُلِبَ بِاسمِه ‏2026-10-12 (‏ADR-032 الخُطوَة 8 — شَطرُ <c>CardOffered</c>، تَيّارُ S9 — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق)</b> — كانَ
    /// <c>Both_readers_pass_the_tenant_flag_and_the_providers_charge_capability</c>، والقارِئانِ يُمَرِّرانِ المُدخَلَينِ إلى <c>CardOffered</c>
    /// و<c>Decide</c> مُباشَرَة. <b>وصارا يُمَرِّرانِهِما إلى <c>GatewayPayments.CardFactsAsync</c> بِحَرفٍ واحِد</b> — تَحسُبُ مَعَهُما ثَلاثَةَ
    /// بَوّابَةِ المَتجَرِ مَرَّةً — ويُقَرِّرانِ بِـ<c>CheckoutCardFacts</c> نَفسِها: <c>Decide(…, Collects, ChargerCanCharge)</c>. والمُدخَلانِ
    /// القَديمانِ باقِيانِ بِحَرفِهِما في النِداء، وجَدوَلا <c>Decide</c> أَعلاه بِخَلاياهُما — فَالدالَّةُ لَم تَتَغَيَّر، والقارِئانِ تَغَيَّرا.</para></summary>
    [Fact]
    public void Both_readers_pass_the_tenant_flag_and_the_providers_charge_capability_to_one_card_facts_call_and_decide_by_it()
    {
        var razor = CheckoutRazor();
        Assert.Contains("Facts = await Gateway.CardFactsAsync(Slug, tenant?.PaymentProviderConfigured ?? false, PaymentChargeSurface.CanCharge(Payments));",
            razor, StringComparison.Ordinal);
        Assert.Contains("bool CardOffered => Facts.CardOffered;", razor, StringComparison.Ordinal);
        Assert.Contains("CheckoutPaymentPolicy.Decide(Pay, Facts.Collects, Facts.ChargerCanCharge)", razor, StringComparison.Ordinal);
        Assert.DoesNotContain("CheckoutPaymentPolicy.CardOffered(", razor, StringComparison.Ordinal);

        var body = CheckoutSubmitBody();
        Assert.Contains("var card = await gateway.CardFactsAsync(slug, tenant?.PaymentProviderConfigured ?? false, ACommerce.Kit.Payments.PaymentChargeSurface.CanCharge(payments));",
            body, StringComparison.Ordinal);
        Assert.Contains("CheckoutPaymentPolicy.Decide(pay, card.Collects, card.ChargerCanCharge);", body, StringComparison.Ordinal);
        Assert.Equal(1, body.Split("CardFactsAsync(").Length - 1);
        Assert.Equal(1, razor.Split("CardFactsAsync(").Length - 1);
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
    /// <para><b>P0-1 مُغلَقاً — نَقيضُ ما كانَ مُثَبَّتاً</b>: مُستَأجِرٌ لَصَقَ
    /// رابِطَ مُيَسِّر ⇒ يَقبِض ⇒ <b>ولا تُعرَضُ البِطاقَةُ</b> لِأَنّ مُزَوِّدَ
    /// الإنتاجِ لا يَستَطيعُ القَبض؛ وطَلَبُها يُرَدُّ بِرَمزٍ ولا يُنادى
    /// المُزَوِّد. وفي التَطويرِ (المُحاكي) تُعرَضُ كَما كانَت — فَالتَغييرُ
    /// مَحصورٌ حَيثُ قُصِد (القاعِدَة ١٣).</para>
    /// </summary>
    [Fact]
    public void A_pasted_link_alone_no_longer_offers_a_card_when_the_production_provider_cannot_charge()
    {
        var collects = TenantProviderSet.FromDocuments("t", new[] { Bound("https://moyasar.com/i/inv_1") }).CollectsMoney;
        Assert.True(collects);

        // الإنتاج: الفَشَلُ المُغلَق.
        Assert.Equal(PaymentProviderChoice.Unavailable, PaymentProviderSelection.Decide(false, null));
        var production = PaymentChargeSurface.CanCharge(new UnavailablePaymentProvider());
        Assert.False(production);
        Assert.False(CheckoutPaymentPolicy.CardOffered(collects, production));

        var (method, refusal) = CheckoutPaymentPolicy.Decide("card", collects, production);
        Assert.Equal(CheckoutPayMethods.CashOnDelivery, method);
        Assert.Equal(CheckoutPayMethods.CardUnavailable, refusal);
        Assert.False(CheckoutPaymentPolicy.CallsProvider(method));

        // التَطوير: المُحاكي يَقبِض (مُحاكاةً) — فَالبِطاقَةُ تُعرَضُ كَما كانَت.
        Assert.Equal(PaymentProviderChoice.Mock, PaymentProviderSelection.Decide(true, null));
        Assert.True(CheckoutPaymentPolicy.CardOffered(collects, PaymentChargeSurface.CanCharge(new MockPaymentProvider())));
    }

    /// <summary>والنَصُّ المَعروضُ عَلى الخِيارِ <b>لَم يَعُد يُسَمّي</b>
    /// بَوّابَتَينِ غَيرَ مُدمَجَتَين (كانَ «بِطاقَة (Moyasar/Noon)» —
    /// `ar.json:1276`)، ولا شَبَكَتَي بِطاقاتٍ في تَلميحِه (كانَ «مَدى أَو
    /// فيزا»). والصِياغَةُ الجَديدَةُ مُسَجَّلَةٌ في سِجِلِّ أَسئِلَةِ
    /// المالِك «أ» قابِلَةً لِلنَقض.</summary>
    [Fact]
    public void The_card_label_no_longer_names_gateways_that_are_not_integrated()
    {
        var ar = Source("libs", "core", "ACommerce.Platform.I18n", "Locales", "ar.json");
        Assert.DoesNotContain("\"checkout.payment.card\": \"بِطاقَة (Moyasar/Noon)\"", ar, StringComparison.Ordinal);
        Assert.Contains("\"checkout.payment.card\": \"الدَفع بِالبِطاقَة\"", ar, StringComparison.Ordinal);
        Assert.DoesNotContain("مَدى أَو فيزا", ar, StringComparison.Ordinal);

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
