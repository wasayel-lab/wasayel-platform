using System.Security.Cryptography;
using System.Text;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ ما يَبقى مِن نافِذَةِ التَرقِيَةِ قَبلَ أَن تَكُفَّ عَن الأَسبابِ المُتَقاعِدَة (‏2026-11-07، الموجَة ١٣ — مُراجَعَةُ C1 · W13-1·F1) ═══
//
// **المَقيسُ قَبل** (عِندَ `71ca286`، إقلاعٌ بارِدٌ عَلى Development): `GET /studio?upgrade=analyze` يَرسُمُ «بَلَغتَ حَدّ تَحاليل هذا الشَّهر»، و`?upgrade=refine`
// «بَلَغتَ حَدّ التَّحسينات»، و`?upgrade=export` «تَصديرُ الدِراسَةِ غَيرُ مَشمولٍ في باقَتِك» — والأَخيرَةُ كاذِبَةٌ مُنذُ `6420199` (التَصديرُ بِلا شَرطِ باقَة)،
// ولا بَوّابَةَ تَكتُبُ الرُموزَ الثَلاثَة بَعدَ اليَوم؛ فَلا تُبلَغُ إلّا بِرابِطٍ قَديمٍ أَو مَصنوع، والصَفَحاتُ الثَلاثُ (`/studio`، `/studio/new`، `/studio/s/{id}`)
// تُمَرِّرُ `?upgrade=` إلى المُكَوِّنِ نَفسِه. والقَرارُ §٧ سَمّى «أَسبابَ التَرقِيَة» بَينَ ما يَكُفُّ عَن العَرض.
//
// **وما يُثَبِّتُه هذا المِلَفُّ — ما يَجِبُ أَن يَنجوَ مِن التَبديل، ولا يُعادُ حَرفٌ مِنه بَعدَه** (القاعِدَة ٣): لافِتَةُ حَدِّ المَتاجِرِ (`build` — البَوّابَةُ
// الوَحيدَةُ الحَيَّةُ، يَكتُبُها بابُ الإنشاء) لِكُلِّ دَرَجَةٍ بايتاً، ولافِتَةُ السَبَبِ المَجهولِ (فَرعُ الافتِراضِ «هذِه الميزَةُ غَيرُ مَشمولَة») بايتاً، والسَبَبُ
// الغائِبُ لا يَرسُمُ شَيئاً. والبَصمَةُ أَوَّلُ ‏16 مِن SHA-256 عَلى اللافِتاتِ مَربوطَةً بِدَرَجَتِها — مَقيسَةٌ عِندَ `71ca286` بِالمُصَيِّرِ نَفسِه
// (`UpgradePromptTextsCharacterizationTests`'s `RenderAll` — `HtmlRenderer` بِلا قاعِدَةٍ ولا خادِم).

public class UpgradePromptLiveReasonsCharacterizationTests(ITestOutputHelper output)
{
    /// <summary>سَبَبٌ لا يَعرِفُه المُعجَم — ما يَلتَقِطُه صَفُّ <c>studio-upgrade-feature</c>.</summary>
    internal const string UnknownReason = "feature";

    internal static async Task<IReadOnlyList<(string Tier, string Html)>> RenderFor(string? reason)
    {
        await using var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
            .AddSingleton(new ACommerce.Platform.I18n.L(new Microsoft.AspNetCore.Http.HttpContextAccessor()))
            .BuildServiceProvider();
        await using var renderer = new Microsoft.AspNetCore.Components.Web.HtmlRenderer(services, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        var all = new List<(string, string)>();
        foreach (var tier in TierCatalog.All.Keys.OrderBy(t => t, StringComparer.Ordinal))
        {
            var html = await renderer.Dispatcher.InvokeAsync(async () =>
                (await renderer.RenderComponentAsync<ACommerce.Templates.Customer.Marketplace.Components.UpgradePrompt>(
                    Microsoft.AspNetCore.Components.ParameterView.FromDictionary(new Dictionary<string, object?>
                    {
                        ["Reason"] = reason,
                        ["CurrentTier"] = tier,
                    }))).ToHtmlString());
            all.Add((tier, html));
        }
        return all;
    }

    internal static string Fingerprint(IEnumerable<(string Tier, string Html)> banners)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n§\n", banners.Select(b => $"{b.Tier}\n{b.Html}")))))[..16].ToLowerInvariant();

    /// <summary>لافِتاتُ حَدِّ المَتاجِرِ لِكُلِّ دَرَجَة — مَقيسَةٌ عِندَ `71ca286`.</summary>
    internal const string StoresBannersFingerprint = "2e76bd8db83ab328";

    /// <summary>لافِتاتُ السَبَبِ المَجهولِ لِكُلِّ دَرَجَة — مَقيسَةٌ عِندَ `71ca286`.</summary>
    internal const string UnknownReasonBannersFingerprint = "4df4db1d3df9b9b3";

    [Fact]
    public async Task The_store_limit_banner_of_every_tier_is_pinned_byte_for_byte()
    {
        var banners = await RenderFor(StudioUpgradeReason.Stores);
        output.WriteLine($"لافِتاتُ «{StudioUpgradeReason.Stores}»: {banners.Count} · {string.Join(" · ", banners.Select(b => $"{b.Tier}={b.Html.Length}"))} · البَصمَة {Fingerprint(banners)}");
        Assert.True(banners.Count >= 4 && banners.All(b => b.Html.Contains("<strong", StringComparison.Ordinal)), "أَداةٌ عَمياء: لافِتَةٌ بِلا عُنوان.");
        Assert.Equal(StoresBannersFingerprint, Fingerprint(banners));
    }

    [Fact]
    public async Task The_unknown_reason_banner_of_every_tier_is_pinned_byte_for_byte()
    {
        var banners = await RenderFor(UnknownReason);
        output.WriteLine($"لافِتاتُ «{UnknownReason}»: {banners.Count} · {string.Join(" · ", banners.Select(b => $"{b.Tier}={b.Html.Length}"))} · البَصمَة {Fingerprint(banners)}");
        Assert.True(banners.Count >= 4 && banners.All(b => b.Html.Contains("<strong", StringComparison.Ordinal)), "أَداةٌ عَمياء: لافِتَةٌ بِلا عُنوان.");
        Assert.Equal(UnknownReasonBannersFingerprint, Fingerprint(banners));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task No_reason_draws_nothing(string? reason)
        => Assert.All(await RenderFor(reason), b => Assert.Equal("", b.Html.Trim()));
}
