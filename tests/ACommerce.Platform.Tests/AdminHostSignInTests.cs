using ACommerce.Kit.Auth;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.LiveAdminHostSignInCharacterizationTests;
using static ACommerce.Platform.Tests.LiveStoreBirthOnceCharacterizationTests;

namespace ACommerce.Platform.Tests;

// ═══ دُخولٌ عَلى مُضيفِ الإدارَةِ لا يَنتَهي بِدَورِها يُرَدُّ بِلا كَعكَةٍ إلى رَفضٍ بِمَخرَج (‏2026-11-11، مُراجَعَةُ إغلاقِ W13-4 الجَولَةُ الأُولى · SEC-C2z-R1-3؛ ADR-050 §١٤) ═══
//
// **المَقيسُ قَبل** (عِندي، مَتجَرُ rewalk بِأَدوارِه الثَلاثَة): عُضوٌ بِلا دَورٍ مُخَزَّنٍ يَدخُلُ عَلى `{slug}-admin.localhost` ← 302 `/me/name?next=%2Fme%2Frole` بِالكَعكَةِ
// العامَّةِ وَحدَها، ثُمَّ ثَلاثُ صَفَحاتٍ تَطلُبُ الدُخول. **والآن**: `AdminHostSignIn.Refusal` يُجيبُ عَلى مُضيفِ الإدارَةِ لِهذا المَتجَرِ بِعَينِه حينَ لا يَبقى دَورُ الإدارَةِ
// بَعدَ فَحصِ الحِيازَة ← `/login?err=not_store_admin` بِلا كَعكَةٍ ولا تَسكينِ دَور؛ والمُديرُ ومُضيفُ المَتجَرِ ووَضعُ المَسارِ بِحَرفِهِم (التَوصيفُ بِبايتاتِه).

public class AdminHostSignInTests(ITestOutputHelper output)
{
    private const string Apex = "example.test";
    private static HostView On(string hostSlug, string? hostRole, string port = "") => new(Apex, hostSlug, "https", port) { HostRole = hostRole };

    [Theory]
    [InlineData("ejar", "tenant_admin", "", "/login?err=not_store_admin")]
    [InlineData("ejar", "tenant_admin", "customer", "/login?err=not_store_admin")]
    [InlineData("ejar", "tenant_admin", "vendor", "/login?err=not_store_admin")]
    [InlineData("ejar", "tenant_admin", "tenant_admin", null)]
    [InlineData("ejar", null, "", null)]
    [InlineData("ejar", "vendor", "", null)]
    [InlineData("ashare", "tenant_admin", "", null)]
    public void The_refusal_answers_only_on_this_stores_admin_host_without_the_admin_role(string hostSlug, string? hostRole, string held, string? expected)
    {
        var got = AdminHostSignIn.Refusal(On(hostSlug, hostRole), "ejar", held);
        output.WriteLine($"{hostSlug}{(hostRole is null ? "" : "-" + hostRole)} · يَحمِلُ «{held}» ⇒ {got ?? "—"}");
        Assert.Equal(expected, got);
    }

    [Fact]
    public void Path_mode_never_refuses()
    {
        Assert.Null(AdminHostSignIn.Refusal(default, "ejar", ""));
        Assert.Null(AdminHostSignIn.Refusal(new HostView(null, null, "http", ""), "ejar", "customer"));
    }

    [Theory]
    [InlineData("ejar", "tenant_admin", "", "https://ejar.example.test/")]
    [InlineData("ejar", "tenant_admin", ":5050", "https://ejar.example.test:5050/")]
    [InlineData("ejar", "vendor", "", "https://ejar.example.test/")]
    [InlineData("ejar", null, "", "/")]
    public void The_way_out_is_the_store_on_its_own_host(string hostSlug, string? hostRole, string port, string expected)
        => Assert.Equal(expected, AdminHostSignIn.StoreHome(On(hostSlug, hostRole, port), "ejar"));

    [Fact]
    public void The_way_out_in_path_mode_is_the_store_path()
        => Assert.Equal("/ejar", AdminHostSignIn.StoreHome(default, "ejar"));
}

[Collection(LiveProofCollection.Name)]
public class LiveAdminHostSignInTests(ITestOutputHelper output)
{
    private static bool Enabled => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>العُضوُ عَلى مُضيفِ الإدارَةِ بِدَعوى الإدارَةِ وبِدَعوى دَورِه: 302 إلى الرَفضِ عَلى المُضيفِ نَفسِه، وصِفرُ كَعكَةٍ لِلمَتجَر، ودَورُه المُخَزَّنُ كَما كان.
    /// والمُديرُ بَعدَه يَدخُلُ كَما ثَبَّتَه التَوصيف.</summary>
    [Fact]
    public async Task A_member_on_the_admin_host_is_refused_without_a_cookie_and_the_admin_still_signs_in_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var slug = NewSlug("w13ahs-");
        var (member, admin) = await PutStoreAsync(host, slug);
        try
        {
            var claimed = await SignInAsync(host, slug, member, "tenant_admin", "tenant_admin");
            var ownRole = await SignInAsync(host, slug, member, "tenant_admin", "customer");
            var asAdmin = await SignInAsync(host, slug, admin, "tenant_admin", "tenant_admin");
            await using var q = host.Store.QuerySession(slug);
            var stored = (await q.LoadAsync<User>(member.Id))!.ActiveRole;
            foreach (var (label, r) in new[] { ("العُضوُ بِدَعوى الإدارَة", claimed), ("العُضوُ بِدَعوى دَورِه", ownRole), ("المُدير", asAdmin) })
                output.WriteLine($"[live] {label} عَلى {slug}-admin ⇒ {r.Status} {r.Location} · كَعكات [{string.Join(", ", r.Cookies)}]");
            output.WriteLine($"[live] دَورُ العُضوِ المُخَزَّنُ بَعدَهُما: {stored}");

            Assert.Equal((302, "/login?err=not_store_admin"), (claimed.Status, claimed.Location));
            Assert.Empty(claimed.Cookies);
            Assert.Equal((302, "/login?err=not_store_admin"), (ownRole.Status, ownRole.Location));
            Assert.Empty(ownRole.Cookies);
            Assert.Equal("customer", stored);
            Assert.Equal((302, "/manage"), (asAdmin.Status, asAdmin.Location));
            Assert.Contains($"__Host-.acommerce.auth.{slug}.tenant_admin", asAdmin.Cookies);
        }
        finally
        {
            await using (var s = host.Store.LightweightSession(slug)) { s.Delete<User>(member.Id); s.Delete<User>(admin.Id); await s.SaveChangesAsync(); }
            await CleanAsync(host, [], slug);
        }
    }
}
