using System.Text.RegularExpressions;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ نِطاقُ عامِلِ الخِدمَةِ مِن بادِئَةِ العُنوانِ الظاهِر — لا مِن «أَعَلى مُضيف؟» (‏2026-11-11، W13-4·C2z·1 — مُراجَعَةُ C2a · C2a-P3-01) ═══
//
// **المَقيسُ قَبل** (Chrome عَلى الرَأسِ بِـBaseDomain=localhost): `ashare.localhost/ashare/r/customer/explore` — شَكلُ المَسارِ الَّذي يَصِلُ بِه رابِطٌ مَحفوظٌ أَو
// `RelatedUrl` أَو عَودَةُ استِمارَةٍ إلى المُضيف — سَجَّلَ عامِلَه بِنِطاقِ `/r/customer/`، فَلا مُتَحَكِّمَ بَعدَ إعادَةِ التَحميل و`serviceWorker.ready` لا يُحَلّ،
// وبَيانُ الصَفحَةِ يُعلِنُ `start_url=/ashare/r/customer/` خارِجَ ذلك النِطاق. **السَبَب**: السكربتُ يَبني البادِئَةَ مِن `hostMode` (`'' : '/' + slug`).
// **والآن**: الخادِمُ يُرسِلُ `base` — البادِئَةُ الَّتي يَحمِلُها العُنوانُ الظاهِرُ فِعلاً — والنِطاقُ ومِفتاحُ التَثبيتِ مِنه. وفي وَضعِ المَسارِ `base` = `/{slug}`، أَي
// ما كانَ السكربتُ يَبنيه بِحَرفِه.

public class StoreHostPageBaseTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static HttpContext Ctx(string requestPath, string? hostSlug, string? visible, string? hostRole = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = requestPath;
        if (hostSlug is not null)
        {
            ctx.Items[StoreHostItems.Slug] = hostSlug;
            ctx.Items[StoreHostItems.OriginalPath] = visible;
            if (hostRole is not null) ctx.Items[StoreHostItems.Role] = hostRole;
        }
        return ctx;
    }

    [Theory]
    // وَضعُ المَسار (والجَذرُ R) — البادِئَةُ كَما كانَ السكربتُ يَبنيها
    [InlineData("/ashare/r/customer/explore", null, null, "ashare", "/ashare")]
    [InlineData("/ashare", null, null, "ashare", "/ashare")]
    // صَفَحاتُ المَنَصَّة — لا مَتجَر
    [InlineData("/studio", null, null, "", "")]
    // مُضيفُ المَتجَر: العُنوانُ النَظيفُ بِلا بادِئَة، وشَكلُ المَسارِ بِبادِئَتِه كَما كُتِبَت
    [InlineData("/ashare/r/customer/explore", "ashare", "/r/customer/explore", "ashare", "")]
    [InlineData("/ashare/explore", "ashare", "/explore", "ashare", "")]
    [InlineData("/ashare", "ashare", "/", "ashare", "")]
    [InlineData("/ashare/r/customer/explore", "ashare", "/ashare/r/customer/explore", "ashare", "/ashare")]
    [InlineData("/ashare/r/vendor/x", "ashare", "/ashare/r/vendor/x", "ashare", "/ashare")]
    [InlineData("/ASHARE/r/vendor/x", "ashare", "/ASHARE/r/vendor/x", "ashare", "/ASHARE")]
    [InlineData("/ashare", "ashare", "/ashare", "ashare", "/ashare")]
    // ولا بادِئَةَ تَكفي: مَتجَرٌ آخَرُ يَبدَأُ بِاسمِه لَيسَ مَقطَعَه
    [InlineData("/ashare/ashare-x/explore", "ashare", "/ashare-x/explore", "ashare", "")]
    public void The_base_is_the_store_prefix_the_visible_url_carries(string requestPath, string? hostSlug, string? visible, string slug, string expected)
    {
        var got = StoreHostPage.VisibleBase(Ctx(requestPath, hostSlug, visible), slug);
        output.WriteLine($"{visible ?? requestPath} · مُضيف {hostSlug ?? "—"} ⇒ «{got}»");
        Assert.Equal(expected, got);
    }

    [Fact]
    public void On_a_role_host_the_base_follows_the_visible_url_too()
    {
        Assert.Equal("", StoreHostPage.VisibleBase(Ctx("/ashare/r/vendor/me", "ashare", "/me", "vendor"), "ashare"));
        Assert.Equal("/ashare", StoreHostPage.VisibleBase(Ctx("/ashare/r/vendor/me", "ashare", "/ashare/me", "vendor"), "ashare"));
    }

    [Fact]
    public void The_client_context_carries_the_base_and_keeps_the_four_fields_it_had()
    {
        Assert.Equal("""{"slug":"ashare","role":"customer","hostMode":true,"home":false,"base":"/ashare"}""",
                     StoreHostPage.ClientContextJson("ashare", "customer", true, false, "/ashare"));
        Assert.Equal("""{"slug":"","role":"","hostMode":false,"home":false,"base":""}""",
                     StoreHostPage.ClientContextJson(null, null, false, false, null));
        Assert.DoesNotContain("<", StoreHostPage.ClientContextJson("ashare", null, false, false, "</script>"), StringComparison.Ordinal);
    }

    /// <summary>الصَدَفَةُ تُرسِلُ البادِئَةَ، ونِطاقُ العامِلِ ومِفتاحُ التَثبيتِ مِنها — ولا سَطرَ يَبنيهِما مِن <c>hostMode</c> بَعدُ (القاعِدَة ٢).</summary>
    [Fact]
    public void The_shell_builds_the_worker_scope_and_the_install_key_from_the_base()
    {
        var app = File.ReadAllText(Path.Combine(RepoRoot, "libs/templates/ACommerce.Templates.Customer.Marketplace/Components/App.razor"));
        var code = Regex.Replace(app, @"@\*.*?\*@", "", RegexOptions.Singleline);
        var fromBase = Regex.Matches(code, @"var\s+(scope|scopeKey)\s*=\s*t\.base\s*\+").Count;
        var fromHostMode = Regex.Matches(code, @"var\s+(scope|scopeKey)\s*=\s*\(?\s*(store|t\.hostMode)\b").Count;
        output.WriteLine($"مِن t.base: {fromBase} · مِن hostMode: {fromHostMode}");
        Assert.Equal(2, fromBase);
        Assert.Equal(0, fromHostMode);
        Assert.Contains("StoreHostPage.VisibleBase(HttpCtx.HttpContext,", code, StringComparison.Ordinal);
    }
}
