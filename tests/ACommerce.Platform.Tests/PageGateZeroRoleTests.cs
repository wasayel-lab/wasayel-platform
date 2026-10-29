using System.Security.Cryptography;
using System.Text;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Roles;
using ACommerce.Platform.I18n;
using ACommerce.Platform.Shared;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Gates;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Queries;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using GatedPage = ACommerce.Templates.Customer.Marketplace.Components.GatedPage;

namespace ACommerce.Platform.Tests;

// ═══ الحارِسُ الرابِع: صَفحَةٌ مَحروسَةٌ بِصَلاحِيَّةٍ (`<GatedPage Permission="…">`) في مَتجَرٍ بِلا أَدوار ═══
//
// **مُراجَعَةُ تَيّارِ S1 الجَولَة ٢ (‏2026-10-29، كُبرى عَلى `4f61e8a` و`8ff2b12`)**: ‏ADR-040 §٢‑3 يَقول «لا بَوّابَةَ تَختَصِرُ الدالَّة»، وماسِحُه
// يَقرَأُ ثَلاثَةَ حُرّاسٍ (`PermissionFilter` و`GatePipeline` و`HasPermissionAsync`) بِشَكلِ `if (… Roles.Count == 0)` وَحدَه. والرابِعُ — `GatedPage`،
// حارِسُ صَفحَةِ النَشرِ في ADR-043 — يَسأَلُ البابَ داخِلَ `if (tenant is { Roles.Count: > 0 })` وَحدَه، ثُمَّ `_state = GateState.Allowed`: مَتجَرٌ بِلا
// أَدوارٍ (أَو بِلا مُستَأجِر) يَفتَحُ الصَفحَةَ لِأَيِّ صَلاحِيَّة، ومِنها `tenant.manage` الَّتي تَرُدُّها `RolePermissions.Has` عَلى صِفرِ الأَدوار. واليَومَ
// لا صَفحَةَ تَحرُسُها صَلاحِيَّةٌ إدارِيَّة (الوَحيدَةُ `listing.create`) — فَالسُلوكُ الحَيُّ لَم يَتَغَيَّر بَعد، والبابُ مَفتوحٌ لِأَوَّلِ صَفحَةٍ تُضاف.
//
// **يُثَبَّتُ قَبلَ التَبديل (القاعِدَة ٣)**: الصَفحَةُ مَرسومَةٌ لا مَقروءَة — `HtmlRenderer` (نَمَطُ `StudioConsentReturnUrlTests`) عَلى Postgres مَحَلِّيٍّ
// (المُكَوِّنُ يَقرَأُ المُستَأجِرَ والدَورَ مِن الوَثائِق). صَفّانِ دائِمانِ (صَلاحِيَّةُ العُضوِ في صِفرِ الأَدوار، والمَتجَرُ ذو الأَدوار) يَطبَعانِ بَصمَةَ
// الوَسمِ ويَبقَيانِ بِلا تَعديلِ حَرف؛ و`Today_…` الحَيُّ والمَصدَرِيُّ يَنقَلِبانِ بِاسمَيهِما في كوميتِ التَبديل.
public class PageGateZeroRoleTests(ITestOutputHelper output)
{
    internal const string Member = "listing.create";
    internal const string Administrative = "tenant.manage";

    internal static string GateSource()
        => WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(
            ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "GatedPage.razor"))));

    /// <summary><b>جَوابُ الدالَّةِ الَّتي يَسأَلُها البابُ — صَفٌّ دائِم</b>: عَلى صِفرِ الأَدوارِ <c>PermissionDoor.Opens</c> تُعطي صَلاحِيَّةَ العُضوِ وتَرُدُّ
    /// الإدارِيَّةَ لِكُلِّ دَورٍ (مَسارٍ ومُخَزَّن) ولِلغائِب — ADR-040 §٢‑1 بِحَرفِه. فَما تَفعَلُه الصَفحَةُ في صِفرِ الأَدوارِ هُوَ سُؤالُ: أَتَسأَلُها؟</summary>
    [Fact]
    public void On_zero_roles_the_door_the_page_asks_grants_the_member_permission_and_refuses_the_administrative_one_for_every_role()
    {
        var none = Array.Empty<Role>();
        string?[] roles = { null, "", "customer", "vendor", "tenant_admin" };
        var cells = 0;
        foreach (var url in roles)
            foreach (var stored in roles)
            {
                cells++;
                Assert.True(PermissionDoor.Opens(none, url, stored, Member));
                Assert.False(PermissionDoor.Opens(none, url, stored, Administrative));
            }
        output.WriteLine($"خاناتٌ مَفحوصَة (دَورُ مَسار × مُخَزَّن): {cells} · {Member}: تُفتَح · {Administrative}: تُرَدّ");
        Assert.True(cells >= 25, $"أَداةٌ عَمياء: {cells}");
    }

    /// <summary><b>«اليَوم» في المَصدَر (يُقلَبُ بِاسمِه)</b>: الصَفحَةُ تَسأَلُ البابَ داخِلَ فَرعِ «لِلمَتجَرِ أَدوار» وَحدَه — <c>if (tenant is { Roles.Count: > 0 })</c>
    /// — ولا فَرعَ لِصِفرِ الأَدوار، ولا سُؤالَ لِلدالَّةِ خارِجَه؛ فَما بَعدَ الفَرعِ (<c>_state = GateState.Allowed;</c>) يَبلُغُه صِفرُ الأَدوارِ والمُستَأجِرُ الغائِبُ
    /// بِلا سُؤال. والماسِحانِ (<c>RoleCatalogAdministrativeTests.GateFiles</c> و<c>ComposedRolePublishTests</c>) لا يَقرَآنِ هذا المِلَفّ.</summary>
    [Fact]
    public void Today_the_page_gate_asks_the_door_only_inside_its_positive_role_count_branch_so_zero_roles_reach_allowed_unasked()
    {
        var code = GateSource();
        var branch = code.IndexOf("if (tenant is { Roles.Count: > 0 })", StringComparison.Ordinal);
        var asks = System.Text.RegularExpressions.Regex.Matches(code, @"PermissionDoor\.Opens\(").Select(m => m.Index).ToList();
        var allowed = code.LastIndexOf("_state = GateState.Allowed;", StringComparison.Ordinal);
        output.WriteLine($"فَرعُ «لِلمَتجَرِ أَدوار»: {branch} · أَسئِلَةُ البابِ: {asks.Count} ({string.Join(", ", asks)}) · فَرعُ صِفرٍ صَريح: "
            + $"{code.Contains("Roles.Count == 0", StringComparison.Ordinal)} · Allowed الأَخير: {allowed}");

        Assert.True(branch > 0);
        Assert.Single(asks);
        Assert.True(asks[0] > branch && asks[0] < allowed, "سُؤالُ البابِ داخِلَ الفَرعِ المَوجِب، قَبلَ Allowed.");
        Assert.DoesNotContain("Roles.Count == 0", code, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant is null", code, StringComparison.Ordinal);
    }
}

/// <summary>
/// <para><b>بُرهانٌ حَيٌّ عَلى Postgres — المُكَوِّنُ نَفسُه مَرسوماً</b>: مَتجَرٌ بِلا أَدوارٍ وعُضوٌ فيه، ومَتجَرٌ بِدَورَي <c>customer</c> و<c>vendor</c> وعُضوانِ
/// بِهِما؛ يُرسَمُ <c>&lt;GatedPage Slug Permission&gt;</c> بِمُحتَوىً عَلامَتُه <see cref="Marker"/> بِجَلسَةِ العُضوِ (كوكي <c>.acommerce.auth.{slug}</c>
/// مُوَقَّعٌ بِـ<c>AuthHandlers.MakeToken</c>)، ويُطبَعُ: أَرُسِمَ المُحتَوى، أَم نَصُّ الرَفض، وبَصمَةُ الوَسمِ (المُعَرِّفُ العابِرُ مُستَبدَلٌ بِـ<c>{slug}</c>).</para>
/// <para>يَتَخَطّى نَفسَه ما لَم تُضبَط <c>WASAYEL_LIVE_PROOF=1</c> و<c>ConnectionStrings__Postgres</c>، ويُنشِئُ مَتجَرَيهِ ويَحذِفُهُما.</para>
/// </summary>
[Collection(LiveProofCollection.Name)]
public class LivePageGateZeroRoleProofTests(ITestOutputHelper output)
{
    internal const string Marker = "PAGE-BEHIND-THE-GATE";

    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>مَتجَرٌ بِأَدوارٍ مُعطاة (أَو بِلا أَدوار)، وعُضوٌ لِكُلِّ دَورٍ نَشِطٍ مُعطىً — و«""» بِلا دَور، قيمَةُ <c>User.ActiveRole</c> الافتِراضِيَّةُ لِعُضوٍ سَجَّلَ نَفسَه — ويُعيدُ مُعَرِّفاتِهِم بِتَرتيبِها.</summary>
    internal static async Task<IReadOnlyList<Guid>> SeedAsync(Marten.IDocumentStore store, string slug, IEnumerable<string> catalogRoles, params string[] activeRoles)
    {
        var roles = catalogRoles.Select((s, i) => RoleCatalog.InstantiateRole(RoleCatalog.Find(s)!, i)).ToList();
        await using (var g = store.LightweightSession())
        {
            g.Store(new ACommerce.Kit.Tenants.Tenant { Id = slug, Name = "مَتجَرُ بُرهانِ الحارِسِ الرابِع", OwnerUserId = Guid.NewGuid(), AuthChannel = "phone", Roles = roles });
            await g.SaveChangesAsync();
        }
        var ids = new List<Guid>();
        await using (var s = store.LightweightSession(slug))
        {
            foreach (var role in activeRoles)
            {
                var id = Guid.NewGuid();
                ids.Add(id);
                s.Store(new User { Id = id, ActiveRole = role });
            }
            await s.SaveChangesAsync();
        }
        return ids;
    }

    /// <summary>يَرسُمُ <c>GatedPage</c> بِجَلسَةِ <paramref name="userId"/> في <paramref name="slug"/> — بِخَدَماتِه الحَقيقِيَّةِ عَلى المَخزَن.</summary>
    internal static async Task<string> RenderAsync(Marten.IDocumentStore store, string slug, Guid userId, string permission)
    {
        var http = new DefaultHttpContext();
        http.Request.Path = $"/{slug}/create-listing";
        http.Request.Headers.Cookie = $"{AuthSession.CookieName(slug)}={AuthHandlers.MakeToken(userId, slug)}";
        var accessor = new HttpContextAccessor { HttpContext = http };

        await using var services = new ServiceCollection()
            .AddSingleton<IHttpContextAccessor>(accessor)
            .AddSingleton(new AuthSession())
            .AddSingleton<ITenantContext>(new VendorRegisterBucketTests.ResolvedTenant(slug))
            .AddSingleton(new TenantRoleService(store))
            .AddSingleton(new AccountQueries(store))
            .AddSingleton(new L(new HttpContextAccessor()))
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<GatedPage>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(GatedPage.Slug)] = slug,
                [nameof(GatedPage.Permission)] = permission,
                [nameof(GatedPage.ChildContent)] = (RenderFragment)(b => b.AddContent(0, Marker)),
            }))).ToHtmlString());
        return html.Replace(slug, "{slug}", StringComparison.Ordinal);
    }

    internal static bool Shows(string html) => html.Contains(Marker, StringComparison.Ordinal);

    internal static bool Refuses(string html)
        => !Shows(html) && System.Net.WebUtility.HtmlDecode(html)
            .Contains(LocaleCatalog.Find(LocaleCatalog.Arabic, "gate.forbidden.title")!, StringComparison.Ordinal);

    private void Print(string label, string html)
        => output.WriteLine($"{label}: {(Shows(html) ? "المُحتَوى" : Refuses(html) ? "رَفض" : "غَيرُهُما")} · {html.Length} حَرفاً · "
            + $"sha256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(html)))[..16]}");

    // ─── صَفّانِ دائِمانِ — لا يَتَغَيَّرانِ بِالتَبديل ───────────────────────────────────────────────────

    /// <summary>صِفرُ الأَدوار: صَلاحِيَّةُ العُضوِ تَفتَحُ الصَفحَةَ لِلعُضوِ بِلا دَورٍ نَشِط — صَفحَةُ النَشرِ في <c>ashare</c> و<c>order</c> و<c>theme-demo</c> كَما هِيَ.</summary>
    [Fact]
    public async Task A_member_of_a_zero_role_store_sees_the_page_behind_a_member_permission_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "pg-zz-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var ids = await SeedAsync(store, slug, Array.Empty<string>(), "");
            var html = await RenderAsync(store, slug, ids[0], PageGateZeroRoleTests.Member);
            Print($"صِفرُ أَدوار · عُضوٌ بِلا دَور · {PageGateZeroRoleTests.Member}", html);
            Assert.True(Shows(html));
            Assert.Equal(Marker, html);
        }
        finally { await LiveZeroRoleAdminProofTests.CleanAsync(store, slug); }
    }

    /// <summary>مَتجَرٌ بِأَدوار: الدَورُ الحامِلُ لِلصَلاحِيَّةِ يَرى الصَفحَة، والمُشتَري يُرَدُّ بِنَصِّ الرَفضِ ومَخرَجِ «اِختَر دَوراً آخَر» — فَرعُ الأَدوارِ كَما هُو.</summary>
    [Fact]
    public async Task In_a_store_with_roles_the_page_opens_for_the_role_carrying_the_permission_and_refuses_the_one_without_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "pg-zz-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var ids = await SeedAsync(store, slug, new[] { "customer", "vendor" }, "vendor", "customer");
            var vendor = await RenderAsync(store, slug, ids[0], PageGateZeroRoleTests.Member);
            var customer = await RenderAsync(store, slug, ids[1], PageGateZeroRoleTests.Member);
            Print($"customer+vendor · البائِع · {PageGateZeroRoleTests.Member}", vendor);
            Print($"customer+vendor · المُشتَري · {PageGateZeroRoleTests.Member}", customer);
            Assert.Equal(Marker, vendor);
            Assert.True(Refuses(customer));
            Assert.Contains("/{slug}/me/role", customer, StringComparison.Ordinal);
        }
        finally { await LiveZeroRoleAdminProofTests.CleanAsync(store, slug); }
    }

    // ─── «اليَوم» — يُقلَبُ بِاسمِه في كوميتِ التَبديل ──────────────────────────────────────────────────

    /// <summary><b>الثَغرَةُ مَرسومَة</b>: صِفرُ الأَدوار، والصَلاحِيَّةُ <c>tenant.manage</c> الَّتي تَرُدُّها الدالَّة — والصَفحَةُ تَرسُمُ مُحتَواها لِعُضوٍ بِلا دَور.</summary>
    [Fact]
    public async Task Today_a_member_of_a_zero_role_store_sees_the_page_behind_an_administrative_permission_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "pg-zz-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var ids = await SeedAsync(store, slug, Array.Empty<string>(), "");
            var html = await RenderAsync(store, slug, ids[0], PageGateZeroRoleTests.Administrative);
            Print($"صِفرُ أَدوار · عُضوٌ بِلا دَور · {PageGateZeroRoleTests.Administrative} (الدالَّةُ تَرُدّ: {!PermissionDoor.Opens(Array.Empty<Role>(), null, null, PageGateZeroRoleTests.Administrative)})", html);
            Assert.True(Shows(html));
        }
        finally { await LiveZeroRoleAdminProofTests.CleanAsync(store, slug); }
    }

    /// <summary><b>والمُستَأجِرُ الغائِبُ مَرسوماً</b> (‏2026-10-29، يُلحَقُ قَبلَ التَبديلِ في كوميتٍ مُستَقِلّ): جَلسَةٌ مُوَقَّعَةٌ لِمَتجَرٍ لا وَثيقَةَ لَه
    /// (حُذِفَ أَو لَم يوجَد) — <c>Roles.LoadAsync</c> يُعيدُ <c>Tenant = null</c>، والفَرعُ المَوجِبُ لا يَدخُلُه، فَالصَفحَةُ تَرسُمُ مُحتَواها لِأَيِّ صَلاحِيَّة؛
    /// والحُرّاسُ الثَلاثَةُ الآخَرونَ يَرُدّونَه (<c>PermissionFilter</c> ‏404، و<c>HasPermissionAsync</c> ‏<c>false</c>).</summary>
    [Fact]
    public async Task Today_a_session_in_a_store_without_a_tenant_document_sees_the_page_behind_any_permission_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "pg-zz-" + Guid.NewGuid().ToString("N")[..8];
        var member = await RenderAsync(store, slug, Guid.NewGuid(), PageGateZeroRoleTests.Member);
        var admin = await RenderAsync(store, slug, Guid.NewGuid(), PageGateZeroRoleTests.Administrative);
        Print($"بِلا وَثيقَةِ مُستَأجِر · {PageGateZeroRoleTests.Member}", member);
        Print($"بِلا وَثيقَةِ مُستَأجِر · {PageGateZeroRoleTests.Administrative}", admin);
        Assert.True(Shows(member));
        Assert.True(Shows(admin));
    }
}
