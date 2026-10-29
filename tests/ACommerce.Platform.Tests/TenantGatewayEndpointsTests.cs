using System.Net;
using System.Security.Cryptography;
using System.Text;
using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Payments;
using ACommerce.Platform.I18n;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Billing;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Providers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ العَودَةُ مِن بَوّابَةِ المَتجَرِ ونُقطَةُ التَحَقُّق — ‏ADR-032 §٢‑هـ الخُطوَة 9 ═════════════════════════════
//
// **ما يُقاسُ هُنا**: (١) سُلطَةُ الدافِعِ بِرُموزِها الثَلاثَة — مُوجَبٌ وسالِبٌ لِكُلٍّ؛ (٢) عَقدُ صَفحَةِ العَودَة — قِراءَةُ المَرجِعِ
// ومَسارُ الرَدِّ ومَعجَمُ الشَريطِ بِنُصوصٍ حَيَّةٍ في القامُوس؛ (٣) الخِدمَةُ: السُلطَةُ قَبلَ فَكِّ المِفتاحِ وقَبلَ البَوّابَة، والمِفتاحُ
// المَفكوكُ هُوَ ما يَبلُغُ رَأسَ المُصادَقَة، والبُرهانُ مِن `GatewayPaymentProof.Gate`، و**صِفرُ كِتابَةٍ** في هذِه الخُطوَة؛ (٤) النُقطَةُ
// مُشَغَّلَةً عَلى مُضيفٍ مُصَغَّر: الحارِسُ قَبلَ أَيِّ حَقلٍ **سُلوكاً لا نَصّاً**؛ (٥) الصَفحَةُ لا تَسأَلُ البَوّابَةَ ولا تَكتُب.
//
// **ولا مِفتاحَ بَوّابَةٍ حَقيقيّاً هُنا** — مُعالِجُ `MoyasarClientTests` يُعيدُ رُدودَ الوَثيقَة، والمِفتاحُ مَختومٌ بِخِزانَةٍ حَقيقِيَّةٍ
// ومِفتاحٍ رَئيسٍ عَشوائِيٍّ لِكُلِّ عالَم. والبُرهانُ الحَيُّ بِمِفتاحِ اختِبارٍ عِندَ المالِكِ في سِجِلِّه «ج».

/// <summary>لوغٌ يُلتَقَطُ نَصّاً — لِيُقاسَ أَنّ المِفتاحَ لا يَبلُغُه.</summary>
internal sealed class GatewayCapturingLogger : ILogger<GatewayPayments>
{
    public List<string> Lines { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) => Lines.Add(formatter(state, exception));
}

public class TenantGatewayEndpointsTests(ITestOutputHelper output)
{
    internal const string Slug = "zz-gateway";
    internal const string Key = MoyasarClientTests.Key;
    internal const string InvoiceId = MoyasarClientTests.InvoiceId;
    internal static readonly Guid Buyer = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    internal static readonly Guid Seller = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    internal static readonly Guid Other = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");

    private static string Root => ThemeZeroEquivalenceTests.RepoRoot;
    private static string Template(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { Root, "libs", "templates", "ACommerce.Templates.Customer.Marketplace" }.Concat(parts).ToArray()));

    // ═══ ١) سُلطَةُ الدافِع — ثَلاثَةُ رُموزٍ، مُوجَبٌ وسالِبٌ لِكُلٍّ ═══════════════════════════════════════

    private static Deal PaidByGateway(Guid initiator, string? invoice = InvoiceId)
    {
        var d = new Deal { Id = Guid.NewGuid(), TenantSlug = Slug, InitiatorId = initiator, AmountSar = 150m };
        if (invoice is not null) d.Refs[GatewayPaymentProof.InvoiceRef] = invoice;
        return d;
    }

    [Fact]
    public void The_lexicon_is_three_codes_and_deal_not_found_is_the_accept_lexicons_own()
    {
        Assert.Equal(new[] { "deal_not_found", "actor_not_payer", "gateway_invoice_missing" }, DealPayAuthorization.All);
        Assert.Equal(DealAcceptAuthorization.DealNotFound, DealPayAuthorization.DealNotFound);
        Assert.Equal(DealPayAuthorization.All.Count, DealPayAuthorization.All.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void The_buyer_of_a_deal_carrying_its_invoice_may_verify_it()
    {
        var deal = PaidByGateway(Buyer);
        Assert.Null(DealPayAuthorization.Validate(deal, new DealPayer(Buyer)));
        Assert.True(DealPayAuthorization.IsAllowed(deal, new DealPayer(Buyer)));

        // ولا يَسأَلُ عَن حالِ الصَفقَة: دَفعٌ وَقَعَ ثُمَّ أُلغِيَت الصَفقَةُ يُثبَتُ ولا يُبتلَع.
        deal.Status = DealStatus.Cancelled;
        Assert.Null(DealPayAuthorization.Validate(deal, new DealPayer(Buyer)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void No_deal_is_deal_not_found_and_a_present_one_is_not(bool present)
    {
        var refusal = DealPayAuthorization.Validate(present ? PaidByGateway(Buyer) : null, new DealPayer(Buyer));
        Assert.Equal(present ? null : DealPayAuthorization.DealNotFound, refusal?.Code);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Another_users_deal_is_actor_not_payer_and_ones_own_is_not(bool foreign)
    {
        var refusal = DealPayAuthorization.Validate(PaidByGateway(foreign ? Other : Buyer), new DealPayer(Buyer));
        Assert.Equal(foreign ? DealPayAuthorization.ActorNotPayer : null, refusal?.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(InvoiceId)]
    public void A_deal_without_a_recorded_invoice_is_gateway_invoice_missing_and_one_with_it_is_not(string? invoice)
    {
        var deal = PaidByGateway(Buyer, invoice);
        var refusal = DealPayAuthorization.Validate(deal, new DealPayer(Buyer));
        Assert.Equal(string.IsNullOrWhiteSpace(invoice) ? DealPayAuthorization.InvoiceMissing : null, refusal?.Code);
    }

    // ═══ ٢) عَقدُ صَفحَةِ العَودَة ════════════════════════════════════════════════════════════

    /// <summary><b>المَرجِعُ مِن رَأسِ القيمَة</b>: المُعَرِّفُ بِصيغَةِ <c>N</c>، وما يُلحَقُ بَعدَه بِلا «&amp;» صَحيحَة لا يُضَيِّعُه —
    /// الوَثيقَةُ لا تَقولُ أَتُلحِقُ البَوّابَةُ مُعامِلاتٍ بِرابِطِ العَودَة. وما سِواه <c>null</c>.</summary>
    [Fact]
    public void The_reference_is_read_from_the_head_of_the_value_and_nothing_else_parses()
    {
        var id = Guid.NewGuid();
        Assert.Equal(id, GatewayReturn.DealIdOf(id.ToString("N")));
        Assert.Equal(id, GatewayReturn.DealIdOf(id.ToString("N") + "?id=7c2f8a1e&status=paid"));
        Assert.Null(GatewayReturn.DealIdOf(id.ToString("D")));
        Assert.Null(GatewayReturn.DealIdOf("not-a-deal"));
        Assert.Null(GatewayReturn.DealIdOf(id.ToString("N")[..31]));
        Assert.Null(GatewayReturn.DealIdOf(""));
        Assert.Null(GatewayReturn.DealIdOf(null));
    }

    [Fact]
    public void The_return_path_carries_the_deal_and_the_code_and_a_proven_payment_goes_to_the_deal_page()
    {
        var id = Guid.Parse("11111111-2222-4333-8444-555555555555");
        Assert.Equal("pay/return?deal=11111111222243338444555555555555&err=gateway_invoice_unpaid",
            GatewayReturn.PathFor(id, GatewayPaymentProof.InvoiceUnpaid));
        Assert.Equal("pay/return?err=deal_not_found", GatewayReturn.PathFor(null, DealPayAuthorization.DealNotFound));
        Assert.Equal("pay/return?deal=11111111222243338444555555555555", GatewayReturn.PathFor(id, null));
        Assert.Equal("pay/return", GatewayReturn.PathFor(null, null));

        Assert.Equal("pay/return?deal=11111111222243338444555555555555&err=gateway_refused",
            GatewayReturn.AfterVerify(new GatewayVerifyResult(id, null, MoyasarFailures.Refused)));
        var proven = GatewayPaymentProof.Gate(
            new MoyasarInvoice(InvoiceId, "paid", 15000, "SAR", null, GatewayPaymentProof.MetadataFor(new[] { id })),
            new[] { new Deal { Id = id, AmountSar = 150m, Refs = { [GatewayPaymentProof.InvoiceRef] = InvoiceId } } }).Payment;
        Assert.NotNull(proven);
        Assert.Equal($"deals/{id}", GatewayReturn.AfterVerify(new GatewayVerifyResult(id, proven, null)));
    }

    /// <summary><b>المَعجَمُ اتِّحادُ المَعاجِمِ الثَلاثَة</b> — سُلطَةُ الدافِعِ وفَشَلُ العَميلِ وبَوّابَةُ البُرهان — ثَلاثَةَ عَشَرَ بِلا تَكرار؛
    /// والمِفتاحُ بِالبادِئَةِ في المَقطَعِ الثالِث؛ وما خارِجَه <c>null</c>.</summary>
    [Fact]
    public void The_strip_lexicon_is_the_union_of_the_three_lexicons_with_one_key_each()
    {
        // (‏2026-10-29، قَرارُ المالِك د5 — ولا يُعادُ حَرفٌ فَوقَه: ومَعَها رَمزُ حِصَّةِ النَقَرات `gateway_verify_rate_limited` — خَمسَةَ عَشَر.)
        var expected = DealPayAuthorization.All.Concat(MoyasarFailures.All).Concat(GatewayPaymentProof.Codes).Append(GatewayReturn.VerifyRateLimited)
            .OrderBy(c => c, StringComparer.Ordinal);
        Assert.Equal(expected, GatewayReturn.Codes.OrderBy(c => c, StringComparer.Ordinal));
        Assert.Equal(15, GatewayReturn.Codes.Count);   // 13 ← 14 (‏2026-10-13، G5 (xvii-g): +gateway_invoice_closed) ← 15 (‏2026-10-29، د5)
        Assert.All(GatewayReturn.Codes, c => Assert.Equal(GatewayReturn.TextPrefix + c, GatewayReturn.KeyFor(c)));
        Assert.All(GatewayReturn.Codes, c => Assert.Equal(3, GatewayReturn.KeyFor(c)!.Split('.').Length));
        Assert.Null(GatewayReturn.KeyFor("not_a_code"));
        Assert.Null(GatewayReturn.KeyFor(DealsService.ChangedMeanwhile));
        Assert.Null(GatewayReturn.KeyFor(null));
    }

    /// <summary><b>لِكُلِّ رَمزٍ نَصٌّ عَرَبِيٌّ حَيّ</b> — لا نائِبٌ ولا المِفتاحُ خاماً — ولِنُصوصِ الصَفحَةِ الأَربَعَةِ كَذلك. والعَدّادُ يُطبَع.</summary>
    [Fact]
    public void Every_strip_code_and_every_page_text_has_a_live_arabic_text()
    {
        var keys = GatewayReturn.Codes.Select(c => GatewayReturn.KeyFor(c)!)
            .Concat(new[] { "pay.return.title", "pay.return.need_login", "pay.return.pending", "pay.return.verify" })
            .ToArray();
        var checkedCount = 0;
        foreach (var key in keys)
        {
            var text = LocaleCatalog.Text(LocaleCatalog.Arabic, key);
            Assert.NotEqual(key, text);
            Assert.False(LocaleCatalog.IsPlaceholder(text), $"‏{key} نائِب.");
            Assert.True(text.Length > 8, $"‏{key} بِنَصٍّ أَقصَرَ مِن أَن يُفهَم.");
            checkedCount++;
        }
        output.WriteLine($"· نُصوصُ صَفحَةِ العَودَة: {checkedCount} مِفتاحاً حَيّاً (‏{GatewayReturn.Codes.Count} رَمزاً + ‏4 نُصوص).");
        Assert.Equal(19, checkedCount);   // 17 ← 18 (‏2026-10-13، G5 (xvii-g)) ← 19 (‏2026-10-29، قَرارُ المالِك د5 (٥): +gateway_verify_rate_limited)
    }

    // ═══ ٣) الخِدمَة — السُلطَةُ قَبلَ المِفتاح، والمِفتاحُ قَبلَ البَوّابَة، وصِفرُ كِتابَة ═══════════════════════

    /// <summary>عالَمُ البَوّابَة — <c>internal</c> (‏2026-10-12، ‏ADR-032 الخُطوَة 10): <c>GatewayPaymentConfirmTests</c> يَبني عَلَيه التَأكيدَ
    /// وإتمامَ الشِراء — نُسخَتانِ مِن أَداةِ قِياسٍ تَنجَرِفان.</summary>
    internal sealed record World(
        DocWorld Docs, DealsService Deals, GatewayPayments Gateway, MoyasarScriptedHandler Moyasar,
        DealMoneyClaimFirstTests.RecordingPaymentProvider Platform, Guid[] Ids, GatewayCapturingLogger Log);

    /// <summary>تَعريفُ الدَفعِ الَّذي يَحمِلُ مِفتاحاً سِرِّيّاً مَختوماً — مِن الكاتالوجِ لا بِسلاجِه.</summary>
    internal static ProviderDefinition SealedKeyDefinition()
        => ProviderCatalog.Definitions.Single(d => d.Capability == ProviderCapabilities.Payments
            && d.Credential.Fields.Any(f => f.Kind == CredentialKinds.SecretKey && ProviderVault.Seals(f.Kind)));

    /// <summary>رَبطُ الدَفعِ كَما تَكتُبُه نُقطَةُ الرَبط — مِن <c>ProviderBindSurface.FromForm</c> نَفسِها، مَختوماً بِسِياقِ
    /// <paramref name="sealedFor"/> (مَتجَرُ العالَمِ افتِراضاً، ومَتجَرٌ آخَرُ لِظَرفٍ مَنسوخ).</summary>
    internal static TenantProviderBinding PaymentsBinding(ProviderVault vault, string sealedFor = Slug,
        string status = TenantProviderBinding.StatusActive)
    {
        var read = ProviderBindSurface.FromForm(SealedKeyDefinition().Slug, _ => Key, sealedFor, vault);
        Assert.Null(read.RefusalCode);
        return new TenantProviderBinding
        {
            Id = read.Capability, Slug = read.Capability, TenantSlug = Slug, ProviderSlug = read.ProviderSlug,
            Status = status, Values = new Dictionary<string, StoredValue>(read.Values, StringComparer.Ordinal),
            BoundBy = "owner", BoundAt = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc),
        };
    }

    internal static ProviderVault NewVault() => ProviderVault.FromKek(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

    /// <summary>
    /// <para>عالَمُ سَلَّةٍ دُفِعَت بِفاتورَةٍ واحِدَة: صَفقَتانِ لِلمُشتَري (‏100 و‏50 ريالاً = ‏15000 هَلَلَة) عَلى كُلٍّ مِنهُما مُعَرِّفُ
    /// الفاتورَة، ورَبطُ دَفعٍ بِمِفتاحٍ مَختوم. <paramref name="binding"/> يُبنى بِخِزانَةِ العالَم — أَو <c>null</c> فَلا رَبط؛
    /// و<paramref name="serviceVault"/> خِزانَةُ الخِدمَةِ إن خالَفَت خِزانَةَ الخَتم.</para>
    /// <para>و<paramref name="withSeller"/> (‏2026-10-12، ‏ADR-032 الخُطوَة 10): بائِعٌ مالِكُ إعلانِ كُلِّ صَفقَةٍ مَربوطٌ طَرَفاً كَما يَربِطُه
    /// إتمامُ الشِراء — لِيُقبَلَ ويُؤَكَّدَ ويُشعَر؛ و<paramref name="invoice"/> <c>false</c> يُولِدُها بِلا فاتورَة (لِفَتحِها).</para>
    /// </summary>
    /// <remarks>و<paramref name="bornForGateway"/> (‏2026-10-13، G5 (xvi-g)): تولَدُ الصَفَقاتُ بِعَلامَةِ طَريقِ البَوّابَةِ كَما يُولِدُها
    /// <c>/checkout/submit</c> — بِـ<see cref="GatewayPayments.MarkedAtBirth"/> نَفسِها؛ والافتِراضُ بِلا عَلامَةٍ فَلا يَتَحَرَّكُ صَفٌّ قائِم.</remarks>
    /// <remarks>و<paramref name="clock"/> (‏2026-10-29، قَرارُ المالِك د5 (٣)): ساعَةُ الخِدمَةِ لِتاريخِ انتِهاءِ الفاتورَة — والافتِراضُ ساعَةُ النِظام.</remarks>
    internal static async Task<World> PaidCart(
        Func<ProviderVault, TenantProviderBinding?>? binding = null, ProviderVault? serviceVault = null,
        bool withSeller = false, bool invoice = true, bool bornForGateway = false, TimeProvider? clock = null)
    {
        var docs = new DocWorld();
        var platform = new DealMoneyClaimFirstTests.RecordingPaymentProvider();
        var deals = new DealsService(docs.Store, platform);
        var ids = new List<Guid>();
        foreach (var amount in new[] { 100m, 50m })
        {
            var deal = await deals.StartAsync(Slug, "marketplace", Buyer, "مُشتَرٍ", null, "سِلعَة", amount, GatewayPayments.MarkedAtBirth(new()
            {
                [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.Card,
            }, bornForGateway));
            if (invoice) await deals.AttachRefAsync(Slug, deal.Id, GatewayPaymentProof.InvoiceRef, InvoiceId);
            if (withSeller)
            {
                await deals.AttachRefAsync(Slug, deal.Id, DealsService.ListingOwnerRef, Seller.ToString());
                await deals.BindListingOwnerAsync(Slug, deal.Id, Seller.ToString());
            }
            ids.Add(deal.Id);
        }

        var vault = NewVault();
        if ((binding ?? (v => PaymentsBinding(v)))(vault) is { } b) docs.Put(b);

        var moyasar = new MoyasarScriptedHandler();
        var log = new GatewayCapturingLogger();
        var gateway = new GatewayPayments(deals, new TenantProviderService(docs.Store), serviceVault ?? vault,
            new MoyasarClient(new HttpClient(moyasar), NullLogger<MoyasarClient>.Instance), log, clock);
        return new World(docs, deals, gateway, moyasar, platform, ids.ToArray(), log);
    }

    internal static string PaidInvoice(World w, string status = "paid", IReadOnlyList<Guid>? listed = null)
        => MoyasarClientTests.InvoiceJson(status, 15000, metadata: GatewayPaymentProof.MetadataFor(listed ?? w.Ids));

    internal static string BasicOf(string key) => Convert.ToBase64String(Encoding.UTF8.GetBytes(key + ":"));

    /// <summary>
    /// <para><b>المُوجِبُ كامِلاً</b>: صَفقَةُ المُشتَري المَرجِعُ ← المِفتاحُ يُفَكُّ ← <c>GET /v1/invoices/{id}</c> واحِدٌ بِـ
    /// <c>Authorization: Basic</c> مِن <b>المِفتاحِ المَفكوكِ نَفسِه</b> ← البَوّابَةُ تُثبِتُ الدَفعَ بِصَفقَتَيه. <b>وصِفرُ نِداءٍ لِمُزَوِّدِ
    /// النُسخَة</b>، واللوغُ بِلا المِفتاح.</para>
    /// <para><b>قُلِبَ بِاسمِه ‏2026-10-12 (‏ADR-032 الخُطوَة 10، تَيّارُ S9 — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق)</b> — كانَ
    /// <c>A_paid_invoice_is_proven_with_the_unsealed_key_and_nothing_is_written</c>: «وصِفرُ كِتابَة — الكاتِبُ خُطوَةُ الصَفِّ العاشِر».
    /// وجاءَ الكاتِب: الدَفعُ الثابِتُ يُكتَبُ عَلى كُلِّ صَفقَةٍ مَرَّةً (<c>payment_id</c> الفاتورَة، و<c>payment_status = Captured</c>، وسَطرُ
    /// <c>gateway_paid</c>)، ويُعادُ ما كُتِبَ الآن؛ <b>والتَحَقُّقُ الثاني لا يَكتُبُ شَيئاً</b> — لا حِفظَ ولا وَثيقَة — ولا يُعيدُ صَفقَة.</para>
    /// </summary>
    [Fact]
    public async Task A_paid_invoice_is_proven_with_the_unsealed_key_and_recorded_once_on_every_deal()
    {
        var w = await PaidCart();
        w.Moyasar.Then(HttpStatusCode.OK, PaidInvoice(w)).Then(HttpStatusCode.OK, PaidInvoice(w));

        var res = await w.Gateway.VerifyAsync(Slug, new DealPayer(Buyer), w.Ids[0].ToString("N"));

        Assert.True(res.Ok, res.Code);
        Assert.Equal(w.Ids[0], res.DealId);
        Assert.Equal(InvoiceId, res.Payment!.InvoiceId);
        Assert.Equal(w.Ids.OrderBy(x => x), res.Payment.DealIds.OrderBy(x => x));
        Assert.Equal((15000L, "SAR"), (res.Payment.Amount, res.Payment.Currency));
        Assert.Equal(w.Ids.OrderBy(x => x), res.Confirmed.OrderBy(x => x));

        var get = Assert.Single(w.Moyasar.Requests);
        Assert.Equal((HttpMethod.Get, $"https://api.moyasar.com/v1/invoices/{InvoiceId}"), (get.Method, get.RequestUri!.ToString()));
        Assert.Equal(("Basic", BasicOf(Key)), (get.Headers.Authorization!.Scheme, get.Headers.Authorization.Parameter));

        foreach (var id in w.Ids)
        {
            var deal = w.Docs.Read<Deal>(id.ToString())!;
            Assert.Equal((InvoiceId, "Captured"), (deal.Refs["payment_id"], deal.Refs["payment_status"]));
            var line = Assert.Single(deal.Timeline, e => e.Action == DealEvent.GatewayPaid);
            Assert.Equal((Buyer, DealStage.Offered), (line.ActorId!.Value, line.StageAfter));
            Assert.Equal(DealStage.Offered, deal.Stage);   // لا قَفزَ فَوقَ المَراحِل: «دَفع» بَعدَ «تأكيد».
        }
        Assert.Empty(w.Platform.Calls);
        Assert.DoesNotContain(w.Log.Lines, l => l.Contains(Key, StringComparison.Ordinal) || l.Contains(Key[^4..], StringComparison.Ordinal));
        Assert.Contains(w.Log.Lines, l => l.Contains("proven", StringComparison.Ordinal));

        // ‏والثاني: يُثبِتُ ولا يَكتُب.
        var (saves, stored) = (w.Docs.SaveCalls, w.Docs.Stored.Count);
        var again = await w.Gateway.VerifyAsync(Slug, new DealPayer(Buyer), w.Ids[1].ToString("N"));
        Assert.True(again.Ok, again.Code);
        Assert.Empty(again.Confirmed);
        Assert.Equal((saves, stored), (w.Docs.SaveCalls, w.Docs.Stored.Count));
        Assert.All(w.Ids, id => Assert.Single(w.Docs.Read<Deal>(id.ToString())!.Timeline, e => e.Action == DealEvent.GatewayPaid));
    }

    /// <summary><b>السُلطَةُ أَوَّلاً</b> — صَفقَةُ غَيرِه، أَو بِلا فاتورَة، أَو لا صَفقَة: رَمزُها، <b>وصِفرُ نِداءٍ لِلبَوّابَة</b>
    /// (المِفتاحُ لا يُفَكُّ أَصلاً)، وصِفرُ كِتابَة.</summary>
    [Theory]
    [InlineData("foreign", DealPayAuthorization.ActorNotPayer)]
    [InlineData("no_invoice", DealPayAuthorization.InvoiceMissing)]
    [InlineData("unknown", DealPayAuthorization.DealNotFound)]
    [InlineData("garbage", DealPayAuthorization.DealNotFound)]
    public async Task Authority_is_asked_before_the_key_and_the_gateway(string kind, string code)
    {
        var w = await PaidCart();
        var referenced = w.Ids[0];
        string? dealRef = referenced.ToString("N");
        var payer = new DealPayer(Buyer);
        switch (kind)
        {
            case "foreign": payer = new DealPayer(Other); break;
            case "no_invoice":
                w.Docs.Read<Deal>(referenced.ToString())!.Refs.Remove(GatewayPaymentProof.InvoiceRef); break;
            case "unknown": dealRef = Guid.NewGuid().ToString("N"); break;
            case "garbage": dealRef = "<script>"; break;
        }
        w.Moyasar.Then(HttpStatusCode.OK, PaidInvoice(w));
        var (saves, stored) = (w.Docs.SaveCalls, w.Docs.Stored.Count);

        var res = await w.Gateway.VerifyAsync(Slug, payer, dealRef);

        Assert.False(res.Ok);
        Assert.Equal(code, res.Code);
        Assert.Equal(kind is "foreign" or "no_invoice" ? referenced : null, res.DealId);
        Assert.Empty(w.Moyasar.Requests);
        Assert.Equal((saves, stored), (w.Docs.SaveCalls, w.Docs.Stored.Count));
    }

    /// <summary><b>لا مِفتاحَ يُفَكُّ ← <c>gateway_not_configured</c> بِلا نِداء</b>: لا رَبط، رَبطٌ مَسحوب، خِزانَةٌ بِلا مِفتاحٍ رَئيس، ظَرفٌ
    /// مَختومٌ بِسِياقِ مَتجَرٍ آخَر (مَنسوخٌ إلى هُنا)، وخِزانَةٌ بِمِفتاحٍ رَئيسٍ آخَر. والمُوجِبُ المُقابِلُ في الصَفِّ الأَوَّل.</summary>
    [Theory]
    [InlineData("no_binding")]
    [InlineData("revoked")]
    [InlineData("vault_unavailable")]
    [InlineData("sealed_for_another_store")]
    [InlineData("another_master_key")]
    public async Task No_key_that_opens_is_gateway_not_configured_without_a_call(string kind)
    {
        var w = kind switch
        {
            "no_binding"               => await PaidCart(_ => null),
            "revoked"                  => await PaidCart(v => PaymentsBinding(v, status: TenantProviderBinding.StatusRevoked)),
            "vault_unavailable"        => await PaidCart(serviceVault: ProviderVault.FromKek(null)),
            "sealed_for_another_store" => await PaidCart(v => PaymentsBinding(v, sealedFor: "zz-other-store")),
            "another_master_key"       => await PaidCart(serviceVault: NewVault()),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        w.Moyasar.Then(HttpStatusCode.OK, PaidInvoice(w));

        var res = await w.Gateway.VerifyAsync(Slug, new DealPayer(Buyer), w.Ids[0].ToString("N"));

        Assert.Equal(MoyasarFailures.NotConfigured, res.Code);
        Assert.Equal(w.Ids[0], res.DealId);
        Assert.Empty(w.Moyasar.Requests);
    }

    /// <summary><b>البَوّابَةُ تَحكُم — والخِدمَةُ لا تُليِّن</b>: فاتورَةٌ لَم تُدفَع؛ وفاتورَةٌ تُسَمّي صَفقَةَ غَيرِه — مَبلَغُها يُطابِقُ
    /// المَجموعَ لَو حُسِبَت صَفقَتُه، فَيَعبُرُ البُرهانُ لَو حُمِّلَت؛ ولا تُحَمَّل، فَأَوَّلُ خَرقٍ المَبلَغ (`gateway_amount_mismatch`)؛
    /// وفاتورَةٌ لا تُسَمّي المَرجِع؛ ورَفضُ البَوّابَة (‏404) — كُلٌّ بِرَمزِه، وصِفرُ كِتابَة.</summary>
    [Theory]
    [InlineData("unpaid", GatewayPaymentProof.InvoiceUnpaid)]
    [InlineData("lists_a_foreign_deal", GatewayPaymentProof.AmountMismatch)]
    [InlineData("omits_the_reference", GatewayPaymentProof.DealsMismatch)]
    [InlineData("gateway_404", MoyasarFailures.Refused)]
    public async Task The_gateway_and_the_proof_decide_and_the_service_does_not_soften(string kind, string code)
    {
        var w = await PaidCart();
        switch (kind)
        {
            case "unpaid": w.Moyasar.Then(HttpStatusCode.OK, PaidInvoice(w, "initiated")); break;
            case "lists_a_foreign_deal":
                // صَفقَةُ غَيرِه بِالفاتورَةِ نَفسِها مُسَجَّلَةً عَلَيها، بِمَبلَغٍ يُكمِلُ المَجموعَ لَو حُسِبَت — ولا تُحسَب.
                var foreign = await w.Deals.StartAsync(Slug, "marketplace", Other, "غَيرُه", null, "سِلعَة", 50m);
                await w.Deals.AttachRefAsync(Slug, foreign.Id, GatewayPaymentProof.InvoiceRef, InvoiceId);
                w.Moyasar.Then(HttpStatusCode.OK, PaidInvoice(w, listed: new[] { w.Ids[0], foreign.Id }));
                break;
            case "omits_the_reference": w.Moyasar.Then(HttpStatusCode.OK, PaidInvoice(w, listed: new[] { w.Ids[1] })); break;
            case "gateway_404":
                w.Moyasar.Then(HttpStatusCode.NotFound, """{"type":"invalid_request_error","message":"Object not found"}""");
                break;
        }
        var (saves, stored) = (w.Docs.SaveCalls, w.Docs.Stored.Count);

        var res = await w.Gateway.VerifyAsync(Slug, new DealPayer(Buyer), w.Ids[0].ToString("N"));

        Assert.False(res.Ok);
        Assert.Equal(code, res.Code);
        Assert.Single(w.Moyasar.Requests);
        Assert.Equal((saves, stored), (w.Docs.SaveCalls, w.Docs.Stored.Count));
    }

    /// <summary><b>ومَرجِعٌ أَلحَقَت بِه البَوّابَةُ «?…» بِلا «&amp;»</b> — الصَفقَةُ تُقرَأُ مِن رَأسِه، فَلا يَخسَرُ مَن دَفَعَ صَفقَتَه.</summary>
    [Fact]
    public async Task A_reference_with_trailing_gateway_parameters_still_reaches_the_deal()
    {
        var w = await PaidCart();
        w.Moyasar.Then(HttpStatusCode.OK, PaidInvoice(w));

        var res = await w.Gateway.VerifyAsync(Slug, new DealPayer(Buyer), w.Ids[1].ToString("N") + "?id=7c2f8a1e&status=paid");

        Assert.True(res.Ok, res.Code);
        Assert.Equal(w.Ids[1], res.DealId);
    }

    // ═══ ٤) النُقطَةُ مُشَغَّلَة — الحارِسُ قَبلَ أَيِّ حَقلٍ سُلوكاً ═══════════════════════════════════════

    private static int FreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>مُضيفٌ مُصَغَّرٌ يَحمِلُ <c>MapTenantGateway</c> وَحدَها — نَسَقُ <c>PayPalHost</c>: نَفسُ المُوَجِّهِ ورَبطِ الوُسَطاءِ
    /// وقِراءَةِ النَموذَج، ولا مُتابَعَةَ تَحويل (رَمزُ الرَدِّ في <c>Location</c> هُوَ المَقيس).</summary>
    private static async Task<(WebApplication App, HttpClient Client)> HostAsync(World w)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(w.Gateway);
        // إشعارُ البائِعينَ بَعدَ التَحَقُّق (‏ADR-032 الخُطوَة 10): الأُنبوبُ القائِمُ بِعالَمِ الوَثائِقِ نَفسِه — نَسَقُ `DealNoticesTests`.
        builder.Services.AddSignalR();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<L>();
        builder.Services.AddSingleton(new ACommerce.Templates.Customer.Marketplace.Services.WebPushService(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            NullLogger<ACommerce.Templates.Customer.Marketplace.Services.WebPushService>.Instance));
        builder.Services.AddSingleton(sp => new DealNotices(w.Docs.Store,
            sp.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<ACommerce.Kit.Realtime.Server.RealtimeHub>>(),
            sp.GetRequiredService<ACommerce.Templates.Customer.Marketplace.Services.WebPushService>()));
        var port = FreePort();
        var app = builder.Build();
        app.Urls.Add($"http://127.0.0.1:{port}");
        app.MapTenantGateway();
        await app.StartAsync();
        return (app, new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
        });
    }

    private static HttpRequestMessage Verify(HttpContent content, Guid? sessionUser)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/{Slug}/payments/verify") { Content = content };
        if (sessionUser is { } u)
            req.Headers.Add("Cookie", $"{AuthSession.CookieName(Slug)}={AuthHandlers.MakeToken(u, Slug)}");
        return req;
    }

    /// <summary>
    /// <para><b>الحارِسُ قَبلَ أَيِّ حَقلٍ — مَقيساً بِالسُلوكِ لا بِتَرتيبِ نَصّ</b>: طَلَبٌ بِلا جَلسَةٍ جِسمُه JSON لا استِمارَة. لَو قُرِئَ
    /// حَقلٌ قَبلَ الحارِسِ لَرَمى <c>req.Form</c> («Incorrect Content-Type») فَأَجابَت النُقطَةُ ‏500؛ والمَقيسُ ‏302 إلى الدُخولِ بِلا
    /// نِداءٍ لِلبَوّابَة. ثُمَّ المُشتَري بِاستِمارَتِه: إلى صَفحَةِ الصَفقَة؛ وغَيرُه: إلى صَفحَةِ العَودَةِ بِرَمزِه — بِلا نِداء.</para>
    /// </summary>
    [Fact]
    public async Task The_endpoint_asks_the_session_before_reading_any_field_and_returns_each_outcome_to_its_page()
    {
        var w = await PaidCart();
        w.Moyasar.Then(HttpStatusCode.OK, PaidInvoice(w));
        var (app, client) = await HostAsync(w);
        try
        {
            // ‏(‏2026-10-24، S15 C — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَ ما سَبَق: المِجَسُّ كانَ جِسماً JSON بِلا جَلسَة، وصارَ يُجيبُه ‏415
            // `.RequireFormBody()` قَبلَ الجِسم — فَالحارِسُ يُقاسُ بِاستِمارَةٍ بِلا جَلسَة: حَقلُها مَقروءٌ قَبلَ الحارِسِ لَكانَ أَعطى
            // صَفقَةً ونِداءً لِلبَوّابَة، والمَقيسُ ‏302 إلى الدُخولِ بِلا نِداء. وJSON ‏415 في الصَفِّ أَعلاه.)
            var form = (Guid id) => new FormUrlEncodedContent(new Dictionary<string, string> { [GatewayReturn.DealKey] = id.ToString("N") });

            var anonymous = await client.SendAsync(Verify(form(w.Ids[0]), sessionUser: null));
            Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
            Assert.Equal($"/{Slug}/login", anonymous.Headers.Location!.OriginalString);
            Assert.Empty(w.Moyasar.Requests);

            var foreign = await client.SendAsync(Verify(form(w.Ids[0]), Other));
            Assert.Equal(HttpStatusCode.Redirect, foreign.StatusCode);
            Assert.Equal($"/{Slug}/pay/return?deal={w.Ids[0]:N}&err={DealPayAuthorization.ActorNotPayer}",
                foreign.Headers.Location!.OriginalString);
            Assert.Empty(w.Moyasar.Requests);

            var buyer = await client.SendAsync(Verify(form(w.Ids[0]), Buyer));
            Assert.Equal(HttpStatusCode.Redirect, buyer.StatusCode);
            Assert.Equal($"/{Slug}/deals/{w.Ids[0]}", buyer.Headers.Location!.OriginalString);
            Assert.Single(w.Moyasar.Requests);

            w.Moyasar.Then(HttpStatusCode.OK, PaidInvoice(w, "initiated"));
            var unpaid = await client.SendAsync(Verify(form(w.Ids[0]), Buyer));
            Assert.Equal($"/{Slug}/pay/return?deal={w.Ids[0]:N}&err={GatewayPaymentProof.InvoiceUnpaid}",
                unpaid.Headers.Location!.OriginalString);
        }
        finally
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-24، S15 C)</b>: جِسمٌ JSON — بِجَلسَةِ المُشتَري وبِلا جَلسَة — يُرَدُّ ‏415 مِن
    /// <c>.RequireFormBody()</c> قَبلَ أَيِّ قِراءَة، بِلا نِداءٍ لِلبَوّابَة. (كانَ بِالجَلسَةِ ‏500.)</summary>
    [Fact]
    public async Task A_json_body_is_refused_415_before_the_form_read_with_or_without_a_session()
    {
        var w = await PaidCart();
        var (app, client) = await HostAsync(w);
        try
        {
            foreach (var who in new Guid?[] { Buyer, null })
            {
                var json = await client.SendAsync(Verify(
                    new StringContent("{\"deal\":\"x\"}", Encoding.UTF8, "application/json"), sessionUser: who));
                Assert.Equal(HttpStatusCode.UnsupportedMediaType, json.StatusCode);
            }
            Assert.Empty(w.Moyasar.Requests);
        }
        finally
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    /// <summary><b>ونَصُّ النُقطَةِ يُطابِقُ ما قيسَ سُلوكاً</b>: المَسارُ حَرفِيّ، والجَلسَةُ ثُمَّ سِجِلُّ السُلطَةِ ثُمَّ الحَقل، والرَدُّ مِن
    /// <c>GatewayReturn.AfterVerify</c>، ولا <c>RequestAborted</c> ولا <c>RequireStoreWritable</c> (مُثَبَّتَةٌ بِسَبَبِها). والتَسجيلُ
    /// والرَبطُ في القالِبِ سَطرانِ مُسَمَّيان.</summary>
    [Fact]
    public void The_endpoint_text_declares_the_guard_before_the_field_and_the_template_wires_it()
    {
        var file = WriteEndpointGuardTests.StripComments(Template("Billing", "TenantGatewayEndpoints.cs"));
        var at = file.IndexOf("app.MapPost(\"/{slug}/payments/verify\"", StringComparison.Ordinal);
        Assert.True(at >= 0, "أَداةٌ عَمياء: لا نُقطَةَ تَحَقُّقٍ بِمَسارِها الحَرفِيّ.");
        var body = file[at..file.IndexOf(".DisableAntiforgery();", at, StringComparison.Ordinal)];

        var session = body.IndexOf("AuthSession.ResolveToken(req, slug)", StringComparison.Ordinal);
        var payer = body.IndexOf("new Services.Deals.DealPayer(userId)", StringComparison.Ordinal);
        var field = body.IndexOf("req.Form[", StringComparison.Ordinal);
        Assert.True(session >= 0 && payer > session && field > payer, $"التَرتيب: جَلسَة {session} ← سُلطَة {payer} ← حَقل {field}.");
        Assert.Equal(1, body.Split("req.Form[").Length - 1);
        Assert.Contains("gateway.VerifyAsync(slug, payer, ", body, StringComparison.Ordinal);
        Assert.Contains("Services.Deals.GatewayReturn.AfterVerify(res)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestAborted", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".RequireStoreWritable(", file, StringComparison.Ordinal);

        var template = Template("MarketplaceTemplateExtensions.cs");
        Assert.Equal(1, template.Split("services.AddScoped<Services.Deals.GatewayPayments>();").Length - 1);
        Assert.Equal(1, template.Split("app.MapTenantGateway();").Length - 1);
    }

    /// <summary><b>قُلِبَ بِاسمِه</b> (‏2026-10-29، قَرارُ المالِك د5 (٥) «ب»؛ تَوصيفُه <c>23fae63</c> — كانَ <c>Today_the_verify_door_consumes_no_quota_before_the_gateway_call</c>):
    /// الحِصَّةُ القائِمَةُ بَعدَ الجَلسَةِ وسِجِلِّ السُلطَةِ وقَبلَ نِداءِ الخِدمَة، بِمِفتاحِ المَتجَرِ والمُشتَري؛ والمُتَجاوِزُ يَعودُ إلى صَفحَةِ العَودَةِ بِرَمزِه بِلا نِداء.</summary>
    [Fact]
    public void The_verify_door_consumes_the_action_quota_after_the_session_and_before_the_gateway_call()
    {
        var file = WriteEndpointGuardTests.StripComments(Template("Billing", "TenantGatewayEndpoints.cs"));
        var at = file.IndexOf("app.MapPost(\"/{slug}/payments/verify\"", StringComparison.Ordinal);
        Assert.True(at >= 0, "أَداةٌ عَمياء: لا نُقطَةَ تَحَقُّقٍ بِمَسارِها الحَرفِيّ.");
        var body = file[at..file.IndexOf(".DisableAntiforgery();", at, StringComparison.Ordinal)];
        var session = body.IndexOf("AuthSession.ResolveToken(req, slug)", StringComparison.Ordinal);
        var quota = body.IndexOf("AuthHandlers.TryConsumeActionQuota(Services.Deals.GatewayReturn.VerifyQuotaKey(slug, userId))", StringComparison.Ordinal);
        var call = body.IndexOf("gateway.VerifyAsync(slug, payer, dealRef)", StringComparison.Ordinal);
        var refused = body.IndexOf("Services.Deals.GatewayReturn.RateLimited(dealRef)", StringComparison.Ordinal);
        Assert.True(session >= 0 && quota > session && call > quota && refused > call, $"جَلسَة {session} ← حِصَّة {quota} ← نِداء {call} ← رَدّ {refused}");
        Assert.Equal(1, body.Split("TryConsume").Length - 1);
        Assert.DoesNotContain("TryConsume", WriteEndpointGuardTests.StripComments(Template("Services", "Deals", "GatewayPayments.cs")), StringComparison.Ordinal);
    }

    /// <summary><b>المُوجِبُ والسالِب</b>: المِفتاحُ لِكُلِّ مَتجَرٍ ومُشتَرٍ؛ والحِصَّةُ القائِمَةُ تَقبَلُ عَشراً وتَرُدُّ الحادِيَةَ عَشرَة (مِفتاحٌ فَريدٌ لِلصَفّ)؛ والرَدُّ يَعودُ إلى
    /// صَفحَةِ العَودَةِ بِالصَفقَةِ ورَمزِه، ولِلرَمزِ نَصٌّ حَيّ.</summary>
    [Fact]
    public void Ten_verifies_an_hour_pass_and_the_eleventh_returns_its_code_to_the_return_page()
    {
        var payer = Guid.NewGuid();
        var key = GatewayReturn.VerifyQuotaKey("zz-quota-" + Guid.NewGuid().ToString("N")[..8], payer);
        Assert.Contains($"|gateway-verify:{payer:D}", key, StringComparison.Ordinal);
        Assert.NotEqual(key, GatewayReturn.VerifyQuotaKey("other", payer));
        for (var i = 0; i < 10; i++) Assert.True(AuthHandlers.TryConsumeActionQuota(key), $"النَقرَةُ {i + 1}");
        Assert.False(AuthHandlers.TryConsumeActionQuota(key));

        var deal = Guid.NewGuid();
        var limited = GatewayReturn.RateLimited(deal.ToString("N"));
        Assert.False(limited.Ok);
        Assert.Equal($"pay/return?deal={deal:N}&err=gateway_verify_rate_limited", GatewayReturn.AfterVerify(limited));
        var text = LocaleCatalog.Text(LocaleCatalog.Arabic, GatewayReturn.KeyFor(GatewayReturn.VerifyRateLimited)!);
        Assert.False(LocaleCatalog.IsPlaceholder(text));
        Assert.Contains("لَم نَسأَلِ البَوّابَة", text, StringComparison.Ordinal);
    }

    /// <summary><b>اليَوم</b> (‏2026-10-29، تَوصيفٌ قَبلَ قَرارِ المالِك د5 (٤) «ب»: «يُلغى الطلب كله آليًّا، وتُلغى الفاتورة عند ميسر إن بقيت مفتوحة، ويُخبَر المشتري وكل بائع
    /// فيه»): تَحَقُّقٌ يَرى الفاتورَةَ «expired» عِندَ البَوّابَةِ يُجيبُ <c>gateway_invoice_closed</c> ويَكتُبُ مَرجِعَ الإغلاق — <b>والصَفقَتانِ قائِمَتانِ</b> تُمسِكانِ
    /// بائِعَيهِما بِلا نِهايَة. يُقلَبُ بِاسمِه.</summary>
    [Fact]
    public async Task Today_an_expired_invoice_seen_at_verify_leaves_its_deals_active()
    {
        var w = await PaidCart(withSeller: true);
        w.Moyasar.Then(HttpStatusCode.OK, PaidInvoice(w, "expired"));

        var res = await w.Gateway.VerifyAsync(Slug, new DealPayer(Buyer), w.Ids[0].ToString("N"));

        Assert.Equal(GatewayPaymentProof.InvoiceClosed, res.Code);
        Assert.All(w.Ids, id =>
        {
            var d = w.Docs.Read<Deal>(id.ToString())!;
            Assert.Equal(DealStatus.Active, d.Status);
            Assert.True(GatewayReturn.IsClosed(d));
        });
    }

    // ═══ ٥) صَفحَةُ العَودَة — قِراءَةٌ خالِصَةٌ لا تَسأَلُ البَوّابَة ═══════════════════════════════════════

    /// <summary>
    /// <para><b>الـGET صَفحَةٌ لا نُقطَة، ولا يَسأَلُ البَوّابَةَ ولا يَكتُب</b>: مَساراها (بِلا دَورٍ وبِدَور)، ولا <c>MapGet</c> عَلى المَسارِ
    /// نَفسِه في أَيِّ مِلَفٍّ (لا تَنازُعَ في التَوجيه)، ولا خِدمَةَ بَوّابَةٍ ولا عَميلَ ولا تَحَقُّقَ في الكود؛ والسُلطَةُ سُلطَةُ النُقطَةِ
    /// نَفسُها؛ واستِمارَةٌ واحِدَةٌ بِزِرٍّ واحِدٍ تُرسِلُ إلى نُقطَةِ التَحَقُّقِ بِالحَقلِ الَّذي تَقرَؤُه. و«لا تَكتُب» يُفرَضُ عَلَيها كَما
    /// يُفرَضُ عَلى كُلِّ صَفحَة (<c>No_razor_page_writes_state</c>).</para>
    /// </summary>
    [Fact]
    public void The_return_page_is_a_pure_read_that_renders_one_button_posting_to_verify()
    {
        var raw = Template("Components", "Pages", "GatewayPayReturn.razor");
        Assert.Contains("@page \"/{slug}/pay/return\"", raw, StringComparison.Ordinal);
        Assert.Contains("@page \"/{slug}/r/{role}/pay/return\"", raw, StringComparison.Ordinal);

        var code = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(raw));
        foreach (var outbound in new[] { "GatewayPayments", "MoyasarClient", "VerifyAsync", "FetchInvoiceAsync" })
            Assert.DoesNotContain(outbound, code, StringComparison.Ordinal);
        Assert.Contains("DealPayAuthorization.Validate(deal, new DealPayer(Auth.UserId!.Value))", code, StringComparison.Ordinal);

        Assert.Equal(1, code.Split("<form").Length - 1);
        Assert.Equal(1, code.Split("type=\"submit\"").Length - 1);
        Assert.Contains("<form method=\"post\" action=\"@($\"/{Slug}/payments/verify\")\">", code, StringComparison.Ordinal);
        Assert.Contains("name=\"@GatewayReturn.DealKey\"", code, StringComparison.Ordinal);
        Assert.Contains("[SupplyParameterFromQuery(Name = GatewayReturn.DealKey)]", code, StringComparison.Ordinal);
        Assert.Contains("GatewayReturn.KeyFor(Err)", code, StringComparison.Ordinal);

        var routes = WriteEndpointGuardTests.AllMinimalApiEndpoints().Select(e => e.Route).ToList();
        Assert.True(routes.Count > 100, $"أَداةٌ عَمياء: {routes.Count} نُقطَة.");
        Assert.DoesNotContain("/{slug}/pay/return", routes);
        Assert.Contains("/{slug}/payments/verify", routes);
    }
}
