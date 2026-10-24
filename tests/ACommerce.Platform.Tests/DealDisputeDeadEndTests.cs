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
    /// <para><b>«اليَوم» عَلى الخِدمَةِ الحَقيقِيَّة</b>: صَفقَةُ بِطاقَةٍ مَقبوضَةٌ عِندَ «دَفع» يَفتَحُ صاحِبُ المَتجَرِ عَلَيها نِزاعاً — ثُمَّ لا يُلغيها المُشتَري
    /// ولا البائِعُ ولا صاحِبُ المَتجَر (<c>deal_not_active</c>)، ولا يُحَرِّكُها البائِع، ولا يُفتَحُ نِزاعٌ ثانٍ؛ والحالَةُ <c>Disputed</c> والمالُ لَم يُمَسّ.</para>
    /// </summary>
    [Fact]
    public void Today_a_disputed_paid_deal_cannot_be_cancelled_moved_or_resolved_by_anyone()
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
        var moved = Now(deals.AdvanceAsync(Slug, deal.Id, Seller, "بائِع", ""));

        Assert.All(codes, c => Assert.Equal(DealCancelAuthorization.DealNotActive, c));
        Assert.False(moved.Ok);
        var stored = world.Read<Deal>(deal.Id.ToString())!;
        Assert.Equal(DealStatus.Disputed, stored.Status);
        Assert.Equal(DealStage.Paid, stored.Stage);
        Assert.DoesNotContain(stored.Timeline, e => e.Action is "refunded" or "refund_failed" or DealEvent.GatewayRefundDue);
        output.WriteLine($"نِزاعٌ عَلى «دَفع» مَقبوضَة: إلغاءُ المُشتَري/البائِع/صاحِبِ المَتجَر وزِراعُ نِزاعٍ ثانٍ ⇒ {string.Join(",", codes)} · تَحريكُ البائِع ok={moved.Ok} · الحالَة {stored.Status}");
    }

    /// <summary>
    /// <para><b>«اليَوم» في المَصدَر</b>: كُتّابُ الحالَةِ في <c>DealsService</c> أَربَعَة — الوِلادَةُ <c>Active</c>، والاكتِمالُ، والإلغاءُ، والنِزاع — ولا كاتِبَ
    /// يَكتُبُ <c>Active</c> بَعدَ الوِلادَة؛ و<c>ConfirmGatewayPaymentAsync</c> يَكتُبُ «استِرجاعُه مُستَحَقّ» لِلمُلغاةِ وَحدَها؛ والشَرطُ يَعِدُ بِحَلِّ النِزاع.</para>
    /// </summary>
    [Fact]
    public void Today_no_status_writer_leaves_Disputed_and_the_terms_promise_platform_resolution()
    {
        var service = WriteEndpointGuardTests.StripComments(Source(Template, "Services", "Deals", "DealsService.cs"));
        var writes = Regex.Matches(service, @"Status = DealStatus\.(\w+)").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(new[] { "Active", "Completed", "Cancelled", "Disputed" }, writes);
        Assert.Contains("Status = DealStatus.Active,", service, StringComparison.Ordinal);   // مُهَيِّئُ `StartAsync` وَحدَه
        Assert.DoesNotContain("deal.Status = DealStatus.Active", service, StringComparison.Ordinal);
        Assert.Contains("if (d.Status == DealStatus.Cancelled)", service, StringComparison.Ordinal);
        Assert.DoesNotContain("DealStatus.Disputed)", service, StringComparison.Ordinal);

        Assert.Equal("قَبول حَلّ النِزاعات عَبر آلِيّات المَنصَّة الداخِلِيَّة.",
            ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "legal.terms.item_disputes"));
        Assert.Contains("@L.Markup(\"legal.terms.item_disputes\")", Source(Template, "Components", "Pages", "LegalHub.razor"), StringComparison.Ordinal);
        output.WriteLine($"كُتّابُ الحالَة: {string.Join(" · ", writes)} — لا مَخرَجَ مِن Disputed، والشَرطُ يَعِدُ بِالحَلّ");
    }
}
