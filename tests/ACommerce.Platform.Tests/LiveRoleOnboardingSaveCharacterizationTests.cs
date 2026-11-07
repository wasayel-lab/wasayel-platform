using ACommerce.Kit.Auth;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.LiveStoreBirthOnceCharacterizationTests;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ «حِفظِ بَياناتِ الدَور» بِشَكلِ المَسارِ لِما يَنجو، قَبلَ أَن يُحرَسَ الدَورُ الفَعّالُ الغائِب (‏2026-11-07، مُراجَعَةُ C2b الجَولَةُ الأُولى · SEC-C2b-1) ═══
//
// **المَقيسُ قَبل** (عِندَ a9bc2cf): `POST /{slug}/me/role/onboarding/save` يَكتُبُ حُقولَ الاستِمارَةِ ثُمَّ يَبني التَحويلَ بِـ`active.Slug` حَيثُ `active` قَد يَكونُ
// `null` (عُضوٌ بِلا دَورٍ فَعّال، أَو دَورُه المُخَزَّنُ لَيسَ مِن أَدوارِ المَتجَر) — والمُتَرجِمُ يَقولُها: `MarketplaceTemplateExtensions.cs(825,39): warning CS8602`.
// فَيُجابُ 500 **بَعدَ** أَن تَقَعَ الكِتابَة. وعِندَ الأَساسِ 50d6293 كانَ ذلكَ الفَرعُ `302 /{slug}`.
//
// **وما يُثَبِّتُه هذا المِلَفُّ — وهُوَ ما يَجِبُ أَن يَنجوَ مِن الإصلاح، ولا يُعادُ حَرفٌ مِنه بَعدَه** (القاعِدَة ٣): بِشَكلِ المَسار (بِلا نِطاقٍ مُهَيَّإ) — عُضوٌ
// دَورُه الفَعّالُ مِن أَدوارِ المَتجَرِ يُحَوَّلُ إلى بَيتِ دَورِه (`/{slug}/me/listings` لِلبائِع، و`/{slug}` لِدَورٍ بَيتُه فارِغ)، والحَقلُ يُكتَبُ في
// `RoleAttributesJson`؛ وطَلَبٌ بِلا جَلسَةٍ إلى `/{slug}/login` بِلا كِتابَة. **وصَفُّ الدَورِ الغائِبِ لَيسَ هُنا**: جَوابُه اليَومَ استِثناءٌ لا يُخضَرُّ
// قَبلَ الإصلاح — فَيُثَبِّتُه كوميتُ الإصلاحِ بِجَوابِ الأَساسِ (`302 /{slug}`) في `LiveRoleOnboardingNoActiveRoleProofTests`.
// يَتَخَطّى نَفسَه ما لَم تُضبَط WASAYEL_LIVE_PROOF=1 وConnectionStrings__Postgres — عَقدُ إخوَتِه، وفي مَجموعَتِهِم التَسلسُلِيَّة.

[Collection(LiveProofCollection.Name)]
public class LiveRoleOnboardingSaveCharacterizationTests(ITestOutputHelper output)
{
    private static bool Enabled => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);
    private const string Door = "/{slug}/me/role/onboarding/save";

    /// <summary>مَتجَرٌ بِثَلاثَةِ أَدوار (كَـ<c>ejar</c>): مُشتَرٍ بَيتُه فارِغ، وبائِعٌ بَيتُه <c>/me/listings</c>، وشاحِنٌ بَيتُه <c>/explore</c>.</summary>
    internal static async Task PutStoreAsync(LiveTemplateDoorHost host, string slug)
    {
        await using var s = host.Store.LightweightSession();
        s.Store(new Tenant
        {
            Id = slug, Name = "حِفظُ بَياناتِ الدَور", BrandColor = "#0f766e",
            Roles =
            [
                RoleCatalog.InstantiateRole(RoleCatalog.Find("customer")!, 0),
                RoleCatalog.InstantiateRole(RoleCatalog.Find("vendor")!, 1),
                RoleCatalog.InstantiateRole(RoleCatalog.Find("shipper")!, 2),
            ],
        });
        await s.SaveChangesAsync();
    }

    internal static async Task<User> PutMemberAsync(LiveTemplateDoorHost host, string slug, string? activeRole)
    {
        var user = new User
        {
            Id = Guid.NewGuid(), TenantSlug = slug, FullName = "عُضوٌ يَحفَظ", ActiveRole = activeRole,
            Phone = "+9665" + Random.Shared.Next(10_000_000, 99_999_999), PhoneVerified = true,
        };
        await using var s = host.Store.LightweightSession(slug);
        s.Store(user);
        await s.SaveChangesAsync();
        return user;
    }

    internal static async Task<User?> MemberAsync(LiveTemplateDoorHost host, string slug, Guid id)
    {
        await using var q = host.Store.QuerySession(slug);
        return await q.LoadAsync<User>(id);
    }

    internal static Task<(int Status, string Location)> SaveAsync(LiveTemplateDoorHost host, string slug, User? who)
        => host.PostFormAsync(Door, new Dictionary<string, string> { ["slug"] = slug }, slug,
            who is null ? null : ACommerce.Kit.Auth.Server.AuthHandlers.MakeToken(who.Id, slug, who.SessionVersion),
            ("role_x_attr_y", "1"));

    internal static async Task DropAsync(LiveTemplateDoorHost host, string slug, params User[] people)
    {
        await using (var s = host.Store.LightweightSession(slug)) { foreach (var p in people) s.Delete<User>(p.Id); await s.SaveChangesAsync(); }
        await CleanAsync(host, [], slug);
    }

    [Fact]
    public async Task A_member_whose_active_role_is_in_the_store_lands_on_its_home_and_the_field_is_written_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var slug = NewSlug("w13ob-");
        await PutStoreAsync(host, slug);
        var vendor = await PutMemberAsync(host, slug, "vendor");
        var customer = await PutMemberAsync(host, slug, "customer");
        try
        {
            var asVendor = await SaveAsync(host, slug, vendor);
            var asCustomer = await SaveAsync(host, slug, customer);
            var written = (await MemberAsync(host, slug, vendor.Id))!.RoleAttributesJson;
            output.WriteLine($"[live] {Door} — البائِع ⇒ {asVendor.Status} {asVendor.Location} · المُشتَري ⇒ {asCustomer.Status} {asCustomer.Location}"
                + $" · المَكتوب: {string.Join(",", written.Select(kv => kv.Key + ":" + string.Join(";", kv.Value.Select(f => f.Key + "=" + f.Value))))}");
            Assert.Equal((302, $"/{slug}/me/listings"), asVendor);
            Assert.Equal((302, $"/{slug}"), asCustomer);
            Assert.Equal("1", written["x"]["y"]);
        }
        finally { await DropAsync(host, slug, vendor, customer); }
    }

    [Fact]
    public async Task A_request_without_a_session_goes_to_sign_in_and_writes_nothing_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var slug = NewSlug("w13ob0-");
        await PutStoreAsync(host, slug);
        var vendor = await PutMemberAsync(host, slug, "vendor");
        try
        {
            var anonymous = await SaveAsync(host, slug, who: null);
            var written = (await MemberAsync(host, slug, vendor.Id))!.RoleAttributesJson;
            output.WriteLine($"[live] {Door} بِلا جَلسَة ⇒ {anonymous.Status} {anonymous.Location} · حُقولٌ مَكتوبَة: {written.Count}");
            Assert.Equal((302, $"/{slug}/login"), anonymous);
            Assert.Empty(written);
        }
        finally { await DropAsync(host, slug, vendor); }
    }
}
