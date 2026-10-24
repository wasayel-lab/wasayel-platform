using System.Text.RegularExpressions;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ «اليَوم» — كوكي جَلسَةِ الاستوديو (هُوِيَّةُ مُشرِفِ المَنَصَّة) يُصدَرُ بِلا `Secure` عَلى HTTPS، وكوكي اسمِه بِلا `HttpOnly` (تَدقيقُ الإطلاق lens-security-6) ═══
//
// **المَقيس** (‏2026-10-24، عِندَ `ea97a14`): `StudioAuth.WriteCookie` يَكتُبُ `.acommerce.studio` بِـ`HttpOnly`/`SameSite=Lax`/‏30 يَوماً
// **بِلا `Secure`**، و`.acommerce.studio.name` بِلا `HttpOnly` ولا `Secure` ولا `SameSite`؛ بَينَما كوكي المَتجَرِ (`AuthSession.BuildOpts`) يَضبِطُ
// `Secure = ShouldUseSecure` — أَي HTTPS الطَلَب (بَعدَ `X-Forwarded-Proto`) إلّا بِـ`ACOMMERCE_FORCE_INSECURE_COOKIES=1`. فَكوكي المَتجَرِ
// لا يُرسَلُ عَلى HTTP، وكوكي الاستوديو — وهُوَ وَحدَه يُنتِجُ جَلسَةَ مُشرِف — يُرسَلُ. و‏ADR-022 §٥ يُسَمّيه «يُوصى بِه قَبلَ المَوجَة».
//
// **يُثَبَّتُ قَبلَ العِلاجِ** (القاعِدَة ٣): صَفُّ الاستوديو (`Today_…`) يَنقَلِبُ بِاسمِه، وصُفوفُ المَتجَرِ وHTTP والعَدّادِ تَبقى بِلا تَعديلِ حَرف.
// والصِفاتُ تُقرَأُ مِن رُؤوسِ `Set-Cookie` الَّتي يَكتُبُها الكاتِبُ نَفسُه عَلى `DefaultHttpContext` بِمُخَطَّطِ `https` — لا مِن نَصِّ المَصدَر.
//
// **العِلاج** (‏2026-10-24): `AuthSession.SecureFor(HttpRequest?)` — القاعِدَةُ الواحِدَةُ لِطَلَبٍ بِعَينِه (و`ShouldUseSecure` يُناديها بِالطَلَبِ
// الحاليّ كَما كان) — يَسأَلُها كاتِبُ الاستوديو بِطَلَبِ رَدِّه، والكوكيّانِ بِالصِفاتِ نَفسِها؛ والمَحوُ بِـ`Secure` الكِتابَة. صَفُّ الاستوديو
// انقَلَبَ بِاسمِه، وصَفٌّ لِلمَحو جَديد، والباقي كَما أُودِع.
public class AuthCookieFlagsTests(ITestOutputHelper output)
{
    private static HttpContext Request(bool https)
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = https ? "https" : "http";
        http.Request.Host = new HostString("wasayel.example");
        return http;
    }

    /// <summary>صِفاتُ كُلِّ كوكي كَتَبَه الرَدّ — بِاسمِه، بِأَحرُفٍ صَغيرَة.</summary>
    internal static Dictionary<string, HashSet<string>> Written(HttpResponse res)
    {
        var all = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var header in res.Headers.SetCookie)
        {
            var parts = header!.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var name = parts[0].Split('=')[0];
            all[name] = parts.Skip(1).Select(a => a.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        }
        Assert.True(all.Count > 0, "لا Set-Cookie — الأَداةُ عَمياء.");
        return all;
    }

    private static StudioUser Studio() => new() { Id = Guid.NewGuid(), Phone = "0500000000", FullName = "مُشرِفُ تَجرِبَة" };

    /// <summary>كوكي المَتجَرِ بِالكاتِبِ نَفسِه — و<c>AuthSession.HttpAccessor</c> يَحمِلُ سِياقَ الطَلَبِ كَما يَضبِطُه <c>Program.cs</c>.</summary>
    private static HttpResponse StoreCookies(bool https, string? role = null)
    {
        var http = Request(https);
        var previous = AuthSession.HttpAccessor;
        AuthSession.HttpAccessor = new HttpContextAccessor { HttpContext = http };
        try
        {
            AuthSession.WriteCookie(http.Response, "theme-demo",
                new AuthResult(Guid.NewGuid(), "عُضوُ تَجرِبَة", "0500000000", AuthHandlers.MakeToken(Guid.NewGuid(), "theme-demo"), "customer"),
                role);
            AuthSession.UpdateNameCookie(http.Response, "theme-demo", "عُضوٌ آخَر", role);
        }
        finally { AuthSession.HttpAccessor = previous; }
        return http.Response;
    }

    // ─── ١. كوكي المَتجَرِ عَلى HTTPS: HttpOnly وLax وSecure — لا يَتَغَيَّرُ بِالعِلاج ─────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("vendor")]
    public void The_store_session_cookies_are_httponly_lax_and_secure_on_https(string? role)
    {
        var written = Written(StoreCookies(https: true, role));
        output.WriteLine(string.Join(" | ", written.Select(w => $"{w.Key}: {string.Join(",", w.Value.Where(a => !a.StartsWith("expires") && !a.StartsWith("path")))}")));
        Assert.True(written.Count >= 2);
        foreach (var (name, attrs) in written)
        {
            Assert.Contains("httponly", attrs);
            Assert.Contains("samesite=lax", attrs);
            Assert.Contains("secure", attrs);
            Assert.StartsWith(".acommerce.auth.theme-demo", name, StringComparison.Ordinal);
        }
    }

    // ─── ٢. وعَلى HTTP بِلا Secure (التَطويرُ المَحَلِّيّ) — لا يَتَغَيَّرُ بِالعِلاج ─────────────────────

    [Fact]
    public void On_plain_http_no_auth_cookie_is_marked_secure_so_local_development_still_signs_in()
    {
        foreach (var (name, attrs) in Written(StoreCookies(https: false)))
            Assert.DoesNotContain("secure", attrs);

        var http = Request(https: false);
        StudioAuth.WriteCookie(http.Response, Studio());
        foreach (var (name, attrs) in Written(http.Response))
            Assert.DoesNotContain("secure", attrs);
    }

    // ─── ٣. كوكي الاستوديو عَلى HTTPS: HttpOnly وLax وSecure، واسمُه مِثلُه ──────────────────────────
    //
    // انقَلَبَ بِاسمِه (‏2026-10-24) مِن `Today_the_studio_session_cookie_is_issued_without_secure_on_https_and_its_name_cookie_is_bare`.

    [Fact]
    public void The_studio_session_and_name_cookies_are_httponly_lax_and_secure_on_https()
    {
        var http = Request(https: true);
        StudioAuth.WriteCookie(http.Response, Studio());
        var written = Written(http.Response);
        output.WriteLine(string.Join(" | ", written.Select(w => $"{w.Key}: {string.Join(",", w.Value.Where(a => !a.StartsWith("expires") && !a.StartsWith("path")))}")));

        Assert.Equal(new[] { StudioAuth.CookieName, StudioAuth.CookieName + ".name" }, written.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var (_, attrs) in written)
        {
            Assert.Contains("httponly", attrs);
            Assert.Contains("samesite=lax", attrs);
            Assert.Contains("secure", attrs);
        }
    }

    /// <summary>والمَحوُ بِصِفَةِ الكِتابَة: عَلى HTTPS يَحمِلُ <c>Secure</c> (يَستَبدِلُ الكوكيَّ الآمِنَ ولا يُرَدّ)، وعَلى HTTP لا.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_studio_logout_clears_both_cookies_with_the_secure_flag_they_were_written_with(bool https)
    {
        var http = Request(https);
        StudioAuth.DeleteCookie(http.Response);
        var written = Written(http.Response);
        Assert.Equal(new[] { StudioAuth.CookieName, StudioAuth.CookieName + ".name" }, written.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var (_, attrs) in written)
        {
            Assert.Contains(attrs, a => a.StartsWith("expires=thu, 01 jan 1970", StringComparison.Ordinal));
            Assert.Equal(https, attrs.Contains("secure"));
        }
    }

    /// <summary>والقاعِدَةُ واحِدَة: <c>SecureFor</c> لِطَلَبِ HTTPS نَعَم، ولِـHTTP ولِلغِيابِ لا.</summary>
    [Fact]
    public void One_secure_decision_serves_both_writers()
    {
        Assert.True(AuthSession.SecureFor(Request(https: true).Request));
        Assert.False(AuthSession.SecureFor(Request(https: false).Request));
        Assert.False(AuthSession.SecureFor(null));
    }

    // ─── ٤. كُلُّ كاتِبِ كوكي مَعروف — والعَدَدُ يُطبَعُ ويَحمَرُّ عَلى الجَديد (القاعِدَة ١٠) ─────────────

    /// <summary>عَدَدُ <c>Cookies.Append(</c> في نَصٍّ بِلا تَعليقات.</summary>
    internal static int Appends(string source)
        => Regex.Matches(WriteEndpointGuardTests.StripComments(source), @"\.Cookies\.Append\(").Count;

    private static readonly (string File, int Appends, string Why)[] KnownWriters =
    {
        ("libs/templates/ACommerce.Templates.Customer.Marketplace/AuthSession.cs", 6, "جَلسَةُ المَتجَرِ واسمُها — بِالدَورِ وبِالعامّ، ثُمَّ تَحديثُ الاسم"),
        ("libs/templates/ACommerce.Templates.Customer.Marketplace/Services/Incubator/StudioAuth.cs", 2, "جَلسَةُ الاستوديو واسمُها"),
        ("libs/templates/ACommerce.Templates.Customer.Marketplace/MarketplaceTemplateExtensions.cs", 2, "تَفضيلُ اللُغَة، ومُطالَبَةُ الاستوديو المُعَلَّقَة — لَيسا كوكي جَلسَة"),
    };

    [Fact]
    public void Every_cookie_writer_is_a_known_one()
    {
        var found = new List<string>();
        var total = 0;
        foreach (var f in new[] { "libs", "apps" }
                     .SelectMany(d => Directory.EnumerateFiles(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, d), "*.cs", SearchOption.AllDirectories))
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                              && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            var n = Appends(File.ReadAllText(f));
            if (n == 0) continue;
            total += n;
            found.Add($"{Path.GetRelativePath(ThemeZeroEquivalenceTests.RepoRoot, f).Replace('\\', '/')}={n}");
        }
        output.WriteLine($"كُتّابُ الكوكي: {string.Join(" · ", found)} — {total} نِداءً");
        Assert.True(total >= 10, "أَداةٌ عَمياء — " + string.Join(" · ", found));
        Assert.Equal(KnownWriters.Select(k => $"{k.File}={k.Appends}").OrderBy(x => x, StringComparer.Ordinal),
            found.OrderBy(x => x, StringComparer.Ordinal));

        // والعَدّادُ يَحمَرُّ عَلى كاتِبٍ جَديد: سَطرٌ مَحقونٌ يُرى، والمُعَلَّقُ لا يُعَدّ.
        Assert.Equal(1, Appends("res.Cookies.Append(\"x\", \"y\", opts);"));
        Assert.Equal(0, Appends("// res.Cookies.Append(\"x\", \"y\", opts);"));
    }
}
