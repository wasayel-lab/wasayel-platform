using ACommerce.Kit.Auth.Server;
using ACommerce.Templates.Customer.Marketplace.Services;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ I-SEC-3 (تَدقيقُ الموجَة ١١، ‏2026-10-29): مُشاهَدَةُ إعلانٍ غَيرِ مَوجودٍ تَحجِزُ مِفتاحاً يَوماً ═══
//
// `POST /{slug}/listings/{id}/view` يَستَهلِكُ مِفتاحَ «مُشاهَدَةٍ واحِدَة» (`ListingViews.Counts`) **قَبلَ** قِراءَةِ الإعلان — وذاكَ مَقصود
// (الحَدُّ قَبلَ القِراءَة، lens-security-5) — ثُمَّ يُجيبُ ‏404 لِإعلانٍ غَيرِ مَوجودٍ أَو مَحذوف ولا يُعيدُ المِفتاح. فَمُعَرِّفاتٌ عَشوائِيَّةٌ
// (لا تُكَلِّفُ المُرسِلَ شَيئاً) تَملَأُ سَقفَ المِئَةِ أَلفِ مِفتاح (`AuthHandlers.MaxViewKeys`، د58) يَوماً كامِلاً، وعِندَها لا تُعَدُّ
// مُشاهَدَةُ زائِرٍ حَقيقيٍّ لِإعلانٍ حَقيقيّ (`FullAfterSweep` — ولا يَرى أَحَدٌ خَطَأ).
public class ListingViewMissingTests(ITestOutputHelper output)
{
    private static string Slug() => $"lvm-{Guid.NewGuid():N}"[..16];

    private const string ViewRoute = "/{slug}/listings/{id:guid}/view";

    private static string ViewBody()
    {
        var views = WriteEndpointGuardTests.AllMinimalApiEndpoints().Where(e => e.Route == ViewRoute).ToList();
        Assert.True(views.Count == 1, $"نُقاطُ المُشاهَدَة: {views.Count} — الأَداةُ عَمياء.");
        return views[0].Body;
    }

    private static Microsoft.AspNetCore.Http.HttpRequest From(string ip)
    {
        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(ip);
        return ctx.Request;
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-29، I-SEC-3 — كوميتُ التَبديل)</b> — كانَ <c>Today_a_view_of_a_missing_listing_keeps_its_key_for_the_window</c>
    /// (‏<c>c74b2ac</c>): «فَرعُ لا إعلان ‹return Results.NotFound();› · أَعضاءُ OncePerWindow Consume · الأُولى True والثانِيَة False». والآن الفَرعُ يَرُدُّ
    /// بِـ<c>ListingViews.NotFound</c>: مُعَرِّفٌ لا مَجرى لَه يُعيدُ مِفتاحَه فَيُعَدُّ مِن جَديد، والمَحذوفُ يُبقيه (لا إعادَةَ بِناءٍ لِمَجراه في كُلِّ طَلَب).</summary>
    [Fact]
    public void A_view_of_a_missing_listing_gives_its_key_back_and_a_deleted_one_keeps_it()
    {
        var body = ViewBody();
        var read = body.IndexOf("AggregateStreamAsync<Listing>(", StringComparison.Ordinal);
        var branch = body[read..];
        branch = branch[..branch.IndexOf("Events.Append(", StringComparison.Ordinal)];
        var members = typeof(AuthHandlers.OncePerWindow).GetMethods()
            .Where(m => m.DeclaringType == typeof(AuthHandlers.OncePerWindow) && !m.IsSpecialName).Select(m => m.Name).Order(StringComparer.Ordinal).ToList();

        var slug = Slug();
        var missing = Guid.NewGuid();
        var first = ListingViews.Counts(slug, missing, null, "198.51.100.31");
        var answer = ListingViews.NotFound(From("198.51.100.31"), slug, missing, null, existed: false);
        var again = ListingViews.Counts(slug, missing, null, "198.51.100.31");

        var deleted = Guid.NewGuid();
        var firstDeleted = ListingViews.Counts(slug, deleted, null, "198.51.100.31");
        ListingViews.NotFound(From("198.51.100.31"), slug, deleted, null, existed: true);
        var againDeleted = ListingViews.Counts(slug, deleted, null, "198.51.100.31");
        output.WriteLine($"بَعدَ التَبديل: أَعضاءُ OncePerWindow {string.Join(",", members)} · مُعَرِّفٌ لا مَجرى لَه: الأُولى {first} والثانِيَة {again} · مَحذوف: الأُولى {firstDeleted} والثانِيَة {againDeleted}");
        Assert.Contains("if (listing is null || listing.IsDeleted) return Services.ListingViews.NotFound(req, slug, id, viewerId, existed: listing is not null);", branch, StringComparison.Ordinal);
        Assert.DoesNotContain("return Results.NotFound();", branch, StringComparison.Ordinal);
        Assert.Contains("Services.ListingViews.ClientIp(req)", body, StringComparison.Ordinal);
        Assert.Equal(new[] { "Consume", "Forget" }, members);
        Assert.Equal(404, Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IStatusCodeHttpResult>(answer).StatusCode);
        Assert.Equal((true, true), (first, again));
        Assert.Equal((true, false), (firstDeleted, againDeleted));
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-29، I-SEC-3)</b> — كانَ <c>Today_missing_ids_fill_a_bounded_counter_and_a_real_view_is_not_counted</c>:
    /// «مَفاتيحُ بَعدَ ثَلاثَةِ مُعَرِّفاتٍ 3 · مُشاهَدَةٌ حَقيقِيَّة FullAfterSweep». والآن مُعَرِّفاتٌ تُعادُ مَفاتيحُها لا تَملَأُ العَدّاد — عَشَرَةٌ بِسَقفِ ثَلاثَة
    /// تُبقي 0، والمُشاهَدَةُ الحَقيقِيَّةُ تُعَدّ.</summary>
    [Fact]
    public void Missing_ids_given_back_leave_a_bounded_counter_room_for_a_real_view()
    {
        var counter = new AuthHandlers.OncePerWindow(TimeSpan.FromHours(24), 3);
        var now = new DateTime(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);
        var counted = 0;
        foreach (var i in Enumerable.Range(0, 10))
        {
            var key = $"zz|view:{Guid.NewGuid():N}|ip:198.51.100.{i}";
            if (counter.Consume(key, now) == AuthHandlers.ViewCount.Counted) counted++;
            Assert.True(counter.Forget(key));
        }
        var real = counter.Consume($"zz|view:{Guid.NewGuid():N}|ip:203.0.113.9", now);
        output.WriteLine($"بَعدَ التَبديل: عَشَرَةُ مُعَرِّفاتٍ لا مَجرى لَها (عُدَّ {counted}) · المَفاتيحُ بَعدَها {counter.Keys - 1} · مُشاهَدَةٌ حَقيقِيَّة: {real}");
        Assert.Equal(10, counted);
        Assert.Equal(AuthHandlers.ViewCount.Counted, real);
        Assert.Equal(1, counter.Keys);
        Assert.False(counter.Forget("zz|absent"));
    }

    /// <summary>السالِب: مِفتاحُ مُشاهَدَةٍ حَقيقِيَّةٍ يَبقى بَعدَ ‏404 لِمُعَرِّفٍ آخَرَ مِن الزائِرِ نَفسِه — الإعادَةُ لِمِفتاحِ ذلِكَ المُعَرِّفِ وَحدَه.</summary>
    [Fact]
    public void A_real_view_key_stays_after_a_404_for_another_id_from_the_same_viewer()
    {
        var slug = Slug();
        var real = Guid.NewGuid();
        var viewer = Guid.NewGuid();
        Assert.True(ListingViews.Counts(slug, real, viewer, "198.51.100.32"));
        var missing = Guid.NewGuid();
        Assert.True(ListingViews.Counts(slug, missing, viewer, "198.51.100.32"));
        ListingViews.NotFound(From("198.51.100.32"), slug, missing, viewer, existed: false);
        Assert.False(ListingViews.Counts(slug, real, viewer, "198.51.100.32"));
        Assert.True(ListingViews.Counts(slug, missing, viewer, "198.51.100.32"));
    }
}
