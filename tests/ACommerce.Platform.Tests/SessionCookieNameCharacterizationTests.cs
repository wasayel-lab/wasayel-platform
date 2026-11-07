using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Gates;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ أَسماءِ كوكيّاتِ الجَلسَةِ لِما يَنجو قَبلَ البادِئَةِ `__Host-` (‏2026-11-07، ADR-050 §٥ · W13-4·C2c·2 — القاعِدَة ٣) ═══
//
// **الكُتّابُ والقُرّاءُ أَنفُسُهُم** (`AuthSession.WriteCookie`/`UpdateNameCookie`/`ClearAllCookiesForTenant`/`ResolveCookieBase`/`Load`، و`StudioAuth`
// بِكاتِبِه وماحيه وقارِئِه، و`EffectiveRole.OwnsRole`) عَلى `DefaultHttpContext` بِمُخَطَّطِ الطَلَب — لا نَصُّ المَصدَر. وكُلُّهُم صاروا في هذا الكوميتِ يَسأَلونَ
// اسماً واحِداً لِلطَلَب (`AuthSession.CookieNameOn` و`StudioAuth.CookieNameOn` — إعادَةُ تَركيبٍ بِلا سُلوك: الاسمُ اليَومَ هُوَ الاسمُ القائِم).
//
// **وما يُثَبَّتُ هُنا ما يَنجو مِن البادِئَة**: عَلى HTTP (التَطويرُ المَحَلِّيّ) الأَسماءُ بِحُروفِها — `.acommerce.auth.{slug}` و`.{role}` و`.name` و`.acommerce.studio`؛
// وعَلى HTTPS **ما يَكتُبُه الكاتِبُ يَقرَؤُه القارِئُ ويَمحوه الماحي** — المَتجَرُ بِلا دَورٍ وبِدَور، والاستوديو، ومِلكُ الدَور، والخُروج — مَهما كانَ الاسم؛
// وصِفاتُ كُلِّ كوكيٍّ يُكتَب: `HttpOnly` و`SameSite=Lax` و`Path=/` وبِلا `Domain`، و`Secure` عَلى HTTPS وَحدَه.
// **وما يَتَغَيَّرُ عَمداً لا يَرِد**: الاسمُ عَلى HTTPS. والمِلَفُّ يَبقى بِبَصمَتِه بَعدَ البادِئَة.
public class SessionCookieNameCharacterizationTests(ITestOutputHelper output)
{
    private const string Slug = "theme-demo";

    private static DefaultHttpContext Request(bool https, string path = "/theme-demo", string? cookie = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = https ? "https" : "http";
        http.Request.Host = new HostString("wasayel.example");
        http.Request.Path = path;
        if (cookie is not null) http.Request.Headers.Cookie = cookie;
        return http;
    }

    /// <summary>كُلُّ <c>Set-Cookie</c> بِتَرتيبِه: الاسمُ والقيمَةُ (كَما كُتِبَت) والصِفاتُ بِأَحرُفٍ صَغيرَة.</summary>
    private static List<(string Name, string Value, HashSet<string> Attrs)> SetCookies(HttpResponse res)
    {
        var all = new List<(string, string, HashSet<string>)>();
        foreach (var header in res.Headers.SetCookie)
        {
            var parts = header!.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var pair = parts[0].Split('=', 2);
            all.Add((pair[0], pair.Length > 1 ? pair[1] : "", parts.Skip(1).Select(a => a.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal)));
        }
        Assert.True(all.Count > 0, "لا Set-Cookie — الأَداةُ عَمياء.");
        return all;
    }

    private static bool IsDelete(HashSet<string> attrs) => attrs.Any(a => a.StartsWith("expires=thu, 01 jan 1970", StringComparison.Ordinal));

    private static (Guid Id, AuthResult Auth) Member()
    {
        var id = Guid.NewGuid();
        return (id, new AuthResult(id, "عُضوُ تَجرِبَة", "0500000000", AuthHandlers.MakeToken(id, Slug), "customer"));
    }

    /// <summary>كاتِبُ المَتجَرِ عَلى طَلَبٍ بِمُخَطَّطِه، والوَصّالُ الساكِنُ يَحمِلُ الطَلَبَ نَفسَه كَما يَضبِطُه <c>Program.cs</c>.</summary>
    private static HttpResponse WriteStore(bool https, AuthResult auth, string? role)
    {
        var http = Request(https);
        var previous = AuthSession.HttpAccessor;
        AuthSession.HttpAccessor = new HttpContextAccessor { HttpContext = http };
        try { AuthSession.WriteCookie(http.Response, Slug, auth, role); }
        finally { AuthSession.HttpAccessor = previous; }
        return http.Response;
    }

    private static string CookieHeader(IEnumerable<(string Name, string Value, HashSet<string> Attrs)> written)
        => string.Join("; ", written.Where(w => !IsDelete(w.Attrs)).GroupBy(w => w.Name).Select(g => $"{g.Key}={g.Last().Value}"));

    // ─── ١. عَلى HTTP: الأَسماءُ بِحُروفِها — التَطويرُ المَحَلِّيُّ يَبقى كَما هُوَ ────────────────────────────────────────────

    [Fact]
    public void On_http_the_store_writer_names_are_today_names()
    {
        var (_, auth) = Member();
        var generic = SetCookies(WriteStore(https: false, auth, role: null)).Select(c => c.Name).ToArray();
        var vendor = SetCookies(WriteStore(https: false, auth, role: "vendor")).Select(c => c.Name).ToArray();
        output.WriteLine($"بِلا دَور: {string.Join(" · ", generic)}");
        output.WriteLine($"بِدَور: {string.Join(" · ", vendor)}");
        Assert.Equal(new[] { ".acommerce.auth.theme-demo", ".acommerce.auth.theme-demo.name" }, generic);
        Assert.Equal(new[]
        {
            ".acommerce.auth.theme-demo.vendor", ".acommerce.auth.theme-demo.vendor.name",
            ".acommerce.auth.theme-demo", ".acommerce.auth.theme-demo.name",
        }, vendor);

        // رَدّانِ مُنفَصِلان: الرَدُّ الواحِدُ يُبقي آخِرَ ما كُتِبَ بِالاسمِ نَفسِه (المَحوُ يَطوي الكِتابَة).
        var rename = Request(https: false);
        AuthSession.UpdateNameCookie(rename.Response, Slug, "اسمٌ آخَر", "vendor");
        var renamed = SetCookies(rename.Response).Select(c => c.Name).ToArray();
        var clear = Request(https: false);
        AuthSession.ClearAllCookiesForTenant(clear.Response, Slug, ["vendor"]);
        var cleared = SetCookies(clear.Response).Select(c => c.Name).ToArray();
        output.WriteLine($"تَحديثُ الاسم: {string.Join(" · ", renamed)} · المَحو: {string.Join(" · ", cleared)}");
        Assert.Equal(new[] { ".acommerce.auth.theme-demo.vendor.name", ".acommerce.auth.theme-demo.name" }, renamed);
        Assert.Equal(new[]
        {
            ".acommerce.auth.theme-demo", ".acommerce.auth.theme-demo.name",
            ".acommerce.auth.theme-demo.vendor", ".acommerce.auth.theme-demo.vendor.name",
        }, cleared);
    }

    [Fact]
    public void On_http_the_studio_writer_and_its_delete_use_today_names()
    {
        var write = Request(https: false, "/studio");
        StudioAuth.WriteCookie(write.Response, new StudioUser { Id = Guid.NewGuid(), Phone = "0500000000", FullName = "مُشرِفُ تَجرِبَة" });
        var delete = Request(https: false, "/studio");
        StudioAuth.DeleteCookie(delete.Response);
        var names = SetCookies(write.Response).Concat(SetCookies(delete.Response)).Select(c => c.Name).ToArray();
        output.WriteLine(string.Join(" · ", names));
        Assert.Equal(new[] { ".acommerce.studio", ".acommerce.studio.name", ".acommerce.studio", ".acommerce.studio.name" }, names);
    }

    [Fact]
    public void On_http_the_readers_read_today_names()
    {
        var (id, auth) = Member();
        var token = auth.Token;
        string? Resolve(string path, string cookie) => AuthSession.ResolveCookieBase(Request(false, path, cookie).Request, Slug);

        Assert.Equal(".acommerce.auth.theme-demo", Resolve("/theme-demo/explore", $".acommerce.auth.theme-demo={token}"));
        Assert.Equal(".acommerce.auth.theme-demo.vendor", Resolve("/theme-demo/r/vendor/me", $".acommerce.auth.theme-demo.vendor={token}"));
        Assert.Equal(".acommerce.auth.theme-demo.vendor", Resolve("/theme-demo/explore", $".acommerce.auth.theme-demo.vendor={token}"));
        Assert.Null(Resolve("/theme-demo/r/vendor/me", $".acommerce.auth.theme-demo={token}"));
        Assert.True(EffectiveRole.OwnsRole(Request(false, "/theme-demo", $".acommerce.auth.theme-demo.vendor={token}").Request, Slug, id, "vendor"));

        var studioId = Guid.NewGuid();
        var studio = new StudioAuth(new HttpContextAccessor
        {
            HttpContext = Request(false, "/studio", $".acommerce.studio={AuthHandlers.MakeToken(studioId, StudioAuth.Tenant)}; .acommerce.studio.name=x"),
        });
        studio.Load();
        Assert.Equal((studioId, "x"), (studio.UserId!.Value, studio.UserName));
    }

    [Fact]
    public void The_role_cookies_of_a_store_are_read_from_today_names()
    {
        var roles = AuthSession.RoleCookiesIn(
            [".acommerce.auth.theme-demo.vendor", ".acommerce.auth.theme-demo.vendor.name", ".acommerce.auth.theme-demo", ".acommerce.auth.theme-demo.name",
             ".acommerce.auth.other.rider", ".ACOMMERCE.AUTH.THEME-DEMO.Driver", ".acommerce.lang"], Slug);
        output.WriteLine(string.Join(" · ", roles));
        Assert.Equal(new[] { "vendor", "Driver" }, roles);
    }

    // ─── ٢. عَلى HTTPS: ما يَكتُبُه الكاتِبُ يَقرَؤُه القارِئُ ويَمحوه الماحي — مَهما كانَ الاسم ────────────────────────────

    [Theory]
    [InlineData(null, "/theme-demo/explore")]
    [InlineData("vendor", "/theme-demo/r/vendor/me")]
    [InlineData("vendor", "/theme-demo/explore")]
    public void On_https_what_the_store_writer_writes_the_reader_reads(string? role, string path)
    {
        var (id, auth) = Member();
        var written = SetCookies(WriteStore(https: true, auth, role));
        var http = Request(true, path, CookieHeader(written));
        var name = AuthSession.ResolveCookieBase(http.Request, Slug);
        var session = new AuthSession();
        session.Load(http, Slug);
        output.WriteLine($"{path}: مَكتوبٌ {string.Join(" · ", written.Select(w => w.Name))} ⇒ مَقروءٌ {name} · {session.UserName}");
        Assert.Contains(name, written.Select(w => w.Name));
        Assert.Equal((id, "عُضوُ تَجرِبَة"), (session.UserId!.Value, session.UserName));
    }

    [Fact]
    public void On_https_what_the_studio_writer_writes_the_reader_reads_and_the_delete_clears()
    {
        var user = new StudioUser { Id = Guid.NewGuid(), Phone = "0500000000", FullName = "مُشرِفُ تَجرِبَة" };
        var write = Request(true, "/studio");
        StudioAuth.WriteCookie(write.Response, user);
        var written = SetCookies(write.Response);
        var studio = new StudioAuth(new HttpContextAccessor { HttpContext = Request(true, "/studio", CookieHeader(written)) });
        studio.Load();
        Assert.Equal((user.Id, "مُشرِفُ تَجرِبَة"), (studio.UserId!.Value, studio.UserName));

        var delete = Request(true, "/studio");
        StudioAuth.DeleteCookie(delete.Response);
        var deleted = SetCookies(delete.Response);
        output.WriteLine($"مَكتوبٌ {string.Join(" · ", written.Select(w => w.Name))} · مَمحُوٌّ {string.Join(" · ", deleted.Select(d => d.Name))}");
        Assert.Equal(written.Select(w => w.Name), deleted.Select(d => d.Name));
        Assert.All(deleted, d => Assert.True(IsDelete(d.Attrs) && d.Attrs.Contains("secure")));
    }

    [Fact]
    public void On_https_the_role_written_is_the_role_owned()
    {
        var (id, auth) = Member();
        var written = SetCookies(WriteStore(https: true, auth, "vendor"));
        Assert.True(EffectiveRole.OwnsRole(Request(true, "/theme-demo", CookieHeader(written)).Request, Slug, id, "vendor"));
        Assert.False(EffectiveRole.OwnsRole(Request(true, "/theme-demo", CookieHeader(written)).Request, Slug, id, "rider"));
    }

    /// <summary>الخُروجُ كَما يُجريه البابُ (<c>ClearAllCookiesForTenant</c> بِـ<c>RoleCookiesIn</c> مِن كوكيّاتِ الطَلَب) يَمحو كُلَّ اسمٍ كَتَبَه الدُخولُ — بِلا زِيادَة.</summary>
    [Fact]
    public void On_https_logout_clears_every_name_the_sign_in_wrote()
    {
        var (_, auth) = Member();
        var written = SetCookies(WriteStore(https: true, auth, "vendor"));
        var http = Request(true, "/theme-demo/auth/logout", CookieHeader(written));
        AuthSession.ClearAllCookiesForTenant(http.Response, Slug, AuthSession.RoleCookiesIn(http.Request.Cookies.Keys, Slug));
        var deleted = SetCookies(http.Response);
        output.WriteLine($"مَكتوبٌ {string.Join(" · ", written.Select(w => w.Name))} · مَمحُوٌّ {string.Join(" · ", deleted.Select(d => d.Name))}");
        Assert.Equal(written.Select(w => w.Name).OrderBy(n => n, StringComparer.Ordinal), deleted.Select(d => d.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.All(deleted, d => Assert.True(IsDelete(d.Attrs)));
    }

    // ─── ٣. الصِفاتُ الَّتي تَنجو — وهي شُروطُ البادِئَةِ نَفسُها ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Every_session_cookie_written_is_host_only_at_the_root_and_secure_exactly_on_https(bool https)
    {
        var (_, auth) = Member();
        var all = SetCookies(WriteStore(https, auth, "vendor")).ToList();
        var studio = Request(https, "/studio");
        StudioAuth.WriteCookie(studio.Response, new StudioUser { Id = Guid.NewGuid(), Phone = "0500000000", FullName = "x" });
        all.AddRange(SetCookies(studio.Response));
        output.WriteLine($"{(https ? "https" : "http")}: {all.Count} كوكي");
        Assert.Equal(6, all.Count);
        foreach (var (name, _, attrs) in all)
        {
            Assert.Contains("path=/", attrs);
            Assert.DoesNotContain(attrs, a => a.StartsWith("domain", StringComparison.Ordinal));
            Assert.Contains("httponly", attrs);
            Assert.Contains("samesite=lax", attrs);
            Assert.Equal(https, attrs.Contains("secure"));
        }
    }
}
