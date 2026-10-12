using System.Security.Cryptography;
using ACommerce.Kit.Payments;
using ACommerce.Kit.Subscriptions;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Providers;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ مِفتاحٌ مَختومٌ بِلا عَميلٍ يَطلُبُ بِه — لا بابَ يَنفَتِحُ بِه قَبلَ الخُطوَة 8 ═══
//
// **ما يُثَبَّتُ هُنا (‏2026-10-12، ‏ADR-032 الخُطوَة 7 و§٧‑ج)**: صاحِبُ مَتجَرٍ رَبَطَ
// مِفتاحَ البَوّابَةِ السِرِّيَّ وَحدَه (الخِزانَةُ خَتَمَته، ولا رابِطَ مُستَضافاً) —
// **ولا عَميلَ بَوّابَةٍ في الكودِ يَطلُبُ بِه** (الخُطوات 8–10، المَوجَةُ التالِيَة).
// فَلا يَجوزُ أَن يُفتَحَ بِه بابٌ يُفضي إلى لا شَيء (القاعِدَة ١٢):
//
//   • **البِطاقَةُ في إتمامِ الشِراء** (`CheckoutPaymentPolicy.CardOffered`، وقَد جَعَلَتها
//     المَوجَةُ الأولى «مُزَوِّدٌ يَستَطيعُ القَبضَ فِعلاً») — تَبقى مُغلَقَةً **في الإنتاجِ
//     وفي التَطويرِ مَعاً**، لا لِأَنّ مُزَوِّدَ النُسخَةِ يَعجِزُ فَحَسب.
//   • **الباقاتُ المَدفوعَة** — لا تَظهَرُ ولا يُحالُ زِرُّها إلى `PlanPay` بِلا رابِط.
//
// **ولِماذا يَبقى `CollectsMoney` رابِطاً مَملوءاً وَحدَه** — خِلافاً لِحَرفِ الصَفّ 7
// («أَو حَقلٌ سِرِّيٌّ مَختوم»): المَقيسُ في الصَفِّ الثاني أَدناه بِالعَكس — لَو عُدَّ المَختومُ
// لَقُلِبَ `PaymentProviderConfigured` بِلا قابِض، فَظَهَرَت الباقاتُ المَدفوعَةُ وأَحالَ
// زِرُّها إلى صَفحَةٍ لا رابِطَ فيها («غَيرُ مُتاح»)، وعُرِضَت البِطاقَةُ في التَطويرِ عَلى
// المُحاكي لا عَلى مِفتاحِ التاجِر. **يَنقَلِبُ الصَفُّ الأَوَّلُ مَعَ عَميلِ البَوّابَة**.
//
// **وانقَلَبَ (‏2026-10-12، ‏ADR-032 الخُطوَة 8 — شَطرُ `CollectsMoney`، تَيّارُ S9؛ ولا يُعادُ حَرفٌ فَوقَ ما سَبَق)**: بُنِيَ العَميلُ ونُقطَتاه
// وتَأكيدُ الدَفع، فَصارَ `CollectsMoney` «رابِطٌ مَملوءٌ أَو مِفتاحٌ مَختوم» — وحَمَرَت بِه ثَلاثَةُ صُفوفٍ بِأَسمائِها، مَقيسَةً بِقَلبِه وَحدَه
// قَبلَ أَيِّ تَوصيل: الأَوَّلُ هُنا، والثالِثُ هُنا (مُلاحَظَةُ الاستوديو — لا يُسَمّيه ‏ADR-032)، و`ProviderVaultDeploymentTests
// .VerifyProductionBoot_warns_on_a_valid_kek_exactly_while_a_sealed_key_collects_nothing` الَّذي اخضَرَّ بِلا تَعديلِ حَرفٍ حينَ حُذِفَ
// سَطرُ ⚠ وفِقرَتُه. **والبابانِ اللَّذانِ خَشِيَهُما الصَفُّ الثاني لا يُفتَحانِ إلى لا شَيء**: البِطاقَةُ تَقبِضُ بِمِفتاحِ التاجِرِ نَفسِه
// (`CheckoutCardFacts`، ولا تَسقُطُ إلى المُحاكي)، والباقاتُ لا تَقرَأُ `CollectsMoney` بَعدَ اليَوم بَل `PlansPayable` — رابِطٌ تُصَيِّرُه
// `PlanPay`؛ فَالصَفُّ الثاني أَخضَرُ كَما هُوَ ويَشهَدُ لِماذا. والدَبّوسُ ذو الاتِّجاهَينِ آخِرَ المِلَفّ.
public class ProviderSealedKeyDoorsTests
{
    private const string Key = "sk_live_9f3b7c2d41aa58e6";

    /// <summary>رَبطٌ كَما تَكتُبُه نُقطَةُ الرَبطِ لِمِفتاحٍ سِرِّيٍّ وَحدَه — مِن
    /// <c>ProviderBindSurface.FromForm</c> نَفسِها لا مَبنِيّاً بِاليَد. ويَقرَؤُه
    /// <c>ProviderVaultDeploymentTests</c> سَبَباً لِتَنبيهِ بَوّابَةِ الإقلاع.</summary>
    internal static TenantProviderSet SealedOnly()
    {
        var vault = ProviderVault.FromKek(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        var read = ProviderBindSurface.FromForm("moyasar_api", _ => Key, "t", vault);
        Assert.Null(read.RefusalCode);
        Assert.True(Assert.Single(read.Values).Value.IsSealed);

        return TenantProviderSet.FromDocuments("t", new[]
        {
            new TenantProviderBinding
            {
                Id = read.Capability,
                Slug = read.Capability,
                TenantSlug = "t",
                ProviderSlug = read.ProviderSlug,
                Status = TenantProviderBinding.StatusActive,
                Values = new Dictionary<string, StoredValue>(read.Values, StringComparer.Ordinal),
                BoundBy = "owner",
                BoundAt = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc),
            },
        });
    }

    private static readonly Plan Paid = new() { Id = "pro", Name = "pro", Price = 99m, IsActive = true };
    private static readonly Plan Free = new() { Id = "free", Name = "free", Price = 0m, IsActive = true };

    /// <summary><b>قُلِبَ بِاسمِه ‏2026-10-12 (‏ADR-032 الخُطوَة 8 — شَطرُ <c>CollectsMoney</c>، تَيّارُ S9)</b> — كانَ
    /// <c>A_sealed_key_alone_collects_nothing_yet_so_no_card_and_no_paid_plan_opens</c>: المِفتاحُ المَختومُ وَحدَه لا يَقبِض، فَلا بِطاقَةَ
    /// في الإنتاجِ ولا في التَطويرِ ولا باقَةَ مَدفوعَة. <b>والآن يَقبِض — بِبَوّابَةِ المَتجَرِ وَحدَها</b>: البِطاقَةُ تُعرَضُ بِمِفتاحِه حينَ تَفُكُّه
    /// الخِزانَةُ والعَميلُ مُسَجَّل، في الإنتاجِ والتَطويرِ مَعاً، ولا تَسقُطُ إلى مُزَوِّدِ النُسخَةِ حينَ لا تَفُكّ؛ <b>والباقاتُ المَدفوعَةُ ما زالَت
    /// لا تَنفَتِح</b> — بابُها <c>PlansPayable</c> (رابِطٌ تُصَيِّرُه <c>PlanPay</c>) لا <c>CollectsMoney</c>.</summary>
    [Fact]
    public void A_sealed_key_alone_collects_through_the_merchant_key_so_the_card_opens_there_and_still_no_paid_plan_opens()
    {
        var set = SealedOnly();

        // الرَبطُ قائِمٌ ومَحلول — مُزَوِّدٌ مَعروفٌ لِقُدرَتِه، لا لَقطَةُ المَنَصَّة.
        Assert.NotSame(TenantProviderSet.Platform, set);
        var p = set.For(ProviderCapabilities.Payments);
        Assert.NotNull(p);
        Assert.Equal("moyasar_api", p!.Slug);

        // ويَقبِضُ — بِالمِفتاحِ المَختوم، ولا رابِطَ يُنقَر.
        Assert.Null(p.PaymentLink);
        Assert.NotNull(p.SealedSecretKeyField);
        Assert.True(p.CollectsMoney);
        Assert.True(set.CollectsMoney);

        // ‏(١) البِطاقَة — بِمِفتاحِ المَتجَرِ في الإنتاجِ (الفَشَلُ المُغلَقُ لا يُسأَل) وفي التَطويرِ (المُحاكي لا يُسأَل).
        var production = PaymentChargeSurface.CanCharge(new UnavailablePaymentProvider());
        var development = PaymentChargeSurface.CanCharge(new MockPaymentProvider());
        Assert.False(production);
        Assert.True(development);
        foreach (var canCharge in new[] { production, development })
        {
            var open = new CheckoutCardFacts(StoreCollects: set.CollectsMoney, SealedKeyBound: p.SealedSecretKeyField is not null,
                VaultCanUnseal: true, GatewayClientRegistered: true, InstanceCanCharge: canCharge);
            Assert.True(open.CardOffered);
            Assert.True(open.ThroughGateway);
            Assert.Equal((CheckoutPayMethods.Card, (string?)null), CheckoutPaymentPolicy.Decide("card", open.Collects, open.ChargerCanCharge));

            // وخِزانَةٌ لا تَفُكُّ لا تُسقِطُها إلى مُزَوِّدِ النُسخَة — ولا إلى المُحاكي في التَطوير.
            var locked = open with { VaultCanUnseal = false };
            Assert.False(locked.CardOffered);
            var (method, refusal) = CheckoutPaymentPolicy.Decide("card", locked.Collects, locked.ChargerCanCharge);
            Assert.Equal(CheckoutPayMethods.CashOnDelivery, method);
            Assert.Equal(CheckoutPayMethods.CardUnavailable, refusal);
            Assert.False(CheckoutPaymentPolicy.CallsProvider(method));
        }

        // ‏(٢) الباقاتُ المَدفوعَة — لا تُعرَض، وطَلَبُها مُباشَرَةً يُرَدُّ بِرَمزِه لا يُحال: بابُها رابِطٌ تُصَيِّرُه PlanPay.
        Assert.False(set.PlansPayable);
        var visible = PlanPurchasePolicy.Visible(new[] { Free, Paid }, set.PlansPayable);
        Assert.Equal(new[] { "free" }, visible.Select(x => x.Id).ToArray());
        var (route, code) = PlanPurchasePolicy.Decide(Paid, set.PlansPayable);
        Assert.Equal(PlanPurchasePolicy.PlanPurchaseRoute.Refuse, route);
        Assert.Equal(PlanPurchasePolicy.PaidUnavailable, code);
    }

    /// <summary><b>المَقيسُ بِالعَكس — لِماذا لا يُعَدُّ المَختومُ قابِضاً بَعد</b>: لَو قالَ
    /// <c>CollectsMoney</c> «نَعَم» لِهذا الرَبطِ (حَرفُ الصَفِّ 7) لَأُحيلَت الباقَةُ المَدفوعَةُ
    /// إلى «ادفَع عِندَ مُزَوِّدِ التاجِر» — وصَفحَتُه (<c>PlanPay.razor</c>) لا تُصَيِّرُ إلّا
    /// <c>PaymentLink</c>، وهُوَ فارِغٌ هُنا؛ ولَعُرِضَت البِطاقَةُ في التَطويرِ عَلى المُحاكي
    /// لا عَلى مِفتاحِ التاجِر. بابانِ إلى لا شَيء.</summary>
    [Fact]
    public void Were_a_sealed_key_counted_as_collecting_the_paid_plan_would_lead_to_a_page_with_no_link()
    {
        var set = SealedOnly();
        const bool countedAsCollecting = true;   // حَرفُ الصَفّ 7 — ما لَم يُنَفَّذ

        Assert.Equal(PlanPurchasePolicy.PlanPurchaseRoute.PayAtProvider,
            PlanPurchasePolicy.Decide(Paid, countedAsCollecting).Route);
        Assert.Contains(Paid, PlanPurchasePolicy.Visible(new[] { Free, Paid }, countedAsCollecting));
        Assert.Null(set.For(ProviderCapabilities.Payments)!.PaymentLink);

        // وصَفحَةُ الدَفعِ تَقرَأُ الرابِطَ وَحدَه — فَبِلا رابِطٍ تَرسُمُ «غَيرُ مُتاح».
        var planPay = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "PlanPay.razor"));
        Assert.Contains("link = set.For(ProviderCapabilities.Payments)?.PaymentLink;", planPay, StringComparison.Ordinal);
        Assert.Contains("else if (link is null)", planPay, StringComparison.Ordinal);

        Assert.True(CheckoutPaymentPolicy.CardOffered(countedAsCollecting,
            PaymentChargeSurface.CanCharge(new MockPaymentProvider())));
    }

    /// <summary><b>والنَصُّ الَّذي يَقولُه الاستوديو لِمَتجَرٍ لا تُدفَعُ باقاتُه يَصدُقُ</b>: كانَ «ما دامَ بِلا مُزَوِّدِ دَفعٍ مَربوط» — صادِقاً
    /// يَومَ كانَ الرَبطُ رابِطاً وَحدَه، وكاذِباً لِمَتجَرٍ رَبَطَ مِفتاحاً بِلا رابِط. فَصارَ «بِلا رابِطِ دَفعٍ مَربوط» — ما تَحتاجُه الباقاتُ فِعلاً.
    /// <para><b>قُلِبَ بِاسمِه ‏2026-10-12 (‏ADR-032 الخُطوَة 8 — شَطرُ <c>CollectsMoney</c>، تَيّارُ S9)</b> — كانَ
    /// <c>The_studio_plans_note_names_the_missing_payment_link_not_a_missing_provider</c>، ويَرسُمُ المُلاحَظَةَ بِـ<c>!tenant.PaymentProviderConfigured</c>
    /// ويَشتَرِطُ أَنّ المَختومَ لا يَقبِض — <b>والصَفُّ الثالِثُ الَّذي يَحمَرُّ بِقَلبِ <c>CollectsMoney</c> ولا يُسَمّيه ‏ADR-032</b>. فَلَمّا صارَ
    /// المَختومُ يَقبِضُ صارَ العَلَمُ صادِقاً لِمَتجَرٍ بِمِفتاحٍ وَحدَه، فَتَختَفي المُلاحَظَةُ وباقاتُه لا تُعرَض. فَصارَت تُرسَمُ بِالبابِ
    /// نَفسِه الَّذي يُخفي الباقات — <c>PlansPayable</c> — ونَصُّها بِحَرفِه صادِق: لا رابِطَ مَربوط.</para></summary>
    [Fact]
    public void The_studio_plans_note_names_the_missing_payment_link_and_is_drawn_by_the_door_that_hides_the_plans()
    {
        var ar = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "core", "ACommerce.Platform.I18n", "Locales", "ar.json"));
        Assert.Contains("\"studio.app_plans.no_collector\": \"الباقاتُ بِسِعرٍ لا تُعرَضُ لِزُوّارِ مَتجَرِكَ ما دامَ بِلا رابِطِ دَفعٍ مَربوط.",
            ar, StringComparison.Ordinal);
        Assert.DoesNotContain("ما دامَ بِلا مُزَوِّدِ دَفعٍ مَربوط", ar, StringComparison.Ordinal);

        // والمُلاحَظَةُ تُرسَمُ بِالبابِ نَفسِه الَّذي يُخفي الباقات — فَمَتجَرٌ رَبَطَ مِفتاحاً وَحدَه يَراها، وهي صادِقَةٌ لَه.
        var page = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "StudioAppPlans.razor"));
        Assert.Contains("@if (!plansPayable)", page, StringComparison.Ordinal);
        Assert.Contains("plansPayable = (await Providers.ForAsync(Slug)).PlansPayable;", page, StringComparison.Ordinal);
        Assert.Contains("studio.app_plans.no_collector", page, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentProviderConfigured)", page, StringComparison.Ordinal);

        var sealedOnly = SealedOnly();
        Assert.True(sealedOnly.CollectsMoney);
        Assert.False(sealedOnly.PlansPayable);
        Assert.Null(sealedOnly.For(ProviderCapabilities.Payments)!.PaymentLink);
    }
    // ═══ الدَبّوسُ ذو الاتِّجاهَين — مَقيساً مِن الكود (‏ADR-032 الخُطوَة 8، تَيّارُ S9، ‏2026-10-12) ═══════════════════════

    private static string Source(params string[] parts)
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياء.");
        return File.ReadAllText(path);
    }

    private static string Page(string name)
        => Source("libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", name);

    /// <summary>مَقطَعٌ مِن <paramref name="text"/> بَينَ أَوَّلِ <paramref name="from"/> وأَوَّلِ <paramref name="to"/> بَعدَه — بِلا تَعليقات.</summary>
    private static string Segment(string text, string from, string to)
    {
        var start = text.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"‏`{from}` غَيرُ مَوجود — الأَداةُ عَمياء.");
        var end = text.IndexOf(to, start, StringComparison.Ordinal);
        Assert.True(end > start, $"‏`{to}` لا يَتلو `{from}` — الأَداةُ عَمياء.");
        return WriteEndpointGuardTests.StripComments(text[start..end]);
    }

    /// <summary>
    /// <para><b>رَبطٌ بِمِفتاحٍ مَختومٍ وَحدَه «يَقبِض» ⇒ بابُ الباقاتِ يَرُدّ، وإتمامُ الشِراءِ يَقبِضُ بِمِفتاحِ المَتجَر؛ ورَبطٌ بِرابِطٍ وَحدَه
    /// «يَقبِض» ⇒ الباقَةُ تُدفَعُ بِرابِطِه في <c>PlanPay</c>، والبِطاقَةُ لا تَمُرُّ بِالبَوّابَة</b> — الاتِّجاهانِ مَعاً، ثُمَّ القُرّاءُ في الكودِ
    /// يَقرَؤونَ المُسنَدَينِ بِأَسمائِهِما: فَلا يُعَدُّ المَختومُ قابِضاً وتُحالُ باقَتُه إلى صَفحَةٍ بِلا رابِط (ما خَشِيَه الصَفُّ الثاني)، ولا
    /// يُعَدُّ الرابِطُ بابَ بِطاقَةٍ بِمِفتاحٍ لا وُجودَ لَه.</para>
    /// </summary>
    [Fact]
    public void A_sealed_only_binding_charges_through_the_merchant_key_and_refuses_plans_while_a_link_only_binding_pays_plans_by_its_link()
    {
        var development = PaymentChargeSurface.CanCharge(new MockPaymentProvider());

        // ── المَختومُ وَحدَه ─────────────────────────────────────────────
        var sealedSet = SealedOnly();
        var sealedPayments = sealedSet.For(ProviderCapabilities.Payments)!;
        Assert.True(sealedSet.CollectsMoney);
        Assert.False(sealedSet.PlansPayable);
        Assert.Equal((PlanPurchasePolicy.PlanPurchaseRoute.Refuse, PlanPurchasePolicy.PaidUnavailable),
            PlanPurchasePolicy.Decide(Paid, sealedSet.PlansPayable));
        var sealedCard = new CheckoutCardFacts(sealedSet.CollectsMoney, sealedPayments.SealedSecretKeyField is not null, true, true, development);
        Assert.True(sealedCard.CardOffered);
        Assert.True(sealedCard.ThroughGateway);

        // ── الرابِطُ وَحدَه ──────────────────────────────────────────────
        var linkSet = TenantProviderSet.FromDocuments("t", new[]
        {
            new TenantProviderBinding
            {
                Id = ProviderCapabilities.Payments, Slug = ProviderCapabilities.Payments, TenantSlug = "t", ProviderSlug = "moyasar_hosted",
                Status = TenantProviderBinding.StatusActive,
                Values = { ["invoice_url"] = new StoredValue { Kind = CredentialKinds.HostedLink, Plain = "https://moyasar.com/i/inv_1" } },
                BoundBy = "owner", BoundAt = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc),
            },
        });
        var linkPayments = linkSet.For(ProviderCapabilities.Payments)!;
        Assert.True(linkSet.CollectsMoney);
        Assert.True(linkSet.PlansPayable);
        Assert.Equal("https://moyasar.com/i/inv_1", linkPayments.PaymentLink);
        Assert.Equal((PlanPurchasePolicy.PlanPurchaseRoute.PayAtProvider, (string?)null), PlanPurchasePolicy.Decide(Paid, linkSet.PlansPayable));
        var linkCard = new CheckoutCardFacts(linkSet.CollectsMoney, linkPayments.SealedSecretKeyField is not null, true, true, development);
        Assert.False(linkCard.ThroughGateway);

        // ── والقُرّاءُ في الكود — بابُ الباقاتِ الثَلاثَةُ تَقرَأُ `PlansPayable`، و`PlanPay` تُصَيِّرُ الرابِطَ وَحدَه ───────
        Assert.Contains("plans = PlanPurchasePolicy.Visible(all, (await Providers.ForAsync(Slug)).PlansPayable);", Page("Plans.razor"), StringComparison.Ordinal);
        Assert.Contains("plansPayable = (await Providers.ForAsync(Slug)).PlansPayable;", Page("StudioAppPlans.razor"), StringComparison.Ordinal);
        var endpoints = Source("libs", "templates", "ACommerce.Templates.Customer.Marketplace", "MarketplaceTemplateExtensions.cs");
        var subscribe = Segment(endpoints, "app.MapPost(\"/{slug}/plans/{planId}/subscribe\"", ".RequireStoreWritable();");
        Assert.Contains("var configured = (await providers.ForAsync(slug)).PlansPayable;", subscribe, StringComparison.Ordinal);
        var planReaders = new[] { WriteEndpointGuardTests.StripMarkupComments(Page("Plans.razor")), WriteEndpointGuardTests.StripMarkupComments(Page("StudioAppPlans.razor")), subscribe };
        Assert.All(planReaders, r => Assert.DoesNotContain("PaymentProviderConfigured", WriteEndpointGuardTests.StripComments(r), StringComparison.Ordinal));
        var planPay = Page("PlanPay.razor");
        Assert.Contains("link = set.For(ProviderCapabilities.Payments)?.PaymentLink;", planPay, StringComparison.Ordinal);
        Assert.Contains("else if (link is null)", planPay, StringComparison.Ordinal);

        // ── وإتمامُ الشِراءِ يَقبِضُ بِمِفتاحِ المَتجَرِ حَيثُ رُبِط: الطَريقُ مِن المُدخَلِ نَفسِه، والحَجزُ عِندَ مُزَوِّدِ النُسخَةِ خَلفَ حارِسِه ─────
        var submit = Segment(endpoints, "app.MapPost(\"/{slug}/checkout/submit\"", ".RequireStoreWritable();");
        Assert.Contains("var viaGateway = Services.Deals.CheckoutPaymentPolicy.CallsProvider(payMethod) && card.ThroughGateway;", submit, StringComparison.Ordinal);
        var guard = submit.IndexOf("if (!viaGateway)", StringComparison.Ordinal);
        var hold = submit.IndexOf("payments.AuthorizeAsync(", StringComparison.Ordinal);
        Assert.True(guard > 0 && hold > guard, $"guard={guard} hold={hold}");
        Assert.Contains("await gateway.OpenInvoiceAsync(", submit, StringComparison.Ordinal);
    }
}
