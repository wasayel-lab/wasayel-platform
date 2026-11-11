using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ مُخَطَّطُ العُنوانِ المُطلَقِ عَلى المُضيفاتِ مِن {http, https} وَحدَهُما، يُقرَأُ مَرَّةً عِندَ مَصدَرِه (‏2026-11-11، مُراجَعَةُ إغلاقِ W13-4 الجَولَةُ الثانِيَة ·
//     SEC-C2z-R2-1) ═══
//
// **المَقيسُ قَبل** (حَيّاً عِندَ المُراجِع، والصُفوفُ أَدناه حَمراءُ عِندَ 77d630b): `POST /{slug}/auth/logout` عَلى مُضيفِ المَتجَرِ بِـ`X-Forwarded-Proto: javascript`
// ← `302 Location: javascript://{slug}-customer.localhost:5101/{slug}/auth/logout/continue?t=…`؛ وكَذلك آخِرُ الجَولَة، ورابِطُ «المَتجَرُ عَلى مُضيفِه»، والرابِطُ
// العابِرُ، و«افتَح المَتجَر»، وأَصلُ المَنَصَّة — كُلُّها تَأخُذُ مُخَطَّطَها مِن `StoreUrl.ViewOf` الَّذي كانَ يَنسَخُ `Request.Scheme` بِحَرفِه (و`KnownProxies`
// فارِغٌ فَيُصَدَّقُ الرَأسُ مِن أَيِّ نِدّ). **والآن**: `ViewOf` يَقرَؤُه بِقاعِدَةِ `SchemeOf` نَفسِها (‏023b686): `http` بِلا حالَةِ أَحرُفٍ يَبقى `http`، وما سِواه
// `https` — فَيَرِثُه البُناةُ الخَمسَةُ وجَولَةُ الخُروج. وما لا يَتَغَيَّرُ يُثَبِّتُه `HostSchemeCharacterizationTests` بِبايتاتِه. **والحارِسُ ماسِحٌ**: في مُجَلَّدِ
// `Services/Ux` لا يَقرَأُ `Request.Scheme` إلّا `ViewOf` عَبرَ `SchemeOf` — فَبانٍ جَديدٌ يَنسَخُه بِحَرفِه يُحمِّرُه (الثالِثُ بَعدَ 436cb9f و187e832).

public class HostSchemeTests(ITestOutputHelper output)
{
    private const string Apex = "example.test";
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] CustomerVendor = ["vendor", "customer"];

    private static DefaultHttpContext Request(string scheme, bool onStore, string? hostRole = null, bool apexServes = false,
        string? cookieToken = null, string? ticket = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = scheme;
        var name = !onStore ? Apex : hostRole is null ? $"ashare.{Apex}" : $"ashare-{RoleHostNames.ByCatalogSlug[hostRole]}.{Apex}";
        ctx.Request.Host = new HostString(name + ":5101");
        ctx.Items[StoreHostItems.Apex] = Apex;
        if (onStore) ctx.Items[StoreHostItems.Slug] = "ashare";
        if (hostRole is not null) ctx.Items[StoreHostItems.Role] = hostRole;
        ctx.Items[StoreHostItems.HostedRoles] = CustomerVendor;
        if (apexServes) ctx.Items[StoreHostItems.ApexServesStore] = true;
        if (cookieToken is not null) ctx.Request.Headers.Cookie = $"{AuthSession.CookieNameOn(ctx.Request, "ashare")}={cookieToken}";
        if (ticket is not null) ctx.Request.QueryString = new QueryString($"?{LogoutHops.TicketKey}={ticket}");
        return ctx;
    }

    /// <summary>كُلُّ عُنوانٍ مُطلَقٍ يَبنيه طَلَبٌ عَلى المُضيفاتِ — مَحَطّاتُ جَولَةِ الخُروجِ الأَربَعُ والبُناةُ الخَمسَة.</summary>
    private static IReadOnlyList<(string Why, string? Url)> Absolutes(string scheme)
    {
        var user = Guid.NewGuid();
        var token = AuthHandlers.MakeToken(user, "ashare", 1);
        var fromStore = AuthHandlers.MakeLogoutHop(user, "ashare", LogoutHops.StoreHost, Now);
        var fromApex = AuthHandlers.MakeLogoutHop(user, "ashare", LogoutHops.Apex, Now);
        var onStore = StoreUrl.ViewOf(Request(scheme, onStore: true));
        return
        [
            ("الخُروجُ عَلى مُضيفِ المَتجَر", LogoutHops.Begin(Request(scheme, onStore: true), "ashare", token, Now)),
            ("الخُروجُ عَلى الجَذر", LogoutHops.Begin(Request(scheme, onStore: false, apexServes: true), "ashare", token, Now)),
            ("مَحَطَّةٌ في الطَريق", LogoutHops.Continue(Request(scheme, onStore: true, "customer", cookieToken: token, ticket: fromStore), "ashare", Now).Next),
            ("آخِرُ جَولَةٍ بَدَأَت عَلى الجَذر", LogoutHops.Continue(Request(scheme, onStore: true, "vendor", apexServes: true, cookieToken: token, ticket: fromApex), "ashare", Now).Next),
            ("App: مُضيفُ دَورٍ آخَر", StoreUrl.App(onStore, "ashare", "vendor", "login")),
            ("App: دَورٌ مُؤَلَّفٌ مِن مُضيفِ دَور", StoreUrl.App(StoreUrl.ViewOf(Request(scheme, onStore: true, "vendor")), "ashare", "mandoob", "me")),
            ("Open: مِن الجَذر", StoreUrl.Open(StoreUrl.ViewOf(Request(scheme, onStore: false)), "ashare")),
            ("StoreHome: مِن مُضيفِ الإدارَة", AdminHostSignIn.StoreHome(StoreUrl.ViewOf(Request(scheme, onStore: true, "tenant_admin")), "ashare")),
            ("PlatformOrigin", StoreUrl.PlatformOrigin(Request(scheme, onStore: true).Request)),
        ];
    }

    [Theory]
    [InlineData("javascript", "https")]
    [InlineData("data", "https")]
    [InlineData("vbscript", "https")]
    [InlineData("ftp", "https")]
    [InlineData("HTTP", "http")]
    [InlineData("Http", "http")]
    [InlineData("HTTPS", "https")]
    public void Every_absolute_url_on_the_hosts_takes_its_scheme_from_http_or_https_only(string scheme, string want)
    {
        Assert.Equal(want, StoreUrl.ViewOf(Request(scheme, onStore: true)).Scheme);
        var rows = Absolutes(scheme);
        foreach (var (why, url) in rows) output.WriteLine($"«{scheme}» {why} ⇒ {url}");
        Assert.All(rows, r =>
        {
            Assert.NotNull(r.Url);
            Assert.StartsWith(want + "://", r.Url, StringComparison.Ordinal);
        });
        Assert.Equal(9, rows.Count);
    }

    /// <summary>صَفُّ المُراجِعِ بِعَينِه: خُروجٌ عَلى مُضيفِ المَتجَرِ وطَلَبُه بِمُخَطَّطِ <c>javascript</c> (ما يَترُكُه <c>UseForwardedHeaders</c> مِن <c>X-Forwarded-Proto: javascript</c>)
    /// ← وِجهَتُه <c>https://{slug}-customer.R{port}/…</c>.</summary>
    [Fact]
    public void Logout_on_a_store_host_with_a_javascript_forwarded_proto_hops_to_https()
    {
        var token = AuthHandlers.MakeToken(Guid.NewGuid(), "ashare", 1);
        var begun = LogoutHops.Begin(Request("javascript", onStore: true), "ashare", token, Now);
        output.WriteLine($"Location: {begun}");
        Assert.StartsWith("https://ashare-customer.example.test:5101/ashare/auth/logout/continue?t=", begun);
    }

    /// <summary>
    /// <para><b>الماسِح</b>: في <c>Services/Ux</c> قارِئٌ واحِدٌ لِـ<c>Request.Scheme</c> — <c>StoreUrl.ViewOf</c> عَبرَ <c>SchemeOf</c>. يَطبَعُ ما مَسَحَه ويَفشَلُ عَلى صِفر.</para>
    /// </summary>
    [Fact]
    public void Only_the_host_view_reads_the_request_scheme_in_the_url_builders()
    {
        var dir = Path.Combine(RepoRoot(), "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Services", "Ux");
        var files = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories);
        var readers = new List<string>();
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var code = lines[i].TrimStart();
                if (code.StartsWith("//", StringComparison.Ordinal) || code.StartsWith("///", StringComparison.Ordinal)) continue;
                if (code.Contains("Request.Scheme", StringComparison.Ordinal)) readers.Add($"{Path.GetFileName(file)}:{i + 1}: {code}");
            }
        }
        output.WriteLine($"مِلَفّاتٌ مَمسوحَة: {files.Length} · قُرّاءُ Request.Scheme: {readers.Count}");
        foreach (var r in readers) output.WriteLine("  " + r);
        Assert.True(files.Length >= 5, $"فَحصٌ أَعمى: {files.Length} مِلَفّ");
        var only = Assert.Single(readers);
        Assert.StartsWith("StoreUrl.cs:", only, StringComparison.Ordinal);
        Assert.Contains("SchemeOf(ctx.Request.Scheme)", only, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "PlatformV1.slnx"))) return dir.FullName;
        throw new InvalidOperationException("PlatformV1.slnx غائِب");
    }
}
