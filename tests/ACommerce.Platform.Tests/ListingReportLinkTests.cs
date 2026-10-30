using ACommerce.Kit.Notifications;
using ACommerce.Kit.Auth;
using ACommerce.Templates.Customer.Marketplace.Services.Identity;
using ACommerce.Templates.Customer.Marketplace.Services.Listings;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>رابِطُ إشعارِ البَلاغِ يَفتَحُ صَفحَة</b> (‏2026-10-30، تَدقيقُ الموجَة ١١ CO7). <b>تَوصيفُ «اليَوم» قَبلَ التَبديل</b> (القاعِدَة ٣): بابُ
/// <c>POST /{slug}/listings/{id}/report</c> يُخطِرُ مُديري المَتجَرِ بِإشعارٍ رابِطُه <c>/admin/tenants/{slug}/tickets</c> — ولا صَفحَةَ ولا نُقطَةَ بِهذا المَسار
/// (جَدوَلُ المَساراتِ المَمسوح)، فَالنَقرُ ‏404 (القاعِدَة ١٢). وإشعارا المُديرينَ الآخَرانِ يَفتَحانِ مَساراً مُسَجَّلاً: التَسجيلُ <c>/admin/tenants/{slug}/users</c>
/// والإعلانُ الجَديدُ <c>/{slug}/listings/{id}</c>.</para>
/// <para><b>ورَبطُ المَحوِ لِإشعارِ البَلاغِ لا يَمُرُّ بِالرابِط</b>: بَلاغُه بِتَذكِرَتِه (<c>ListingReport.Announces</c>) أَو بِوَسيلَتِه — والرابِطُ اليَومَ لا يُطابِقُ
/// أَيَّ رابِطٍ «يَخُصُّه» (صَفقَة، مُحادَثَة، إعلان). فَرابِطُ إعلانٍ في إشعارِ بَلاغٍ يُدخِلُه «ما يَخُصُّه» إن كانَ لَه مُطابَقَةٌ عَلى ذلكَ الإعلان — ويُمحى اسمُه كَلِمَةً تامَّةً
/// مِن بَلاغِ سَمِيِّه أَو مَن يَبدَأُ اسمُه بِاسمِه، العَطَبُ الَّذي أَغلَقَته مُراجَعَةُ S2 الجَولَة ٢. الصَفُّ الثاني يُثبِتُ ذلكَ قَبلَ تَبديلِ الرابِط.</para>
/// </summary>
public class ListingReportLinkTests(ITestOutputHelper output)
{
    private static readonly string Tpl = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace");

    internal static string ReportDoorBody()
    {
        var src = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(Tpl, "MarketplaceTemplateExtensions.cs")));
        var start = src.IndexOf("app.MapPost(\"/{slug}/listings/{id:guid}/report\"", StringComparison.Ordinal);
        Assert.True(start > 0, "لا بابَ بَلاغ — الأَداةُ عَمياء.");
        var end = src.IndexOf("RequireStoreWritable", start, StringComparison.Ordinal);
        Assert.True(end > start, "لا نِهايَةَ لِجِسمِ بابِ البَلاغ — الأَداةُ عَمياء.");
        return src[start..end];
    }

    /// <summary>روابِطُ إشعاراتِ المُديرينَ الثَلاثَة كَما يَكتُبُها البابُ (الوَسيطُ الأَخيرُ لِـ<c>NotifyAdminsAsync</c>) — مِن المَصدَرِ لا مِن قائِمَةٍ مَكتوبَة.</summary>
    internal static List<(string Type, string Url)> AdminNoticeLinks()
    {
        var src = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(Tpl, "MarketplaceTemplateExtensions.cs")));
        var links = new List<(string, string)>();
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(src,
                     @"NotifyAdminsAsync\(store, slug, ""(?<t>[a-z_]+)"",(?<args>.*?)\);", System.Text.RegularExpressions.RegexOptions.Singleline))
        {
            var url = System.Text.RegularExpressions.Regex.Matches(m.Groups["args"].Value, @"\$""(?<u>/[^""]*)""|(?<u>Services\.Listings\.ListingReport\.NoticeUrl\([^)]*\))")
                .Select(u => u.Groups["u"].Value).LastOrDefault() ?? "";
            links.Add((m.Groups["t"].Value, url));
        }
        return links;
    }

    /// <summary>قالِبُ المَسارِ مِن رابِطٍ مَكتوب: <c>{slug}</c> و<c>{id}</c> كَما هُما، فَيُقارَنُ بِجَدوَلِ المَساراتِ المَطبوع (<c>ComplianceProbe.Normalize</c>).</summary>
    internal static string Template(string url) => url.Split('?')[0];

    [Fact]
    public void Today_the_report_notice_links_an_admin_tickets_page_no_route_serves()
    {
        var links = AdminNoticeLinks();
        var routes = ComplianceInspectorTests.RepoRoutes;
        output.WriteLine($"مَساراتٌ مَمسوحَة: {routes.Count} · روابِطُ إشعاراتِ المُديرين: {links.Count} — " +
                         string.Join(" | ", links.Select(l => $"{l.Type} → {l.Url} ({(routes.Contains(Template(l.Url)) ? "مُسَجَّل" : "لا مَسار")})")));

        Assert.True(routes.Count > 100, "جَدوَلُ المَساراتِ أَعمى.");
        Assert.Equal(4, links.Count);
        var report = Assert.Single(links, l => l.Type == ListingReport.NoticeType);
        Assert.Equal("/admin/tenants/{slug}/tickets", report.Url);
        Assert.DoesNotContain(Template(report.Url), routes);
        Assert.All(links.Where(l => l.Type == AccountErasure.NewUserNoticeType), l => Assert.Equal("/admin/tenants/{slug}/users", l.Url));
        Assert.Contains("/admin/tenants/{slug}/users", routes);
        Assert.Equal("/{slug}/listings/{id}", Assert.Single(links, l => l.Type == "new_listing").Url);
        Assert.Contains("/{slug}/listings/{id}", routes);
    }

    [Fact]
    public void Today_a_report_notice_that_linked_a_listing_he_matched_would_erase_a_namesakes_report()
    {
        var me = Guid.NewGuid();
        var listing = Guid.NewGuid();
        var urls = new[] { $"/listings/{listing}" };
        var namesakes = new Notification { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Type = ListingReport.NoticeType,
            Title = ListingReport.NoticeTitle("مُخالِف"), Body = ListingReport.NoticeBody("عَلي حَسَن"), RelatedUrl = $"/zz/listings/{listing}" };
        var erased = AccountErasure.EraseFrom(namesakes, me, new[] { "عَلي" }, urls, Array.Empty<string>(), new HashSet<Guid>());
        output.WriteLine($"بَلاغُ «عَلي حَسَن» بِرابِطِ إعلانٍ طابَقَه «عَلي»: مُحِيَ {erased} ← {namesakes.Body}");

        Assert.True(erased);
        Assert.Equal(AccountDeletion.ErasedName + " حَسَن بَلَّغَ عَن إعلان", namesakes.Body);
    }
}
