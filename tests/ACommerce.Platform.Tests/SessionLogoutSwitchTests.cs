using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Tenants;
using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ الخُروجُ مِن هذا الجِهازِ يَمسَحُ الكوكيّاتِ وإن تَعَثَّرَت خُطوَةُ الجِهاز — بَعدَ التَبديل (‏2026-11-05، الموجَة ١٢ G2 · ٢) ══════════════════════════
//
// قُلِبَ ما طَبَعَه التَوصيف (`SessionLogoutTests.cs`، `c368e9d`، والمِلَفُّ بِبَصمَتِه): «WriteThrows/Unreachable/استِمارَةٌ مَكسورَة ⇒ يَرمي … ⇒ 500 · يَمحو لا كوكي».
// والآن سَحبُ اشتِراكِ المُتَصَفِّحِ (ومَعَه قِراءَةُ الاستِمارَة) بِأَفضَلِ جُهدٍ وسَطرٍ في السِجِلّ، والأَدوارُ مِن كوكيّاتِ المُتَصَفِّحِ نَفسِها لا مِن وَثيقَةِ المَتجَر،
// ومَسحُ الكوكيّاتِ والتَحويلُ يَجريانِ بَعدَها دائِماً.

public class SessionLogoutSwitchTests(ITestOutputHelper output)
{
    private const string Slug = "ejar";

    /// <summary><c>AuthSession.RoleCookiesIn</c> — نَقِيَّة. <b>المُوجَب</b>: كوكيُّ دَورٍ، وكوكيُّ اسمِه، وحالَةُ أَحرُفٍ أُخرى — كُلٌّ يُسَمّي الدَور.
    /// <b>والسَوالِب</b>: كوكيُّ الجَلسَةِ العامُّ واسمُه، وكوكيُّ مَتجَرٍ آخَرَ (ومَتجَرٍ يَبدَأُ سلاجُه بِهذا)، والاستوديو، وما لَيسَ مِن العائِلَة — لا دَور.</summary>
    [Theory]
    [InlineData(".acommerce.auth.ejar.buyer", "buyer")]
    [InlineData(".acommerce.auth.ejar.buyer.name", "buyer")]
    [InlineData(".ACOMMERCE.AUTH.EJAR.Driver", "Driver")]
    [InlineData(".acommerce.auth.ejar", null)]
    [InlineData(".acommerce.auth.ejar.name", null)]
    [InlineData(".acommerce.auth.order.buyer", null)]
    [InlineData(".acommerce.auth.ejar2.buyer", null)]
    [InlineData(".acommerce.studio", null)]
    [InlineData("lang", null)]
    public void The_roles_a_browser_carries_are_read_from_its_cookie_names_alone(string cookie, string? role)
    {
        var roles = ACommerce.Templates.Customer.Marketplace.AuthSession.RoleCookiesIn([cookie], Slug);
        output.WriteLine($"{cookie} ⇒ {(roles.Count == 0 ? "لا دَور" : string.Join(" ، ", roles))}");
        Assert.Equal(role is null ? Array.Empty<string>() : new[] { role }, roles);
    }

    [Fact]
    public void A_role_and_its_name_cookie_name_the_role_once()
        => Assert.Equal(new[] { "buyer" }, ACommerce.Templates.Customer.Marketplace.AuthSession.RoleCookiesIn(
            [".acommerce.auth.ejar", ".acommerce.auth.ejar.name", ".acommerce.auth.ejar.buyer", ".acommerce.auth.ejar.buyer.name", ".ACOMMERCE.AUTH.EJAR.BUYER"], Slug));

    // ─── الفاحِص (القاعِدَة ٢): خُطوَتا القاعِدَةِ في البابِ داخِلَ try، والمَسحُ والتَحويلُ بَعدَهُما خارِجَه ──────────────────────────────────────────

    /// <summary>سَطرُ المَسحِ بَعدَ التَبديل — الأَدوارُ مِن كوكيّاتِ المُتَصَفِّحِ نَفسِها.</summary>
    internal const string ClearLine = "AuthSession.ClearAllCookiesForTenant(http.Response, slug, AuthSession.RoleCookiesIn(http.Request.Cookies.Keys, slug));";

    /// <summary>خَرقُ بابِ الخُروجِ لِـ«المَسحُ والتَحويلُ يَجريانِ دائِماً» — <c>null</c> إن كانَ نِداءُ <c>ForgetDeviceAsync</c> ومَعَه <c>ReadFormAsync</c> داخِلَ <c>try</c>
    /// لَه <c>catch (Exception</c>، ولا قِراءَةَ لِلمَخزَنِ في البابِ غَيرُها، وسَطرا المَسحِ (<see cref="ClearLine"/>) والتَحويلِ بَعدَ آخِرِ <c>catch</c>. وإلّا سَبَبُ الخَرق.</summary>
    internal static string? LogoutBreach(string body)
    {
        var forget = body.IndexOf("Services.SessionVersionGate.ForgetDeviceAsync(", StringComparison.Ordinal);
        var read = body.IndexOf("ReadFormAsync()", StringComparison.Ordinal);
        var clear = body.IndexOf(ClearLine, StringComparison.Ordinal);
        var back = body.IndexOf("return Results.Redirect($\"/{slug}\");", StringComparison.Ordinal);
        if (forget < 0 || read < 0) return "لا سَحبَ لِاشتِراكِ المُتَصَفِّح — الأَداةُ عَمياء";
        if (clear < 0 || back < 0) return "لا يَمسَحُ بِأَدوارِ كوكيّاتِ المُتَصَفِّحِ أَو لا يُحَوِّل";
        var tryAt = System.Text.RegularExpressions.Regex.Matches(body[..Math.Min(forget, read)], @"\btry\s*\{").LastOrDefault()?.Index ?? -1;
        var catchAt = body.IndexOf("catch (Exception", Math.Max(forget, read), StringComparison.Ordinal);
        if (tryAt < 0 || catchAt < 0) return "خُطوَةُ الجِهازِ بِلا try/catch";
        var closed = body.IndexOf("catch", tryAt, StringComparison.Ordinal);
        if (closed < Math.Max(forget, read)) return "خُطوَةُ الجِهازِ خارِجَ آخِرِ try";
        if (System.Text.RegularExpressions.Regex.IsMatch(body, @"\b(?:QuerySession|LightweightSession)\s*\(|\bLoadAsync\s*<")) return "يَقرَأُ المَخزَنَ في البابِ خارِجَ خُطوَةِ الجِهاز";
        var lastCatch = body.LastIndexOf("catch (Exception", StringComparison.Ordinal);
        if (clear < lastCatch || back < clear) return "المَسحُ أَو التَحويلُ قَبلَ آخِرِ catch";
        return null;
    }

    private static string LogoutDoor()
    {
        var doors = WriteEndpointGuardTests.AllMinimalApiEndpoints().Where(e => e.Route == "/{slug}/auth/logout").ToList();
        Assert.True(doors.Count == 1, $"«/{{slug}}/auth/logout»: {doors.Count} باباً — الأَداةُ عَمياء.");
        return doors[0].Body;
    }

    [Fact]
    public void The_logout_door_keeps_its_device_step_best_effort_reads_no_store_beyond_it_and_always_clears_and_redirects()
    {
        var breach = LogoutBreach(LogoutDoor());
        output.WriteLine($"بابُ الخُروج ⇒ {breach ?? "خُطوَةُ الجِهازِ بِأَفضَلِ جُهدٍ ولا قِراءَةَ لِلمَخزَنِ غَيرُها والمَسحُ والتَحويلُ بَعدَها"}");
        Assert.Null(breach);
    }

    /// <summary>حَقنُ عَيب (القاعِدَة ١٠): جِسمُ اليَومِ بِحَرفِه قَبلَ التَبديل، وقِراءَةُ المَخزَنِ خارِجَ الخُطوَة، والمَسحُ داخِلَ try، وقِراءَةُ الاستِمارَةِ قَبلَ try —
    /// كُلٌّ يُرَدّ؛ والبابُ الحَقيقيُّ يَمُرّ.</summary>
    [Theory]
    [InlineData("today", "var token = AuthSession.ResolveToken(http.Request, slug);\nif (ok)\n    await Services.SessionVersionGate.ForgetDeviceAsync(store, slug, session.UserId, (await http.Request.ReadFormAsync())[\"push_endpoint\"].ToArray(), admit: a);\nawait using var qs = store.QuerySession();\nvar tenant = await qs.LoadAsync<ACommerce.Kit.Tenants.Tenant>(slug);\nAuthSession.ClearAllCookiesForTenant(http.Response, slug, roles);\nreturn Results.Redirect($\"/{slug}\");")]
    [InlineData("reads-the-store-outside", "try { await Services.SessionVersionGate.ForgetDeviceAsync(store, slug, id, (await http.Request.ReadFormAsync())[\"push_endpoint\"].ToArray(), admit: a); }\ncatch (Exception ex) { }\nvar tenant = await qs.LoadAsync<ACommerce.Kit.Tenants.Tenant>(slug);\nAuthSession.ClearAllCookiesForTenant(http.Response, slug, AuthSession.RoleCookiesIn(http.Request.Cookies.Keys, slug));\nreturn Results.Redirect($\"/{slug}\");")]
    [InlineData("clear-inside-try", "try { await Services.SessionVersionGate.ForgetDeviceAsync(store, slug, id, (await http.Request.ReadFormAsync())[\"push_endpoint\"].ToArray(), admit: a);\nAuthSession.ClearAllCookiesForTenant(http.Response, slug, AuthSession.RoleCookiesIn(http.Request.Cookies.Keys, slug));\nreturn Results.Redirect($\"/{slug}\"); }\ncatch (Exception ex) { }")]
    [InlineData("form-read-outside", "var form = await http.Request.ReadFormAsync();\ntry { await Services.SessionVersionGate.ForgetDeviceAsync(store, slug, id, form[\"push_endpoint\"].ToArray(), admit: a); }\ncatch (Exception ex) { }\nAuthSession.ClearAllCookiesForTenant(http.Response, slug, AuthSession.RoleCookiesIn(http.Request.Cookies.Keys, slug));\nreturn Results.Redirect($\"/{slug}\");")]
    public void The_logout_check_refuses_a_door_body_in_an_old_or_broken_shape(string shape, string body)
    {
        var breach = LogoutBreach(body);
        output.WriteLine($"{shape}: {breach ?? "يَمُرّ"}");
        Assert.NotNull(breach);
        Assert.Null(LogoutBreach(LogoutDoor()));
    }
}

/// <summary>مُسَجِّلُ سِجِلٍّ لا يَكتُبُ — يَحفَظُ كُلَّ سَطرٍ بِمُستَواه وفِئَتِه.</summary>
internal sealed class RecordingLogs : ILoggerFactory
{
    public List<(LogLevel Level, string Category, string Text)> Lines { get; } = new();
    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);
    public void AddProvider(ILoggerProvider provider) { }
    public void Dispose() { }

    private sealed class Logger(RecordingLogs owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (owner.Lines) owner.Lines.Add((logLevel, category, formatter(state, exception)));
        }
    }
}

[Collection(LiveProofCollection.Name)]
public class LiveSessionLogoutSwitchProofTests(ITestOutputHelper output)
{
    /// <summary><b>قُلِبَ ما طَبَعَه التَوصيف</b> («يَرمي … ⇒ 500 · يَمحو لا كوكي»): كِتابَةٌ تَرمي، وقاعِدَةٌ لا تُبلَغ، واستِمارَةٌ مَكسورَة — كُلٌّ ‏302 إلى <c>/{slug}</c>
    /// وتُمحى كوكيّاتُ الجَلسَةِ والاسمِ والدَورِ الأَربَعَة (الدَورُ مِن كوكيِّ المُتَصَفِّحِ نَفسِه). والاستِمارَةُ المَكسورَةُ لا تَمَسُّ الاشتِراكَين.
    /// والسالِبُ (المَخزَنُ السَليمُ يَمحو عُنوانَ هذا المُتَصَفِّحِ وَحدَه) يُثَبِّتُه التَوصيفُ بِلا تَعديل.</summary>
    [Theory]
    [InlineData(LiveSessionLogoutProofTests.Store.WriteThrows, false)]
    [InlineData(LiveSessionLogoutProofTests.Store.Unreachable, false)]
    [InlineData(LiveSessionLogoutProofTests.Store.Healthy, true)]
    public async Task A_logout_whose_device_step_stumbles_still_clears_the_cookies_and_redirects_on_Postgres(LiveSessionLogoutProofTests.Store store, bool malformedForm)
    {
        if (!LiveSessionLogoutProofTests.Enabled) return;
        var outcome = await LiveSessionLogoutProofTests.RunAsync(store, malformedForm);
        output.WriteLine($"{store}{(malformedForm ? " · استِمارَةٌ مَكسورَة" : "")} ⇒ {outcome}");
        Assert.Equal("302", outcome.Answer);
        Assert.Equal("/{slug}", outcome.Location);
        Assert.Equal(new[] { ".acommerce.auth.{slug}", ".acommerce.auth.{slug}.name", ".acommerce.auth.{slug}.buyer", ".acommerce.auth.{slug}.buyer.name" }.Order(), outcome.Cleared.Order());
        if (malformedForm) Assert.Equal(new[] { LiveSessionLogoutProofTests.PushA, LiveSessionLogoutProofTests.PushB }, outcome.PushLeft);
    }

    /// <summary>«يُقالُ ولا يُبتلَع»: خُطوَةُ الجِهازِ إن تَعَثَّرَت سَطرُ تَحذيرٍ واحِدٌ في فِئَةِ <c>ACommerce.Logout</c> بِسلاجِ المَتجَرِ ونَوعِ العَطَب — لِكِتابَةٍ تَرمي
    /// ولِقاعِدَةٍ لا تُبلَغ (والأَدوارُ مِن كوكيّاتِ المُتَصَفِّحِ فَلا قِراءَةَ ثانِيَة) — والخُروجُ السَليمُ بِلا سَطر.</summary>
    [Fact]
    public async Task Each_stumbling_step_writes_one_warning_and_a_healthy_logout_writes_none_on_Postgres()
    {
        if (!LiveSessionLogoutProofTests.Enabled) return;
        var counts = new List<string>();
        foreach (var store in new[] { LiveSessionLogoutProofTests.Store.WriteThrows, LiveSessionLogoutProofTests.Store.Unreachable, LiveSessionLogoutProofTests.Store.Healthy })
        {
            var logs = new RecordingLogs();
            var slug = "zz-g2lw-" + Guid.NewGuid().ToString("N")[..8];
            var tenant = new Tenant { Id = slug, Roles = [new ACommerce.Kit.Roles.Role { Slug = LiveSessionLogoutProofTests.Role, CatalogSlug = LiveSessionLogoutProofTests.Role }] };
            var user = new User { Id = Guid.NewGuid(), TenantSlug = slug, SessionVersion = 1 };
            user.PushSubscriptions.Add(new PushSubscription { Endpoint = LiveSessionLogoutProofTests.PushA, P256dh = "p", Auth = "a" });
            DocWorld? world = store == LiveSessionLogoutProofTests.Store.Healthy ? null : new DocWorld().Put(tenant).Put(user);
            await using var host = LiveTemplateDoorHost.Build(services: s =>
            {
                s.AddSingleton<ILoggerFactory>(logs);
                if (world is not null) s.AddSingleton<IDocumentStore>(world.Store);
            });
            if (world is null)
            {
                await using (var g = host.Store.LightweightSession()) { g.Store(tenant); await g.SaveChangesAsync(); }
                await using (var s = host.Store.LightweightSession(slug)) { s.Store(user); await s.SaveChangesAsync(); }
            }
            else world.BeforeMember = store == LiveSessionLogoutProofTests.Store.WriteThrows
                ? m => { if (m == "SaveChangesAsync") throw new InvalidOperationException("عَطَب"); }
                : _ => throw new InvalidOperationException("غائِبَة");
            try
            {
                var (status, at) = await host.PostFormAsync("/{slug}/auth/logout", new Dictionary<string, string> { ["slug"] = slug }, slug,
                    AuthHandlers.MakeToken(user.Id, slug, 1), ("push_endpoint", LiveSessionLogoutProofTests.PushA));
                var warnings = logs.Lines.Where(l => l.Category == "ACommerce.Logout" && l.Level == LogLevel.Warning).ToList();
                counts.Add($"{store}: {status} · تَحذيرات {warnings.Count}");
                Assert.Equal(302, status);
                Assert.Equal($"/{slug}", at);
                Assert.Equal(store switch { LiveSessionLogoutProofTests.Store.WriteThrows => 1, LiveSessionLogoutProofTests.Store.Unreachable => 1, _ => 0 }, warnings.Count);
                Assert.All(warnings, w => Assert.Contains(slug, w.Text, StringComparison.Ordinal));
            }
            finally { if (world is null) await LiveZeroRoleAdminProofTests.CleanAsync(host.Store, slug); }
        }
        output.WriteLine(string.Join(" | ", counts));
    }
}
