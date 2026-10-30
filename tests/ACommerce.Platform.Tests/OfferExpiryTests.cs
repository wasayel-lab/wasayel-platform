using ACommerce.Kit.Offers;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>عَرضٌ انتَهَت مُدَّتُه لا يُقبَل</b> (‏2026-10-30، تَدقيقُ الموجَة ١١ CO8). <b>تَوصيفُ «اليَوم» قَبلَ التَبديل</b> (القاعِدَة ٣): بابُ
/// <c>POST /{slug}/offers/{id}/accept</c> يَسأَلُ «مُعَلَّق؟» و«المالِك؟» و«حَذَفَ صاحِبُه حِسابَه؟» ثُمَّ يَكتُبُ <c>OfferAccepted</c> — ولا يَسأَلُ
/// <c>ExpiresAt</c>؛ فَمالِكٌ فَتَحَ صَفحَةَ الإعلانِ قَبلَ انتِهاءِ العَرضِ ونَقَرَ «قبول» بَعدَه يَقبَلُ عَرضاً مُنتَهِياً، وتولَدُ مُطابَقَةٌ ومُحادَثَةٌ وإشعار.
/// وقائِمَةُ عُروضِ الإعلانِ تُرَشِّحُ المُنتَهِيَ عِندَ الرَسمِ (<c>StorefrontQueries.ListingDetailAsync</c>: <c>ExpiresAt &gt; now</c>) — والبابُ لا يَعرِفُ ذلك.
/// و«عُروضي» (صَفحَةُ مُقَدِّمِ العَرض) لا تَرسُمُ «قبول» أَصلاً، وتَرسُمُ لِلمُعَلَّقِ المُنتَهي «قَيد المُراجَعَة · صالِح حَتَّى» وزِرَّ «سَحب».</para>
/// </summary>
public class OfferExpiryTests(ITestOutputHelper output)
{
    private static readonly string Tpl = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace");

    internal static string AcceptDoorBody()
    {
        var src = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(Tpl, "MarketplaceTemplateExtensions.cs")));
        var start = src.IndexOf("\"/{slug}/offers/{id:guid}/accept\"", StringComparison.Ordinal);
        Assert.True(start > 0, "لا بابَ قَبول — الأَداةُ عَمياء.");
        var end = src.IndexOf("RequireStoreWritable", start, StringComparison.Ordinal);
        Assert.True(end > start, "لا نِهايَةَ لِجِسمِ بابِ القَبول — الأَداةُ عَمياء.");
        return src[start..end];
    }

    private static string Page(string name)
        => WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(Tpl, "Components", "Pages", name)));

    [Fact]
    public void Today_the_accept_door_never_asks_whether_the_offer_expired()
    {
        var body = AcceptDoorBody();
        var predicates = typeof(Offer).Assembly.GetTypes().Count(t => t.Name.Contains("Expiry", StringComparison.Ordinal));
        var mine = Page("MyOffers.razor");
        var detail = Page("TenantListingDetail.razor");
        output.WriteLine($"ذِكرُ offer.ExpiresAt في جِسمِ بابِ القَبول: {body.Contains("offer.ExpiresAt", StringComparison.Ordinal)} · نَوعُ انتِهاءٍ في كيت العُروض: {predicates} · " +
                         $"«قبول» في «عُروضي»: {mine.Contains("/accept", StringComparison.Ordinal)} · رَمزُ offer_expired في صَفحَةِ الإعلان: {detail.Contains("offer_expired", StringComparison.Ordinal)}");

        // ‏`ExpiresAt` الوَحيدُ في الجِسمِ مُهلَةُ المُحادَثَةِ الَّتي يَفتَحُها القَبول (`ExpiresAt = now.AddHours(24)`) — لا مُدَّةُ العَرض.
        Assert.DoesNotContain("offer.ExpiresAt", body, StringComparison.Ordinal);
        Assert.Contains("ExpiresAt = now.AddHours(24)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("offer_expired", body, StringComparison.Ordinal);
        Assert.Equal(0, predicates);
        Assert.DoesNotContain("/accept", mine, StringComparison.Ordinal);
        Assert.Contains("@StatusLabel(o.Status)", mine, StringComparison.Ordinal);
        Assert.DoesNotContain("offer_expired", detail, StringComparison.Ordinal);
    }
}
