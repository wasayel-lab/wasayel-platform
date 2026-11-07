using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.LiveRoleOnboardingSaveCharacterizationTests;
using static ACommerce.Platform.Tests.LiveStoreBirthOnceCharacterizationTests;

namespace ACommerce.Platform.Tests;

// ═══ حِفظُ بَياناتِ الدَورِ بِلا دَورٍ فَعّالٍ يُجابُ بِجَذرِ المَتجَر — لا 500 بَعدَ الكِتابَة (‏2026-11-07، مُراجَعَةُ C2b الجَولَةُ الأُولى · SEC-C2b-1) ═══
//
// **المَقيسُ قَبل** (عِندَ a9bc2cf، بِشَكلِ المَسار): عُضوٌ جَديدٌ في مَتجَرٍ بِثَلاثَةِ أَدوار (لا دَورَ فَعّالَ بَعدَ أَوَّلِ دُخول) يُرسِلُ الاستِمارَة ⇒
// `NullReferenceException` عِندَ `MarketplaceTemplateExtensions.cs:824` (المُتَرجِمُ: CS8602 في `(825,39)`) — **بَعدَ** أَن كُتِبَ `RoleAttributesJson`.
// وكَذلكَ مَن دَورُه المُخَزَّنُ لَيسَ مِن أَدوارِ المَتجَر (دَورٌ أَزالَه المالِك). **والآن**: الدَورُ الغائِبُ يُجابُ بِجَوابِ الأَساسِ 50d6293 بِحَرفِه —
// `302 /{slug}` — والكِتابَةُ كَما كانَت؛ ومَن لَه دَورٌ فَعّالٌ يَبقى إلى بَيتِ دَورِه (الصَفُّ السالِب، وتَوصيفُه بِبَصمَتِه في
// `LiveRoleOnboardingSaveCharacterizationTests`). **والحارِسُ الثاني في البِناء**: `.editorconfig` القالِبِ يَجعَلُ CS8602 خَطَأً في هذا المِلَفّ.

[Collection(LiveProofCollection.Name)]
public class LiveRoleOnboardingNoActiveRoleProofTests(ITestOutputHelper output)
{
    private static bool Enabled => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>المُوجَب: لا دَورَ فَعّال (<c>null</c>)، أَو دَورٌ مُخَزَّنٌ لَيسَ مِن أَدوارِ المَتجَر ⇒ <c>302 /{slug}</c> والحَقلُ مَكتوب.</summary>
    [Fact]
    public async Task A_member_with_no_active_role_of_the_store_lands_on_the_store_root_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var slug = NewSlug("w13obn-");
        await PutStoreAsync(host, slug);
        var fresh = await PutMemberAsync(host, slug, activeRole: null);
        var orphaned = await PutMemberAsync(host, slug, activeRole: "broker");
        try
        {
            var asFresh = await SaveAsync(host, slug, fresh);
            var asOrphaned = await SaveAsync(host, slug, orphaned);
            var writtenFresh = (await MemberAsync(host, slug, fresh.Id))!.RoleAttributesJson;
            var writtenOrphaned = (await MemberAsync(host, slug, orphaned.Id))!.RoleAttributesJson;
            output.WriteLine($"[live] بِلا دَورٍ فَعّال ⇒ {asFresh.Status} {asFresh.Location} · دَورٌ لَيسَ في المَتجَر ⇒ {asOrphaned.Status} {asOrphaned.Location}");
            Assert.Equal((302, $"/{slug}"), asFresh);
            Assert.Equal((302, $"/{slug}"), asOrphaned);
            Assert.Equal("1", writtenFresh["x"]["y"]);
            Assert.Equal("1", writtenOrphaned["x"]["y"]);
        }
        finally { await DropAsync(host, slug, fresh, orphaned); }
    }

    /// <summary>السالِب: مَن دَورُه الفَعّالُ مِن أَدوارِ المَتجَرِ لا يُرَدُّ إلى الجَذرِ — بَيتُ دَورِه (<c>/me/listings</c> لِلبائِع، <c>/explore</c> لِلشاحِن).</summary>
    [Fact]
    public async Task A_member_with_an_active_role_of_the_store_is_not_sent_to_the_root_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var slug = NewSlug("w13oba-");
        await PutStoreAsync(host, slug);
        var vendor = await PutMemberAsync(host, slug, "vendor");
        var shipper = await PutMemberAsync(host, slug, "shipper");
        try
        {
            var asVendor = await SaveAsync(host, slug, vendor);
            var asShipper = await SaveAsync(host, slug, shipper);
            output.WriteLine($"[live] البائِع ⇒ {asVendor.Status} {asVendor.Location} · الشاحِن ⇒ {asShipper.Status} {asShipper.Location}");
            Assert.Equal((302, $"/{slug}/me/listings"), asVendor);
            Assert.Equal((302, $"/{slug}/explore"), asShipper);
        }
        finally { await DropAsync(host, slug, vendor, shipper); }
    }
}
