using ACommerce.Kit.Auth.Server;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Services;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ مُشاهَدَةٌ واحِدَةٌ لِكُلِّ زائِرٍ في الإعلانِ في النافِذَة (تَدقيقُ الإطلاق lens-security-5، كُبرى؛ ‏2026-10-24) ═══
//
// `POST /{slug}/listings/{id}/view` عامَّةٌ بِالتَصميم، وكانَت تُلحِقُ `ListingViewed` بِكُلِّ طَلَبٍ بِلا حَدّ. الحَدُّ الآن `ListingViews.Counts`
// قَبلَ أَيِّ قِراءَةٍ أَو كِتابَة (مَفروضٌ تَرتيبُه في `WriteEndpointGuardTests.The_view_door_is_pinned_public_and_bounded_before_it_reads_or_appends`)،
// وهُنا الدالَّتانِ بِحُدودِهِما: مَن الزائِر، ومَتى تُعَدّ. والنافِذَةُ ساعَةُ حِصَّةِ الإرسالِ (لا رَقَمَ جَديد — «ب» W8-2).
// ‏(‏2026-10-28، قَرارُ المالِك د58؛ يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) النافِذَةُ يَوم (‏`AuthHandlers.ViewWindow`)، والمَفاتيحُ بِسَقف (‏`AuthHandlers.MaxViewKeys`).
public class ListingViewBoundTests(ITestOutputHelper output)
{
    private static string Slug() => $"lv-{Guid.NewGuid():N}"[..16];

    // ─── د58: تَوصيفُ «اليَوم» قَبلَ نافِذَةِ اليَومِ وسَقفِ المَفاتيح (‏2026-10-28، القاعِدَة ٣) ─────────────────────────
    //
    // قَرارُ المالِك د58: «مُشاهَدَةٌ واحِدَةٌ لِكُلِّ زائِرٍ لِكُلِّ إعلانٍ في اليَوم (‏٢٤ ساعَة)»، و«حَدٌّ أَقصى تِقنِيٌّ لِحَجمِ العَدّادِ في الذاكِرَةِ
    // بِحُدودِ مِئَةِ أَلفِ مِفتاح… عِندَ بُلوغِه تُحذَفُ المَفاتيحُ المُنتَهِيَةُ أَوَّلاً. فَإن بَقِيَ مُمتَلِئاً لا تُعَدُّ المُشاهَدَةُ الجَديدَة ولا يَرى الزائِرُ
    // أَيَّ خَطَأ، ويُكتَبُ تَحذيرٌ في سِجِلِّ الخادِم». وهذا ما هِيَ عَلَيه قَبلَه.

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-28، كوميتُ تَبديلِ د58)</b> — كانَ
    /// <c>Today_the_view_quota_counts_one_per_key_per_hour_and_its_keys_have_no_bound</c> (‏<c>cf37171</c>): <c>TryConsume(_viewRl, key, 1,
    /// TimeSpan.FromHours(1))</c> بِلا سَقف. والآن الحِصَّةُ <c>OncePerWindow</c> بِنافِذَةِ يَومٍ وسَقفِ مِئَةِ أَلفِ مِفتاح، وأَلفُ مِفتاحٍ مُختَلِفٍ تُعَدُّ
    /// كُلُّها كَما كانَت (السَقفُ بَعيد)؛ والنُقطَةُ تُمَرِّرُ <c>logs</c> و<c>Counts</c> لا يَكتُبُ التَحذيرَ إلّا لِـ<c>FullAfterSweep</c>.</summary>
    [Fact]
    public void The_view_quota_counts_one_per_key_per_day_and_its_keys_are_bounded_at_one_hundred_thousand()
    {
        var auth = WriteEndpointGuardTests.StripComments(StudioConsentCharacterizationTests.Source(
            "libs", "kits", "Auth", "ACommerce.Kit.Auth.Server", "AuthHandlers.cs"));
        var at = auth.IndexOf("public static ViewCount ConsumeViewQuota(string key)", StringComparison.Ordinal);
        Assert.True(at > 0, "لا ConsumeViewQuota — الأَداةُ عَمياء.");
        var body = auth[at..(auth.IndexOf(';', at) + 1)];
        var views = WriteEndpointGuardTests.StripComments(StudioConsentCharacterizationTests.Source(
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Services", "ListingViews.cs"));
        var slug = Slug();
        var counted = Enumerable.Range(0, 1000).Count(_ => ListingViews.Counts(slug, Guid.NewGuid(), null, "198.51.100.7"));
        output.WriteLine($"جِسمُ الحِصَّة: {body.Trim()} · النافِذَة: {AuthHandlers.ViewWindow} · السَقف: {AuthHandlers.MaxViewKeys} · "
            + $"مَفاتيحُ مُختَلِفَة: 1000 · عُدَّ مِنها: {counted}");

        Assert.Contains("=> _views.Consume(key, DateTime.UtcNow);", body, StringComparison.Ordinal);
        Assert.Contains("private static readonly OncePerWindow _views = new(ViewWindow, MaxViewKeys);", auth, StringComparison.Ordinal);
        Assert.DoesNotContain("_viewRl", auth, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromHours(24), AuthHandlers.ViewWindow);
        Assert.Equal(100_000, AuthHandlers.MaxViewKeys);
        Assert.Equal(1000, counted);
        Assert.Contains("if (outcome == AuthHandlers.ViewCount.FullAfterSweep)", views, StringComparison.Ordinal);
        Assert.Contains("return outcome == AuthHandlers.ViewCount.Counted;", views, StringComparison.Ordinal);
    }

    /// <summary><b>مُوجَبٌ وسالِب لِنافِذَةِ اليَوم</b>: المِفتاحُ يُعَدُّ مَرَّة، ولا يُعَدُّ قَبلَ تَمامِ اليَوم ولو بِدَقيقَة، ويُعَدُّ بَعدَه.</summary>
    [Fact]
    public void A_key_counts_once_within_the_day_and_again_once_the_day_has_passed()
    {
        var q = new AuthHandlers.OncePerWindow(AuthHandlers.ViewWindow, AuthHandlers.MaxViewKeys);
        var t0 = new DateTime(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc);
        var rows = new (TimeSpan After, AuthHandlers.ViewCount Expected)[]
        {
            (TimeSpan.Zero, AuthHandlers.ViewCount.Counted),
            (TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1), AuthHandlers.ViewCount.Repeat),   // ‏نافِذَةُ السّاعَةِ القَديمَةِ كانَت سَتَعُدُّها
            (TimeSpan.FromHours(24) - TimeSpan.FromMinutes(1), AuthHandlers.ViewCount.Repeat),
            (TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1), AuthHandlers.ViewCount.Counted),
        };
        var got = rows.Select(r => q.Consume("lv|view:x|ip:198.51.100.7", t0 + r.After)).ToList();
        output.WriteLine($"حالات: {rows.Length} · " + string.Join(" · ", rows.Zip(got, (r, g) => $"+{r.After}: {g}")));
        Assert.Equal(rows.Select(r => r.Expected), got);
        Assert.Equal(1, q.Keys);
    }

    /// <summary><b>مُوجَبٌ وسالِب لِلسَقف</b> (بِسَقفٍ صَغيرٍ في عَدّادٍ مُستَقِلٍّ — لا يَمَسُّ عَدّادَ المَنَصَّة): مِفتاحٌ جَديدٌ والعَدّادُ مُمتَلِئ ⇒
    /// كَنسٌ فَوريٌّ ثُمَّ <c>FullAfterSweep</c> إن لَم يَنقَضِ شَيء؛ وبَعدَه <c>Full</c> بِلا كَنسٍ حَتّى أَقرَبِ انقِضاء؛ ومِفتاحٌ قائِمٌ لا يُرَدُّ بِالسَقف؛
    /// وعِندَ أَقرَبِ انقِضاءٍ يُكنَسُ المُنتَهي ويُعَدُّ الجَديد.</summary>
    [Fact]
    public void A_full_counter_sweeps_expired_keys_first_then_refuses_new_keys_until_one_expires()
    {
        var q = new AuthHandlers.OncePerWindow(TimeSpan.FromHours(24), 3);
        var t0 = new DateTime(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc);
        var log = new List<string>();
        AuthHandlers.ViewCount At(string key, TimeSpan after)
        {
            var r = q.Consume(key, t0 + after);
            log.Add($"{key}@+{after.TotalHours}h={r}");
            return r;
        }

        Assert.Equal(AuthHandlers.ViewCount.Counted, At("a", TimeSpan.Zero));
        Assert.Equal(AuthHandlers.ViewCount.Counted, At("b", TimeSpan.FromHours(1)));
        Assert.Equal(AuthHandlers.ViewCount.Counted, At("c", TimeSpan.FromHours(2)));
        Assert.Equal(AuthHandlers.ViewCount.FullAfterSweep, At("d", TimeSpan.FromHours(3)));   // ‏كُنِسَ ولا مُنتَهي
        Assert.Equal(AuthHandlers.ViewCount.Full, At("e", TimeSpan.FromHours(4)));             // ‏لا كَنسَ قَبلَ أَقرَبِ انقِضاء (a عِندَ +24)
        Assert.Equal(AuthHandlers.ViewCount.Repeat, At("a", TimeSpan.FromHours(5)));           // ‏القائِمُ لا يُرَدُّ بِالسَقف
        Assert.Equal(3, q.Keys);
        Assert.Equal(AuthHandlers.ViewCount.Counted, At("d", TimeSpan.FromHours(24) + TimeSpan.FromMinutes(1)));   // ‏a انقَضى فَكُنِس
        Assert.Equal(3, q.Keys);
        Assert.Equal(AuthHandlers.ViewCount.Full, At("f", TimeSpan.FromHours(24) + TimeSpan.FromMinutes(2)));      // ‏أَقرَبُ انقِضاءٍ الآن b عِندَ +25
        output.WriteLine($"خُطُوات: {log.Count} · {string.Join(" · ", log)}");
        Assert.Equal(8, log.Count);
    }

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
