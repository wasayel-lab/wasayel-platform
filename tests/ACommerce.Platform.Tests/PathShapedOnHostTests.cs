using ACommerce.Kit.Tenants;
using ACommerce.Platform.MultiTenancy;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ صَفحَةٌ بِشَكلِ المَسارِ عَلى مُضيفِها ← 302 إلى عُنوانِها النَظيف (‏2026-11-11، الإغلاقُ الأَخيرُ لِـW13-4 · W13-4·C2f·6 — C2z-PATHS-R1-04
//     وC2z-PATHS-R2-07 وC2z-PATHS-R1-07) ═══
//
// **المَقيسُ قَبل** (عِندَ ad0a770): `ashare.R/ashare/explore` و`ashare-vendor.R/ashare/r/vendor/me` 200 بِشَكلِهِما — والوُصولُ إلَيهِما مِن `RelatedUrl` المُخَزَّن
// (`/{slug}/deals/{id}`، `/{slug}/me`)، ومِن رابِطٍ مَحفوظ، ومِن `returnUrl` صَفحَةِ عَودَةِ الدَفع (`/{slug}/pay/return?deal=`) — فَيُسَجَّلُ عامِلُ الخِدمَةِ ومِفتاحُ
// التَثبيتِ بِبادِئَةٍ لا يُعلِنُها بَيانُ المُضيف. **والآن**: GET لِقالِبِ صَفحَةِ مَتجَرٍ بِشَكلِ المَسارِ عَلى مُضيفِه ← 302 إلى ما يَعودُ بِالحاقِنِ إلى الصَفحَةِ نَفسِها،
// والاستِعلامُ بِحَرفِه. وما لا يَتَغَيَّرُ يُثَبِّتُه `PathShapedOnHostCharacterizationTests` بِبايتاتِه، ورَوابِطُ التَطبيقِ لا تَأخُذُ القَفزَةَ (`StoreUrlRoundTripTests`).

public class PathShapedOnHostTests(ITestOutputHelper output)
{
    private const string Apex = "example.test";
    private const string Id = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";

    private static StoreHostAnswer Answer(string host, string method, string path, string query = "")
        => TenantResolverMiddleware.AnswerStoreHost(host, Apex, method, path, query, "https", RoleHostPages.Templates);

    [Theory]
    [InlineData("ashare.example.test", "/ashare", "", "/")]
    [InlineData("ashare.example.test", "/ashare/", "", "/")]
    [InlineData("ashare.example.test", "/ashare/explore", "?q=x&page=2", "/explore?q=x&page=2")]
    [InlineData("ashare.example.test", "/ASHARE/explore", "", "/explore")]
    [InlineData("ashare.example.test", "/ashare/deals/" + Id, "", "/deals/" + Id)]                // RelatedUrl في DealNotices
    [InlineData("ashare.example.test", "/ashare/me", "", "/me")]                                  // RelatedUrl في AccountContacts
    [InlineData("ashare.example.test", "/ashare/pay/return", "?deal=3f2504e04f8911d39a0c0305e82c3301", "/pay/return?deal=3f2504e04f8911d39a0c0305e82c3301")]
    [InlineData("ashare.example.test", "/ashare/login", "?returnUrl=%2Fashare%2Fpay%2Freturn", "/login?returnUrl=%2Fashare%2Fpay%2Freturn")]
    [InlineData("ashare.example.test", "/ashare/explore", "?q=شقة", "/explore?q=%D8%B4%D9%82%D8%A9")]
    [InlineData("ashare.example.test", "/ashare/r/customer/login", "", "/r/customer/login")]
    [InlineData("ashare.example.test", "/ashare/r/customer/terms", "", "/r/customer/terms")]                // كَما يَكتُبُه StoreUrl عَلى مُضيفِ المَتجَر
    [InlineData("ashare-vendor.example.test", "/ashare/r/vendor/me", "", "/me")]
    [InlineData("ashare-vendor.example.test", "/ashare/r/vendor", "", "/")]
    [InlineData("ashare-vendor.example.test", "/ashare/r/vendor/me/listings", "?page=2", "/me/listings?page=2")]
    [InlineData("ashare-vendor.example.test", "/ashare/me", "", "/me")]
    [InlineData("ashare-vendor.example.test", "/ashare/deals/" + Id, "", "/deals/" + Id)]
    [InlineData("ashare-admin.example.test", "/ashare/r/tenant_admin/manage", "", "/manage")]
    public void A_path_shaped_page_on_its_host_is_sent_to_its_clean_url(string host, string path, string query, string location)
    {
        var a = Answer(host, "GET", path, query);
        output.WriteLine($"GET {host}{path}{query} ⇒ {a.Verdict} {a.Location}");
        Assert.Equal((StoreHostVerdict.Redirect, "ashare", location), (a.Verdict, a.Slug, a.Location));
    }

    /// <summary>
    /// <para><b>لِكُلِّ قالِبِ صَفحَةِ مَتجَرٍ عَلى كُلِّ مُضيف</b>: GET بِشَكلِ المَسارِ إمّا 302 إلى ذَيلٍ مَحَلِّيٍّ يَعودُ بِالحاقِنِ (حَقناً لا تَحويلاً — فَلا حَلقَة) إلى
    /// ما يَخدِمُه HEAD لِلمَسارِ نَفسِه (HEAD لا يُحَوَّل — فَهُوَ «ما كانَ يُخدَم»)، وإمّا جَوابُ HEAD بِحَرفِه. وما يَبقى بِشَكلِه ذَيلٌ يَبدَأُ بِما يُخدَمُ مِن الجَذرِ
    /// وَحدَه (<c>terms</c> — يَكتُبُه <c>StoreUrl</c> بِشَكلِ المَسارِ عَمداً). والعَدُّ يُطبَعُ ويَفشَلُ صِفراً (القاعِدَة ١٠).</para>
    /// </summary>
    [Fact]
    public void Every_store_page_template_on_every_host_either_lands_on_the_same_page_clean_or_is_left_as_it_was()
    {
        var hosts = new (string Host, string Role)[]
        {
            ("ashare.example.test", "customer"), ("ashare.example.test", "vendor"),
            ("ashare-vendor.example.test", "vendor"), ("ashare-customer.example.test", "customer"), ("ashare-admin.example.test", "tenant_admin"),
        };
        var templates = RoleHostPages.Templates.StoreTemplates;
        Assert.NotEmpty(templates);
        int moved = 0, kept = 0;
        var keptRows = new List<string>();
        foreach (var (host, role) in hosts)
        foreach (var template in templates)
        {
            var path = template.Replace("{slug}", "ashare").Replace("{role}", role).Replace("{id:guid}", Id).Replace("{conversationId:guid}", Id)
                .Replace("{vendorId:guid}", Id).Replace("{section}", "privacy").Replace("{planId}", "p1");
            var get = Answer(host, "GET", path, "?x=1");
            var head = Answer(host, "HEAD", path, "?x=1");
            var served = head.Verdict switch { StoreHostVerdict.Rewrite => head.Path, StoreHostVerdict.Pass when head.Slug is not null => path, _ => null };
            if (get.Verdict == StoreHostVerdict.Redirect && get.Location is { } location && location.StartsWith('/'))
            {
                Assert.False(location.StartsWith("//", StringComparison.Ordinal), $"{host}{path} ⇒ {location}");
                Assert.EndsWith("?x=1", location, StringComparison.Ordinal);
                Assert.NotNull(served);
                var clean = location[..location.IndexOf('?')];
                var again = Answer(host, "GET", clean, "?x=1");
                Assert.True(again.Verdict == StoreHostVerdict.Rewrite, $"{host}{clean} ⇒ {again.Verdict} {again.Location}");
                Assert.Equal(served!.TrimEnd('/'), again.Path!.TrimEnd('/'), ignoreCase: true);
                moved++;
                continue;
            }
            Assert.Equal((head.Verdict, head.Slug, head.Path, head.Location), (get.Verdict, get.Slug, get.Path, get.Location));
            kept++;
            keptRows.Add($"{host}{path} ⇒ {get.Verdict}");
        }
        output.WriteLine($"قَوالِبُ صَفَحاتِ المَتجَر: {templates.Count} · مُضيفات: {hosts.Length} · حُوِّلَ إلى النَظيف: {moved} · بَقِيَ بِشَكلِه: {kept}");
        foreach (var row in keptRows) output.WriteLine("  بَقِيَ: " + row);
        Assert.True(moved > 0);
        Assert.All(keptRows, row => Assert.Contains("/terms", row, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("ashare.example.test", "/ejar/explore")]               // مَتجَرٌ آخَرُ بِشَكلِ المَسار — لَيسَ شَكلَ مَسارِ مَتجَرِ المُضيف
    [InlineData("ashare.example.test", "/explore")]
    [InlineData("ashare-vendor.example.test", "/me")]
    [InlineData("ashare.example.test", "/ashare/listings/not-a-guid")]  // لا قالِب
    [InlineData("ashare-vendor.example.test", "/ashare/r/vendor/terms")]
    public void What_is_not_a_path_shaped_page_of_this_host_is_not_redirected(string host, string path)
    {
        var get = Answer(host, "GET", path);
        var head = Answer(host, "HEAD", path);
        output.WriteLine($"GET {host}{path} ⇒ {get.Verdict} {get.Path ?? get.Location}");
        Assert.Equal((head.Verdict, head.Path, head.Location), (get.Verdict, get.Path, get.Location));
    }

    // ‏(‏2026-11-11، W13-4·C2f-R1·2 — SEC-C2f-R1-2 وC2f-PATHS-R1-02 — يُلحَق) **مَقطَعٌ فارِغٌ لَيسَ صَفحَةً بِشَكلِ المَسار**: القالِبُ يُقرَأُ بِلا المَقاطِعِ الفارِغَة،
    // فَكانَ `/ashare//me` يُطابِقُ `/{slug}/me` ويُحَوَّلُ إلى `Location: //me` — عُنوانٌ بِلا مُخَطَّطٍ يَقرَؤُه المُتَصَفِّحُ `https://me/`. الآن يُجابُ كَما يُجابُ HEAD
    // (ما كانَ قَبلَ C2f·6)، ولا `Location` يَبدَأُ بِـ`//` أَو `/\`.
    [Theory]
    [InlineData("ashare.example.test", "/ashare//me")]
    [InlineData("ashare.example.test", "/ashare//")]
    [InlineData("ashare.example.test", "/ashare///me")]
    [InlineData("ashare.example.test", "/ashare//login")]
    [InlineData("ashare.example.test", "/ashare//legal/x")]
    [InlineData("ashare.example.test", "/ashare//deals/" + Id)]
    [InlineData("ashare.example.test", "/ashare//r/customer/login")]
    [InlineData("ashare.example.test", "/ashare/r/vendor//me")]
    [InlineData("ashare.example.test", "/ashare/explore//")]
    [InlineData("ashare-vendor.example.test", "/ashare//me")]
    [InlineData("ashare-vendor.example.test", "/ashare/r/vendor//me")]
    [InlineData("ashare-vendor.example.test", "/ashare/r/vendor//")]
    [InlineData("ashare-vendor.example.test", "/ashare//r/vendor")]
    [InlineData("ashare-vendor.example.test", "/ashare/explore//")]
    [InlineData("ashare-admin.example.test", "/ashare/r/tenant_admin//manage")]
    public void A_path_with_an_empty_segment_is_not_redirected_and_no_location_leaves_the_site(string host, string path)
    {
        var get = Answer(host, "GET", path);
        var head = Answer(host, "HEAD", path);
        output.WriteLine($"GET {host}{path} ⇒ {get.Verdict} {get.Path ?? get.Location}");
        Assert.False(OffSite(get.Location), $"{host}{path} ⇒ {get.Location}");
        Assert.Equal((head.Verdict, head.Slug, head.Path, head.Location), (get.Verdict, get.Slug, get.Path, get.Location));
    }

    /// <summary>
    /// <para><b>لِكُلِّ قالِبِ صَفحَةِ مَتجَرٍ عَلى كُلِّ مُضيفٍ وبِكُلِّ مَوضِعٍ لِمَقطَعٍ فارِغ</b> (بَعدَ المَتجَر، وبَعدَ كُلِّ مَقطَع، وفي الآخِر): لا <c>Location</c>
    /// يَبدَأُ بِـ<c>//</c> أَو <c>/\</c>، وGET كَـHEAD. يَطبَعُ عَدَدَ ما فَحَصَه ويَفشَلُ صِفراً (القاعِدَة ١٠).</para>
    /// </summary>
    [Fact]
    public void No_store_page_template_with_an_empty_segment_on_any_host_is_redirected_off_the_site()
    {
        var hosts = new (string Host, string Role)[]
        {
            ("ashare.example.test", "customer"), ("ashare-vendor.example.test", "vendor"), ("ashare-customer.example.test", "customer"), ("ashare-admin.example.test", "tenant_admin"),
        };
        int checkedRows = 0, offSite = 0;
        foreach (var (host, role) in hosts)
        foreach (var template in RoleHostPages.Templates.StoreTemplates)
        {
            var path = template.Replace("{slug}", "ashare").Replace("{role}", role).Replace("{id:guid}", Id).Replace("{conversationId:guid}", Id)
                .Replace("{vendorId:guid}", Id).Replace("{section}", "privacy").Replace("{planId}", "p1");
            var slashes = Enumerable.Range(0, path.Length).Where(i => path[i] == '/').ToList();
            var variants = slashes.Select(i => path.Insert(i, "/")).Append(path + "//").Distinct();
            foreach (var variant in variants)
            {
                var get = Answer(host, "GET", variant, "?x=1");
                var head = Answer(host, "HEAD", variant, "?x=1");
                checkedRows++;
                if (OffSite(get.Location)) { offSite++; output.WriteLine($"  ✗ {host}{variant} ⇒ {get.Location}"); }
                Assert.Equal((head.Verdict, head.Slug, head.Path, head.Location), (get.Verdict, get.Slug, get.Path, get.Location));
            }
        }
        output.WriteLine($"مَساراتٌ بِمَقطَعٍ فارِغ: {checkedRows} · خارِجَ المَوقِع: {offSite}");
        Assert.True(checkedRows > 100, $"فَحصٌ أَعمى: {checkedRows}");
        Assert.Equal(0, offSite);
    }

    private static bool OffSite(string? location)
        => location is { } l && (l.StartsWith("//", StringComparison.Ordinal) || l.StartsWith("/\\", StringComparison.Ordinal));

    [Fact]
    public void The_store_templates_are_the_page_routes_under_the_store_and_include_the_role_pages()
    {
        var t = RoleHostPages.Templates;
        Assert.Contains("/{slug}", t.StoreTemplates);
        Assert.Contains("/{slug}/deals/{id:guid}", t.StoreTemplates);
        Assert.All(t.Templates, role => Assert.Contains(role, t.StoreTemplates));
        Assert.All(t.StoreTemplates, s => Assert.True(RolePageTemplates.IsStoreTemplate(s), s));
        Assert.False(RolePageTemplates.IsStoreTemplate("/{slugx}/a"));
        Assert.False(RolePageTemplates.IsStoreTemplate("/studio"));
        Assert.True(t.MatchesStorePage("/"));
        Assert.True(t.MatchesStorePage("/deals/" + Id));
        Assert.False(t.MatchesStorePage("/auth/logout/continue"));
        Assert.False(t.MatchesStorePage("/me/data/download"));
    }
}
