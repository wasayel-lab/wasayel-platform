using System.Text.RegularExpressions;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.MultiTenancy;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ الرابِطُ النَظيفُ يَعودُ إلى صَفحَتِه — ما لا يَحقِنُه الحاقِنُ يَبقى بِشَكلِ المَسار (‏2026-11-07، ADR-050 §٩ المَرحَلَتانِ ٢ و٣ · W13-4·C2b·1) ═══
//
// **المَقيسُ قَبل** (عِندَ 17947cb): `StoreUrl.For` يَكتُبُ عَلى مُضيفِ المَتجَرِ `/{tail}` لِكُلِّ ذَيل — و`terms` مِنطَقَةُ مَنَصَّة، فَـ`TermsFilter` و`GatedPage`
// (`LinkFor(slug, role, "terms?returnUrl=…")`) يُرسِلانِ عُضواً لَم يَقبَل شُروطَ المَتجَرِ إلى `/terms` ← 301 إلى شُروطِ المَنَصَّةِ عَلى `R` بِلا جَلسَتِه
// (الكَعكَةُ مَقصورَةٌ عَلى المُضيف) فَلا يَبلُغُ زِرَّ القَبولِ أَبَداً، وكُلُّ كِتابَةٍ تُرَدُّ إلَيه (القاعِدَة ١٢). والصَفحَةُ قائِمَةٌ عَلى المُضيفِ بِشَكلِ المَسار
// (`{slug}.R/{slug}/terms` يَمُرُّ كَما هُوَ). **والقاعِدَةُ الآن مِن الحاقِنِ نَفسِه**: الشَكلُ النَظيفُ لِذَيلٍ يَحقِنُه وَحدَه — ومَقطَعٌ أَوَّلُ يُخدَمُ مِن الجَذر
// (`ReservedTenantSlugs.ServedAtRoot`، القائِمَةُ الَّتي يَسأَلُها `PathWithSlug`) أَو اسمُ المَتجَرِ نَفسُه يَبقى `/{slug}/…`. **والحارِس**: كُلُّ قالِبِ صَفحَةٍ تَحتَ
// `/{slug}` يُبنى رابِطُه بِـ`For` ثُمَّ يُمَرَّرُ بِـ`AnswerStoreHost` الحَقيقِيِّ (بِقَوالِبِ القالِب) فَيَجِبُ أَن يُخدَمَ بِمَسارِه هُوَ — عَلى مُضيفِ المَتجَرِ كُلُّها،
// وعَلى مُضيفِ الدَورِ صَفَحاتُ دَورِه.

public class StoreUrlRoundTripTests(ITestOutputHelper output)
{
    private const string Apex = "example.test";
    private static readonly HostView StoreHost = new(Apex, "ashare", "https", "");
    private static HostView RoleHost(string role) => new(Apex, "ashare", "https", "") { HostRole = role };

    /// <summary>القالِبُ بِقِيَمٍ: <c>{slug}</c> ← ashare، و<c>{role}</c> ← <paramref name="role"/>، وكُلُّ وَسيطٍ آخَرَ بِقيمَةٍ تُطابِقُ قَيدَه.</summary>
    private static string Concrete(string template, string role)
        => Regex.Replace(template, @"\{([^}:]+)(:[^}]+)?\}", m => m.Groups[1].Value switch
        {
            "slug" => "ashare",
            "role" => role,
            _ => m.Groups[2].Value == ":guid" ? "3f2504e0-4f89-11d3-9a0c-0305e82c3301" : "x1",
        });

    private static string Served(string host, string link)
    {
        var a = TenantResolverMiddleware.AnswerStoreHost(host, Apex, "GET", link, "", "https", RoleHostPages.Templates);
        return a.Verdict switch
        {
            StoreHostVerdict.Rewrite => a.Path!,
            StoreHostVerdict.Pass => link,
            _ => $"✗ {a.Verdict} {a.Location}",
        };
    }

    [Fact]
    public void Every_store_page_link_built_on_a_host_is_served_as_that_page()
    {
        var pages = RoleHostPages.AllRoutes.Where(t => t == "/{slug}" || t.StartsWith("/{slug}/", StringComparison.Ordinal)).ToArray();
        var (checkedRows, wrong) = (0, new List<string>());

        void Check(string host, HostView view, string? role, string template)
        {
            var page = Concrete(template, role ?? "");
            var tail = role is null
                ? page["/ashare".Length..].TrimStart('/')
                : page[$"/ashare/r/{role}".Length..].TrimStart('/');
            var link = StoreUrl.For(view, "ashare", role, tail);
            var served = Served(host, link);
            checkedRows++;
            if (served != page) wrong.Add($"{host}: {template} ⇒ For «{link}» ⇒ {served}");
        }

        foreach (var t in pages)
        {
            var isRole = RolePageTemplates.IsRoleTemplate(t);
            Check("ashare.example.test", StoreHost, isRole ? "vendor" : null, t);
            if (isRole) Check("ashare-vendor.example.test", RoleHost("vendor"), "vendor", t);
        }

        output.WriteLine($"قَوالِبُ تَحتَ /{{slug}}: {pages.Length} · صُفوفٌ مَفحوصَة: {checkedRows} · لا تَعودُ إلى صَفحَتِها: {wrong.Count}");
        foreach (var w in wrong) output.WriteLine("  ✗ " + w);
        Assert.True(pages.Length >= 60, $"فَحصٌ أَعمى: {pages.Length} قالِباً");
        Assert.Empty(wrong);
    }

    /// <summary><b>ذَيلٌ لا يَحقِنُه الحاقِن</b> — مِنطَقَةُ مَنَصَّةٍ (<c>terms</c>) أَو اسمُ المَتجَرِ نَفسُه — بِشَكلِ المَسارِ عَلى المُضيف، والاستِعلامُ لا يُغَيِّرُ الحُكم.</summary>
    [Theory]
    [InlineData("store", "ashare", null, "terms?returnUrl=%2Fashare%2Flistings%2Fcreate", "/ashare/terms?returnUrl=%2Fashare%2Flistings%2Fcreate")]
    [InlineData("store", "ashare", null, "terms", "/ashare/terms")]
    [InlineData("store", "ashare", "customer", "terms", "/r/customer/terms")]
    [InlineData("role", "ashare", "vendor", "terms?returnUrl=%2Fx", "/ashare/r/vendor/terms?returnUrl=%2Fx")]
    [InlineData("role", "ashare", null, "terms", "/ashare/terms")]
    [InlineData("store", "ashare", null, "ashare/explore", "/ashare/ashare/explore")]
    [InlineData("store", "ashare", null, "termsx", "/termsx")]
    [InlineData("store", "ashare", null, "explore?q=terms", "/explore?q=terms")]
    [InlineData("store", "ashare", null, "", "/")]
    public void A_tail_the_injector_would_not_inject_keeps_the_path_form(string where, string slug, string? role, string path, string expected)
    {
        var got = StoreUrl.For(where == "role" ? RoleHost("vendor") : StoreHost, slug, role, path);
        output.WriteLine($"{where}: ({slug}, {role ?? "—"}, «{path}») ⇒ {got}");
        Assert.Equal(expected, got);
    }

    /// <summary>والرابِطُ العابِرُ إلى تَطبيقٍ آخَرَ بِالقاعِدَةِ نَفسِها — <c>StoreUrl.Link</c>.</summary>
    [Theory]
    [InlineData("store", "vendor", "terms", "https://ashare-vendor.example.test/ashare/r/vendor/terms")]
    [InlineData("store", "vendor", "me", "https://ashare-vendor.example.test/me")]
    // ‏(‏2026-11-07، مُراجَعَةُ C2b الجَولَةُ الأُولى · SEC-C2b-2) كانا «https://ashare.example.test/ashare/terms» و«https://ashare.example.test/explore»: رابِطٌ بِلا دَورٍ
    // عَلى مُضيفِ الدَورِ لَيسَ تَطبيقاً آخَر — يَبقى عَلى المُضيفِ بِالقاعِدَةِ نَفسِها (ما لا يَحقِنُه الحاقِنُ بِشَكلِ المَسار).
    [InlineData("role", null, "terms", "/ashare/terms")]
    [InlineData("role", null, "explore", "/explore")]
    [InlineData("role", "customer", "terms", "https://ashare-customer.example.test/ashare/r/customer/terms")]
    public void A_link_to_another_app_of_the_store_keeps_the_same_rule(string where, string? role, string path, string expected)
    {
        var got = StoreUrl.Link(where == "role" ? RoleHost("vendor") : StoreHost, "ashare", role, path);
        output.WriteLine($"{where}: ({role ?? "—"}, «{path}») ⇒ {got}");
        Assert.Equal(expected, got);
    }

    /// <summary>
    /// <para><b>مَتجَرٌ اسمُه <c>r</c> بِلا مُضيفٍ وبِمَسارِه</b> (‏2026-11-07، مُراجَعَةُ C2b الجَولَةُ الثانِيَة · C2b-P2-01). كانَ <c>r.R</c> يُحَلّ، وعَلَيه
    /// <c>/r/{role}/…</c> — كُلُّ رابِطِ دَورٍ نَظيفٍ يَبنيه <see cref="StoreUrl"/> — مَساراً يَحمِلُ المَتجَرَ فَلا يُحقَنُ ← 404 (مَقيسٌ حَيّاً عِندَ f8e02c1). والآن مُضيفاه
    /// 404 كَأَيِّ مُعَرِّفٍ عَلى غَيرِ الشَكل، و«افتَح المَتجَر» ورَوابِطُه مِن الجَذرِ بِشَكلِ المَسارِ الَّذي يَحِلُّه المَسار.</para>
    /// </summary>
    [Theory]
    [InlineData("r.example.test", "GET", "/r/vendor/me/listings")]
    [InlineData("r.example.test", "GET", "/r/vendor/login")]
    [InlineData("r.example.test", "GET", "/")]
    [InlineData("r.example.test", "POST", "/auth/phone/login")]
    [InlineData("r-vendor.example.test", "GET", "/me/listings")]
    [InlineData("r-vendor.example.test", "GET", "/r/customer/explore")]
    [InlineData("r-customer.example.test", "GET", "/")]
    public void A_store_named_r_has_no_host(string host, string method, string path)
    {
        var a = TenantResolverMiddleware.AnswerStoreHost(host, Apex, method, path, "", "https", RoleHostPages.Templates);
        output.WriteLine($"{host} {method} {path} ⇒ {a.Verdict}");
        Assert.Equal(StoreHostVerdict.NotFound, a.Verdict);
        Assert.Null(TenantResolverMiddleware.StoreSlugFromHost(host, Apex));
        Assert.Null(TenantResolverMiddleware.RoleFromHost(host, Apex));
    }

    [Fact]
    public void A_store_named_r_is_opened_and_linked_by_its_path()
    {
        var apexed = new HostView(Apex, null, "https", "");
        Assert.Equal("/r", StoreUrl.Open(apexed, "r"));
        foreach (var (role, tail, expected) in new[] { ("vendor", "me/listings", "/r/r/vendor/me/listings"), ("driver", "me/role/onboarding", "/r/r/driver/me/role/onboarding"), ((string?)null, "explore", "/r/explore") })
        {
            var link = StoreUrl.For(apexed, "r", role, tail);
            Assert.Equal(expected, link);
            Assert.Equal("r", TenantResolverMiddleware.SlugFromPath(link));
        }
    }

    /// <summary><b>لا اسمَ مُضيفٍ صالِحٌ هُوَ مَقطَعٌ أَوَّلُ ذو مَعنىً عَلى المُضيف</b> (‏2026-11-07، C2b-P2-01): ما يُخدَمُ مِن الجَذرِ وعَلامَةُ الدَور — فَلا يَلتَبِسُ
    /// عَلى مُضيفِ مَتجَرٍ مَسارٌ يَحمِلُ المَتجَرَ بِذَيلٍ لَه مَعنىً آخَر. يَطبَعُ ما عَدَّه.</summary>
    [Fact]
    public void No_valid_host_name_is_a_first_segment_with_a_meaning_on_the_host()
    {
        var names = ReservedTenantSlugs.RootEndpoints.Concat(ReservedTenantSlugs.PlatformAreas).Append(ReservedTenantSlugs.ApiRoot)
            .Append(ReservedTenantSlugs.RoleSegment).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var valid = names.Where(StoreHostName.IsValid).ToArray();
        output.WriteLine($"مَقاطِعُ ذاتُ مَعنىً عَلى المُضيف: {names.Length} · صالِحَةٌ اسمَ مُضيف: {valid.Length}{(valid.Length > 0 ? " — " + string.Join(" · ", valid) : "")}");
        Assert.True(names.Length >= 20, $"فَحصٌ أَعمى: {names.Length}");
        Assert.Empty(valid);
    }

    /// <summary>القائِمَةُ الواحِدَة: ما يُخدَمُ مِن الجَذرِ هُوَ ما لا يَحقِنُه <c>PathWithSlug</c> — لِكُلِّ اسمٍ في القَوائِمِ الثَلاث.</summary>
    [Fact]
    public void Served_at_root_is_the_set_the_injector_refuses()
    {
        var names = ReservedTenantSlugs.RootEndpoints.Concat(ReservedTenantSlugs.PlatformAreas).Append(ReservedTenantSlugs.ApiRoot)
            .Concat(["explore", "me", "r", "listings", "support"]).ToArray();
        foreach (var n in names)
            Assert.Equal(ReservedTenantSlugs.ServedAtRoot(n), TenantResolverMiddleware.PathWithSlug("/" + n, "ashare") is null);
        output.WriteLine($"أَسماءٌ مَفحوصَة: {names.Length}");
        Assert.False(ReservedTenantSlugs.ServedAtRoot(null));
        Assert.False(ReservedTenantSlugs.ServedAtRoot(""));
        Assert.True(ReservedTenantSlugs.ServedAtRoot("TERMS"));
    }
}
