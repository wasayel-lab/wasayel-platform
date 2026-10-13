using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ أَيُّ بابٍ يُحَرِّكُ الصَفقَةَ يُخبِرُ المُشتَري؟ — الاستوديو والـAPI بِجِوارِ أَبوابِ العُضو (مُراجَعَةُ المَوجَة 3a، G4 (iii)) ═══
//
// **المَقيسُ قَبلَ اللَمس** (‏2026-10-13): أَبوابُ العُضوِ تَمُرُّ بِـ`DealNotices` — `/accept` يُخبِرُ المُشتَري بِالقَبول، و`/cancel` بِالإلغاءِ حينَ
// يُلغي غَيرُه (`TellsTheBuyer`). أَمّا نُقطَةُ الإلغاءِ الإدارِيَّةُ في الاستوديو فَتَكتُبُ التَدقيقَ ولا تُخبِرُ أَحَداً، ونُقطَتا الـAPI (`advance` و`cancel`)
// لا تَعرِفانِ `DealNotices` أَصلاً — والبائِعُ المَربوطُ يُخرِجُ `Offered` إلى `Booked` بِمِفتاحِه (‏ADR-033 §٦‑ط‑٤: «يُعادِلُ القَبولَ بِلا إشعارِ
// المُشتَري»، و§٦‑ط‑٦ تَرَكَه سُؤالاً). فَالمُشتَري يَعلَمُ بِإلغاءِ طَلَبِه أَو قَبولِه بِحَسَبِ البابِ الَّذي سَلَكَه الطَرَفُ الآخَر.
//
// **التَوصيفُ قَبلَ التَبديل** (القاعِدَة ٣): صَفُّ «اليَوم» يُودَعُ وَحدَه، ويُقلَبُ بِاسمِه في كوميتِ الأُنبوب.

public class DealNoticeDoorsTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    private static readonly Guid Buyer    = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Seller   = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly Guid AppOwner = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");

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

    private static Deal CartDeal() => new()
    {
        Id = Guid.NewGuid(), Pattern = "marketplace", AmountSar = 150m, ListingTitle = "سِلعَة",
        InitiatorId = Buyer, InitiatorName = "مُشتَرٍ", CounterpartyId = Seller, CounterpartyName = "بائِع",
    };

    // ═══ «اليَوم» ═════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Today_the_studio_cancel_and_both_api_deal_writers_tell_the_buyer_nothing_while_the_member_doors_do()
    {
        var endpoints = Endpoints();

        // أَبوابُ العُضوِ تُخبِر.
        var memberAccept = Segment(endpoints, "\"/{slug}/deals/{id:guid}/accept\"", ".RequireStoreWritable();");
        var memberCancel = Segment(endpoints, "\"/{slug}/deals/{id:guid}/cancel\"", ".RequireStoreWritable();");
        Assert.Contains("await notices.NotifyAcceptedAsync(", memberAccept, StringComparison.Ordinal);
        Assert.Contains("await notices.NotifyCancelledAsync(", memberCancel, StringComparison.Ordinal);

        // وإلغاءُ الاستوديو الإداريُّ لا يَعرِفُ الأُنبوب — والمُشتَري غَيرُ المُلغي فَكانَ يُخبَرُ لَو سَلَكَ بابَ العُضو.
        var studioCancel = Segment(endpoints, "\"/studio/apps/{slug}/deals/{id:guid}/cancel\"", ".DisableAntiforgery();");
        Assert.DoesNotContain("notices", studioCancel, StringComparison.Ordinal);
        Assert.DoesNotContain("DealNotices", studioCancel, StringComparison.Ordinal);
        Assert.True(DealNotices.TellsTheBuyer(CartDeal(), AppOwner));

        // والـAPI لا يَعرِفُه أَصلاً — لا إلغاءٌ ولا تَقَدُّمٌ (ومِنه `Offered` → `Booked` بِمِفتاحِ البائِع: قَبولٌ بِلا إشعار).
        var api = Api();
        var apiAdvance = Segment(api, "\"/api/v1/deals/{id:guid}/advance\"", ".RequireApiKey(ApiScopeCatalog.DealsWrite);");
        var apiCancel = Segment(api, "\"/api/v1/deals/{id:guid}/cancel\"", ".RequireApiKey(ApiScopeCatalog.DealsWrite);");
        Assert.DoesNotContain("Notices", api, StringComparison.Ordinal);
        Assert.DoesNotContain("Notify", apiAdvance, StringComparison.Ordinal);
        Assert.DoesNotContain("Notify", apiCancel, StringComparison.Ordinal);
        Assert.Equal("counterparty", DealsPolicy.RequiredActorToLeave(new Deal { Pattern = "marketplace", Stage = DealStage.Offered }));
        output.WriteLine("العُضو: قَبولٌ وإلغاءٌ يُخبِران · الاستوديو: الإلغاءُ صامِت · الـAPI: التَقَدُّمُ والإلغاءُ صامِتان");
    }
}
