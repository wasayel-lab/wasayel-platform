using System.Text.RegularExpressions;
using ACommerce.Kit.Auth.Server;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ «اليَوم» — اسمُ الفاعِلِ الَّذي تَكتُبُه النِقاطُ في الصَفقَةِ والتَقييمِ والمُحادَثَةِ والتَدقيقِ يُقرَأُ مِن كوكي `.name` غَيرِ المُوَقَّع (تَدقيقُ الإطلاق lens-security-11) ═══
//
// **المَقيس** (‏2026-10-24، عِندَ `ef539cb`): `AuthSession.ResolveUserName` يَقرَأُ `{cookie}.name` — خارِجَ رَمزِ HMAC، يَكتُبُه العَميلُ
// كَما يَشاء — وأَربَعَ عَشرَةَ نُقطَةً تَكتُبُه اسماً لِلفاعِل: سَطرُ الخَطِّ الزَمَنِيِّ في الصَفقَة، و`DealReviewer` في التَقييم، والمُحادَثَةُ،
// والعَرض، والبَلاغ، والدَعم، والشَحن. و`StudioAuth.Load` يَقرَأُ `.acommerce.studio.name` كَذلِك، وسِتَّةُ مَواضِعَ تَكتُبُه: سَطرا تَدقيقِ
// تَصعيدِ المُشرِفِ وسَحبِه، ومالِكُ الدِراسَةِ في بابَي الاستِئناف، وتَصديرُ المَتجَر، وفاعِلُ تَدقيقِ إعداداتِ المَتجَر. فَجَلسَةٌ صالِحَةٌ وكوكي
// اسمٍ مُزَوَّرٌ بِجِوارِها = تَقييمٌ «موثَّقٌ» بِاسمِ غَيرِ صاحِبِه.
//
// **يُثَبَّتُ قَبلَ العِلاجِ** (القاعِدَة ٣): الصُفوفُ الثَلاثَةُ `Today_…` تَنقَلِبُ بِأَسمائِها.
//
// **العِلاج** (‏2026-10-24): الاسمُ مِن الوَثيقَةِ وَقتَ الكِتابَة — `AccountQueries.ActorNameAsync` (`User.FullName` في مَتجَرِه) و
// `StudioQueries.ActorNameAsync` (`StudioUser.FullName`)، والقاعِدَةُ بَينَهُما `ActorName.Of`؛ و`ResolveUserName` حُذِفَ بِصِفرِ مُنادٍ.
// والكوكي باقٍ لِلعَرضِ عَلى صاحِبِه وَحدَه. الصُفوفُ الثَلاثَةُ انقَلَبَت بِأَسمائِها.
public class ActorNameSourceTests(ITestOutputHelper output)
{
    private const string Slug = "theme-demo";

    internal static int Matches(string pattern, Func<string, bool>? fileFilter = null)
    {
        var total = 0;
        foreach (var f in Directory.EnumerateFiles(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs"), "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                              && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                     .Where(f => fileFilter?.Invoke(f) ?? true))
            total += Regex.Matches(WriteEndpointGuardTests.StripComments(File.ReadAllText(f)), pattern).Count;
        return total;
    }

    // انقَلَبَ بِاسمِه (‏2026-10-24) مِن `Today_a_forged_name_cookie_beside_a_valid_session_is_the_name_store_endpoints_write`.
    [Fact]
    public async Task The_store_actor_name_is_the_users_document_name_and_no_cookie_reader_remains()
    {
        Assert.Null(typeof(AuthSession).GetMethod("ResolveUserName"));

        var world = new DocWorld();
        var named = new ACommerce.Kit.Auth.User { Id = Guid.NewGuid(), TenantSlug = Slug, FullName = "  سارَة  " };
        var blank = new ACommerce.Kit.Auth.User { Id = Guid.NewGuid(), TenantSlug = Slug, FullName = "  " };
        world.Put(named).Put(blank);
        var accounts = new ACommerce.Templates.Customer.Marketplace.Services.Queries.AccountQueries(world.Store);

        Assert.Equal("سارَة", await accounts.ActorNameAsync(Slug, named.Id));
        Assert.Null(await accounts.ActorNameAsync(Slug, blank.Id));
        Assert.Null(await accounts.ActorNameAsync(Slug, Guid.NewGuid()));
    }

    // انقَلَبَ بِاسمِه (‏2026-10-24) مِن `Today_a_forged_studio_name_cookie_is_the_name_the_studio_writes`.
    [Fact]
    public async Task The_studio_actor_name_is_the_studio_users_document_name_whatever_the_cookie_says()
    {
        var world = new DocWorld();
        var admin = new StudioUser { Id = Guid.NewGuid(), Phone = "0500000000", FullName = "مُشرِفٌ حَقيقيّ" };
        world.Put(admin);
        var studio = new ACommerce.Templates.Customer.Marketplace.Services.Queries.StudioQueries(world.Store);

        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie =
            $"{StudioAuth.CookieName}={AuthHandlers.MakeToken(admin.Id, StudioAuth.Tenant)}; {StudioAuth.CookieName}.name={Uri.EscapeDataString("مُشرِفٌ مُزَوَّر")}";
        var auth = new StudioAuth(new HttpContextAccessor { HttpContext = http });
        auth.Load();

        // الكوكي لِلعَرضِ عَلى صاحِبِه — والمَكتوبُ مِن الوَثيقَة.
        Assert.Equal("مُشرِفٌ مُزَوَّر", auth.UserName);
        Assert.Equal("مُشرِفٌ حَقيقيّ", await studio.ActorNameAsync(auth.UserId));
        Assert.Null(await studio.ActorNameAsync(null));
        Assert.Null(await studio.ActorNameAsync(Guid.NewGuid()));
    }

    private const string StoreCookieRead = @"AuthSession\.ResolveUserName\(";
    private const string StudioCookieRead = @"\b(?:auth|studioAuth)\.UserName\b";

    // انقَلَبَ بِاسمِه (‏2026-10-24) مِن `Today_fourteen_store_sites_and_six_studio_sites_take_the_actor_name_from_the_cookie`.
    [Fact]
    // ‏2026-10-29 (قَرارُ المالِك د30): وحادٍ وعِشرون — بابُ «طَلَب استِرجاع» يَقرَأُ اسمَ المُشتَري مِن وَثيقَتِه؛ فَتَبَدَّلَ الاسمُ بِعَدَدِه (كانَ `…_and_twenty_read_it_from_the_document`).
    public void No_endpoint_takes_an_actor_name_from_a_cookie_and_twenty_one_read_it_from_the_document()
    {
        var store = Matches(StoreCookieRead);
        var studio = Matches(StudioCookieRead, f => f.EndsWith("MarketplaceTemplateExtensions.cs", StringComparison.Ordinal));
        var fromDocs = Matches(@"\.ActorNameAsync\(", f => !f.Contains($"{Path.DirectorySeparatorChar}Queries{Path.DirectorySeparatorChar}"));
        output.WriteLine($"مَواضِعُ المَتجَر: {store} · مَواضِعُ الاستوديو: {studio} · مِن الوَثيقَة: {fromDocs}");
        Assert.Equal(0, store);
        Assert.Equal(0, studio);
        Assert.Equal(21, fromDocs);

        // والنَمَطانِ يَحمَرّانِ عَلى سَطرِ اليَومِ حَرفاً — والمُعَلَّقُ لا يُعَدّ.
        Assert.Matches(StoreCookieRead, "var userName = AuthSession.ResolveUserName(req, slug) ?? \"عميل\";");
        Assert.Matches(StudioCookieRead, "await audit.WriteAsync(slug, auth.UserId, auth.UserName ?? \"admin\",");
        Assert.Matches(StudioCookieRead, "return (studioAuth.UserId, studioAuth.UserName ?? \"studio\");");
        Assert.DoesNotMatch(StudioCookieRead, WriteEndpointGuardTests.StripComments("// auth.UserName"));
    }
}
