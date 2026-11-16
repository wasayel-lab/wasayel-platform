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

// ═══ G8·ب — بَوّابَةُ الإصدارِ تَحكُمُ عَلى ما يَقرَؤُه القارِئ (‏2026-11-07، مُراجَعَةُ C2c الجَولَةُ الأُولى · SEC-C2c-1 وC2c-P-01، حاجِبَتان) ═══
//
// **العِلَّةُ المَقيسَة**: `c297172` جَعَلَ القارِئَ يَقرَأُ الاسمَ بِالبادِئَةِ `__Host-` مِن رَأسِ الطَلَبِ بِمُحَلِّلِه (`AuthSession.CookieValue`: أَوَّلُ زَوجٍ بِحُروفِه، والقيمَةُ
// بِلا عَلامَتَي التَنصيص)، وبَقِيَت `SessionVersionGate` تَحكُمُ بِـ`Request.Cookies` (بِلا حالَةِ أَحرُف، وآخِرُ المُكَرَّرِ يَربَح، والتَنصيصُ يَبقى). فَعَلى HTTPS:
// `__Host-.acommerce.auth.X=<ساقِط>; __Host-.acommerce.auth.X=x` — تَفحَصُ البَوّابَةُ «x» (لا يُقرَأُ رَمزاً فَيُتَخَطّى) ويَفتَحُ القارِئُ الساقِط. وكَذلِكَ `__HOST-…=x`
// بَعدَه، والسَطرانِ، والساقِطُ بَينَ عَلامَتَي تَنصيص — والاستوديو مِثلُها. فَحامِلُ رَمزٍ مَسروقٍ يَهزِمُ «اخرج من كل الأجهزة» (د28) وحَذفَ الحِساب.
// والمِعيارُ عَلى HTTP خارِجٌ في كُلِّ ذلك (القامُوسُ هُوَ القارِئ) — فَالعِلَّةُ مِن `c297172`.
//
// **والحَدّ** (القاعِدَة ٢): (١) بَعدَ البَوّابَةِ لا يَرى قارِئٌ رَمزاً ساقِطاً بِأَيِّ صورَةٍ مِن صُوَرِ الرَأس، في العائِلاتِ الأَربَعِ وبِسَبَبَي الإسقاط؛ (٢) **بِالبِناء**: كُلُّ رَمزٍ يُعيدُه
// قارِئٌ قيمَةُ زَوجٍ حَكَمَت عَلَيه البَوّابَة (`SessionVersionGate.ReadablePairs`) — فَلا يَختَلِفُ المُحَلِّلانِ بَعدَ اليَومِ بِلا أَن يَحمَرَّ هذا؛ (٣) القارِئُ يَفُكُّ
// القيمَةَ فَكَّ القامُوسِ بِحَرفِه (التَنصيصُ يَبقى، والنِسبَةُ المِئَوِيَّةُ تُفَكّ). والتَوصيفُ `SessionGateCookieParseCharacterizationTests` يَبقى بِبَصمَتِه.
public class SessionGateReadsWhatTheReaderReadsTests(ITestOutputHelper output)
{
    private const string Slug = "zz-gate";

    private static string Generic => AuthSession.CookieNameFor(true, Slug);
    private static string Role => AuthSession.CookieNameFor(true, Slug, "vendor");
    private static string Studio => StudioAuth.CookieNameFor(true);

    /// <summary>الاسمُ بِحالَةٍ أُخرى: البادِئَةُ <c>__HOST-</c>، والاسمُ بِلا بادِئَةٍ بِأَحرُفٍ كَبيرَة.</summary>
    private static string Upper(string name) => name.StartsWith(AuthSession.HostPrefix, StringComparison.Ordinal)
        ? "__HOST-" + name[AuthSession.HostPrefix.Length..]
        : name.ToUpperInvariant();

    /// <summary>صُوَرُ الرَأسِ لِرَمزٍ <paramref name="t"/> تَحتَ <paramref name="n"/> — كُلُّ صورَةٍ كانَت تَفتَحُ الجَلسَةَ بَعدَ الإسقاطِ أَو تُقارِبُها.</summary>
    internal static (string Shape, StringValues Cookie)[] Shapes(string n, string t) =>
    [
        ("مُكَرَّرٌ بِحُروفِه", $"{n}={t}; {n}=x"),
        ("مُكَرَّرٌ بِحالَةٍ أُخرى", $"{n}={t}; {Upper(n)}=x"),
        ("سَطرانِ لِلرَأس", new StringValues(new[] { $"{n}={t}", $"{n}=x" })),
        ("بَينَ عَلامَتَي تَنصيص", $"{n}=\"{t}\""),
        ("فَراغٌ بَعدَ =", $"{n}= {t}"),
        ("بِتَرميزِ المُتَصَفِّحِ ومَعَه مُكَرَّر", $"{n}={Uri.EscapeDataString(t)}; {n}=x"),
        ("ومَعَه كوكيُّ اسمٍ بِحالَةٍ أُخرى", $"{n}={t}; {Upper(n)}.name=x; {n}=x"),
    ];

    /// <summary>العائِلاتُ الأَربَع: المَسارُ والاسمُ ومَن يَقرَأ.</summary>
    private static (string Family, string Path, string Name, bool IsStudio)[] Families =>
    [
        ("المَتجَرُ العامّ", $"/{Slug}/me", Generic, false),
        ("الدَورُ عَلى مَسارِه", $"/{Slug}/r/vendor/me", Role, false),
        ("الدَورُ بِالسُقوط", $"/{Slug}/explore", Role, false),
        ("الاستوديو", "/studio", Studio, true),
    ];

    private static DefaultHttpContext Https(string path, object? account, StringValues cookie)
    {
        var ctx = SessionRevocationTests.GateContext(path, account);
        ctx.Request.Scheme = "https";
        ctx.Request.Host = new HostString("zz-gate.example.test");
        ctx.Request.Headers.Cookie = cookie;
        return ctx;
    }

    /// <summary>ما يَقرَؤُه القُرّاءُ أَنفُسُهُم — رَمزٌ أَو <c>null</c>: <c>ResolveToken</c> لِلمَتجَر، ولِلاستوديو ما يَقرَؤُه <c>StudioAuth.Load</c> (مَعَ هُوِيَّتِه).</summary>
    private static string? ReaderToken(DefaultHttpContext ctx, bool isStudio)
    {
        if (!isStudio) return AuthSession.ResolveToken(ctx.Request, Slug);
        var auth = new StudioAuth(new HttpContextAccessor { HttpContext = ctx });
        auth.Load();
        return auth.IsAuthenticated ? AuthSession.CookieValue(ctx.Request, StudioAuth.CookieNameOn(ctx.Request)) : null;
    }

    // ─── (١) بَعدَ البَوّابَةِ لا رَمزَ ساقِطاً عِندَ أَيِّ قارِئ ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task After_the_gate_no_reader_opens_a_revoked_or_deleted_accounts_token_whatever_follows_it()
    {
        int rows = 0, openBefore = 0;
        var leaks = new List<string>();
        foreach (var (family, path, name, isStudio) in Families)
            foreach (var cause in isStudio ? new[] { "رَفعُ الإصدار" } : new[] { "رَفعُ الإصدار", "حَذفُ الحِساب" })
            {
                var id = Guid.NewGuid();
                object account = isStudio
                    ? new StudioUser { Id = id, SessionVersion = 2 }
                    : cause == "حَذفُ الحِساب"
                        ? new User { Id = id, TenantSlug = Slug, SessionVersion = 1, DeletedAt = DateTime.UtcNow }
                        : new User { Id = id, TenantSlug = Slug, SessionVersion = 2 };
                var token = AuthHandlers.MakeToken(id, isStudio ? StudioAuth.Tenant : Slug, 1);
                foreach (var (shape, cookie) in Shapes(name, token))
                {
                    var ctx = Https(path, account, cookie);
                    if (ReaderToken(ctx, isStudio) is not null) openBefore++;
                    await SessionVersionGate.ApplyAsync(ctx);
                    var after = ReaderToken(ctx, isStudio);
                    var owns = !isStudio && EffectiveRole.OwnsRole(ctx.Request, Slug, id, "vendor");
                    rows++;
                    if (after is not null || owns) leaks.Add($"{family} · {cause} · {shape}");
                }
            }
        output.WriteLine($"صُفوفٌ مَقيسَة: {rows} · مَفتوحَةٌ قَبلَ البَوّابَة: {openBefore} · مَفتوحَةٌ بَعدَها: {leaks.Count}");
        foreach (var l in leaks) output.WriteLine("✗ " + l);
        Assert.True(rows >= 49 && openBefore > 0, $"أَداةٌ عَمياء: {rows} صَفّاً، {openBefore} مَفتوحاً قَبلَ البَوّابَة.");
        Assert.Empty(leaks);
    }

    /// <summary>والمَحوُ يَتبَعُ: الاسمُ المُسقَطُ يُمحى بِـ<c>Secure</c> و<c>Path=/</c> — بِإحدى حالَتَيه في الرَدِّ الواحِد، لِأَنّ <c>Response.Cookies.Delete</c> يُبدِلُ
    /// <c>Set-Cookie</c> سابِقاً لِلاسمِ نَفسِه بِلا حالَةِ أَحرُف (مَقيسٌ ومَقبولٌ في الموجَة ١١: <c>SessionRevocationTests.A_shadowed_case_duplicate_of_a_revoked_cookie_is_stripped_with_it</c>
    /// — والأُخرى تُنزَعُ مِن كُلِّ طَلَبٍ وتُمحى في الرَدِّ التالي).</summary>
    [Fact]
    public async Task The_revoked_name_and_its_case_variant_are_deleted_secure()
    {
        var user = new User { Id = Guid.NewGuid(), TenantSlug = Slug, SessionVersion = 2 };
        var ctx = Https($"/{Slug}/me", user, $"{Generic}={AuthHandlers.MakeToken(user.Id, Slug, 1)}; {Upper(Generic)}=x");
        await SessionVersionGate.ApplyAsync(ctx);
        var deleted = ctx.Response.Headers.SetCookie.Select(h => h!).ToList();
        output.WriteLine(string.Join(" ‖ ", deleted.Select(d => d.Split(';')[0])));
        Assert.Equal("", ctx.Request.Headers.Cookie.ToString());
        Assert.Contains(deleted, d => d.StartsWith($"{Generic}=;", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(deleted, d => d.StartsWith($"{Generic}.name=;", StringComparison.OrdinalIgnoreCase));
        Assert.All(deleted, d => Assert.True(d.Contains("secure", StringComparison.OrdinalIgnoreCase) && d.Contains("path=/", StringComparison.OrdinalIgnoreCase)));
    }

    // ─── (٢) بِالبِناء: ما يُعيدُه قارِئٌ قيمَةُ زَوجٍ حَكَمَت عَلَيه البَوّابَة ─────────────────────────────────────────────────────────────

    [Fact]
    public void Every_token_a_reader_returns_is_the_value_of_a_pair_the_gate_judges()
    {
        int rows = 0, read = 0;
        var outside = new List<string>();
        var id = Guid.NewGuid();
        foreach (var (family, path, name, isStudio) in Families)
            foreach (var https in new[] { true, false })
            {
                var n = https ? name : name[AuthSession.HostPrefix.Length..];
                var token = AuthHandlers.MakeToken(id, isStudio ? StudioAuth.Tenant : Slug, 1);
                var shapes = Shapes(n, token)
                    .Append(("وَحدَه", (StringValues)$"{n}={token}")).Append(("تالِفٌ أَوَّلاً", (StringValues)$"{n}=x; {n}={token}"));
                foreach (var (shape, cookie) in shapes)
                {
                    var ctx = Https(path, null, cookie);
                    if (!https) ctx.Request.Scheme = "http";
                    var judged = SessionVersionGate.ReadablePairs(ctx.Request).Select(p => p.Value).ToHashSet(StringComparer.Ordinal);
                    var got = ReaderToken(ctx, isStudio);
                    rows++;
                    if (got is null) continue;
                    read++;
                    if (!judged.Contains(got)) outside.Add($"{family} · {(https ? "https" : "http")} · {shape}");
                }
            }
        output.WriteLine($"صُفوفٌ: {rows} · قَرَأَ فيها قارِئٌ رَمزاً: {read} · خارِجَ ما حَكَمَت عَلَيه البَوّابَة: {outside.Count}");
        foreach (var o in outside) output.WriteLine("✗ " + o);
        Assert.True(rows >= 72 && read > 0, $"أَداةٌ عَمياء: {rows} صَفّاً، {read} مَقروءاً.");
        Assert.Empty(outside);
    }

    /// <summary>والقائِمَةُ هِيَ ما تَحكُمُ بِه البَوّابَةُ فِعلاً: جِسمُ <c>ApplyAsync</c> يَسأَلُ <c>ReadablePairs(ctx.Request)</c> لِلفَحصِ الأَوَّلِ ولِكُلِّ مَرَّة، ولا يَقرَأُ
    /// <c>Request.Cookies</c> بِيَدِه — وإلّا صَدَقَ الصَفُّ السابِقُ عَلى قائِمَةٍ لا يَسأَلُها أَحَد (مَقيس: عَلى البَوّابَةِ قَبلَ الإصلاحِ والقائِمَةُ مُعَرَّفَةٌ أَخضَرُ هُوَ وَحدَه).</summary>
    [Fact]
    public void The_gate_judges_exactly_the_readable_pairs()
    {
        var code = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs/templates/ACommerce.Templates.Customer.Marketplace/Services/SessionVersionGate.cs")));
        var body = code[code.IndexOf("public static async Task ApplyAsync(HttpContext ctx)", StringComparison.Ordinal)..];
        body = body[..body.IndexOf("\n    }\n", StringComparison.Ordinal)];
        var asks = System.Text.RegularExpressions.Regex.Matches(body, @"ReadablePairs\(ctx\.Request\)").Count;
        var byHand = System.Text.RegularExpressions.Regex.Matches(body, @"Request\.Cookies\b").Count;
        output.WriteLine($"ApplyAsync: {body.Split('\n').Length} سَطراً · يَسأَلُ ReadablePairs {asks} · Request.Cookies بِيَدِه {byHand}");
        Assert.True(body.Length > 200, "أَداةٌ عَمياء: جِسمُ ApplyAsync غائِب.");
        Assert.Equal(2, asks);
        Assert.Equal(0, byHand);
    }

    // ─── (٣) القارِئُ يَفُكُّ القيمَةَ فَكَّ القامُوس ─────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("plain")]
    [InlineData("percent")]
    [InlineData("quoted")]
    public void The_reader_decodes_a_single_pair_exactly_as_the_dictionary(string shape)
    {
        var token = AuthHandlers.MakeToken(Guid.NewGuid(), Slug, 1);
        var value = shape switch { "percent" => Uri.EscapeDataString(token), "quoted" => $"\"{token}\"", _ => token };
        var ctx = Https($"/{Slug}/me", null, $"{Generic}={value}");
        var dictionary = ctx.Request.Cookies[Generic];
        var reader = AuthSession.CookieValue(ctx.Request, Generic);
        output.WriteLine($"{shape}: القامُوس {(dictionary == token ? "الرَمز" : dictionary)} · القارِئ {(reader == token ? "الرَمز" : reader)}");
        Assert.Equal(dictionary, reader);
        Assert.Equal(shape != "quoted", AuthSession.ResolveToken(ctx.Request, Slug) is not null);
    }
}
