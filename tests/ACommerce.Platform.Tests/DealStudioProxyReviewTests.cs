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

// **وقُلِبَ بِاسمَيه** (‏2026-10-29، قَرارُ المالِك د39 «ب»: «يُحذف الباب: لا يكتب التقييم إلا صاحبه بعد اكتمال صفقته»؛ التَوصيفُ أَعلاه مُودَعٌ
// قَبلَه): حُذِفَت النُقطَةُ وكُتلَةُ «التَقييم المُتَبادَل» في صَفحَةِ صَفقَةِ الاستوديو ونَصُّ تَلميحِها ومَفاتيحُها الثَمانِيَةُ في القامُوس (لا قارِئَ لَها
// غَيرُها)، فَلا يَكتُبُ تَقييماً مَربوطاً بِصَفقَةٍ إلّا `DealReviews` خَلفَ `DealReviewAuthorization`. والبَديلُ الصادِقُ بِوَسمِ «مِن إدارَةِ المَتجَر» يُبنى عِندَ
// أَوَّلِ طَلَبٍ مِن مَتجَر (القاعِدَة ١).

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

    /// <summary>مَفاتيحُ القامُوسِ الثَمانِيَةُ الَّتي لَم يَكُن يَقرَؤُها إلّا كُتلَةُ «التَقييم المُتَبادَل» في صَفحَةِ الاستوديو — حُذِفَت مَعَها.</summary>
    private static readonly string[] RemovedKeys =
    {
        "studio.deal.review", "studio.deal.review_hint", "studio.deal.review_submit", "studio.deal.reviewer",
        "studio.deal.reviews_verb", "studio.deal.stars", "studio.deal.comment", "studio.deal.review_placeholder",
    };

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (كانَ <c>Today_the_studio_review_door_writes_in_a_partys_name_without_the_review_gate_at_any_stage</c>): لا نُقطَةَ
    /// تَحتَ <c>/studio/</c> تَكتُبُ تَقييماً — صِفرُ مَسارٍ يَنتَهي بِـ<c>/review</c> بَينَ نِقاطِ الاستوديو المَعدودَة — وصَفحَةُ صَفقَةِ الاستوديو بِلا استِمارَةِ
    /// تَقييمٍ ولا مَفاتيحِها، والمَفاتيحُ الثَمانِيَةُ غائِبَةٌ عَن القامُوس؛ وبابُ العُضوِ باقٍ.</para>
    /// </summary>
    [Fact]
    public void The_studio_has_no_review_door_and_its_deal_page_draws_no_review_form()
    {
        var endpoints = WriteEndpointGuardTests.AllMinimalApiEndpoints().ToList();
        var studio = endpoints.Where(e => e.Route.StartsWith("/studio/", StringComparison.Ordinal)).ToList();
        Assert.True(studio.Count > 20, $"أَداةٌ عَمياء: عُدَّت {studio.Count} نُقطَةَ استوديو فَقَط.");
        Assert.DoesNotContain(studio, e => e.Route.EndsWith("/review", StringComparison.Ordinal));
        Assert.Single(endpoints, e => e.Route == "/{slug}/deals/{id:guid}/review");
        Assert.DoesNotContain("/studio/apps/{slug}/deals/{id:guid}/review",
            WriteEndpointGuardTests.StripComments(Source(Template, "MarketplaceTemplateExtensions.cs")), StringComparison.Ordinal);

        var page = WriteEndpointGuardTests.StripMarkupComments(Source(Template, "Components", "Pages", "StudioAppDealDetail.razor"));
        Assert.DoesNotContain("/review\")", page, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"rating\"", page, StringComparison.Ordinal);
        foreach (var key in RemovedKeys)
        {
            Assert.DoesNotContain($"\"{key}\"", page, StringComparison.Ordinal);
            Assert.Null(ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, key));
        }
        output.WriteLine($"نِقاطُ الاستوديو المَعدودَة {studio.Count}: صِفرُ بابِ تَقييم · المَفاتيحُ المَحذوفَة {RemovedKeys.Length} غائِبَة · بابُ العُضوِ واحِد");
    }

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (كانَ <c>Today_the_gate_refuses_the_parties_themselves_on_an_active_delivered_deal_that_the_studio_door_writes_for</c>):
    /// البَوّابَةُ تَرُدُّ الطَرَفَينِ عَلى صَفقَةٍ فَعّالَةٍ عِندَ «تَسليم» كَما كانَت — <b>ولا كاتِبَ يَتَجاوَزُها بَعدَ اليَوم</b>: نِداءُ <c>SubmitAsync</c> بِمُعَرِّفِ
    /// صَفقَةٍ (<c>dealId:</c>) في مَصادِرِ <c>libs</c> و<c>apps</c> كُلِّها واحِدٌ، في <c>DealReviews</c> بَعدَ <c>DealReviewAuthorization.Validate</c>.</para>
    /// </summary>
    [Fact]
    public void The_gate_refuses_the_parties_on_an_active_delivered_deal_and_no_writer_bypasses_it()
    {
        var deal = new Deal
        {
            Id = Guid.NewGuid(), Pattern = "marketplace", Stage = DealStage.Delivered, Status = DealStatus.Active, AmountSar = 70m,
            InitiatorId = Buyer, InitiatorName = "مُشتَرٍ", CounterpartyId = Seller, CounterpartyName = "بائِع",
        };
        Assert.Equal(DealReviewAuthorization.DealNotCompleted, DealReviewAuthorization.Validate(deal, new DealReviewer(Buyer, "مُشتَرٍ"))!.Code);
        Assert.Equal(DealReviewAuthorization.DealNotCompleted, DealReviewAuthorization.Validate(deal, new DealReviewer(Seller, "بائِع"))!.Code);

        var scanned = 0;
        var writers = new List<string>();
        foreach (var root in new[] { "libs", "apps" })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot, root), "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
                                 && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                 && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            scanned++;
            var text = WriteEndpointGuardTests.StripComments(File.ReadAllText(file));
            for (var at = text.IndexOf("SubmitAsync(", StringComparison.Ordinal); at >= 0; at = text.IndexOf("SubmitAsync(", at + 1, StringComparison.Ordinal))
            {
                var call = text.Substring(at, Math.Min(400, text.Length - at));
                var close = call.IndexOf(");", StringComparison.Ordinal);
                if (close > 0 && call[..close].Contains("dealId:", StringComparison.Ordinal))
                    writers.Add(Path.GetRelativePath(RepoRoot, file).Replace('\\', '/'));
            }
        }
        Assert.True(scanned > 200, $"أَداةٌ عَمياء: مُسِحَ {scanned} مِلَفّاً فَقَط.");
        Assert.Equal(new[] { Template + "/Services/Deals/DealReviews.cs" }, writers.ToArray());
        var service = WriteEndpointGuardTests.StripComments(Source(Template, "Services", "Deals", "DealReviews.cs"));
        Assert.True(service.IndexOf("DealReviewAuthorization.Validate(deal, by)", StringComparison.Ordinal)
                    < service.IndexOf("_reviews.SubmitAsync(", StringComparison.Ordinal), "الكاتِبُ لا يَسأَلُ البَوّابَةَ قَبلَ الكِتابَة.");
        output.WriteLine($"«تَسليم» فَعّالَة: الطَرَفانِ مَردودانِ بِـ{DealReviewAuthorization.DealNotCompleted} · مَلَفّاتٌ مَمسوحَة {scanned} · كُتّابُ تَقييمِ الصَفقَة: {string.Join(", ", writers)}");
    }
}
