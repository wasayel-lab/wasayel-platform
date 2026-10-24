using ACommerce.Kit.Auth.Server;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ مُشاهَدَةٌ واحِدَةٌ لِكُلِّ زائِرٍ في الإعلانِ في النافِذَة (تَدقيقُ الإطلاق lens-security-5، كُبرى؛ ‏2026-10-24) ═══
//
// `POST /{slug}/listings/{id}/view` عامَّةٌ بِالتَصميم، وكانَت تُلحِقُ `ListingViewed` بِكُلِّ طَلَبٍ بِلا حَدّ. الحَدُّ الآن `ListingViews.Counts`
// قَبلَ أَيِّ قِراءَةٍ أَو كِتابَة (مَفروضٌ تَرتيبُه في `WriteEndpointGuardTests.The_view_door_is_pinned_public_and_bounded_before_it_reads_or_appends`)،
// وهُنا الدالَّتانِ بِحُدودِهِما: مَن الزائِر، ومَتى تُعَدّ. والنافِذَةُ ساعَةُ حِصَّةِ الإرسالِ (لا رَقَمَ جَديد — «ب» W8-2).
public class ListingViewBoundTests
{
    private static string Slug() => $"lv-{Guid.NewGuid():N}"[..16];

    [Fact]
    public void One_view_per_viewer_per_listing_counts_within_the_window()
    {
        var slug = Slug();
        var listing = Guid.NewGuid();
        var user = Guid.NewGuid();

        // المُوَثَّق: الأُولى تُعَدّ والثانِيَةُ لا، وإعلانٌ آخَرُ يُعَدّ.
        Assert.True(ListingViews.Counts(slug, listing, user, "198.51.100.7"));
        Assert.False(ListingViews.Counts(slug, listing, user, "198.51.100.7"));
        Assert.False(ListingViews.Counts(slug, listing, user, "203.0.113.5"));
        Assert.True(ListingViews.Counts(slug, Guid.NewGuid(), user, "198.51.100.7"));

        // المَجهولُ بِعُنوانِه: الأُولى تُعَدّ والثانِيَةُ لا، وعُنوانٌ آخَرُ يُعَدّ — والمُوَثَّقُ والمَجهولُ بِالعُنوانِ نَفسِه زائِران.
        Assert.True(ListingViews.Counts(slug, listing, null, "198.51.100.7"));
        Assert.False(ListingViews.Counts(slug, listing, null, "198.51.100.7"));
        Assert.True(ListingViews.Counts(slug, listing, null, "203.0.113.5"));
        Assert.True(ListingViews.Counts(slug, listing, Guid.NewGuid(), "198.51.100.7"));

        // ومَتجَرٌ آخَرُ بِالإعلانِ والزائِرِ نَفسَيهِما عَدّادٌ آخَر.
        Assert.True(ListingViews.Counts(Slug(), listing, user, "198.51.100.7"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_anonymous_viewer_without_an_address_shares_one_key_per_listing(string? ip)
    {
        var slug = Slug();
        var listing = Guid.NewGuid();
        Assert.Equal($"{slug}|view:{listing:N}|ip:-", ListingViews.QuotaKey(slug, listing, null, ip));
        Assert.True(ListingViews.Counts(slug, listing, null, ip));
        Assert.False(ListingViews.Counts(slug, listing, null, "-"));
    }

    [Fact]
    public void The_key_names_the_store_the_listing_and_the_viewer()
    {
        var listing = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");
        var user = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
        Assert.Equal("theme-demo|view:3f2504e04f8911d39a0c0305e82c3301|u:000000000000000000000000000000aa",
            ListingViews.QuotaKey("theme-demo", listing, user, "198.51.100.7"));
        Assert.Equal("theme-demo|view:3f2504e04f8911d39a0c0305e82c3301|ip:198.51.100.7",
            ListingViews.QuotaKey("theme-demo", listing, null, " 198.51.100.7 "));
    }

    [Fact]
    public void The_viewer_is_the_session_of_this_store_only_and_absent_is_anonymous()
    {
        var user = Guid.NewGuid();
        static HttpRequest With(string? cookie)
        {
            var http = new DefaultHttpContext();
            http.Request.Path = "/theme-demo/listings/x/view";
            if (cookie is not null) http.Request.Headers.Cookie = cookie;
            return http.Request;
        }

        Assert.Equal(user, ListingViews.ViewerOf(
            With($"{AuthSession.CookieName("theme-demo")}={AuthHandlers.MakeToken(user, "theme-demo")}"), "theme-demo"));
        Assert.Null(ListingViews.ViewerOf(
            With($"{AuthSession.CookieName("theme-demo")}={AuthHandlers.MakeToken(user, "ashare")}"), "theme-demo"));
        Assert.Null(ListingViews.ViewerOf(
            With($"{AuthSession.CookieName("ashare")}={AuthHandlers.MakeToken(user, "ashare")}"), "theme-demo"));
        Assert.Null(ListingViews.ViewerOf(With(null), "theme-demo"));
        Assert.Null(ListingViews.ViewerOf(With($"{AuthSession.CookieName("theme-demo")}=forged"), "theme-demo"));
    }
}
