using ACommerce.Kit.Payments;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ نَقرَتانِ عَلى استِمارَةٍ واحِدَة — أَتُحَرِّكانِ الصَفقَةَ مَرحَلَتَين؟ (تَدقيقُ الإطلاق ‏2026-10-24، brief-P0-6 / lens-money-integrity-2) ═══
//
// **المَقيسُ قَبلَ اللَمس** (عِندَ `dace7cd`): أَبوابُ «تَأكيد: …» الثَلاثَة — بابُ العُضو `POST /{slug}/deals/{id}/advance` (استِمارَةُ
// `MyDealDetail`)، والمَسارُ الإداريّ `POST /studio/apps/{slug}/deals/{id}/advance` (استِمارَةُ `StudioAppDealDetail`)، و`POST /api/v1/deals/{id}/advance`
// — لا تَحمِلُ المَرحَلَةَ الَّتي قَرَأَها الناقِر، فَتُنادي `DealsService.AdvanceAsync` بِلا `from:`؛ والحارِسُ قائِمٌ في الخِدمَةِ اختِيارِيّاً
// (`from is { } expected && deal.Stage != expected` ⇒ `deal_changed_meanwhile`) ومُنادِياه الشَحنُ والوارِدُ مِن OTO وَحدَهُما. و‏ADR-036 يَرُدُّ
// الكِتابَتَينِ **المُتَداخِلَتَين** (نُسخَةٌ قُرِئَت ثُمَّ تَغَيَّرَت) لا **المُتَتالِيَتَين**: النَقرَةُ الثانِيَةُ طَلَبٌ جَديدٌ يَقرَأُ ما كَتَبَته الأولى ويَمضي
// مِنه. فَنَقرَةٌ مُزدَوِجَةٌ أَو لِسانٌ قَديمٌ يُحَرِّكُ صَفقَةَ النَقدِ مِن «حَجز» إلى «شَحن» بِلا بوليصَة (ويُغلَقُ بابُ OTO لِطَلَبِها)، أَو مِن «شَحن»
// إلى «تَسليم» بِسَطرِ «استُلِمَ النَقد» ثُمَّ إلى «تَقييم» مُكتَمِلَة.
//
// **التَوصيفُ قَبلَ التَبديل** (القاعِدَة ٣): صَفّا «اليَوم» أَدناه يُودَعانِ وَحدَهُما، ويُقلَبانِ بِاسمَيهِما في كوميتِ الحَقلِ المَخفِيّ.

public class DealAdvanceExpectedStageTests(ITestOutputHelper output)
{
    private const string Slug = "zz-advance-expected-stage";
    private static readonly Guid Buyer  = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Seller = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    /// <summary>مُزَوِّدٌ يَرمي عِندَ أَيِّ نِداء — صَفَقاتُ هذا المِلَفِّ نَقدٌ بِلا مُعَرِّفِ دَفع.</summary>
    private sealed class ForbiddenPaymentProvider : IPaymentProvider
    {
        private static InvalidOperationException Touched() => new("صَفقَةُ نَقدٍ لا تَمَسُّ المال — نودِيَ مُزَوِّدُ الدَفع.");
        public string ProviderName => "forbidden";
        public Task<PaymentResult> AuthorizeAsync(PaymentRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> CaptureAsync(string paymentId, decimal? amount = null, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> RefundAsync(string paymentId, decimal amount, string reason, CancellationToken ct = default) => throw Touched();
        public Task<SubscriptionResult> CreateSubscriptionAsync(SubscriptionRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<bool> CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default) => throw Touched();
        public Task<Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default) => throw Touched();
    }

    /// <summary>عالَمُ الوَثائِقِ يُتِمُّ كُلَّ انتِظارٍ في مَكانِه — وإن لَم يَكتَمِل فَالأَداةُ تَكذِب.</summary>
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

    private static string Segment(string source, string from, string to)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"‏`{from}` غَيرُ مَوجود — الأَداةُ عَمياء.");
        var end = source.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"‏`{to}` لا يَتلو `{from}` — الأَداةُ عَمياء.");
        return source.Substring(start, end - start);
    }

    private static string Endpoints() => WriteEndpointGuardTests.StripComments(Source(Template, "MarketplaceTemplateExtensions.cs"));
    private static string Api() => WriteEndpointGuardTests.StripComments(Source(Template, "Api", "ApiV1Endpoints.cs"));
    private static string Page(string name) => WriteEndpointGuardTests.StripMarkupComments(Source(Template, "Components", "Pages", name));

    /// <summary>صَفقَةُ سَلَّةٍ نَقداً عِندَ <paramref name="stage"/> — مُشتَرٍ وبائِعٌ مَربوطٌ مِن الوِلادَة — في عالَمِ وَثائِقَ جَديدٍ تَمشيه
    /// <see cref="DealsService"/> الحَقيقِيَّة.</summary>
    private static (DealsService Deals, DocWorld World, Deal Deal) CashAt(DealStage stage)
    {
        var world = new DocWorld();
        var deals = new DealsService(world.Store, new ForbiddenPaymentProvider());
        var deal = new Deal
        {
            Id = Guid.NewGuid(), TenantSlug = Slug, Pattern = "marketplace", Stage = stage, AmountSar = 120m,
            ListingTitle = "سِلعَة", InitiatorId = Buyer, InitiatorName = "مُشتَرٍ", CounterpartyId = Seller, CounterpartyName = "بائِع",
        };
        deal.Attributes[CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery;
        world.Put(deal);
        return (deals, world, deal);
    }

    // ═══ ١) الأَثَرُ عَلى الخِدمَةِ الحَقيقِيَّة — كَما تُناديها الأَبوابُ اليَوم ═══════════════════════════════════════════════

    /// <summary>
    /// <para><b>«اليَوم»</b>: البائِعُ يَرى «تَأكيد: تَأكيد» عَلى صَفقَةِ نَقدٍ عِندَ «حَجز» ويَنقُرُه مَرَّتَين — والبابُ يُنادي
    /// <c>AdvanceAsync</c> كَما يُناديه (بِلا <c>from:</c>) — فَتَمضي الأولى إلى «تَأكيد» والثانِيَةُ إلى «شَحن». ومِن «شَحن» نَقرَتانِ عَلى
    /// «تَأكيد: تَسليم» تَكتُبانِ «تَسليم» وسَطرَ «استُلِمَ النَقد» ثُمَّ «تَقييم» مُكتَمِلَة.</para>
    /// </summary>
    [Fact]
    public void Today_two_posts_of_one_rendered_advance_form_move_a_cash_deal_two_stages()
    {
        var (deals, world, deal) = CashAt(DealStage.Booked);
        var r1 = Now(deals.AdvanceAsync(Slug, deal.Id, Seller, "بائِع", ""));
        var r2 = Now(deals.AdvanceAsync(Slug, deal.Id, Seller, "بائِع", ""));
        Assert.True(r1.Ok);
        Assert.True(r2.Ok);
        var stored = world.Read<Deal>(deal.Id.ToString())!;
        Assert.Equal(DealStage.Shipping, stored.Stage);
        output.WriteLine($"نَقدٌ عِندَ «حَجز»، نَقرَتانِ لِلبائِع: r1.Ok={r1.Ok} r2.Ok={r2.Ok} stage={stored.Stage}");

        var (deals2, world2, shipping) = CashAt(DealStage.Shipping);
        var s1 = Now(deals2.AdvanceAsync(Slug, shipping.Id, Seller, "بائِع", ""));
        var s2 = Now(deals2.AdvanceAsync(Slug, shipping.Id, Seller, "بائِع", ""));
        var done = world2.Read<Deal>(shipping.Id.ToString())!;
        Assert.True(s1.Ok);
        Assert.True(s2.Ok);
        Assert.Equal(DealStage.Reviewed, done.Stage);
        Assert.Equal(DealStatus.Completed, done.Status);
        Assert.Equal(1, done.Timeline.Count(e => e.Action == DealEvent.CashOnDelivery));
        output.WriteLine($"نَقدٌ عِندَ «شَحن»، نَقرَتانِ لِلبائِع: stage={done.Stage} status={done.Status} cash_line=1");
    }

    // ═══ ٢) الأَبوابُ الثَلاثَة — لا يَحمِلُ أَيٌّ مِنها المَرحَلَةَ الَّتي قُرِئَت ═══════════════════════════════════════════

    /// <summary>
    /// <para><b>«اليَوم»</b>: استِمارَتا «تَأكيد: …» و«→ التالي» بِلا حَقلِ مَرحَلَة، والنُقطَتانِ تُنادِيانِ <c>AdvanceAsync</c> بِلا <c>from:</c>،
    /// وجِسمُ طَلَبِ الـAPI بِلا حَقلِ مَرحَلَة — فَلا يَرى الحارِسُ القائِمُ في الخِدمَةِ ما رآه الناقِر.</para>
    /// </summary>
    [Fact]
    public void Today_no_advance_door_carries_the_stage_its_clicker_read()
    {
        var member = Segment(Page("MyDealDetail.razor"), "action=\"@($\"/{Slug}/deals/{deal.Id}/advance\")\"", "</form>");
        Assert.DoesNotContain("name=\"from\"", member, StringComparison.Ordinal);
        var studio = Segment(Page("StudioAppDealDetail.razor"), "action=\"@($\"/studio/apps/{Slug}/deals/{deal.Id}/advance\")\"", "</form>");
        Assert.DoesNotContain("name=\"from\"", studio, StringComparison.Ordinal);

        var endpoints = Endpoints();
        var memberDoor = Segment(endpoints, "\"/{slug}/deals/{id:guid}/advance\"", ".RequireStoreWritable();");
        Assert.Contains("var res = await deals.AdvanceAsync(slug, id, userId, userName, note);", memberDoor, StringComparison.Ordinal);
        var studioDoor = Segment(endpoints, "\"/studio/apps/{slug}/deals/{id:guid}/advance\"", ".DisableAntiforgery();");
        Assert.Contains("var result = await deals.AdvanceAsync(slug, id, actorId, \"مالِك التَّطبيق (إداريّ)\", note);", studioDoor, StringComparison.Ordinal);
        var apiDoor = Segment(Api(), "\"/api/v1/deals/{id:guid}/advance\"", ".RequireApiKey(ApiScopeCatalog.DealsWrite);");
        Assert.DoesNotContain("from:", apiDoor, StringComparison.Ordinal);
        Assert.Contains("public sealed record AdvanceDealRequest(string? note);", Source(Template, "Api", "DealApi.cs"), StringComparison.Ordinal);
        output.WriteLine("العُضو والاستوديو والـAPI: صِفرُ بابٍ يَحمِلُ المَرحَلَةَ المَقروءَة");
    }
}
