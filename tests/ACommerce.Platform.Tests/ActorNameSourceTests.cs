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

    [Fact]
    public void Today_a_forged_name_cookie_beside_a_valid_session_is_the_name_store_endpoints_write()
    {
        var http = new DefaultHttpContext();
        http.Request.Path = $"/{Slug}/deals/x/review";
        var cookie = AuthSession.CookieName(Slug);
        http.Request.Headers.Cookie = $"{cookie}={AuthHandlers.MakeToken(Guid.NewGuid(), Slug)}; {cookie}.name={Uri.EscapeDataString("اسمٌ مُزَوَّر")}";
        Assert.Equal("اسمٌ مُزَوَّر", AuthSession.ResolveUserName(http.Request, Slug));
    }

    [Fact]
    public void Today_a_forged_studio_name_cookie_is_the_name_the_studio_writes()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie =
            $"{StudioAuth.CookieName}={AuthHandlers.MakeToken(Guid.NewGuid(), StudioAuth.Tenant)}; {StudioAuth.CookieName}.name={Uri.EscapeDataString("مُشرِفٌ مُزَوَّر")}";
        var auth = new StudioAuth(new HttpContextAccessor { HttpContext = http });
        auth.Load();
        Assert.True(auth.IsAuthenticated);
        Assert.Equal("مُشرِفٌ مُزَوَّر", auth.UserName);
    }

    [Fact]
    public void Today_fourteen_store_sites_and_six_studio_sites_take_the_actor_name_from_the_cookie()
    {
        var store = Matches(@"AuthSession\.ResolveUserName\(");
        var studio = Matches(@"\b(?:auth|studioAuth)\.UserName\b", f => f.EndsWith("MarketplaceTemplateExtensions.cs", StringComparison.Ordinal));
        output.WriteLine($"مَواضِعُ المَتجَر: {store} · مَواضِعُ الاستوديو: {studio}");
        Assert.Equal(14, store);
        Assert.Equal(6, studio);
    }
}
