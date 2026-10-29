using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ إشعارُ صاحِبِ المَتجَرِ بِالاستِرجاعِ والفاتورَةِ المَفتوحَة — مَن يَقرَؤُه؟ (مُراجَعَةُ تَيّارِ S3 الجَولَة ٢، ‏2026-10-29، كُبرى عَلى `d85bc0f`) ═══
//
// **قَرارُ المالِك د3 (٢) «د»**: «ومعه إشعار لصاحب المتجر بكل استرجاع مستحقّ وبكل فاتورة تعذّر إلغاؤها» — وسَبَبُه بِنَصِّه: «أن يُنبَّه من عليه
// الفعل لحظة وجوبه». **والمَقيس**: الإشعارُ يُكتَبُ في مُستَأجِرِ المَتجَرِ بِاسمِ `Tenant.OwnerUserId` — وهُوَ مُعَرِّفُ `StudioUser` في مُستَأجِرِ
// `_studio` (‏`StudioAuth.Tenant`، ويُقارِنُه `TenantAdminGuard` بِمُعَرِّفِ جَلسَةِ الاستوديو) — ولا صَندوقَ يَقرَأُ ذلكَ المُعَرِّفَ هُناك: صَفحَةُ
// «الإشعارات» في الواجِهَةِ تَقرَأُ بِمُعَرِّفِ جَلسَةِ العُضو (`AuthSession`)، والدَفعُ الفَوريُّ يُحَمِّلُ `User` المَتجَرِ بِذلكَ المُعَرِّفِ فَلا يَجِدُه،
// والجَرَسُ لا تَسمَعُه صَفحَةُ استوديو؛ ولَوحَةُ التَشغيلِ تَسرُدُ أَحدَثَ ثَمانِيَةِ إشعاراتٍ لِكُلِّ المُستَخدِمينَ عُنواناً ونَوعاً — بِلا رابِطٍ ولا مَتن.
// فَالإشعارُ مَكتوبٌ ولا يَبلُغُ مَن عَلَيه الفِعل (القاعِدَة ١٢).
//
// **التَوصيفُ قَبلَ التَبديل** (القاعِدَة ٣): صَفُّ «Today_» أَدناه يُودَعُ وَحدَه، ويُقلَبُ بِاسمِه في كوميتِ التَبديل.
public class StudioOwnerNoticeReaderTests(ITestOutputHelper output)
{
    internal static string Pages => Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
        "ACommerce.Templates.Customer.Marketplace", "Components", "Pages");

    internal static string Code(string path)
        => WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(path)));

    /// <summary>صَفَحاتٌ تَقرَأُ إشعاراتِ صاحِبِها بِالقارِئِ الواحِد <c>AccountQueries.NotificationsAsync</c> — بِأَسمائِها.</summary>
    internal static List<string> NoticeReaders()
        => Directory.GetFiles(Pages, "*.razor")
            .Where(p => Code(p).Contains("NotificationsAsync(", StringComparison.Ordinal))
            .Select(p => Path.GetFileName(p)!).OrderBy(n => n, StringComparer.Ordinal).ToList();

    /// <summary>
    /// <para><b>اليَوم</b>: الإشعارُ مَكتوبٌ لِمُعَرِّفِ صاحِبِ المَتجَرِ في الاستوديو ورابِطُه صَفحَةُ الصَفقَةِ في الاستوديو؛ وقارِئُ «إشعاراتي» الوَحيدُ صَفحَةُ
    /// الواجِهَةِ بِجَلسَةِ العُضو؛ ولا صَفحَةَ استوديو تَرسُمُ رابِطَ إشعارٍ أَو مَتنَه — ولَوحَةُ التَشغيلِ تَرسُمُ العُنوانَ والنَوعَ في صَفٍّ لا يُنقَر.</para>
    /// </summary>
    [Fact]
    public void Today_the_store_owner_notice_is_written_for_the_studio_identity_and_no_studio_page_draws_it_as_a_link_with_its_body()
    {
        var l = new L(new HttpContextAccessor());
        var owner = Guid.NewGuid();
        var deal = new Deal { Id = Guid.NewGuid(), ListingTitle = "سِلعَة", AmountSar = 100m, Status = DealStatus.Cancelled };
        var refund = DealNotices.RefundDueFor("s", deal, owner, l, DateTime.UtcNow);
        var open = DealNotices.InvoiceLeftOpenFor("s", deal, owner, l, DateTime.UtcNow);
        Assert.Equal(owner, DealNotices.StoreOwnerOf(new ACommerce.Kit.Tenants.Tenant { Id = "s", OwnerUserId = owner }));
        Assert.All(new[] { refund, open }, n =>
        {
            Assert.Equal(owner, n.UserId);
            Assert.Equal($"/studio/apps/s/deals/{deal.Id}", n.RelatedUrl);
        });

        var readers = NoticeReaders();
        var storefront = Code(Path.Combine(Pages, "Notifications.razor"));
        var studio = Directory.GetFiles(Pages, "Studio*.razor");
        var studioLinks = studio.Count(p => Code(p).Contains("RelatedUrl", StringComparison.Ordinal));
        var studioBodies = studio.Count(p => Code(p).Contains("n.Body", StringComparison.Ordinal));
        var console = Code(Path.Combine(Pages, "StudioAppConsole.razor"));
        output.WriteLine($"قُرّاءُ «إشعاراتي»: {string.Join(", ", readers)} · صَفَحاتُ استوديو مَفحوصَة: {studio.Length} · تَرسُمُ رابِطاً: {studioLinks} · مَتناً: {studioBodies}");

        Assert.True(studio.Length >= 20, $"أَداةٌ عَمياء: {studio.Length} صَفحَةَ استوديو");
        Assert.Equal(new[] { "Notifications.razor" }, readers);
        Assert.Contains("@inject AuthSession Auth", storefront, StringComparison.Ordinal);
        Assert.Contains("await LoadAsync(Auth.UserId!.Value);", storefront, StringComparison.Ordinal);
        Assert.Equal(0, studioLinks);
        Assert.Equal(0, studioBodies);
        Assert.Contains("<div class=\"studio-list-row\" style=\"cursor:default;\">", console, StringComparison.Ordinal);
        Assert.Contains("<strong>@n.Title</strong>", console, StringComparison.Ordinal);
        Assert.Contains("<span>@n.Type · @Culture.FormatTime(n.At, \"yyyy-MM-dd HH:mm\")</span>", console, StringComparison.Ordinal);
    }
}
