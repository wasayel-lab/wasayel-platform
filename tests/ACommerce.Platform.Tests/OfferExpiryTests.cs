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

    /// <summary><b>قُلِبَ بِاسمِه (‏2026-10-30، CO8)</b> — كانَ <c>Today_the_accept_door_never_asks_whether_the_offer_expired</c> (‏<c>8041dd7</c>:
    /// «offer.ExpiresAt في جِسمِ البابِ: لا · نَوعُ انتِهاءٍ في الكيت: 0»). والآن البابُ يَسأَلُ <see cref="OfferExpiry.ExpiredAt"/> بَعدَ المالِكِ (وبَعدَ «حَذَفَ صاحِبُه
    /// حِسابَه») وقَبلَ أَوَّلِ كِتابَة، ويَرُدُّ بِـ<see cref="OfferExpiry.Code"/>؛ وصَفحَةُ الإعلانِ تَقرَأُ الرَمزَ بِنَصٍّ في القامُوس، ولا تَرسُمُ «قبول» إلّا لِعَرضٍ لَم يَنتَهِ
    /// بِالدالَّةِ نَفسِها؛ و«عُروضي» — ولا «قبول» فيها — تُسَمّي المُعَلَّقَ المُنتَهي «مُنتَهي» ولا تَرسُمُ لَه «صالِح حَتَّى» ولا «سَحب».</summary>
    [Fact]
    public void The_accept_door_refuses_an_expired_offer_after_the_owner_check_and_before_any_write_and_both_pages_ask_the_same()
    {
        var body = AcceptDoorBody();
        var owner = body.IndexOf("err=not_owner", StringComparison.Ordinal);
        var gone = body.IndexOf("AccountErasure.OffererGone(", StringComparison.Ordinal);
        var expired = body.IndexOf("ACommerce.Kit.Offers.OfferExpiry.ExpiredAt(offer, DateTime.UtcNow)", StringComparison.Ordinal);
        var code = body.IndexOf("?err={ACommerce.Kit.Offers.OfferExpiry.Code}", StringComparison.Ordinal);
        var firstWrite = body.IndexOf("OfferAccepted(", StringComparison.Ordinal);
        var mine = Page("MyOffers.razor");
        var detail = Page("TenantListingDetail.razor");
        var acceptForm = detail.IndexOf("/offers/{o.Id}/accept", StringComparison.Ordinal);
        var acceptGuard = detail.LastIndexOf("@if (isOwner && !ACommerce.Kit.Offers.OfferExpiry.ExpiredAt(o, now))", acceptForm, StringComparison.Ordinal);
        output.WriteLine($"المالِك @{owner} · الغائِب @{gone} · المُنتَهي @{expired} · رَمزُه @{code} · أَوَّلُ كِتابَة @{firstWrite} · حارِسُ «قبول» في صَفحَةِ الإعلان @{acceptGuard} < @{acceptForm}");

        Assert.True(owner > 0 && gone > owner && expired > gone && code > expired && firstWrite > code,
            "حارِسُ الانتِهاءِ لَيسَ بَعدَ المالِكِ وقَبلَ أَوَّلِ كِتابَة.");
        Assert.Equal("offer_expired", OfferExpiry.Code);

        Assert.True(acceptGuard > 0 && acceptForm > acceptGuard, "«قبول» في صَفحَةِ الإعلانِ لا يَسأَلُ الدالَّة.");
        Assert.Contains("now = DateTime.UtcNow;", detail, StringComparison.Ordinal);
        Assert.Contains("Auth.IsAuthenticated ? Auth.UserId : null, now);", detail, StringComparison.Ordinal);
        Assert.Contains("Err == ACommerce.Kit.Offers.OfferExpiry.Code", detail, StringComparison.Ordinal);
        Assert.Contains("@L[\"listings.detail.err_offer_expired\"]", detail, StringComparison.Ordinal);
        Assert.NotNull(ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "listings.detail.err_offer_expired"));

        Assert.DoesNotContain("/accept", mine, StringComparison.Ordinal);
        Assert.Contains("@StatusLabel(Shown(o))", mine, StringComparison.Ordinal);
        Assert.Contains("!ACommerce.Kit.Offers.OfferExpiry.ExpiredAt(o, now)", mine, StringComparison.Ordinal);
        Assert.Equal(2, CountOf(mine, "@if (Live(o))"));
        Assert.Equal(0, CountOf(mine, "@if (o.Status == ACommerce.Kit.Offers.OfferStatus.Pending)"));
    }

    private static int CountOf(string text, string what)
    {
        var n = 0;
        for (var i = text.IndexOf(what, StringComparison.Ordinal); i >= 0; i = text.IndexOf(what, i + what.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    /// <summary>مُوجَبٌ وسالِبٌ لِـ<see cref="OfferExpiry.ExpiredAt"/>: قَبلَ <c>ExpiresAt</c> ⇒ قائِم؛ عِندَها وبَعدَها ⇒ مُنتَهٍ — حَدُّ الاستِعلامَينِ القائِمَينِ نَفسُه
    /// (<c>ExpiresAt &gt; now</c> قائِم)، فَما تَرسُمُه القائِمَةُ يَقبَلُه البابُ في اللَحظَةِ نَفسِها. ولا حَدَثَ يُكتَب: <see cref="OfferExpired"/> بِلا مُصدِرٍ كَما كان.</summary>
    [Fact]
    public void An_offer_is_expired_from_its_expiry_instant_on_and_live_before_it()
    {
        var at = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
        var offer = new Offer();
        offer.Apply(new OfferSubmitted(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "سائِق", 30m, null, 0, 0, at, at.AddMinutes(-15)));

        Assert.False(OfferExpiry.ExpiredAt(offer, at.AddSeconds(-1)));
        Assert.True(OfferExpiry.ExpiredAt(offer, at));
        Assert.True(OfferExpiry.ExpiredAt(offer, at.AddMinutes(1)));
        Assert.Equal(OfferStatus.Pending, offer.Status);
        // الحَدُّ نَفسُه الَّذي تُرَشِّحُ بِه القائِمَتانِ: قائِمٌ ⇔ ExpiresAt > now.
        foreach (var now in new[] { at.AddSeconds(-1), at, at.AddSeconds(1) })
            Assert.Equal(offer.ExpiresAt > now, !OfferExpiry.ExpiredAt(offer, now));
        var emitters = EntitlementContractTests.SourceFiles()
            .Count(f => System.Text.RegularExpressions.Regex.IsMatch(WriteEndpointGuardTests.StripComments(f.Text), @"\bnew\s+(?:[\w.]+\.)?OfferExpired\s*\("));
        output.WriteLine($"مُصدِرو OfferExpired: {emitters}");
        Assert.Equal(0, emitters);
    }
}
