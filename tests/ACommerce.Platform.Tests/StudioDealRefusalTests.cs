using ACommerce.Kit.Payments;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ رَفضُ المَسارِ الإداريِّ في الاستوديو — يُقرَأُ عَلى صَفحَةِ الصَفقَةِ أَم يُبتلَع؟ (مُراجَعَةُ المَوجَة 3a، G4 (i)) ═══
//
// **المَقيسُ قَبلَ اللَمس** (‏2026-10-13، أَساسُ التَيّار `4e32559`): نِقاطُ صَفَقاتِ الاستوديو الثَلاث — `advance` و`cancel` و`dispute` —
// تَعودُ إلى `StudioAppDealDetail` بِلا مُعامِل، والصَفحَةُ (ومَعَها قائِمَةُ `StudioAppDeals`) بِلا `SupplyParameterFromQuery` واحِد. والرَفضُ
// قائِمٌ بِرَمزِه في الخِدمَة: تَعارُضُ النُسخَةِ يُعيدُ `deal_changed_meanwhile` مِن `AdvanceAsync` و`CancelAsync` (‏ADR-036 §٢‑٢: «لا يُكتَب،
// وتَعودُ الصَفحَةُ بِالحالِ القائِم — بِلا شَريط»)، و`AdminPathCanMove` تَرُدُّ صَفقَةَ البَذرَةِ التَجريبِيَّةِ «→ حَجز» (‏ADR-033 §٦‑ط‑٨: «وطَلَبٌ
// مَصنوعٌ يَعودُ بِلا كَلِمَة»). فَالنَقرَةُ المَرفوضَةُ تَعودُ بِصَفحَةٍ كَأَنَّ شَيئاً لَم يَكُن — ما أُغلِقَ لِصَفحَةِ العُضوِ في المَوجَة ١ (القاعِدَة ١٢).
//
// **التَوصيفُ قَبلَ التَبديل** (القاعِدَة ٣): صَفُّ «اليَوم» أَدناه يُودَعُ وَحدَه، ويُقلَبُ بِاسمِه في الكوميتِ الَّذي يَحمِلُ الرَمزَ ويَرسُمُ الشَريط.

public class StudioDealRefusalTests(ITestOutputHelper output)
{
    private const string Slug = "zz-studio-deal-refusal";
    private static readonly Guid Buyer    = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Seller   = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly Guid AppOwner = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");

    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    /// <summary>مُزَوِّدٌ يَرمي عِندَ أَيِّ نِداء — صَفَقاتُ هذا المِلَفِّ بِلا مُعَرِّفِ دَفع.</summary>
    private sealed class ForbiddenPaymentProvider : IPaymentProvider
    {
        private static InvalidOperationException Touched() => new("لا مالَ في صَفَقاتِ هذا المِلَفّ — نودِيَ مُزَوِّدُ الدَفع.");
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

    /// <summary>جِسمُ نُقطَةِ صَفَقاتِ الاستوديو بِاسمِها الأَخير (<c>advance</c>، <c>cancel</c>، <c>dispute</c>) بِلا تَعليقات.</summary>
    private static string StudioEndpoint(string verb)
        => WriteEndpointGuardTests.StripComments(Segment(Source(Template, "MarketplaceTemplateExtensions.cs"),
            $"\"/studio/apps/{{slug}}/deals/{{id:guid}}/{verb}\"", ".DisableAntiforgery();"));

    private static string Page(string name)
        => WriteEndpointGuardTests.StripMarkupComments(Source(Template, "Components", "Pages", name));

    /// <summary>الحِفظُ التالي يَرمي ما يَرميه Marten حينَ كَتَبَ غَيرُ الجَلسَةِ الصَفقَةَ بَعدَ قِراءَتِها — مَرَّةً واحِدَة.</summary>
    private static void ConflictOnNextSave(DocWorld world, Guid id)
    {
        var armed = true;
        world.BeforeMember = name =>
        {
            if (name != "SaveChangesAsync" || !armed) return;
            armed = false;
            throw new JasperFx.ConcurrencyException(typeof(Deal), id);
        };
    }

    /// <summary>صَفقَةُ سَلَّةٍ نَقدِيَّةٌ مَربوطٌ بائِعُها عِندَ الوِلادَة — بِتَسَلسُلِ <c>/checkout/submit</c>.</summary>
    private static (DocWorld World, DealsService Deals, Guid Id) CartDeal()
    {
        var world = new DocWorld();
        var deals = new DealsService(world.Store, new ForbiddenPaymentProvider());
        var deal = Now(deals.StartAsync(Slug, "marketplace", Buyer, "مُشتَرٍ", null, "سِلعَة", 100m,
            new() { [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery }));
        Now(deals.AttachRefAsync(Slug, deal.Id, DealsService.ListingOwnerRef, Seller.ToString()));
        Now(deals.BindListingOwnerAsync(Slug, deal.Id, Seller.ToString()));
        return (world, deals, deal.Id);
    }

    // ═══ ١) «اليَوم» — الرَفضُ قائِمٌ بِرَمزِه في الخِدمَة، والنُقطَةُ تُسقِطُه، والصَفحَةُ لا تَقرَأُ شَيئاً ═════════════

    [Fact]
    public void Today_the_studio_deal_endpoints_drop_the_refusal_code_and_the_studio_deal_pages_read_none()
    {
        // الخِدمَةُ تَرُدُّ بِرَمز: تَعارُضُ النُسخَةِ في التَقَدُّمِ والإلغاء.
        var (world, deals, id) = CartDeal();
        ConflictOnNextSave(world, id);
        var advanced = Now(deals.AdvanceAsync(Slug, id, Seller, "مالِك التَّطبيق (إداريّ)", ""));
        Assert.False(advanced.Ok);
        Assert.Equal(DealsService.ChangedMeanwhile, advanced.Code);

        ConflictOnNextSave(world, id);
        var cancelled = Now(deals.CancelAsync(Slug, id, new DealCanceller(AppOwner, "مالِك التَّطبيق", IsStoreAdmin: true), "إلغاء إداريّ"));
        Assert.False(cancelled.Ok);
        Assert.Equal(DealsService.ChangedMeanwhile, cancelled.Violation!.Code);

        // وصَفقَةُ البَذرَةِ التَجريبِيَّةِ — مُبادِرُها مالِكُ التَطبيق — لا يُحَرِّكُها المَسارُ الإداريّ.
        var seed = Now(deals.StartAsync(Slug, "marketplace", AppOwner, "صاحِب الفِكرَة", null, "عَمَلِيَّة تَجريبيَّة — marketplace", 250m));
        Assert.False(DealsService.AdminPathCanMove(seed, AppOwner));

        // والنِقاطُ الثَلاثُ تَعودُ إلى الصَفحَةِ بِلا رَمز.
        var advance = StudioEndpoint("advance");
        var cancel = StudioEndpoint("cancel");
        var dispute = StudioEndpoint("dispute");
        Assert.Contains("if (!Services.Deals.DealsService.AdminPathCanMove(deal, auth.UserId!.Value)) return Results.Redirect($\"/studio/apps/{slug}/deals/{id}\");",
            advance, StringComparison.Ordinal);
        foreach (var (name, body) in new[] { ("advance", advance), ("cancel", cancel), ("dispute", dispute) })
        {
            Assert.DoesNotContain("?err=", body, StringComparison.Ordinal);
            Assert.EndsWith("return Results.Redirect($\"/studio/apps/{slug}/deals/{id}\");\n        })",
                body.TrimEnd().Replace("\r", ""), StringComparison.Ordinal);
            output.WriteLine($"{name}: يَعودُ إلى الصَفحَةِ بِلا رَمز.");
        }

        // والصَفحَتانِ لا تَقرَآنِ مُعامِلاً.
        Assert.DoesNotContain("SupplyParameterFromQuery", Page("StudioAppDealDetail.razor"), StringComparison.Ordinal);
        Assert.DoesNotContain("SupplyParameterFromQuery", Page("StudioAppDeals.razor"), StringComparison.Ordinal);
    }
}
