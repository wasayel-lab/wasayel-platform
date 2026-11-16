using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.LiveStoreBirthOnceCharacterizationTests;

namespace ACommerce.Platform.Tests;

// ═══ حَدّا الاستِمارَةِ في الخادِم — الاسم 60 والسَطر 120 (‏2026-11-07، مُراجَعَةُ عَدَساتِ C1 · W13-1·SEC-C1-3) ═══
//
// **المَقيسُ قَبل** (تَقريرُ العَدَسَة): `maxlength` في المُتَصَفِّحِ وَحدَه — اسمٌ بِـ200,000 حَرفٍ وَلَدَ مَتجَراً صَفحَتُه العامَّةُ 20,449,093 بايتاً، ورَفضُ
// `slug_taken` بِالاسمِ نَفسِه أَجابَ 302 بِلا `Location`. **والآن** الحَدّانِ رَمزا رَفضٍ في المُعجَمِ المُغلَق، والرَفضُ لا يَنسَخُ ما فاتَ حَدَّه.

[Collection(LiveProofCollection.Name)]
public class LiveStoreFieldLengthProofTests(ITestOutputHelper output)
{
    private static bool Enabled => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private static Task<(int Status, string Location)> CreateAsync(LiveTemplateDoorHost host, Probe p, string slug, string name, string tagline)
        => host.PostStudioFormAsync(StudioStoreCreate.Endpoint, new Dictionary<string, string>(), p.Token,
            [("slug", slug), ("name", name), ("color", StudioStoreCreate.DefaultColor), ("tagline", tagline)]);

    [Fact]
    public void Each_length_code_has_its_arabic_message()
    {
        foreach (var code in new[] { StudioStoreCreate.NameTooLong, StudioStoreCreate.TagLineTooLong })
        {
            Assert.Contains(code, StudioStoreCreate.Refusals);
            Assert.False(string.IsNullOrWhiteSpace(ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, StudioStoreCreate.MessageKey(code))));
        }
    }

    [Fact]
    public async Task A_name_or_tagline_past_its_limit_is_refused_and_writes_nothing_and_at_the_limit_is_born_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var a = await UserAsync(host);
        var (s1, s2, s3) = (NewSlug("w13n-"), NewSlug("w13n-"), NewSlug("w13n-"));
        try
        {
            var longName = await CreateAsync(host, a, s1, new string('ب', StudioStoreCreate.NameMax + 1), "");
            var longTag = await CreateAsync(host, a, s2, "مَتجَر", new string('س', StudioStoreCreate.TagLineMax + 1));
            var atLimit = await CreateAsync(host, a, s3, new string('ب', StudioStoreCreate.NameMax), new string('س', StudioStoreCreate.TagLineMax));
            output.WriteLine($"[live] اسمٌ {StudioStoreCreate.NameMax + 1} ⇒ {longName.Location[..Math.Min(60, longName.Location.Length)]}… · سَطرٌ {StudioStoreCreate.TagLineMax + 1} ⇒ {longTag.Location[..Math.Min(60, longTag.Location.Length)]}… · عَلى الحَدّ ⇒ {atLimit.Location}");
            Assert.StartsWith($"/studio/new?err={StudioStoreCreate.NameTooLong}&slug={s1}&name=&", longName.Location, StringComparison.Ordinal);
            Assert.StartsWith($"/studio/new?err={StudioStoreCreate.TagLineTooLong}&slug={s2}&", longTag.Location, StringComparison.Ordinal);
            Assert.EndsWith("&tagline=", longTag.Location, StringComparison.Ordinal);
            Assert.Equal((302, $"/studio/apps/{s3}?built=1"), atLimit);
            Assert.Null(await TenantAsync(host, s1));
            Assert.Null(await TenantAsync(host, s2));
            Assert.Equal(1, await StoresBuiltAsync(host, a));
        }
        finally { await CleanAsync(host, [a], s1, s2, s3); }
    }
}
