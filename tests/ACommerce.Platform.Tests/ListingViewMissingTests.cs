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

    /// <summary><b>تَوصيفُ اليَوم (‏2026-10-29، I-SEC-3)</b>: فَرعُ «لا إعلان» في البابِ يَرُدُّ ‏404 ولا يُعيدُ المِفتاح، و<c>OncePerWindow</c> لا
    /// عُضوَ فيه يَنزِعُ مِفتاحاً — فَمُشاهَدَةُ مُعَرِّفٍ غَيرِ مَوجودٍ تَبقى مِفتاحاً مَحجوزاً حَتّى تَنقَضيَ النافِذَة (الثانِيَةُ لا تُعَدّ).</summary>
    [Fact]
    public void Today_a_view_of_a_missing_listing_keeps_its_key_for_the_window()
    {
        var body = ViewBody();
        var read = body.IndexOf("AggregateStreamAsync<Listing>(", StringComparison.Ordinal);
        var branch = body[read..];
        branch = branch[..branch.IndexOf("Events.Append(", StringComparison.Ordinal)];
        var removers = typeof(AuthHandlers.OncePerWindow).GetMethods()
            .Where(m => m.DeclaringType == typeof(AuthHandlers.OncePerWindow) && !m.IsSpecialName).Select(m => m.Name).ToList();

        var slug = Slug();
        var missing = Guid.NewGuid();
        var first = ListingViews.Counts(slug, missing, null, "198.51.100.31");
        var again = ListingViews.Counts(slug, missing, null, "198.51.100.31");
        var notFound = branch.Split('\n').Select(l => l.Trim()).First(l => l.Contains("NotFound", StringComparison.Ordinal));
        output.WriteLine($"اليَوم: فَرعُ «لا إعلان» «{notFound}» · أَعضاءُ OncePerWindow {string.Join(",", removers)} · مُشاهَدَةُ مُعَرِّفٍ غَيرِ مَوجود: الأُولى {first} والثانِيَة {again}");
        Assert.Contains("if (listing is null || listing.IsDeleted) return Results.NotFound();", branch, StringComparison.Ordinal);
        Assert.Equal(new[] { "Consume" }, removers);
        Assert.True(first);
        Assert.False(again);
    }

    /// <summary><b>تَوصيفُ اليَوم</b> بِعَدّادٍ مَحَلِّيٍّ بِسَقفِ ثَلاثَة: ثَلاثُ مُعَرِّفاتٍ تَملَؤُه، فَمُشاهَدَةٌ رابِعَةٌ لِإعلانٍ حَقيقيٍّ لا تُعَدّ
    /// (<c>FullAfterSweep</c>) — والمَفاتيحُ لا تُنزَعُ إلّا بِانقِضاءِ النافِذَة.</summary>
    [Fact]
    public void Today_missing_ids_fill_a_bounded_counter_and_a_real_view_is_not_counted()
    {
        var counter = new AuthHandlers.OncePerWindow(TimeSpan.FromHours(24), 3);
        var now = new DateTime(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);
        foreach (var i in Enumerable.Range(0, 3)) counter.Consume($"zz|view:{Guid.NewGuid():N}|ip:198.51.100.{i}", now);
        var real = counter.Consume($"zz|view:{Guid.NewGuid():N}|ip:203.0.113.9", now);
        output.WriteLine($"اليَوم: مَفاتيحُ بَعدَ ثَلاثَةِ مُعَرِّفاتٍ غَيرِ مَوجودَة {counter.Keys} · مُشاهَدَةٌ حَقيقِيَّة: {real}");
        Assert.Equal(3, counter.Keys);
        Assert.Equal(AuthHandlers.ViewCount.FullAfterSweep, real);
    }
}
