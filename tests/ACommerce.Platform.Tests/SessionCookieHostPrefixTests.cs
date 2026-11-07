using System.Text.RegularExpressions;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Gates;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ G8 — كوكيّاتُ الجَلسَةِ بِالبادِئَةِ `__Host-` عَلى HTTPS (‏2026-11-07، ADR-050 §٥ · W13-4·C2c·2) ═══
//
// **العِلَّة**: المُضيفاتُ الشَقيقَةُ تَحتَ النِطاقِ (مَتجَرٌ آخَر، مُضيفُ دَور، سِجِلٌّ لِطَرَفٍ ثالِث) تَستَطيعُ كِتابَةَ `Set-Cookie: .acommerce.auth.{slug}=…; Domain=R`
// فَيَقرَؤُها كُلُّ مُضيف — تَثبيتُ جَلسَة: يَدخُلُ الزائِرُ حِسابَ المُهاجِمِ دونَ أَن يَدري. والكوكيّاتُ المَقصورَةُ عَلى مُضيفِها (`TenantHostResolutionTests` بِحارِسِه
// القائِم) تَمنَعُنا نَحنُ مِن كِتابَةِ `Domain=`، لا الشَقيقَ. والبادِئَةُ يَفرِضُها المُتَصَفِّح: لا `__Host-` إلّا `Secure` بِـ`Path=/` وبِلا `Domain`.
// **والقاعِدَة**: عَلى HTTPS (`SecureFor`) يُكتَبُ الاسمُ بِالبادِئَةِ ويُقرَأُ بِها وَحدَها وبِحُروفِها؛ وعَلى HTTP بِالأَسماءِ القائِمَة. والتَوصيفُ
// `SessionCookieNameCharacterizationTests` يَبقى بِبَصمَتِه.
public class SessionCookieHostPrefixTests(ITestOutputHelper output)
{
    private const string Slug = "theme-demo";

    private static DefaultHttpContext Request(bool https, string path = "/theme-demo", string? cookie = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = https ? "https" : "http";
        http.Request.Host = new HostString("theme-demo.example.test");
        http.Request.Path = path;
        if (cookie is not null) http.Request.Headers.Cookie = cookie;
        return http;
    }

    private static List<(string Name, HashSet<string> Attrs)> SetCookies(HttpResponse res)
        => res.Headers.SetCookie.Select(h =>
        {
            var parts = h!.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return (parts[0].Split('=', 2)[0], parts.Skip(1).Select(a => a.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal));
        }).ToList();

    private static AuthResult Auth(Guid id) => new(id, "عُضوٌ", "0500000000", AuthHandlers.MakeToken(id, Slug), "customer");

    /// <summary>كُلُّ ما يَكتُبُه الكُتّابُ والماحونَ عَلى طَلَبٍ واحِد — المَتجَرُ بِدَورٍ وتَحديثُ اسمِه ومَحوُه، والاستوديو ومَحوُه، كُلٌّ في رَدٍّ وَحدَه.</summary>
    private static List<(string Name, HashSet<string> Attrs)> EverythingWritten(bool https)
    {
        var all = new List<(string, HashSet<string>)>();
        var id = Guid.NewGuid();
        HttpResponse Fresh() => Request(https).Response;
        var r1 = Fresh(); AuthSession.WriteCookie(r1, Slug, Auth(id), "vendor"); all.AddRange(SetCookies(r1));
        var r2 = Fresh(); AuthSession.UpdateNameCookie(r2, Slug, "اسمٌ", "vendor"); all.AddRange(SetCookies(r2));
        var r3 = Fresh(); AuthSession.ClearAllCookiesForTenant(r3, Slug, ["vendor"]); all.AddRange(SetCookies(r3));
        var r4 = Fresh(); StudioAuth.WriteCookie(r4, new StudioUser { Id = id, Phone = "0500000000", FullName = "x" }); all.AddRange(SetCookies(r4));
        var r5 = Fresh(); StudioAuth.DeleteCookie(r5); all.AddRange(SetCookies(r5));
        return all;
    }

    // ─── الكُتّاب: البادِئَةُ عَلى HTTPS بِشُروطِها، ولا بادِئَةَ عَلى HTTP ──────────────────────────────────────────────────

    [Fact]
    public void Session_cookies_carry___Host_on_https()
    {
        var all = EverythingWritten(https: true);
        output.WriteLine($"https — {all.Count} Set-Cookie: {string.Join(" · ", all.Select(a => a.Name).Distinct())}");
        Assert.Equal(14, all.Count);
        foreach (var (name, attrs) in all)
        {
            Assert.StartsWith("__Host-", name, StringComparison.Ordinal);
            Assert.Contains("secure", attrs);                                            // شُروطُ البادِئَة — وإلّا رَدَّها المُتَصَفِّح
            Assert.Contains("path=/", attrs);
            Assert.DoesNotContain(attrs, a => a.StartsWith("domain", StringComparison.Ordinal));
        }
        Assert.Contains(all, a => a.Name == "__Host-.acommerce.auth.theme-demo");
        Assert.Contains(all, a => a.Name == "__Host-.acommerce.auth.theme-demo.vendor.name");
        Assert.Contains(all, a => a.Name == "__Host-.acommerce.studio");
    }

    [Fact]
    public void On_plain_http_no_session_cookie_carries_the_prefix_so_local_development_keeps_its_names()
    {
        var all = EverythingWritten(https: false);
        output.WriteLine($"http — {all.Count} Set-Cookie: {string.Join(" · ", all.Select(a => a.Name).Distinct())}");
        Assert.Equal(14, all.Count);
        Assert.All(all, a => Assert.False(a.Name.StartsWith("__Host-", StringComparison.OrdinalIgnoreCase)));
        Assert.All(all, a => Assert.DoesNotContain("secure", a.Attrs));
    }

    [Theory]
    [InlineData(true, "theme-demo", null, "__Host-.acommerce.auth.theme-demo")]
    [InlineData(true, "theme-demo", "vendor", "__Host-.acommerce.auth.theme-demo.vendor")]
    [InlineData(false, "theme-demo", "vendor", ".acommerce.auth.theme-demo.vendor")]
    [InlineData(false, "theme-demo", null, ".acommerce.auth.theme-demo")]
    public void The_one_name_decision(bool secure, string slug, string? role, string expected)
    {
        Assert.Equal(expected, AuthSession.CookieNameFor(secure, slug, role));
        Assert.Equal(expected, AuthSession.CookieNameOn(Request(secure).Request, slug, role));
        Assert.Equal(secure ? "__Host-.acommerce.studio" : ".acommerce.studio", StudioAuth.CookieNameFor(secure));
        Assert.Equal(StudioAuth.CookieNameFor(secure), StudioAuth.CookieNameOn(Request(secure).Request));
        Assert.False(AuthSession.CookieNameOn(null, slug, role).StartsWith("__Host-", StringComparison.Ordinal));   // بِلا طَلَبٍ لا بادِئَة
    }

    // ─── القُرّاء: عَلى HTTPS البادِئَةُ وَحدَها وبِحُروفِها — الكوكيُّ المَزروعُ مِن شَقيقٍ لا يُقرَأ ───────────────────────────

    /// <summary>كوكيٌّ بِالاسمِ القَديمِ عَلى HTTPS — ما يَستَطيعُ مُضيفٌ شَقيقٌ زَرعَه بِـ<c>Domain=R</c> — لا يُقرَأُ جَلسَةً في أَيِّ قارِئ.</summary>
    [Fact]
    public void A_tossed_cookie_under_the_old_name_is_not_a_session_on_https()
    {
        var attacker = Guid.NewGuid();
        var token = AuthHandlers.MakeToken(attacker, Slug);
        var generic = Request(true, "/theme-demo/explore", $".acommerce.auth.theme-demo={token}; .acommerce.auth.theme-demo.name=x");
        var role = Request(true, "/theme-demo/r/vendor/me", $".acommerce.auth.theme-demo.vendor={token}");
        var fallback = Request(true, "/theme-demo/explore", $".acommerce.auth.theme-demo.vendor={token}");
        var session = new AuthSession();
        session.Load(generic, Slug);
        var studio = new StudioAuth(new HttpContextAccessor { HttpContext = Request(true, "/studio", $".acommerce.studio={AuthHandlers.MakeToken(attacker, StudioAuth.Tenant)}") });
        studio.Load();
        var reads = new (string Reader, object? Got)[]
        {
            ("ResolveCookieBase بِلا دَور", AuthSession.ResolveCookieBase(generic.Request, Slug)),
            ("ResolveCookieBase بِدَور", AuthSession.ResolveCookieBase(role.Request, Slug)),
            ("ResolveCookieBase بِالسُقوط", AuthSession.ResolveCookieBase(fallback.Request, Slug)),
            ("ResolveToken", AuthSession.ResolveToken(generic.Request, Slug)),
            ("AuthSession.Load", session.UserId),
            ("EffectiveRole.OwnsRole", EffectiveRole.OwnsRole(Request(true, "/theme-demo", $".acommerce.auth.theme-demo.vendor={token}").Request, Slug, attacker, "vendor") ? true : null),
            ("StudioAuth.Load", studio.UserId),
        };
        foreach (var (reader, got) in reads) output.WriteLine($"{reader} ⇒ {got ?? "—"}");
        Assert.All(reads, r => Assert.Null(r.Got));

        // والمِعيار: الكوكيُّ نَفسُه عَلى HTTP يُقرَأ — فَالرَدُّ مِن البادِئَةِ لا مِن رَمزٍ باطِل.
        Assert.Equal(".acommerce.auth.theme-demo", AuthSession.ResolveCookieBase(Request(false, "/theme-demo/explore", $".acommerce.auth.theme-demo={token}").Request, Slug));
    }

    /// <summary>
    /// <para>والاسمُ بِالبادِئَةِ يُقرَأُ بِحُروفِه: مُتَصَفِّحٌ يَفحَصُ البادِئَةَ بِحالَتِها يَقبَلُ <c>__HOST-…; Domain=R</c> مِن شَقيق، و<c>Request.Cookies</c> بِلا حالَةِ أَحرُفٍ
    /// يُبقي آخِرَ المُكَرَّر — فَكانَ يَغلِبُ كوكيَّ المُضيفِ الحَقيقيّ. الآن: لا يُقرَأُ وَحدَه، ومَعَ الحَقيقيِّ يُقرَأُ الحَقيقيّ.</para>
    /// </summary>
    [Fact]
    public void A_case_variant_of_the_prefix_is_never_the_session()
    {
        var victim = Guid.NewGuid();
        var attacker = Guid.NewGuid();
        var real = $"__Host-.acommerce.auth.theme-demo={AuthHandlers.MakeToken(victim, Slug)}";
        var tossed = $"__HOST-.acommerce.auth.theme-demo={AuthHandlers.MakeToken(attacker, Slug)}";

        var alone = Request(true, "/theme-demo/explore", tossed);
        var both = Request(true, "/theme-demo/explore", $"{real}; {tossed}");
        var dictionary = both.Request.Cookies["__Host-.acommerce.auth.theme-demo"];
        var s1 = new AuthSession(); s1.Load(alone, Slug);
        var s2 = new AuthSession(); s2.Load(both, Slug);
        output.WriteLine($"القامُوسُ بِلا حالَةِ أَحرُف ⇒ {AuthHandlers.ParseToken(dictionary)?.UserId} (المُهاجِم {attacker}) · وَحدَه ⇒ {s1.UserId?.ToString() ?? "—"} · مَعَ الحَقيقيّ ⇒ {s2.UserId}");
        Assert.Equal(attacker, AuthHandlers.ParseToken(dictionary)!.Value.UserId);   // المِعيار: هذا ما كانَ يُقرَأ
        Assert.Null(s1.UserId);
        Assert.Equal(victim, s2.UserId);
        Assert.Equal(victim, AuthHandlers.ParseToken(AuthSession.ResolveToken(both.Request, Slug))!.Value.UserId);

        var studioBoth = Request(true, "/studio",
            $"__Host-.acommerce.studio={AuthHandlers.MakeToken(victim, StudioAuth.Tenant)}; __HOST-.acommerce.studio={AuthHandlers.MakeToken(attacker, StudioAuth.Tenant)}");
        var studio = new StudioAuth(new HttpContextAccessor { HttpContext = studioBoth });
        studio.Load();
        Assert.Equal(victim, studio.UserId);
    }

    [Fact]
    public void On_https_the_prefixed_role_cookie_is_read_on_its_path_and_as_the_fallback()
    {
        var id = Guid.NewGuid();
        var token = AuthHandlers.MakeToken(id, Slug);
        Assert.Equal("__Host-.acommerce.auth.theme-demo.vendor",
            AuthSession.ResolveCookieBase(Request(true, "/theme-demo/r/vendor/me", $"__Host-.acommerce.auth.theme-demo.vendor={token}").Request, Slug));
        Assert.Equal("__Host-.acommerce.auth.theme-demo.vendor",
            AuthSession.ResolveCookieBase(Request(true, "/theme-demo/explore", $"__Host-.acommerce.auth.theme-demo.vendor={token}").Request, Slug));
        Assert.Null(AuthSession.ResolveCookieBase(Request(true, "/theme-demo/explore", $"__HOST-.acommerce.auth.theme-demo.vendor={token}").Request, Slug));
        Assert.True(EffectiveRole.OwnsRole(Request(true, "/theme-demo", $"__Host-.acommerce.auth.theme-demo.vendor={token}").Request, Slug, id, "vendor"));
    }

    // ─── الخُروجُ وبَوّابَةُ الإصدار ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_logout_reads_prefixed_role_cookies_and_clears_them_secure()
    {
        var roles = AuthSession.RoleCookiesIn(
            ["__Host-.acommerce.auth.theme-demo.vendor", "__Host-.acommerce.auth.theme-demo.vendor.name", "__Host-.acommerce.auth.theme-demo",
             "__Host-.acommerce.auth.theme-demo.rider", ".acommerce.auth.theme-demo.driver", "__HOST-.acommerce.auth.theme-demo.host", "__Host-.acommerce.auth.other.mover"], Slug);
        output.WriteLine(string.Join(" · ", roles));
        Assert.Equal(new[] { "vendor", "rider", "driver" }, roles);

        var http = Request(true, "/theme-demo/auth/logout");
        AuthSession.ClearAllCookiesForTenant(http.Response, Slug, roles);
        var deleted = SetCookies(http.Response);
        Assert.Equal(8, deleted.Count);
        Assert.All(deleted, d => Assert.True(d.Name.StartsWith("__Host-", StringComparison.Ordinal) && d.Attrs.Contains("secure") && d.Attrs.Contains("path=/")));
    }

    [Fact]
    public void The_session_version_gate_checks_prefixed_session_cookies()
    {
        var names = SessionVersionGate.SessionCookieNames(
            ["__Host-.acommerce.auth.theme-demo", "__Host-.acommerce.auth.theme-demo.vendor", "__Host-.acommerce.auth.theme-demo.name",
             "__Host-.acommerce.studio", "__Host-.acommerce.studio.name", ".acommerce.auth.theme-demo", ".acommerce.lang", "__Host-other"]);
        output.WriteLine(string.Join(" · ", names));
        Assert.Equal(new[] { "__Host-.acommerce.auth.theme-demo", "__Host-.acommerce.auth.theme-demo.vendor", "__Host-.acommerce.studio", ".acommerce.auth.theme-demo" }, names);
    }

    // ─── المَصدَر: لا قارِئَ ولا كاتِبَ لِكوكيِّ جَلسَةٍ خارِجَ القَرارِ الواحِد ─────────────────────────────────────────────────

    /// <summary>
    /// <para>كُلُّ اسمِ كوكيِّ جَلسَةٍ يُكتَبُ أَو يُمحى أَو يُقرَأُ في <c>libs</c>/<c>apps</c> يَمُرُّ بِـ<c>CookieNameOn</c>/<c>CookieNameFor</c> — لا <c>CookieName(slug…)</c>
    /// ولا <c>StudioAuth.CookieName</c> داخِلَ <c>Cookies[…]</c> أَو <c>Append</c>/<c>Delete</c>. والعَدّادُ مَطبوعٌ، وحَقنُ العَيبِ يُرى.</para>
    /// </summary>
    [Fact]
    public void Every_session_cookie_name_in_code_goes_through_the_one_decision()
    {
        var bypass = new Regex(@"Cookies(?:\[|\.Append\(|\.Delete\()\s*(?:AuthSession\.|Services\.Incubator\.StudioAuth\.|StudioAuth\.)?CookieName\b(?!On|For)");
        var through = new Regex(@"\bCookieName(?:On|For)\(");
        int files = 0, calls = 0;
        var breaches = new List<string>();
        foreach (var (file, text) in EntitlementContractTests.SourceFiles().Where(f => f.File.EndsWith(".cs") || f.File.EndsWith(".razor")))
        {
            var code = WriteEndpointGuardTests.StripComments(text);
            files++;
            calls += through.Matches(code).Count;
            foreach (Match m in bypass.Matches(code)) breaches.Add($"{Path.GetFileName(file)}: {m.Value}");
        }
        output.WriteLine($"مِلَفّاتٌ مَقروءَة: {files} · نِداءاتُ القَرارِ الواحِد: {calls} · تَجاوُز: {breaches.Count}");
        foreach (var b in breaches) output.WriteLine("✗ " + b);
        Assert.True(files > 300 && calls >= 15, $"أَداةٌ عَمياء: {files} مِلَفّاً · {calls} نِداءً.");
        Assert.Empty(breaches);
        // حَقنُ العَيب: ما كانَ قَبلَ القَرارِ يُرى، وما بَعدَه لا.
        Assert.Matches(bypass, "ctx.Request.Cookies[CookieName]");
        Assert.Matches(bypass, "HttpCtx.HttpContext.Request.Cookies[Services.Incubator.StudioAuth.CookieName]");
        Assert.Matches(bypass, "res.Cookies.Append(CookieName(tenantSlug, role) + \".name\", n, o)");
        Assert.DoesNotMatch(bypass, "req.Cookies[AuthSession.CookieNameOn(req, slug, role)]");
    }
}
