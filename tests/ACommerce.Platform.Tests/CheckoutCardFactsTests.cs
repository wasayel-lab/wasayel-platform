using ACommerce.Kit.Payments;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ البِطاقَةُ في إتمامِ الشِراءِ بِمِفتاحِ المَتجَر — ثَلاثَةُ مُدخَلاتٍ تُحسَبُ مَرَّةً، وقَرارٌ نَقِيّ ═══════════════════════
//
// ‏ADR-032 الخُطوَة 8 — شَطرُ `CardOffered` (تَيّارُ S9، ‏2026-10-12): البِطاقَةُ تُعرَضُ بِمِفتاحِ المَتجَرِ حينَ تَجتَمِعُ (أ) رَبطُ دَفعٍ
// بِمِفتاحٍ سِرِّيٍّ مَختوم، (ب) خِزانَةٌ تَفُكّ، (ج) عَميلُ البَوّابَةِ مُسَجَّل — ولا يُسأَلُ لَها مُزَوِّدُ النُسخَة. ومَتجَرٌ بِلا مِفتاحٍ عَلى
// قَرارِ الموجَةِ الأولى بِحَرفِه: عَلَمُه ومُزَوِّدُ نُسخَةٍ يَقبِضُ فِعلاً — وفي الإنتاجِ لا يَقبِض. فَفي الإنتاجِ: البِطاقَةُ ⇔ (أ)∧(ب)∧(ج).
//
// **ولِماذا لا «⇔ (أ)∧(ب)∧(ج)» في كُلِّ بيئَة** (يُقالُ في ‏ADR-032 §٨‑د): صَفُّ الموجَةِ الأولى
// `A_pasted_link_alone_no_longer_offers_a_card_when_the_production_provider_cannot_charge` يُثَبِّتُ أَنّ الرابِطَ وَحدَه في التَطويرِ
// يَعرِضُ البِطاقَةَ عَلى المُحاكي «كَما كانَت»، و‏ADR-025 بَنى وَضعَ التَجرِبَةِ بِطَلَبِ المالِك — والصَفُّ أَخضَرُ هُنا بِلا تَعديلِ حَرف.
// فَالمَحصورُ هُوَ ما قُصِد: مَتجَرٌ بِمِفتاحٍ لا يَقبِضُ بِغَيرِ مِفتاحِه في أَيِّ بيئَة، ومَتجَرٌ بِلا مِفتاحٍ لا بِطاقَةَ لَه في الإنتاج.
//
// والعَدّاداتُ تُطبَعُ وتَفشَلُ عِندَ الصِفر (القاعِدَة ١٠).

public class CheckoutCardFactsTests(ITestOutputHelper output)
{
    private const string Slug = TenantGatewayEndpointsTests.Slug;

    private static readonly (string Name, bool CanCharge)[] Environments =
    {
        ("production",  PaymentChargeSurface.CanCharge(new UnavailablePaymentProvider())),
        ("development", PaymentChargeSurface.CanCharge(new MockPaymentProvider())),
        ("simulation",  PaymentChargeSurface.CanCharge(new SimulatedPaymentProvider())),
    };

    // ═══ ١) القَرارُ النَقِيّ — مُوجِبٌ، وسالِبٌ لِكُلِّ مُدخَل ══════════════════════════════════════════════════════

    [Fact]
    public void The_environments_are_what_the_rows_below_claim()
    {
        Assert.Equal(new[] { false, true, true }, Environments.Select(e => e.CanCharge).ToArray());
    }

    /// <summary><b>المُوجِب</b>: الثَلاثَةُ مَعاً ⇒ البِطاقَةُ مَعروضَةٌ وطَريقُها البَوّابَة — في كُلِّ بيئَة، وبِعَلَمِ مَتجَرٍ لَم يُزامَن أَيضاً
    /// (رَبطٌ خُتِمَ قَبلَ أَن يَعُدَّه <c>CollectsMoney</c>). و<c>Decide</c> تَقبَلُ البِطاقَةَ بِالمُدخَلَينِ نَفسَيهِما.</summary>
    [Fact]
    public void All_three_gateway_inputs_offer_the_card_through_the_merchant_key_in_every_environment()
    {
        var cases = 0;
        foreach (var (env, canCharge) in Environments)
        foreach (var storeFlag in new[] { true, false })
        {
            var f = new CheckoutCardFacts(storeFlag, SealedKeyBound: true, VaultCanUnseal: true, GatewayClientRegistered: true, canCharge);
            Assert.True(f.CardOffered, env);
            Assert.True(f.ThroughGateway, env);
            Assert.Equal((CheckoutPayMethods.Card, (string?)null), CheckoutPaymentPolicy.Decide("card", f.Collects, f.ChargerCanCharge));
            cases++;
        }
        output.WriteLine($"المُوجِب: {cases} حالَة");
        Assert.Equal(6, cases);
    }

    /// <summary><b>السالِبُ لِـ(ب) ولِـ(ج)</b>: مِفتاحٌ مَربوطٌ وخِزانَةٌ لا تَفُكُّ — أَو عَميلٌ غَيرُ مُسَجَّل — ⇒ لا بِطاقَة، في كُلِّ بيئَة؛
    /// <b>ولا تَسقُطُ إلى مُزَوِّدِ النُسخَة</b> ولو كانَ المُحاكي أَو التَجرِبَة: الطَريقُ ما زالَ البَوّابَة، و<c>Decide</c> تَرُدُّ بِرَمزِها.</summary>
    [Theory]
    [InlineData("vault")]
    [InlineData("client")]
    public void A_missing_vault_or_client_hides_the_card_and_never_falls_back_to_the_instances_provider(string missing)
    {
        var cases = 0;
        foreach (var (env, canCharge) in Environments)
        foreach (var storeFlag in new[] { true, false })
        {
            var f = new CheckoutCardFacts(storeFlag, SealedKeyBound: true,
                VaultCanUnseal: missing != "vault", GatewayClientRegistered: missing != "client", canCharge);
            Assert.False(f.CardOffered, $"{missing}/{env}");
            Assert.True(f.ThroughGateway, $"{missing}/{env}");
            var (method, refusal) = CheckoutPaymentPolicy.Decide("card", f.Collects, f.ChargerCanCharge);
            Assert.Equal(CheckoutPayMethods.CashOnDelivery, method);
            Assert.Equal(CheckoutPayMethods.CardUnavailable, refusal);
            cases++;
        }
        output.WriteLine($"السالِبُ بِلا {missing}: {cases} حالَة");
        Assert.Equal(6, cases);
    }

    /// <summary><b>السالِبُ لِـ(أ)</b>: بِلا مِفتاحٍ مَختومٍ لا تَمُرُّ البِطاقَةُ بِالبَوّابَةِ أَبَداً — ولو كانَت الخِزانَةُ والعَميلُ حاضِرَين —
    /// <b>ولا تُعرَضُ في الإنتاجِ أَبَداً</b> (‏<c>UnavailablePaymentProvider</c>)؛ وخارِجَه قَرارُ الموجَةِ الأولى بِحَرفِه: عَلَمُ المَتجَرِ ومُزَوِّدُ
    /// نُسخَةٍ يَقبِض — الدالَّةُ القائِمَةُ نَفسُها لا نُسخَةٌ مِنها.</summary>
    [Fact]
    public void Without_a_sealed_key_the_gateway_is_never_the_path_and_production_never_offers_the_card()
    {
        var cases = 0;
        foreach (var (env, canCharge) in Environments)
        foreach (var storeFlag in new[] { true, false })
        foreach (var vault in new[] { true, false })
        foreach (var client in new[] { true, false })
        {
            var f = new CheckoutCardFacts(storeFlag, SealedKeyBound: false, vault, client, canCharge);
            Assert.False(f.ThroughGateway);
            Assert.Equal(CheckoutPaymentPolicy.CardOffered(storeFlag, canCharge), f.CardOffered);
            if (env == "production") Assert.False(f.CardOffered);
            cases++;
        }
        output.WriteLine($"بِلا مِفتاح: {cases} حالَة");
        Assert.Equal(24, cases);
    }

    /// <summary><b>وفي الإنتاجِ الجَدوَلُ كامِلاً</b>: لِكُلِّ تَشكيلَةٍ مِن عَلَمِ المَتجَرِ والثَلاثَة — البِطاقَةُ ⇔ (أ)∧(ب)∧(ج). والمُوجِبُ
    /// والسالِبُ يُعَدّانِ ولا يَكونُ أَيٌّ مِنهُما صِفراً.</summary>
    [Fact]
    public void In_production_the_card_is_offered_exactly_when_the_three_gateway_inputs_hold()
    {
        var production = Environments.Single(e => e.Name == "production").CanCharge;
        var offered = 0;
        var hidden = 0;
        foreach (var storeFlag in new[] { true, false })
        foreach (var key in new[] { true, false })
        foreach (var vault in new[] { true, false })
        foreach (var client in new[] { true, false })
        {
            var f = new CheckoutCardFacts(storeFlag, key, vault, client, production);
            Assert.Equal(key && vault && client, f.CardOffered);
            if (f.CardOffered) offered++; else hidden++;
        }
        output.WriteLine($"الإنتاج: {offered + hidden} تَشكيلَة — تُعرَضُ في {offered}، وتُخفى في {hidden}");
        Assert.Equal((2, 14), (offered, hidden));
    }

    // ═══ ٢) المُدخَلاتُ مِن المُخَزَّن — `GatewayPayments.CardFactsAsync` تَحسُبُها مَرَّةً، ولِكُلٍّ غِيابُه ═════════════

    /// <summary>عالَمُ رَبطٍ واحِد: وَثيقَةُ رَبطِ الدَفعِ مَختومَةً بِخِزانَةِ <paramref name="sealing"/> (أَو <c>null</c> فَلا رَبط)، وخِدمَةٌ
    /// بِخِزانَةِ <paramref name="service"/> وعَميلٍ أَو بِدونِه.</summary>
    private static (DocWorld Docs, GatewayPayments Gateway, MoyasarScriptedHandler Moyasar) WorldOf(
        ProviderVault sealing, ProviderVault service, bool withBinding = true, bool withClient = true,
        string status = TenantProviderBinding.StatusActive)
    {
        var docs = new DocWorld();
        if (withBinding) docs.Put(TenantGatewayEndpointsTests.PaymentsBinding(sealing, status: status));
        var moyasar = new MoyasarScriptedHandler();
        var deals = new DealsService(docs.Store, new DealMoneyClaimFirstTests.RecordingPaymentProvider());
        var gateway = new GatewayPayments(deals, new TenantProviderService(docs.Store), service,
            withClient ? new MoyasarClient(new HttpClient(moyasar), NullLogger<MoyasarClient>.Instance) : null);
        return (docs, gateway, moyasar);
    }

    /// <summary><b>المُوجِب</b>: رَبطٌ مَختومٌ وخِزانَةٌ تَفُكُّ وعَميلٌ ⇒ الثَلاثَةُ حاضِرَة، ويُمَرَّرُ المُدخَلانِ القَديمانِ كَما جاءا — <b>بِلا نِداءٍ
    /// لِلبَوّابَةِ ولا حِفظ</b>: المُدخَلاتُ قِراءَةٌ لا أَثَر.</summary>
    [Fact]
    public async Task The_service_reads_the_three_inputs_from_the_live_binding_the_vault_and_the_registration_without_writing()
    {
        var vault = TenantGatewayEndpointsTests.NewVault();
        var (docs, gateway, moyasar) = WorldOf(vault, vault);
        var saves = docs.SaveCalls;

        var f = await gateway.CardFactsAsync(Slug, storeCollects: false, instanceCanCharge: false);

        Assert.Equal(new CheckoutCardFacts(false, SealedKeyBound: true, VaultCanUnseal: true, GatewayClientRegistered: true, false), f);
        Assert.True(f.CardOffered);
        Assert.Empty(moyasar.Requests);
        Assert.Equal(saves, docs.SaveCalls);
    }

    /// <summary><b>والسالِبُ لِكُلِّ مُدخَلٍ بِغِيابِه وَحدَه</b> — والبَقِيَّةُ حاضِرَة: (أ) لا رَبط، أَو رَبطٌ مَسحوب؛ (ب) خِزانَةُ الخِدمَةِ بِلا
    /// مِفتاحٍ رَئيس؛ (ج) خِدمَةٌ بِلا عَميل. وفي كُلٍّ لا بِطاقَة — والإنتاجُ يُمَرَّرُ (مُزَوِّدُ النُسخَةِ لا يَقبِض) فَلا مَهرَبَ إلَيه.</summary>
    [Theory]
    [InlineData("no_binding")]
    [InlineData("withdrawn_binding")]
    [InlineData("no_vault")]
    [InlineData("no_client")]
    public async Task Each_input_is_measured_by_its_own_absence(string absent)
    {
        var vault = TenantGatewayEndpointsTests.NewVault();
        var (_, gateway, moyasar) = absent switch
        {
            "no_binding"        => WorldOf(vault, vault, withBinding: false),
            "withdrawn_binding" => WorldOf(vault, vault, status: TenantProviderBinding.StatusRevoked),
            "no_vault"          => WorldOf(vault, ProviderVault.FromKek(null)),
            "no_client"         => WorldOf(vault, vault, withClient: false),
            _ => throw new ArgumentOutOfRangeException(nameof(absent)),
        };

        var f = await gateway.CardFactsAsync(Slug, storeCollects: true, instanceCanCharge: false);
        output.WriteLine($"{absent}: {f}");

        Assert.Equal(absent is not ("no_binding" or "withdrawn_binding"), f.SealedKeyBound);
        Assert.Equal(absent != "no_vault", f.VaultCanUnseal);
        Assert.Equal(absent != "no_client", f.GatewayClientRegistered);
        Assert.False(f.CardOffered);
        Assert.Empty(moyasar.Requests);
    }

    /// <summary><b>(ج) مَقيساً مِن الحَقنِ نَفسِه</b>: حاوِيَةٌ بِلا تَسجيلِ العَميلِ تَبني الخِدمَةَ ولا تَرمي، ومُدخَلُها «غَيرُ مُسَجَّل»؛ وبِتَسجيلِه
    /// بِنِداءِ القالِبِ نَفسِه (<c>AddMoyasarGateway</c>) «مُسَجَّل».</summary>
    [Fact]
    public async Task The_client_input_is_what_the_container_registered()
    {
        foreach (var registered in new[] { false, true })
        {
            var docs = new DocWorld();
            var vault = TenantGatewayEndpointsTests.NewVault();
            docs.Put(TenantGatewayEndpointsTests.PaymentsBinding(vault));

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IDocumentStore>(docs.Store);
            services.AddSingleton<IPaymentProvider>(new DealMoneyClaimFirstTests.RecordingPaymentProvider());
            services.AddSingleton(sp => new DealsService(sp.GetRequiredService<IDocumentStore>(), sp.GetRequiredService<IPaymentProvider>()));
            services.AddSingleton(new TenantProviderService(docs.Store));
            services.AddSingleton(vault);
            services.AddScoped<GatewayPayments>();
            if (registered) ACommerce.Kit.Payments.MoyasarClientExtensions.AddMoyasarGateway(services);

            await using var sp = services.BuildServiceProvider();
            await using var scope = sp.CreateAsyncScope();
            var f = await scope.ServiceProvider.GetRequiredService<GatewayPayments>()
                .CardFactsAsync(Slug, storeCollects: false, instanceCanCharge: false);
            output.WriteLine($"العَميلُ مُسَجَّل: {registered} ⇒ {f.GatewayClientRegistered} · البِطاقَة: {f.CardOffered}");

            Assert.Equal(registered, f.GatewayClientRegistered);
            Assert.Equal(registered, f.CardOffered);
        }
    }

    /// <summary><b>وخِدمَةٌ بِلا عَميلٍ لا تَفتَحُ فاتورَةً ولا تُثبِتُ دَفعاً</b>: <c>OpenInvoiceAsync</c> تُلغي الصَفَقاتِ بِرَمزِ «غَيرُ مَضبوط» ولا يُخصَمُ
    /// شَيء، و<c>VerifyAsync</c> تَرُدُّ بِه — فَلا بابَ يَبلُغُ نِداءً لا عَميلَ لَه، ولو تَجاوَزَ أَحَدٌ القَرار.</summary>
    [Fact]
    public async Task A_service_without_the_client_opens_no_invoice_and_proves_nothing()
    {
        var w = await TenantGatewayEndpointsTests.PaidCart(invoice: false);
        var bare = new GatewayPayments(w.Deals, new TenantProviderService(w.Docs.Store), TenantGatewayEndpointsTests.NewVault(), client: null);

        var opened = await bare.OpenInvoiceAsync(Slug, new DealPayer(TenantGatewayEndpointsTests.Buyer), w.Ids, "مَتجَر",
            "https://store.example/pay/return", new ACommerce.Platform.I18n.L(new Microsoft.AspNetCore.Http.HttpContextAccessor()));
        Assert.Equal(new GatewayCheckout(null, MoyasarFailures.NotConfigured), opened);
        foreach (var id in w.Ids)
            Assert.Equal(DealStatus.Cancelled, (await w.Deals.LoadAsync(Slug, id))!.Status);
        Assert.Empty(w.Platform.Calls);

        var paid = await TenantGatewayEndpointsTests.PaidCart();
        var verify = await new GatewayPayments(paid.Deals, new TenantProviderService(paid.Docs.Store), TenantGatewayEndpointsTests.NewVault(), client: null)
            .VerifyAsync(Slug, new DealPayer(TenantGatewayEndpointsTests.Buyer), paid.Ids[0].ToString("N"));
        Assert.Equal(MoyasarFailures.NotConfigured, verify.Code);
        Assert.Empty(paid.Moyasar.Requests);
    }
}
