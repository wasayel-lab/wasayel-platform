using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ «تَقييمٌ نِيابَةً» في الاستوديو — يَكتُبُ بِاسمِ طَرَفٍ في أَيِّ مَرحَلَةٍ ويَتَجاوَزُ بَوّابَةَ التَقييم (تَدقيقُ الإطلاق ‏2026-10-24، lens-security-7؛
//     قَرارُ مالِك — «ب» W8-1·٦) ═══
//
// **المَقيسُ عِندَ الرَأس** (`a492d10`، وهُوَ ما كانَ عِندَ `dace7cd`): `POST /studio/apps/{slug}/deals/{id}/review` يَسأَلُ `StudioOwnsAsync` ثُمَّ يَختارُ
// الكاتِبَ مِن الصَفقَة — المُبادِرَ أَو الطَرَف — ويُنادي `ReviewsService.SubmitAsync` بِاسمِه مُباشَرَة: لا `DealReviewAuthorization` (الاكتِمالُ والطَرَفُ
// والنَفس)، ولا مُعَرِّفَ حَتمِيٌّ (مَرَّةً لِكُلِّ كاتِب)، و`VerifiedTransaction = dealId is not null` فَيُعَلَّمُ «مَوثوقاً»، وصَفحَةُ البائِعِ العامَّةُ تَعرِضُ
// اسمَ الكاتِب. والصَفحَةُ تَرسُمُ الاستِمارَةَ عِندَ «تَسليم» و«استِلام» و«تَقييم» ولَو كانَت الصَفقَةُ فَعّالَةً — والبابُ لا يَسأَلُ مَرحَلَةً أَصلاً.
//
// **ولَم يُبنَ شَيء** (القاعِدَة ١٦): تَمريرُه بِـ`DealReviews.SubmitAsync` بِهُوِيَّةِ صاحِبِ المَتجَرِ ووَسمِ «مِن إدارَةِ المَتجَر»، أَو حَذفُه — قَرارُ المالِك.
// هذا المِلَفُّ تَوصيفُ «اليَوم» يُودَعُ قَبلَ أَيِّ تَبديل (القاعِدَة ٣)، ويُقلَبُ بِاسمِه في كوميتِ القَرار.

public class DealStudioProxyReviewTests(ITestOutputHelper output)
{
    private static readonly Guid Buyer  = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Seller = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

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

    /// <summary>
    /// <para><b>«اليَوم» في البابِ والصَفحَة</b>: الكاتِبُ مِن الصَفقَة، والنِداءُ إلى <c>ReviewsService.SubmitAsync</c> بِلا البَوّابَةِ ولا المُعَرِّفِ الحَتمِيّ،
    /// وصِفرُ سُؤالٍ عَن الحالَةِ أَو المَرحَلَة؛ والاستِمارَةُ تُرسَمُ بِالمَرحَلَةِ وَحدَها؛ والتَقييمُ المَربوطُ بِصَفقَةٍ يُعَلَّمُ «مَوثوقاً».</para>
    /// </summary>
    [Fact]
    public void Today_the_studio_review_door_writes_in_a_partys_name_without_the_review_gate_at_any_stage()
    {
        var door = WriteEndpointGuardTests.StripComments(Segment(Source(Template, "MarketplaceTemplateExtensions.cs"),
            "app.MapPost(\"/studio/apps/{slug}/deals/{id:guid}/review\"", ".RequireFormBody().DisableAntiforgery();"));
        Assert.Contains("if (!await StudioOwnsAsync(store, auth, slug)) return Results.Redirect(\"/studio\");", door, StringComparison.Ordinal);
        Assert.Contains("var r = await reviews.SubmitAsync(slug, target, targetName, author, authorName,", door, StringComparison.Ordinal);
        Assert.Contains("deal.InitiatorId, deal.InitiatorName)", door, StringComparison.Ordinal);
        foreach (var absent in new[] { "DealReviewAuthorization", "DealReviews", "ReviewIdFor", "DealStatus", "DealStage", "id: " })
            Assert.DoesNotContain(absent, door, StringComparison.Ordinal);

        var page = WriteEndpointGuardTests.StripMarkupComments(Source(Template, "Components", "Pages", "StudioAppDealDetail.razor"));
        Assert.Contains("@if (deal.Stage is DealStage.Reviewed or DealStage.Delivered or DealStage.Received", page, StringComparison.Ordinal);

        var kit = Source("libs", "kits", "Reviews", "ACommerce.Kit.Reviews.Core", "ReviewsService.cs");
        Assert.Contains("VerifiedTransaction = dealId is not null,", kit, StringComparison.Ordinal);
        output.WriteLine("بابُ الاستوديو: الكاتِبُ مِن الصَفقَة، بِلا بَوّابَةِ التَقييمِ ولا مُعَرِّفٍ حَتمِيٍّ ولا مَرحَلَة، والتَقييمُ «مَوثوق»");
    }

    /// <summary>
    /// <para><b>«اليَوم» — المُقارَنَةُ بِالبَوّابَة</b>: صَفقَةٌ فَعّالَةٌ عِندَ «تَسليم» — الطَرَفانِ بِأَنفُسِهِما مَردودانِ بِـ<c>deal_not_completed</c>، والاستوديو
    /// يَرسُمُ الاستِمارَةَ لَها ويَكتُبُ بِاسمِ أَيِّهِما.</para>
    /// </summary>
    [Fact]
    public void Today_the_gate_refuses_the_parties_themselves_on_an_active_delivered_deal_that_the_studio_door_writes_for()
    {
        var deal = new Deal
        {
            Id = Guid.NewGuid(), Pattern = "marketplace", Stage = DealStage.Delivered, Status = DealStatus.Active, AmountSar = 70m,
            InitiatorId = Buyer, InitiatorName = "مُشتَرٍ", CounterpartyId = Seller, CounterpartyName = "بائِع",
        };
        var buyer = DealReviewAuthorization.Validate(deal, new DealReviewer(Buyer, "مُشتَرٍ"));
        var seller = DealReviewAuthorization.Validate(deal, new DealReviewer(Seller, "بائِع"));
        Assert.Equal(DealReviewAuthorization.DealNotCompleted, buyer!.Code);
        Assert.Equal(DealReviewAuthorization.DealNotCompleted, seller!.Code);
        Assert.True(deal.Stage is DealStage.Reviewed or DealStage.Delivered or DealStage.Received && deal.CounterpartyId is not null);
        output.WriteLine($"«تَسليم» فَعّالَة: المُشتَري {buyer.Code} · البائِع {seller.Code} · استِمارَةُ الاستوديو تُرسَم");
    }
}
