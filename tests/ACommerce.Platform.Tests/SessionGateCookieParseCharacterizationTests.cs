using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Gates;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ بَوّابَةِ الإصدارِ وقُرّاءِ الجَلسَةِ عَلى رَأسِ Cookie بِعَينِه، لِما يَنجو (‏2026-11-07، مُراجَعَةُ C2c الجَولَةُ الأُولى · SEC-C2c-1 وC2c-P-01 — القاعِدَة ٣) ═══
//
// **العِلَّةُ الَّتي يَسبِقُها هذا التَوصيف**: `c297172` جَعَلَ القارِئَ يَقرَأُ الاسمَ بِالبادِئَةِ `__Host-` مِن رَأسِ الطَلَبِ بِمُحَلِّلِه (`AuthSession.CookieValue`: أَوَّلُ زَوجٍ
// بِحُروفِه، والقيمَةُ بِلا عَلامَتَي التَنصيص)، والبَوّابَةُ (`SessionVersionGate`) ما زالَت تَحكُمُ بِـ`Request.Cookies` (بِلا حالَةِ أَحرُف، وآخِرُ المُكَرَّرِ يَربَح، والتَنصيصُ
// يَبقى) — فَتَفحَصُ رَمزاً ويَفتَحُ القارِئُ غَيرَه. والتَبديلُ يَجعَلُ البَوّابَةَ تَحكُمُ عَلى كُلِّ زَوجٍ يَقرَؤُه قارِئ.
//
// **وما يُثَبَّتُ هُنا ما يَنجو مِن التَبديل** — البَوّابَةُ الحَقيقيَّةُ (`SessionVersionGate.ApplyAsync`) بِمَخزَنٍ بَديلٍ يُعيدُ الحِسابَ بِإصدارِه، ثُمَّ القُرّاءُ أَنفُسُهُم
// (`AuthSession.ResolveToken` عَلى المَسارِ بِلا دَورٍ وبِدَورٍ وبِالسُقوط، و`StudioAuth.Load`، و`EffectiveRole.OwnsRole`) عَلى طَلَبِ HTTPS:
//   • الرَمزُ الساقِطُ وَحدَه خارِجٌ ويُمحى بِـ`Secure` و`Path=/` — في العائِلاتِ الأَربَع؛ وبِتَرميزِ النِسبَةِ المِئَوِيَّةِ كَما يُرسِلُه المُتَصَفِّح.
//   • الرَمزُ الحَيُّ وَحدَه، أَو أَوَّلاً ومَعَه مُكَرَّرٌ تالِفٌ بِحُروفِه أَو بِحالَةٍ أُخرى — داخِلٌ، والرَأسُ لا يُمَسّ ولا يُمحى شَيء.
//   • التالِفُ أَوَّلاً والساقِطُ آخِراً — خارِج (القامُوسُ يَرى الساقِط).
//   • ساقِطٌ بِحالَةٍ أُخرى بِجِوارِ حَيّ — يُنزَعانِ مَعاً (البَوّابَةُ تَنزِعُ الاسمَ بِلا حالَةِ أَحرُف — قائِمٌ مُنذُ الموجَة ١١).
//   • الحَيُّ لِلضَحِيَّةِ ومَعَه حَيٌّ لِغَيرِه بِحالَةٍ أُخرى — الضَحِيَّة (G8 بِلا بَوّابَة، وهُنا بِها).
//   • وعَلى HTTP (المِعيار): الاسمُ القَديمُ بِالقامُوس، والساقِطُ أَوَّلاً ومَعَه مُكَرَّرٌ بِحُروفِه أَو بِحالَةٍ أُخرى — خارِج.
// **وما يَتَغَيَّرُ عَمداً لا يَرِد**: الساقِطُ أَوَّلاً ومَعَه مُكَرَّرٌ عَلى HTTPS، والرَمزُ بَينَ عَلامَتَي تَنصيص — يَصيرانِ خارِجَين. والمِلَفُّ يَبقى بِبَصمَتِه بَعدَ التَبديل.
public class SessionGateCookieParseCharacterizationTests(ITestOutputHelper output)
{
    private const string Slug = "zz-gate";

    private static string Generic(bool https) => AuthSession.CookieNameFor(https, Slug);
    private static string Role(bool https) => AuthSession.CookieNameFor(https, Slug, "vendor");
    private static string Studio(bool https) => StudioAuth.CookieNameFor(https);

    /// <summary>طَلَبٌ عَلى <paramref name="path"/> بِرَأسِ <c>Cookie</c> بِعَينِه (سَطرٌ أَو أَكثَر)، يَمُرُّ بِالبَوّابَةِ الحَقيقيَّةِ ومَخزَنُها يُعيدُ <paramref name="account"/>.</summary>
    private static async Task<DefaultHttpContext> ThroughTheGate(bool https, string path, object account, StringValues cookie)
    {
        var ctx = SessionRevocationTests.GateContext(path, account);
        ctx.Request.Scheme = https ? "https" : "http";
        ctx.Request.Host = new HostString("zz-gate.example.test");
        ctx.Request.Headers.Cookie = cookie;
        await SessionVersionGate.ApplyAsync(ctx);
        return ctx;
    }

    /// <summary>مَن يَرى القارِئُ بَعدَ البَوّابَة — <c>null</c> = خارِج.</summary>
    private static Guid? StoreReader(DefaultHttpContext ctx)
        => AuthHandlers.ParseToken(AuthSession.ResolveToken(ctx.Request, Slug))?.UserId;

    private static Guid? StudioReader(DefaultHttpContext ctx)
    {
        var auth = new StudioAuth(new HttpContextAccessor { HttpContext = ctx });
        auth.Load();
        return auth.UserId;
    }

    private static List<(string Name, HashSet<string> Attrs)> Deleted(HttpResponse res)
        => res.Headers.SetCookie.Select(h =>
        {
            var parts = h!.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return (parts[0].Split('=', 2)[0], parts.Skip(1).Select(a => a.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal));
        }).ToList();

    private static string Upper(string name) => name.StartsWith(AuthSession.HostPrefix, StringComparison.Ordinal) ? "__HOST-" + name[AuthSession.HostPrefix.Length..] : name.ToUpperInvariant();

    // ─── الساقِطُ وَحدَه: خارِجٌ في العائِلاتِ الأَربَع، ويُمحى بِشُروطِ البادِئَة ─────────────────────────────────────────────────────────

    [Fact]
    public async Task On_https_a_revoked_token_alone_is_out_in_every_family_and_deleted_secure()
    {
        var user = new User { Id = Guid.NewGuid(), TenantSlug = Slug, SessionVersion = 2 };
        var studio = new StudioUser { Id = Guid.NewGuid(), SessionVersion = 2 };
        var rev = AuthHandlers.MakeToken(user.Id, Slug, 1);
        var srev = AuthHandlers.MakeToken(studio.Id, StudioAuth.Tenant, 1);
        var rows = new (string Label, string Path, object Account, string Name, string Token, bool IsStudio)[]
        {
            ("العامّ", $"/{Slug}/me", user, Generic(true), rev, false),
            ("الدَورُ عَلى مَسارِه", $"/{Slug}/r/vendor/me", user, Role(true), rev, false),
            ("الدَورُ بِالسُقوط", $"/{Slug}/explore", user, Role(true), rev, false),
            ("الاستوديو", "/studio", studio, Studio(true), srev, true),
            ("العامُّ بِتَرميزِ المُتَصَفِّح", $"/{Slug}/me", user, Generic(true), Uri.EscapeDataString(rev), false),
        };
        foreach (var (label, path, account, name, token, isStudio) in rows)
        {
            var ctx = await ThroughTheGate(true, path, account, $"{name}={token}");
            var who = isStudio ? StudioReader(ctx) : StoreReader(ctx);
            var deleted = Deleted(ctx.Response);
            output.WriteLine($"{label}: «{name}» ⇒ {(who is null ? "خارِج" : "داخِل")} · مَمحُوّ {string.Join(" · ", deleted.Select(d => d.Name))}");
            Assert.Null(who);
            Assert.Equal(new[] { name, name + ".name" }, deleted.Select(d => d.Name).ToArray());
            Assert.All(deleted, d => Assert.True(d.Attrs.Contains("secure") && d.Attrs.Contains("path=/") && !d.Attrs.Any(a => a.StartsWith("domain", StringComparison.Ordinal))));
        }
    }

    // ─── الحَيّ: داخِلٌ وَحدَه وأَوَّلاً ومَعَه تالِف — والرَأسُ لا يُمَسّ ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task On_https_a_live_token_alone_or_first_beside_junk_duplicates_stays_in_untouched()
    {
        var user = new User { Id = Guid.NewGuid(), TenantSlug = Slug, SessionVersion = 2 };
        var studio = new StudioUser { Id = Guid.NewGuid(), SessionVersion = 2 };
        var live = AuthHandlers.MakeToken(user.Id, Slug, 2);
        var slive = AuthHandlers.MakeToken(studio.Id, StudioAuth.Tenant, 2);
        var g = Generic(true);
        var r = Role(true);
        var s = Studio(true);
        var rows = new (string Label, string Path, object Account, StringValues Cookie, bool IsStudio)[]
        {
            ("وَحدَه", $"/{Slug}/me", user, $"{g}={live}", false),
            ("بِتَرميزِ المُتَصَفِّح", $"/{Slug}/me", user, $"{g}={Uri.EscapeDataString(live)}", false),
            ("ومَعَه تالِفٌ بِحُروفِه", $"/{Slug}/me", user, $"{g}={live}; {g}=x", false),
            ("ومَعَه تالِفٌ بِحالَةٍ أُخرى", $"/{Slug}/me", user, $"{g}={live}; {Upper(g)}=x", false),
            ("دَورٌ ومَعَه تالِف", $"/{Slug}/r/vendor/me", user, $"{r}={live}; {r}=x", false),
            ("الاستوديو ومَعَه تالِف", "/studio", studio, $"{s}={slive}; {s}=x", true),
        };
        foreach (var (label, path, account, cookie, isStudio) in rows)
        {
            var before = cookie.ToString();
            var ctx = await ThroughTheGate(true, path, account, cookie);
            var who = isStudio ? StudioReader(ctx) : StoreReader(ctx);
            output.WriteLine($"{label} ⇒ {(who is null ? "خارِج" : "داخِل")} · Set-Cookie {ctx.Response.Headers.SetCookie.Count} · الرَأسُ {(ctx.Request.Headers.Cookie.ToString() == before ? "كَما هُوَ" : "تَغَيَّر")}");
            Assert.Equal(isStudio ? studio.Id : user.Id, who);
            Assert.Equal(before, ctx.Request.Headers.Cookie.ToString());
            Assert.Equal(0, ctx.Response.Headers.SetCookie.Count);
        }
        Assert.True(EffectiveRole.OwnsRole((await ThroughTheGate(true, $"/{Slug}", user, $"{r}={live}; {r}=x")).Request, Slug, user.Id, "vendor"));
    }

    // ─── المُكَرَّرُ والساقِطُ آخِراً، والساقِطُ بِحالَةٍ أُخرى بِجِوارِ حَيّ، والضَحِيَّةُ بِجِوارِ حَيٍّ لِغَيرِه ────────────────────────────────

    [Fact]
    public async Task On_https_a_revoked_token_last_behind_junk_is_out()
    {
        var user = new User { Id = Guid.NewGuid(), TenantSlug = Slug, SessionVersion = 2 };
        var studio = new StudioUser { Id = Guid.NewGuid(), SessionVersion = 2 };
        var rev = AuthHandlers.MakeToken(user.Id, Slug, 1);
        var srev = AuthHandlers.MakeToken(studio.Id, StudioAuth.Tenant, 1);
        var g = Generic(true);
        var s = Studio(true);
        var store = await ThroughTheGate(true, $"/{Slug}/me", user, $"{g}=x; {g}={rev}");
        var lines = await ThroughTheGate(true, $"/{Slug}/me", user, new StringValues(new[] { $"{g}=x", $"{g}={rev}" }));
        var st = await ThroughTheGate(true, "/studio", studio, $"{s}=x; {s}={srev}");
        output.WriteLine($"التالِفُ أَوَّلاً والساقِطُ آخِراً ⇒ المَتجَر {StoreReader(store)?.ToString() ?? "خارِج"} · سَطران {StoreReader(lines)?.ToString() ?? "خارِج"} · الاستوديو {StudioReader(st)?.ToString() ?? "خارِج"}");
        Assert.Null(StoreReader(store));
        Assert.Null(StoreReader(lines));
        Assert.Null(StudioReader(st));
        Assert.Contains(Deleted(store.Response), d => d.Name == g);
    }

    [Fact]
    public async Task On_https_a_revoked_case_variant_beside_a_live_token_takes_both_out()
    {
        var user = new User { Id = Guid.NewGuid(), TenantSlug = Slug, SessionVersion = 2 };
        var live = AuthHandlers.MakeToken(user.Id, Slug, 2);
        var rev = AuthHandlers.MakeToken(user.Id, Slug, 1);
        var g = Generic(true);
        var ctx = await ThroughTheGate(true, $"/{Slug}/me", user, $"{g}={live}; {Upper(g)}={rev}");
        output.WriteLine($"حَيٌّ ثُمَّ ساقِطٌ بِحالَةٍ أُخرى ⇒ {StoreReader(ctx)?.ToString() ?? "خارِج"} · الرَأسُ «{ctx.Request.Headers.Cookie}» · مَمحُوّ {string.Join(" · ", Deleted(ctx.Response).Select(d => d.Name).Distinct())}");
        Assert.Null(StoreReader(ctx));
        Assert.Equal("", ctx.Request.Headers.Cookie.ToString());
    }

    [Fact]
    public async Task On_https_the_victims_live_token_beside_a_live_case_variant_of_another_account_stays_the_victims()
    {
        var victim = new User { Id = Guid.NewGuid(), TenantSlug = Slug, SessionVersion = 0 };
        var attacker = Guid.NewGuid();
        var g = Generic(true);
        var cookie = $"{g}={AuthHandlers.MakeToken(victim.Id, Slug)}; {Upper(g)}={AuthHandlers.MakeToken(attacker, Slug)}";
        var ctx = await ThroughTheGate(true, $"/{Slug}/me", victim, cookie);
        output.WriteLine($"الضَحِيَّةُ ومَعَه حَيٌّ لِغَيرِه بِحالَةٍ أُخرى ⇒ {(StoreReader(ctx) == victim.Id ? "الضَحِيَّة" : "غَيرُه")} · Set-Cookie {ctx.Response.Headers.SetCookie.Count}");
        Assert.Equal(victim.Id, StoreReader(ctx));
        Assert.Equal(cookie, ctx.Request.Headers.Cookie.ToString());
    }

    // ─── المِعيارُ عَلى HTTP: الاسمُ القَديمُ بِالقامُوس — الساقِطُ ومَعَه مُكَرَّرٌ بِأَيِّ تَرتيبٍ وحالَة خارِج ─────────────────────────────────

    [Fact]
    public async Task On_http_a_revoked_token_with_any_duplicate_is_out()
    {
        var user = new User { Id = Guid.NewGuid(), TenantSlug = Slug, SessionVersion = 2 };
        var studio = new StudioUser { Id = Guid.NewGuid(), SessionVersion = 2 };
        var rev = AuthHandlers.MakeToken(user.Id, Slug, 1);
        var srev = AuthHandlers.MakeToken(studio.Id, StudioAuth.Tenant, 1);
        var g = Generic(false);
        var s = Studio(false);
        var shapes = new[] { $"{g}={rev}", $"{g}={rev}; {g}=x", $"{g}={rev}; {Upper(g)}=x", $"{g}=x; {g}={rev}" };
        var outs = 0;
        foreach (var shape in shapes)
        {
            var ctx = await ThroughTheGate(false, $"/{Slug}/me", user, shape);
            Assert.Null(StoreReader(ctx));
            outs++;
        }
        var st = await ThroughTheGate(false, "/studio", studio, $"{s}={srev}; {s}=x");
        Assert.Null(StudioReader(st));
        output.WriteLine($"HTTP: صُوَرٌ {shapes.Length} · خارِجَة {outs} · والاستوديو {StudioReader(st)?.ToString() ?? "خارِج"}");
        Assert.Equal(shapes.Length, outs);
    }
}
