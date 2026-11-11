using ACommerce.Kit.Auth;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using ACommerce.Kit.Auth.Server;
using ACommerce.Platform.MultiTenancy;
using ACommerce.Platform.Shared;
using ACommerce.Templates.Customer.Marketplace;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.LiveStoreBirthOnceCharacterizationTests;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ ما يَنجو قَبلَ أَن يُرَدَّ دُخولُ غَيرِ المُديرِ عَلى مُضيفِ الإدارَة (‏2026-11-11، مُراجَعَةُ إغلاقِ W13-4 الجَولَةُ الأُولى · SEC-C2z-R1-3) ═══
//
// **السَبَبُ القادِم**: عَلى `{slug}-admin.R` تُرسِلُ استِمارَةُ الدُخولِ `as=tenant_admin`، و`HeldAdminClaimOrNoneAsync` يُسقِطُ الدَعوى لِمَن لا يَحمِلُه (7337b74)
// فَتُكتَبُ الكَعكَةُ العامَّةُ وَحدَها — والحاقِنُ عَلى ذلكَ المُضيفِ يَجعَلُ كُلَّ صَفحَةٍ `/r/tenant_admin/…` لا تَقرَأُ إلّا كَعكَةَ الإدارَة: فَكُلُّ دُخولٍ يَنجَحُ والصَفحَةُ
// التالِيَةُ تَطلُبُ الدُخول (مَقيسٌ عِندَ المُراجِعِ وعِندي: `/me/name` ثُمَّ `/me/role` ثُمَّ `/me` كُلُّها «سَجِّل دُخولك»). سَيُرَدُّ ذلكَ الدُخولُ بِلا كَعكَة.
// **وهذا المِلَفُّ يُثَبِّتُ ما لا يَتَغَيَّرُ بِذلك** — بابُ التَحَقُّقِ الحَيُّ عَلى Postgres كَما يَترُكُه الحاقِن: المُديرُ المُخَزَّنُ عَلى مُضيفِ الإدارَةِ يَأخُذُ كَعكَتَه ووِجهَتَه،
// وغَيرُ المُديرِ بِالدَعوى نَفسِها عَلى مُضيفِ المَتجَرِ وفي وَضعِ المَسارِ يَأخُذُ الكَعكَةَ العامَّةَ ووِجهَةَ دَورِه المُخَزَّن. ويَبقى بايتاتِه بَعدَ التَبديل (القاعِدَة ٣).
// يَتَخَطّى نَفسَه ما لَم تُضبَط WASAYEL_LIVE_PROOF=1 وConnectionStrings__Postgres — عَقدُ إخوَتِه، وفي مَجموعَتِهِم التَسلسُلِيَّة.

[Collection(LiveProofCollection.Name)]
public class LiveAdminHostSignInCharacterizationTests(ITestOutputHelper output)
{
    private static bool Enabled => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);
    private const string Base = "example.test";
    internal const string Verify = "/{slug}/auth/phone/verify";
    internal const string Code = "246810";

    /// <summary>مَتجَرٌ بِدَورَي المُشتَري والإدارَة، وعُضوٌ مُخَزَّنٌ مُشتَرِياً ومُديرٌ مُخَزَّنٌ مُديراً (أَسماؤُهُما مَحفوظَة — فَلا خُطوَةَ اسم).</summary>
    internal static async Task<(User Member, User Admin)> PutStoreAsync(LiveTemplateDoorHost host, string slug)
    {
        await using (var s = host.Store.LightweightSession())
        {
            s.Store(new Tenant
            {
                Id = slug, Name = "مُضيفُ الإدارَة", BrandColor = "#0f766e", AuthChannel = "phone",
                Roles = [RoleCatalog.InstantiateRole(RoleCatalog.Find("customer")!, 0), RoleCatalog.InstantiateRole(RoleCatalog.Find("tenant_admin")!, 1)],
            });
            await s.SaveChangesAsync();
        }
        User Person(string role, string name) => new()
        {
            Id = Guid.NewGuid(), TenantSlug = slug, FullName = name, ActiveRole = role, CreatedAt = DateTime.UtcNow.AddDays(-1),
            Phone = "05" + Random.Shared.Next(10_000_000, 99_999_999), PhoneVerified = true,
        };
        var (member, admin) = (Person("customer", "عُضوٌ مُشتَرٍ"), Person("tenant_admin", "مُديرُ المَتجَر"));
        await using (var s = host.Store.LightweightSession(slug)) { s.Store(member); s.Store(admin); await s.SaveChangesAsync(); }
        return (member, admin);
    }

    /// <summary>عَلى أَيِّ مُضيف: <c>null</c> = وَضعُ المَسار (<c>localhost</c> بِلا نِطاق)، و<c>""</c> = مُضيفُ المَتجَر، واسمُ دَورٍ = مُضيفُه.</summary>
    internal static Action<HttpContext> On(string slug, string? hostRole, Action<HttpContext> keep) => ctx =>
    {
        if (hostRole is not null)
        {
            ctx.Request.Scheme = "https";
            ctx.Request.Host = new HostString(hostRole.Length == 0 ? $"{slug}.{Base}" : $"{slug}-{(hostRole == "tenant_admin" ? "admin" : hostRole)}.{Base}");
            ctx.Items[StoreHostItems.Apex] = Base;
            ctx.Items[StoreHostItems.Slug] = slug;
            if (hostRole.Length > 0) ctx.Items[StoreHostItems.Role] = hostRole;
        }
        ctx.SetTenant(slug, "مُضيفُ الإدارَة", "#0f766e", "phone", hasRoles: true);
        keep(ctx);
    };

    /// <summary>دُخولٌ بِرَمزٍ مُصدَرٍ لِلتَوّ بِدَعوى <paramref name="claim"/> — يُعيدُ الحالَةَ والوِجهَةَ وأَسماءَ الكَعكاتِ المَكتوبَةِ لِلمَتجَر (بِلا قيَم).</summary>
    internal static async Task<(int Status, string Location, string[] Cookies)> SignInAsync(LiveTemplateDoorHost host, string slug, User who, string? hostRole, string claim)
    {
        AuthHandlers.IssueAttempt(slug, who.Phone!, Code, AuthHandlers.AuthKind.PhoneOtp);
        HttpContext? seen = null;
        var (status, location) = await host.PostFormOnHostAsync(Verify, new Dictionary<string, string> { ["slug"] = slug }, cookie: null,
            On(slug, hostRole, c => seen = c), ("phone", who.Phone!), ("code", Code), ("as", claim));
        var cookies = seen!.Response.Headers.SetCookie.Select(c => c!.Split('=')[0]).Where(n => n.Contains(slug, StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
        return (status, location, cookies);
    }

    [Fact]
    public async Task The_stored_admin_signs_in_on_the_admin_host_and_a_member_elsewhere_keeps_todays_answer_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var slug = NewSlug("w13ahc-");
        var (member, admin) = await PutStoreAsync(host, slug);
        try
        {
            var adminOnAdminHost = await SignInAsync(host, slug, admin, "tenant_admin", "tenant_admin");
            var memberOnStoreHost = await SignInAsync(host, slug, member, "", "tenant_admin");
            var memberOnPath = await SignInAsync(host, slug, member, null, "tenant_admin");
            foreach (var (label, r) in new[] { ("المُديرُ عَلى مُضيفِ الإدارَة", adminOnAdminHost), ("العُضوُ عَلى مُضيفِ المَتجَر", memberOnStoreHost), ("العُضوُ بِالمَسار", memberOnPath) })
                output.WriteLine($"[live] {label} ⇒ {r.Status} {r.Location} · كَعكات [{string.Join(", ", r.Cookies)}]");

            Assert.Equal((302, "/manage"), (adminOnAdminHost.Status, adminOnAdminHost.Location));
            Assert.Equal([$"__Host-.acommerce.auth.{slug}", $"__Host-.acommerce.auth.{slug}.name",
                          $"__Host-.acommerce.auth.{slug}.tenant_admin", $"__Host-.acommerce.auth.{slug}.tenant_admin.name"], adminOnAdminHost.Cookies);

            Assert.Equal((302, "/"), (memberOnStoreHost.Status, memberOnStoreHost.Location));
            Assert.Equal([$"__Host-.acommerce.auth.{slug}", $"__Host-.acommerce.auth.{slug}.name"], memberOnStoreHost.Cookies);

            Assert.Equal((302, $"/{slug}"), (memberOnPath.Status, memberOnPath.Location));
            Assert.Equal([$".acommerce.auth.{slug}", $".acommerce.auth.{slug}.name"], memberOnPath.Cookies);
        }
        finally
        {
            await using (var s = host.Store.LightweightSession(slug)) { s.Delete<User>(member.Id); s.Delete<User>(admin.Id); await s.SaveChangesAsync(); }
            await CleanAsync(host, [], slug);
        }
    }
}
