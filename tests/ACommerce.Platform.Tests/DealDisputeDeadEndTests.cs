using System.Text.RegularExpressions;
using ACommerce.Kit.Payments;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ «نِزاع» طَريقٌ مَسدود — لا كاتِبَ يُخرِجُ الصَفقَةَ مِنه (تَدقيقُ الإطلاق ‏2026-10-24، lens-money-integrity-5؛ قَرارُ مالِك — «ب» W8-1·٥) ═══
//
// **المَقيسُ عِندَ الرَأس** (`c7037c1`، وهُوَ ما كانَ عِندَ `dace7cd`): `DealsService.DisputeAsync` الكاتِبُ الوَحيدُ لِـ`Disputed`، ومُناديه نُقطَةُ الاستوديو؛
// وكُلُّ كاتِبٍ آخَرَ لِلحالَةِ (الاكتِمالُ في `MoveAsync`، والإلغاءُ في `CancelCoreAsync`) يَشتَرِطُ `Active` — فَصَفقَةٌ مُتَنازَعٌ عَلَيها، ولَو مَدفوعَة، لا تُلغى
// ولا يُكتَبُ لَها استِرجاعٌ ولا تُكمَل. و`ConfirmGatewayPaymentAsync` يَكتُبُ دَفعاً عَلى مُتَنازَعٍ عَلَيها ولا يَكتُبُ «استِرجاعُه مُستَحَقّ» إلّا لِلمُلغاة. وشُروطُ
// المَتجَرِ تَعِدُ («legal.terms.item_disputes» في `LegalHub`) بِـ«حَلّ النِزاعات عَبر آلِيّات المَنصَّة الداخِلِيَّة».
//
// **ولَم يُبنَ شَيء** (القاعِدَة ١٦): مَسارُ الحَلِّ ونَصُّ الشَرطِ قَرارُ المالِك. هذا المِلَفُّ تَوصيفُ «اليَوم» يُودَعُ قَبلَ أَيِّ تَبديل (القاعِدَة ٣)، ويُقلَبُ
// بِاسمِه في الكوميتِ الَّذي يَبني الحَلّ.

// **وقُلِبَ بِاسمَيه** (‏2026-10-29، قَرارُ المالِك د38؛ ‏ADR-047 §٢): «صاحب المتجر» يَحسِمُ النِزاعَ — «إلغاء مع تسجيل استرجاع مستحقّ إن كانت مدفوعة، أو
// استئناف من المرحلة نفسها» — بِبابٍ في الاستوديو بِجِوارِ بابِ فَتحِه (`/studio/apps/{slug}/deals/{id}/resolve`)، وكاتِبٍ في الخِدمَة (`ResolveDisputeAsync`)
// خَلفَ بَوّابَةٍ نَقِيَّة (`DealDisputeResolution`). وبابا الإلغاءِ والتَقَدُّمِ العامّانِ مُغلَقانِ عَلى «نِزاع» كَما كانا — الحَسمُ بابُه وَحدَه. وبَندُ الشُروطِ
// بِنَصِّ المالِك. وصُفوفُ البَوّابَةِ والخِدمَةِ والإشعارِ والبابِ في `DealDisputeResolutionTests`.

public class DealDisputeDeadEndTests(ITestOutputHelper output)
{
    private const string Slug = "zz-dispute-dead-end";
    private static readonly Guid Buyer    = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Seller   = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly Guid AppOwner = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");

    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    private sealed class ForbiddenPaymentProvider : IPaymentProvider
    {
        private static InvalidOperationException Touched() => new("صَفقَةٌ مُتَنازَعٌ عَلَيها لا يَمَسُّ أَحَدٌ مالَها هُنا — نودِيَ مُزَوِّدُ الدَفع.");
        public string ProviderName => "forbidden";
        public Task<PaymentResult> AuthorizeAsync(PaymentRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> CaptureAsync(string paymentId, decimal? amount = null, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> RefundAsync(string paymentId, decimal amount, string reason, CancellationToken ct = default) => throw Touched();
        public Task<SubscriptionResult> CreateSubscriptionAsync(SubscriptionRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<bool> CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default) => throw Touched();
        public Task<Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default) => throw Touched();
    }

    private static T Now<T>(Task<T> task)
    {
        Assert.True(task.IsCompleted, "نِداءٌ لَم يَكتَمِل في عالَمٍ مُتَزامِن — الأَداةُ تَكذِب.");
        return task.GetAwaiter().GetResult();
    }

    private static string Source(params string[] parts)
    {
        var path = Path.Combine(RepoRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياء.");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 500, $"أَداةٌ عَمياء: {path} طولُه {text.Length} مِحرَفاً — لَم يُقرَأ.");
        return text;
    }

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (كانَ <c>Today_a_disputed_paid_deal_cannot_be_cancelled_moved_or_resolved_by_anyone</c>): صَفقَةُ بِطاقَةٍ مَقبوضَةٌ عِندَ «دَفع»
    /// يَفتَحُ صاحِبُ المَتجَرِ عَلَيها نِزاعاً — بابا الإلغاءِ والتَقَدُّمِ العامّانِ مَردودانِ كَما كانا (<c>deal_not_active</c>)، والطَرَفُ لا يَحسِمُ
    /// (<c>actor_not_store_admin</c>)، <b>وصاحِبُ المَتجَرِ يَستَأنِفُها مِن «دَفع»</b> فَتَعودُ فَعّالَةً ويُحَرِّكُها البائِع — والمالُ لَم يُمَسّ.</para>
    /// </summary>
    [Fact]
    public void A_disputed_paid_deal_is_closed_to_the_general_doors_and_the_store_owner_resolves_it()
    {
        var world = new DocWorld();
        var deals = new DealsService(world.Store, new ForbiddenPaymentProvider());
        var deal = new Deal
        {
            Id = Guid.NewGuid(), TenantSlug = Slug, Pattern = "marketplace", Stage = DealStage.Paid, AmountSar = 300m,
            ListingTitle = "سِلعَة", InitiatorId = Buyer, InitiatorName = "مُشتَرٍ", CounterpartyId = Seller, CounterpartyName = "بائِع",
        };
        deal.Attributes[CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.Card;
        deal.Refs["payment_id"] = "pay_disputed";
        deal.Refs["payment_status"] = nameof(PaymentStatus.Captured);
        world.Put(deal);

        Assert.True(Now(deals.DisputeAsync(Slug, deal.Id, AppOwner, "مالِك التَّطبيق", "بِضاعَةٌ مُختَلِفَة")).Ok);

        var codes = new[]
        {
            Now(deals.CancelAsync(Slug, deal.Id, new DealCanceller(Buyer, "مُشتَرٍ", IsStoreAdmin: false), "استِرجاع")).Violation!.Code,
            Now(deals.CancelAsync(Slug, deal.Id, new DealCanceller(Seller, "بائِع", IsStoreAdmin: false), "إلغاء")).Violation!.Code,
            Now(deals.CancelAsync(Slug, deal.Id, new DealCanceller(AppOwner, "مالِك التَّطبيق", IsStoreAdmin: true), "حَلُّ النِزاع")).Violation!.Code,
            Now(deals.DisputeAsync(Slug, deal.Id, AppOwner, "مالِك التَّطبيق", "ثانٍ")).Code!,
        };
        Assert.All(codes, c => Assert.Equal(DealCancelAuthorization.DealNotActive, c));
        Assert.False(Now(deals.AdvanceAsync(Slug, deal.Id, Seller, "بائِع", "")).Ok);

        var byParty = Now(deals.ResolveDisputeAsync(Slug, deal.Id, new DealCanceller(Buyer, "مُشتَرٍ", IsStoreAdmin: false),
            DealDisputeResolution.Resume, "أُريدُ الاستِئناف"));
        Assert.Equal(DealDisputeResolution.ActorNotStoreAdmin, byParty.Violation!.Code);
        Assert.Equal(DealStatus.Disputed, world.Read<Deal>(deal.Id.ToString())!.Status);

        var resumed = Now(deals.ResolveDisputeAsync(Slug, deal.Id, new DealCanceller(AppOwner, "مالِك التَّطبيق", IsStoreAdmin: true),
            DealDisputeResolution.Resume, "اتَّفَقَ الطَرَفان"));
        Assert.True(resumed.Ok, resumed.Violation?.MessageAr);
        var stored = world.Read<Deal>(deal.Id.ToString())!;
        Assert.Equal((DealStatus.Active, DealStage.Paid), (stored.Status, stored.Stage));
        Assert.Equal(DealEvent.DisputeResumed, stored.Timeline[^1].Action);
        var moved = Now(deals.AdvanceAsync(Slug, deal.Id, Seller, "بائِع", ""));
        Assert.True(moved.Ok, moved.Reason);
        Assert.DoesNotContain(world.Read<Deal>(deal.Id.ToString())!.Timeline, e => e.Action is "refunded" or "refund_failed" or DealEvent.GatewayRefundDue);
        output.WriteLine($"نِزاعٌ عَلى «دَفع» مَقبوضَة: الأَبوابُ العامَّة ⇒ {string.Join(",", codes)} · حَسمُ الطَرَف ⇒ {byParty.Violation.Code} · استِئنافُ صاحِبِ المَتجَر ⇒ " +
            $"{stored.Status}/{stored.Stage} · ثُمَّ تَحريكُ البائِع ok={moved.Ok}");
    }

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (كانَ <c>Today_no_status_writer_leaves_Disputed_and_the_terms_promise_platform_resolution</c>): كُتّابُ الحالَةِ في
    /// <c>DealsService</c> خَمسَةٌ بِتَرتيبِ المَصدَر — الوِلادَةُ <c>Active</c>، والاكتِمال، والإلغاء، والنِزاع، <b>والاستِئنافُ <c>Active</c> في
    /// <c>ResolveDisputeAsync</c> بَعدَ بَوّابَةِ الحَسم</b>؛ والإلغاءُ بِالحَسمِ يَمُرُّ بِالجِسمِ نَفسِه؛ والشَرطُ بِنَصِّ المالِك.</para>
    /// </summary>
    [Fact]
    public void The_store_owner_writes_Disputed_back_to_Active_or_Cancelled_and_the_terms_say_so()
    {
        var service = WriteEndpointGuardTests.StripComments(Source(Template, "Services", "Deals", "DealsService.cs"));
        var writes = Regex.Matches(service, @"Status = DealStatus\.(\w+)").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(new[] { "Active", "Completed", "Cancelled", "Disputed", "Active" }, writes);
        var resolve = service[service.IndexOf("public async Task<DealResolveResult> ResolveDisputeAsync(", StringComparison.Ordinal)..];
        resolve = resolve[..resolve.IndexOf("public async Task<Deal?> AttachRefAsync(", StringComparison.Ordinal)];
        Assert.True(resolve.IndexOf("DealDisputeResolution.Validate(deal, by, outcome)", StringComparison.Ordinal)
                    < resolve.IndexOf("deal!.Status = DealStatus.Active;", StringComparison.Ordinal), "الاستِئنافُ يُكتَبُ قَبلَ بَوّابَتِه.");
        Assert.Contains("CancelCoreAsync(tenantSlug, dealId, by, reason, wholeInvoice: false, ct, resolvingDispute: true)", resolve, StringComparison.Ordinal);
        Assert.Contains("if (d.Status == DealStatus.Cancelled)", service, StringComparison.Ordinal);

        Assert.Equal("قَبولُ أَن تَحسِمَ إدارَةُ المَتجَرِ أَيَّ نِزاعٍ عَلى صَفقَة: بِإلغائِها مَعَ رَدِّ ما دُفِع، أَو بِاستِئنافِها مِن مَرحَلَتِها، مَعَ إبلاغِ الطَّرَفَينِ بِالنَّتيجَة — دونَ مَساسٍ بِحَقِّكَ في اللُّجوءِ إلى الجِهاتِ المُختَصَّة.",
            ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "legal.terms.item_disputes"));
        Assert.Contains("@L.Markup(\"legal.terms.item_disputes\")", Source(Template, "Components", "Pages", "LegalHub.razor"), StringComparison.Ordinal);
        output.WriteLine($"كُتّابُ الحالَة: {string.Join(" · ", writes)} — والخامِسُ مَخرَجُ «نِزاع»، والشَرطُ يَصِفُ الآلِيَّةَ كَما تَعمَل");
    }
}
