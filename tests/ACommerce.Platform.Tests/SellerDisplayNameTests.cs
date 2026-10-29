using System.Text.RegularExpressions;
using ACommerce.Platform.I18n;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ اسمُ البائِعِ الَّذي يَراه المُشتَري «عُضو جَديد» — واسمُ نَشاطِه الإلزامِيُّ لا يُقرَأُ في أَيِّ مَوضِع ═══
//
// قَرارُ المالِك د40 «ج» (‏2026-10-29): «يُطلب الاسم عند أوّل دخول (وحقل تعديل الاسم موجود اليوم في «حسابي»)، ويُقدَّم اسم النشاط حيث وُجد في الإشعار
// وصفحة الصفقة وصفحة البائع.» **تَوصيفُ «اليَوم» قَبلَ التَبديل** (القاعِدَة ٣) — ما قاسَه `RELEASE-READINESS.md` §٧·١ البَند ٥: الدُخولُ بِالهاتِفِ أَو
// البَريدِ يُنشِئُ الحِسابَ بِـ«عُضو جَديد» ويُحَوِّلُ إلى مَسارِ ما بَعدَ الدُخولِ بِلا خُطوَةِ اسم؛ و`business_name` (إلزامِيٌّ في `vendor.role.json`) يُحفَظُ
// في `RoleAttributesJson` ولا يَقرَؤُه كودٌ واحِد؛ واسمُ الفاعِلِ في السِجِلّاتِ (`AccountQueries.ActorNameAsync`) واسمُ البائِعِ المَربوطِ عِندَ الوِلادَة
// (`DealsService.BindListingOwnerAsync`) وعُنوانُ صَفحَةِ البائِعِ (`VendorProfile.razor`) كُلُّها `FullName`. صُفوفُ «Today_» تُقلَبُ بِأَسمائِها في كوميتِ التَبديل.
public class SellerDisplayNameTests(ITestOutputHelper output)
{
    private const string Template = "ACommerce.Templates.Customer.Marketplace";

    internal static string Code(params string[] parts) => UpgradePromptPriceTests.Code(new[] { "libs", "templates", Template }.Concat(parts).ToArray());

    internal static IReadOnlyList<string> BusinessNameReaders()
    {
        var hits = new List<string>();
        foreach (var root in new[] { "libs", "apps" })
            foreach (var f in Directory.EnumerateFiles(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, root), "*.*", SearchOption.AllDirectories))
            {
                var sep = Path.DirectorySeparatorChar;
                if (f.Contains($"{sep}bin{sep}") || f.Contains($"{sep}obj{sep}") || !(f.EndsWith(".cs") || f.EndsWith(".razor"))) continue;
                var code = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(f)));
                if (code.Contains("business_name", StringComparison.Ordinal)) hits.Add(Path.GetFileName(f));
            }
        return hits;
    }

    [Fact]
    public void Today_the_business_name_is_read_nowhere_and_every_seller_surface_reads_the_person_name()
    {
        var readers = BusinessNameReaders();
        var accounts = Code("Services", "Queries", "AccountQueries.cs");
        var deals = Code("Services", "Deals", "DealsService.cs");
        var vendor = Code("Components", "Pages", "VendorProfile.razor");
        var vendorNames = Regex.Matches(vendor, @"vendor\.FullName").Count;
        output.WriteLine($"قُرّاءُ business_name: {readers.Count} · vendor.FullName في صَفحَةِ البائِع: {vendorNames}");
        Assert.Empty(readers);
        Assert.Contains("=> ActorName.Of((await ProfileAsync(tenantSlug, userId, ct))?.FullName);", accounts, StringComparison.Ordinal);
        Assert.Contains("ownerId, owner?.FullName, ct, DealEvent.BoundAtBirth);", deals, StringComparison.Ordinal);
        Assert.Equal(4, vendorNames);
    }

    [Fact]
    public void Today_the_first_phone_or_email_login_goes_straight_to_the_post_login_route()
    {
        var ext = Code("MarketplaceTemplateExtensions.cs");
        var straight = Regex.Matches(ext, Regex.Escape("return Results.Redirect(await PostLoginRouteAsync(slug, result.UserId, asRole, store));")).Count;
        var pages = Directory.GetFiles(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", Template, "Components", "Pages"), "*.razor", SearchOption.AllDirectories);
        var nameStep = pages.Count(p => File.ReadAllText(p).Contains("@page \"/{slug}/me/name\"", StringComparison.Ordinal));
        output.WriteLine($"تَحويلٌ مُباشِرٌ بَعدَ الدُخول: {straight} · صَفحَةُ خُطوَةِ الاسم: {nameStep}");
        Assert.Equal(3, straight);   // الهاتِفُ والبَريدُ ونَفاذ
        Assert.Equal(0, nameStep);
        Assert.Equal("عُضو جَديد", LocaleCatalog.Find(LocaleCatalog.Arabic, "me.profile.new_member"));
    }
}
