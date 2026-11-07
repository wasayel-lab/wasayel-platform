using System.Text.RegularExpressions;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Subscriptions;
using ACommerce.Platform.MultiTenancy;
using ACommerce.Templates.Customer.Marketplace.Gates;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.WriteGateRedirectCharacterizationTests;

namespace ACommerce.Platform.Tests;

// ═══ تَحويلُ حارِسِ الكِتابَةِ لا يُغادِرُ المُضيفَ الَّذي يَخدِمُ الطَلَب — وما يُحَوِّلُ إلَيه صَفحَةٌ يَخدِمُها (‏2026-11-07، مُراجَعَةُ C2b الجَولَةُ الأُولى · C2b-P-01) ═══
//
// **المَقيسُ قَبل** (حَيّاً عِندَ a9bc2cf، `BaseDomain=localhost`): بائِعٌ جَديدٌ عَلى `rewalk-171701-vendor.localhost` يُرسِلُ استِمارَةَ الإعلانِ ⇒ `TermsFilter` يُحَوِّلُ إلى
// `http://rewalk-171701.localhost/rewalk-171701/terms?returnUrl=…` — مُضيفُ المَتجَرِ حَيثُ لا تُرسَلُ الكَعكَة: 0 استِمارَةِ قَبول، والإعلانُ لا يُنشَأ أَبَداً؛ والصَفحَةُ نَفسُها عَلى
// مُضيفِ الدَورِ فيها الاستِمارَة. والحُرّاسُ الأَربَعَةُ وبابُ القَبولِ تَبني بِـ`AuthSession.LinkFor` (رابِطُ تَنَقُّل). **والآن** `StoreUrl.For(StoreUrl.ViewOf(http), …)`
// — ADR-050 §١٤ بِحَرفِه. **والحارِس**: لِكُلِّ حارِسٍ طَلَبُ `POST` عَلى `ashare-vendor.example.test` — بِلا دَورٍ (ما يَحمِلُه كُلُّ طَلَبِ كِتابَة) وبِدَورٍ مُسَمّىً غَيرِ
// دَورِ المُضيف (دَعوى في سِياقِ الحارِس) — والوِجهَةُ نِسبِيَّة، و`AnswerStoreHost` الحَقيقِيُّ يَخدِمُها `GET` صَفحَةً مِن صَفَحاتِ القالِب. وماسِحٌ يَعُدُّ تَحويلاتِ
// الحُرّاسِ وبابِ القَبولِ ويَحمَرُّ إن عادَ إلَيها `LinkFor`.

public class WriteGateHostTests(ITestOutputHelper output)
{
    private static readonly IReadOnlyList<Regex> PageRoutes = RoleHostPages.AllRoutes
        .Select(t => new Regex("^" + Regex.Replace(Regex.Escape(t).Replace(@"\{", "{"), @"\{([^}:]+)(:[^}]+)?\}",
            m => m.Groups[2].Value == ":guid" ? "[0-9a-fA-F-]{36}" : "[^/]+") + "$", RegexOptions.IgnoreCase))
        .ToList();

    /// <summary>ما يَخدِمُه مُضيفُ الدَورِ لِلوِجهَة بِـ<c>GET</c> (بِقَوالِبِ القالِب)، وأَهُوَ صَفحَة.</summary>
    private static (string Served, bool IsPage) Follow(string location)
    {
        var q = location.IndexOf('?');
        var (path, query) = q < 0 ? (location, "") : (location[..q], location[q..]);
        var a = TenantResolverMiddleware.AnswerStoreHost($"{Slug}-vendor.{Apex}", Apex, "GET", path, query, "https", RoleHostPages.Templates);
        var served = a.Verdict switch
        {
            StoreHostVerdict.Rewrite => a.Path!,
            StoreHostVerdict.Pass => path,
            _ => $"✗ {a.Verdict} {a.Location}",
        };
        return (served, PageRoutes.Any(r => r.IsMatch(served)));
    }

    private static async Task<string> GateAsync(string gate, string? claimedRole)
    {
        var world = new DocWorld();
        var id = Guid.NewGuid();
        world.Put(new User { Id = id, TenantSlug = Slug, Phone = "+966500000002" });
        var services = new ServiceCollection().AddSingleton(world.Store).AddSingleton<IEntitlements>(new StubEntitlements(false)).BuildServiceProvider();
        var ctx = Post("vendor", gate == "owner" ? "/ashare/listings/not-a-guid/edit" : "/ashare/listings/create", services);
        if (gate != "auth")
        {
            ctx.Items[GateKeys.UserId] = id;
            ctx.Items[GateKeys.SlugItem] = Slug;
            ctx.Items[GateKeys.Role] = claimedRole;
        }
        else if (claimedRole is not null) ctx.Request.Path = $"/ashare/r/{claimedRole}/listings/create";
        if (gate == "owner") ctx.Request.RouteValues[ListingOwnerFilter.RouteArgument] = "not-a-guid";
        IEndpointFilter filter = gate switch
        {
            "auth" => new AuthFilter(),
            "terms" => new TermsFilter(),
            "entitlement" => new EntitlementFilter(CapabilityCatalog.ListingCreate, "create-listing", "quota"),
            _ => new ListingOwnerFilter(),
        };
        return await AnswerAsync(filter, ctx);
    }

    [Theory]
    [InlineData("auth", null, "/login?returnUrl=%2Fashare%2Flistings%2Fcreate", "/ashare/r/vendor/login")]
    [InlineData("auth", "customer", "/r/customer/login?returnUrl=%2Fashare%2Fr%2Fcustomer%2Flistings%2Fcreate", "/ashare/r/customer/login")]
    [InlineData("terms", null, "/ashare/terms?returnUrl=%2Fashare%2Flistings%2Fcreate", "/ashare/terms")]
    [InlineData("terms", "customer", "/r/customer/terms?returnUrl=%2Fashare%2Flistings%2Fcreate", "/ashare/r/customer/terms")]
    [InlineData("entitlement", null, "/create-listing?err=quota", "/ashare/r/vendor/create-listing")]
    [InlineData("entitlement", "customer", "/r/customer/create-listing?err=quota", "/ashare/r/customer/create-listing")]
    [InlineData("owner", null, "/me/listings?err=not_owner", "/ashare/r/vendor/me/listings")]
    [InlineData("owner", "customer", "/r/customer/me/listings?err=not_owner", "/ashare/r/customer/me/listings")]
    public async Task A_write_gate_on_a_role_host_redirects_to_a_page_that_host_serves(string gate, string? claimedRole, string location, string served)
    {
        var got = await GateAsync(gate, claimedRole);
        var (follow, isPage) = Follow(got);
        output.WriteLine($"{gate} · {claimedRole ?? "بِلا دَور"}: POST ashare-vendor ⇒ {got} ⇒ GET يُخدَمُ {follow} · صَفحَة: {isPage}");
        Assert.Equal(location, got);
        Assert.True(got.StartsWith('/') && !got.StartsWith("//", StringComparison.Ordinal), $"الوِجهَةُ تُغادِرُ المُضيف: {got}");
        Assert.Equal(served, follow);
        Assert.True(isPage, $"{follow} لَيسَ صَفحَةً مِن صَفَحاتِ القالِب");
    }

    /// <summary>الماسِح: تَحويلاتُ الحُرّاسِ (<c>Gates/*.cs</c>) وبابِ <c>/{slug}/terms/accept</c> تُبنى بِـ<c>StoreUrl.For</c> — ولا <c>LinkFor</c>. يَطبَعُ العَدَّ ويَحمَرُّ صِفراً.</summary>
    [Fact]
    public void Write_gate_redirects_are_built_on_the_serving_host()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "PlatformV1.slnx"))) d = d.Parent;
        var root = Path.Combine(d!.FullName, "libs", "templates", "ACommerce.Templates.Customer.Marketplace");
        static string Code(string text) => string.Join('\n', text.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        var gates = Directory.GetFiles(Path.Combine(root, "Gates"), "*.cs").Select(f => (Name: Path.GetFileName(f), Text: Code(File.ReadAllText(f)))).ToList();
        var ext = File.ReadAllText(Path.Combine(root, "MarketplaceTemplateExtensions.cs"));
        var at = ext.IndexOf("app.MapPost(\"/{slug}/terms/accept\"", StringComparison.Ordinal);
        Assert.True(at > 0, "بابُ القَبولِ غَيرُ مَوجود — الماسِحُ أَعمى.");
        var accept = Code(ext[at..ext.IndexOf(".RequireAuth();", at, StringComparison.Ordinal)]);
        var bodies = gates.Append(("terms/accept", accept)).ToList();
        var linkFor = bodies.Sum(b => Regex.Matches(b.Item2, @"LinkFor\(").Count);
        var forRedirects = bodies.Sum(b => Regex.Matches(b.Item2, @"StoreUrl\.For\(").Count);
        var redirects = bodies.Sum(b => Regex.Matches(b.Item2, @"Results\.Redirect\(").Count);
        output.WriteLine($"مِلَفّاتُ الحُرّاس: {gates.Count} + بابُ القَبول · Results.Redirect: {redirects} · StoreUrl.For: {forRedirects} · LinkFor: {linkFor}");
        Assert.True(redirects >= 7, $"فَحصٌ أَعمى: {redirects} تَحويلاً");
        Assert.Equal(0, linkFor);
        Assert.True(forRedirects >= 7, $"{forRedirects} مِن {redirects}");
    }
}
