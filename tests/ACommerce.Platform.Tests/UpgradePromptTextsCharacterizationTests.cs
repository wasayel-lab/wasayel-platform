using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using UpgradePrompt = ACommerce.Templates.Customer.Marketplace.Components.UpgradePrompt;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ «اليَوم» — ما تَقولُه لافِتَةُ الحَدِّ لِكُلِّ سَبَبٍ وكُلِّ دَرَجَة، وثَلاثُ رَسائِلَ تَدُلُّ عَلى تَرقِيَةٍ لا تُباع (القاعِدَتانِ ١٢ و١٣) ═══
//
// **المَقيس** (‏2026-10-24، عِندَ `2939229`؛ بَعدَ `4e32559` الَّذي أَخفى السِعرَ والزِرَّ عَن دَرَجَةٍ لا تُمنَحُ ذاتِيّاً): راسِما المِفتاحِ
// مُكَوِّنٌ واحِد — `UpgradePrompt.razor` (‏`TitleFor`/`MessageFor`) — تَضَعُه صَفحَتانِ: `StudioHome.razor` و`StudioStudy.razor`. ومَعَ
// `SelfServiceCheckoutExists = false` لا `Offer` لِأَيِّ دَرَجَة، فَاللافِتَةُ عُنوانٌ ورِسالَةٌ لا غَير. والرِسالَةُ لِثَلاثَةِ أَسبابٍ
// («احصُل على … بِترقِيَة الباقَة.») تَدُلُّ عَلى فِعلٍ لا يَكتَمِل: لا دَرَجَةَ تُباعُ مِن أَيِّ شاشَة («ب» S11·١٨). والعُنوانُ
// «تَتَطَلَّب هذه الميزَة تَرقيَة» ورِسالَةُ التَصدير «… ميزَةٌ في الباقاتِ الأَعلى.» تَقولانِ شَرطاً لا تَدعوانِ إلى فِعل.
//
// **يُثَبَّتُ قَبلَ الصِياغَة** (القاعِدَة ٣) — مُصَيَّراً بِـ`HtmlRenderer` لِكُلِّ سَبَبٍ في `StudioUpgradeReason.All` ولِكُلِّ دَرَجَةٍ في
// الكاتالوج: (١) الرِسالَةُ المَرسومَةُ لِكُلِّ سَبَب — صُفوفُ الثَلاثَةِ تَنقَلِبُ بِأَسمائِها في كوميتِ الصِياغَة؛ (٢) بَصمَةُ اللافِتاتِ
// كُلِّها بِحَجبِ نَصِّ الرِسالَةِ وَحدَه — تَبقى بِلا تَعديل، فَالفَرقُ مَحصورٌ في المَفاتيحِ المَقصودَة (القاعِدَة ١٣)؛ (٣) العُنوانُ
// ورِسالَةُ التَصدير — يَبقَيانِ.

public class UpgradePromptTextsCharacterizationTests(ITestOutputHelper output)
{
    /// <summary>كُلُّ لافِتَةٍ لِكُلِّ (دَرَجَة، سَبَب) — مَرسومَةً بِالمُكَوِّنِ نَفسِه، بِلا قاعِدَةٍ ولا خادِم.</summary>
    internal static async Task<IReadOnlyList<(string Tier, string Reason, string Html)>> RenderAll()
    {
        await using var services = new ServiceCollection()
            .AddSingleton(new L(new Microsoft.AspNetCore.Http.HttpContextAccessor()))
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        var all = new List<(string, string, string)>();
        foreach (var tier in TierCatalog.All.Keys.OrderBy(t => t, StringComparer.Ordinal))
            foreach (var reason in StudioUpgradeReason.All)
            {
                var html = await renderer.Dispatcher.InvokeAsync(async () =>
                    (await renderer.RenderComponentAsync<UpgradePrompt>(ParameterView.FromDictionary(new Dictionary<string, object?>
                    {
                        [nameof(UpgradePrompt.Reason)] = reason,
                        [nameof(UpgradePrompt.CurrentTier)] = tier,
                    }))).ToHtmlString());
                all.Add((tier, reason, html));
            }
        return all;
    }

    private static readonly Regex MessageParagraph = new(@"(<p [^>]*>)(.*?)(</p>)", RegexOptions.Singleline);
    private static readonly Regex TitleStrong = new(@"<strong [^>]*>(.*?)</strong>", RegexOptions.Singleline);

    internal static string MessageOf(string html)
    {
        var m = MessageParagraph.Match(html);
        Assert.True(m.Success, "لا فِقرَةَ رِسالَةٍ في اللافِتَة — الأَداةُ عَمياء.");
        return WebUtility.HtmlDecode(m.Groups[2].Value).Trim();
    }

    internal static string TitleOf(string html) => WebUtility.HtmlDecode(TitleStrong.Match(html).Groups[1].Value).Trim();

    /// <summary>بَصمَةُ اللافِتاتِ كُلِّها بِحَجبِ نَصِّ فِقرَةِ الرِسالَةِ وَحدَه — مَقيسَةٌ عِندَ ‏2939229 (أَوَّلُ ‏16 مِن SHA-256).
    /// <para><b>‏(2026-10-29، قَرارُ المالِك د15 — أُعيدَ تَثبيتُها بِقَرارٍ مَرئِيّ)</b>: كانَت <see cref="BannersBeforeTheFeatureTitleFingerprint"/>،
    /// وصارَت هذِه لِأَنَّ عُنوانَ لافِتَةِ التَصديرِ تَبَدَّلَ وَحدَه («هذِه الميزَةُ غَيرُ مَشمولَةٍ في باقَتِك») — والبُرهانُ في الصَفِّ نَفسِه: إعادَةُ العُنوانِ
    /// القَديمِ إلى لافِتاتِ التَصديرِ وَحدَها تُعطي البَصمَةَ القَديمَةَ حَرفاً.</para></summary>
    internal const string BannersWithMessageMaskedFingerprint = "6f2f44c48d8a40c5";

    /// <summary>البَصمَةُ قَبلَ د15 (‏<c>b1c517f6d42a11eb</c>، عِندَ ‏2939229 حَتّى <c>4effde3</c>).</summary>
    internal const string BannersBeforeTheFeatureTitleFingerprint = "b1c517f6d42a11eb";

    internal static string MaskedFingerprint(IEnumerable<(string Tier, string Reason, string Html)> banners)
    {
        var joined = string.Join("\n§\n", banners.Select(b =>
            $"{b.Tier}/{b.Reason}\n" + MessageParagraph.Replace(b.Html, "$1«رِسالَة»$3", 1)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)))[..16].ToLowerInvariant();
    }

    /// <summary>‏(‏2026-11-07، مُراجَعَةُ C1 · W13-1·F1) البَصمَةُ بَعدَ أَن كَفَّت الأَسبابُ المُتَقاعِدَةُ عَن الرَسم — لافِتاتُها فارِغَةٌ وما سِواها بايتاً كَما كان
    /// (‏<c>UpgradePromptLiveReasonsCharacterizationTests</c> — لا يُمَسّ).</summary>
    internal const string BannersAfterTheRetiredReasonsFingerprint = "7e90c28f3a7aa121";

    /// <summary><b>قُلِبَ بِاسمِه (‏2026-11-07، مُراجَعَةُ C1 · W13-1·F1)</b> — كانَ <c>Every_banner_around_its_message_is_pinned_byte_for_byte</c>: بَصمَةُ
    /// <see cref="BannersWithMessageMaskedFingerprint"/> عَلى لافِتاتِ الأَسبابِ الأَربَعَة، وشَطرُ د15 يُعيدُ <see cref="BannersBeforeTheFeatureTitleFingerprint"/>
    /// بِعُنوانِ التَصديرِ القَديم. والآن analyze/refine/export لا تَرسُمُ شَيئاً (القَرار §٧ — ADR-049)، فَالبَصمَتانِ تاريخٌ: كانَتا عَلى لافِتاتٍ لا تُرسَمُ بَعدَ اليَوم،
    /// ولافِتَةُ <c>build</c> الباقِيَةُ مُثَبَّتَةٌ بايتاً هُناك.</summary>
    [Fact]
    public async Task Every_drawn_banner_around_its_message_is_pinned_and_a_retired_reason_draws_nothing()
    {
        var all = await RenderAll();
        output.WriteLine($"لافِتاتٌ مَرسومَة: {all.Count(b => b.Html.Length > 0)} / {all.Count} ({TierCatalog.All.Count} دَرَجاتٍ × {StudioUpgradeReason.All.Count} أَسباب)"
            + $" · البَصمَة {MaskedFingerprint(all)}");
        Assert.True(all.Count >= 16, $"أَداةٌ عَمياء: {all.Count} لافِتَةً فَقَط.");
        Assert.All(all.Where(b => StudioUpgradeReason.Retired.Contains(b.Reason)), b => Assert.Equal("", b.Html.Trim()));
        Assert.All(all.Where(b => !StudioUpgradeReason.Retired.Contains(b.Reason)), b => Assert.Contains("<strong", b.Html, StringComparison.Ordinal));
        Assert.All(all, b => Assert.DoesNotContain("href=\"/studio/billing\"", b.Html, StringComparison.Ordinal));
        Assert.Equal(BannersAfterTheRetiredReasonsFingerprint, MaskedFingerprint(all));
    }

    /// <summary>‏(د15، ‏2026-10-29) شَطرُ العُنوانِ مِن الصَفِّ القَديم — باقٍ بِحَرفِه عَلى المُعجَم: نَصّا العُنوانَينِ في القامُوس، والجَديدُ هُوَ عُنوانُ السَبَبِ المَجهول.</summary>
    [Fact]
    public async Task The_not_included_title_is_the_unknown_reasons_title_and_both_titles_stay_in_the_dictionary()
    {
        // ‏(2026-10-29، د15) الفَرقُ مَحصورٌ في عُنوانِ لافِتَةِ التَصدير: بِعُنوانِه القَديمِ تَعودُ البَصمَةُ القَديمَةُ حَرفاً.
        // (المُصَيِّرُ يُرَمِّزُ النَصَّ بِـ`HtmlEncoder.Default` — فَالعُنوانانِ يُقارَنانِ مُرَمَّزَين كَما في اللافِتَة.)
        // ‏(‏2026-11-07، F1) ولافِتَةُ التَصديرِ لا تُرسَمُ بَعدَ اليَوم، فَالعُنوانُ الجَديدُ يُقرَأُ مِن لافِتَةِ السَبَبِ المَجهولِ (الفَرعُ نَفسُه في `TitleFor`).
        var newTitle = System.Text.Encodings.Web.HtmlEncoder.Default.Encode(LocaleCatalog.Find(LocaleCatalog.Arabic, "upgrade.title.feature_not_included")!);
        var oldTitle = System.Text.Encodings.Web.HtmlEncoder.Default.Encode(LocaleCatalog.Find(LocaleCatalog.Arabic, "upgrade.title.feature")!);
        var unknown = await UpgradePromptLiveReasonsCharacterizationTests.RenderFor(UpgradePromptLiveReasonsCharacterizationTests.UnknownReason);
        Assert.True(unknown.Count >= 4, $"أَداةٌ عَمياء: {unknown.Count} لافِتَة.");
        Assert.All(unknown, b => Assert.Contains(newTitle, b.Html, StringComparison.Ordinal));
        Assert.All(unknown, b => Assert.DoesNotContain(oldTitle, b.Html, StringComparison.Ordinal));
    }

    /// <summary>(‏2026-10-24، كوميتُ الصِياغَة — انقَلَبَ بِقَرارٍ مَرئِيّ: كانَ <c>Today_the_quota_message_directs_to_a_plan_upgrade_nothing_sells</c>
    /// يُثبِتُ الرَسائِلَ الثَلاثَ القَديمَة — «احصُل على تَحاليل أَكثَر بِترقِيَة الباقَة.» و«احصُل على تَحسينات أَكثَر بِترقِيَة الباقَة.»
    /// و«احصُل على إنشاء تَطبيقات إضافيَّة بِترقِيَة الباقَة.» — وصارَ يُثبِتُ ما يَقولُ الحَدَّ ولا يَدُلُّ عَلى تَرقِيَة. وتَعودُ القَديمَةُ
    /// بِحَرفِها يَومَ تُباعُ دَرَجَة (أ‑٤، و<c>SelfServiceCheckoutExists = true</c>).)</summary>
    /// <para>‏(‏2026-11-07، مُراجَعَةُ C1 · W13-1·F1 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) خَرَجَ صَفّا analyze وrefine: لا لافِتَةَ لَهُما (القَرار §٧)، ونَصّاهُما في القامُوسِ
    /// يَتيمانِ مُثَبَّتانِ في <c>TierOrphanFieldsTests</c>؛ والسالِبُ «لا تُرسَم» في <c>UpgradePromptRetiredReasonsTests</c>.</para>
    [Theory]
    [InlineData(StudioUpgradeReason.Stores,   "عَدَدُ التَطبيقاتِ في باقَتِكَ مَحدود.")]
    public async Task The_quota_message_states_the_limit_and_directs_to_no_upgrade(string reason, string message)
    {
        Assert.False(StudioTierPurchase.SelfServiceCheckoutExists);
        var drawn = (await RenderAll()).Where(b => b.Reason == reason).ToList();
        Assert.True(drawn.Count >= 4, $"أَداةٌ عَمياء: {drawn.Count} لافِتَةً لِـ«{reason}».");
        Assert.All(drawn, b => Assert.Equal(message, MessageOf(b.Html)));
        Assert.All(drawn, b => Assert.DoesNotContain("تَرقِيَة", MessageOf(b.Html).Replace("ترقِيَة", "تَرقِيَة"), StringComparison.Ordinal));
    }

    /// <summary><b>قُلِبَ بِاسمِه (‏2026-10-29، قَرارُ المالِك د15 (٢)(٣))</b> — كانَ <c>The_feature_title_and_the_export_message_state_a_requirement_not_an_action</c>:
    /// «تَتَطَلَّب هذه الميزَة تَرقيَة» و«تَصديرُ الدِراسَة ميزَةٌ في الباقاتِ الأَعلى.». والآن، ولا دَرَجَةَ تُشتَرى، «هذِه الميزَةُ غَيرُ مَشمولَةٍ في باقَتِك»
    /// و«تَصديرُ الدِراسَةِ غَيرُ مَشمولٍ في باقَتِك.» (وتُتبَعُ بِجُملَةِ التَواصُلِ حينَ يُملأُ البَريد) — والقَديمانِ باقِيانِ في القامُوسِ بِحَرفِهِما لِيَومِ البَيع.</summary>
    /// <para><b>وقُلِبَ ثانِيَةً بِاسمِه (‏2026-11-07، مُراجَعَةُ C1 · W13-1·F1)</b> — كانَ <c>The_feature_title_and_the_export_message_say_not_included_while_no_tier_sells</c>
    /// يَقرَأُ عُنوانَ لافِتَةِ التَصديرِ ورِسالَتَها. والتَصديرُ صارَ بِلا شَرطِ باقَة (‏6420199)، فَـ«غَيرُ مَشمولٍ في باقَتِك» كاذِبَة، ولافِتَتُه لا تُرسَم؛ والعُنوانُ
    /// «غَيرُ مَشمولَة» عُنوانُ السَبَبِ المَجهولِ وَحدَه. والنُصوصُ الأَربَعَةُ في القامُوسِ يَتيمَةٌ إلى ADR التَسعير.</para>
    [Fact]
    public async Task The_feature_title_says_not_included_while_no_tier_sells_and_the_export_reason_draws_nothing()
    {
        Assert.False(StudioTierPurchase.SelfServiceCheckoutExists);
        var export = (await RenderAll()).Where(b => b.Reason == StudioUpgradeReason.Export).ToList();
        Assert.True(export.Count >= 4, $"أَداةٌ عَمياء: {export.Count} لافِتَةَ تَصدير.");
        Assert.All(export, b => Assert.Equal("", b.Html.Trim()));
        var unknown = await UpgradePromptLiveReasonsCharacterizationTests.RenderFor(UpgradePromptLiveReasonsCharacterizationTests.UnknownReason);
        Assert.All(unknown, b => Assert.Equal("هذِه الميزَةُ غَيرُ مَشمولَةٍ في باقَتِك", TitleOf(b.Html)));
        Assert.Equal("تَتَطَلَّب هذه الميزَة تَرقيَة", LocaleCatalog.Find(LocaleCatalog.Arabic, "upgrade.title.feature"));
        Assert.Equal("تَصديرُ الدِراسَة ميزَةٌ في الباقاتِ الأَعلى.", LocaleCatalog.Find(LocaleCatalog.Arabic, "upgrade.msg.export"));
        Assert.Equal("تَصديرُ الدِراسَةِ غَيرُ مَشمولٍ في باقَتِك.", LocaleCatalog.Find(LocaleCatalog.Arabic, "upgrade.msg.export_not_included"));
    }
}
