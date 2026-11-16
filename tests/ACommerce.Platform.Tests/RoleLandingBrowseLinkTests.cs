using System.Text.RegularExpressions;
using ACommerce.Platform.I18n;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ نَصُّ بابِ الزائِر — مِن القامُوسِ ولا يَعِدُ بِما لا يوجَد (‏ADR-035) ═══════════════
//
// الرابِطُ يُرسَمُ عَلى بَوّابَةِ **كُلِّ** مَتجَرٍ ذي أَدوار — `ashare` (سَكَنٌ مُشتَرَك)
// ومَتاجِرُ الاستوديو والمُجَرَّبَةُ مَعاً — فَنَصُّه يُسَمّي المَتجَرَ لا نَوعَ البِضاعَة:
// «المُنتَجات» كَذِبٌ عَلى مَتجَرِ سَكَن (القاعِدَتان ١٢ و١٦). و«دونَ تَسجيل» صادِقَةٌ
// بِالقِياسِ لا بِالظَنّ: الاستِكشافُ بِلا بَوّابَةِ دُخول — مُثَبَّتٌ في
// `RoleLandingVisitorDoorCharacterizationTests.Explore_has_no_login_gate_…`. والصِياغَةُ
// قَرارٌ تَحتَ التَفويضِ مُسَجَّلٌ في سِجِلِّ المالِك «أ» — يُبَدِّلُها سَطرٌ مِنه، ومَعَه هذا الفَحص.

public class RoleLandingBrowseLinkTests
{
    internal const string BrowseKey  = "roles.landing.browse";
    internal const string BrowseText = "تَصَفَّح المَتجَر دونَ تَسجيل";

    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    /// <summary>المِفتاحُ في المَعجَمِ العَرَبِيِّ بِالصِياغَةِ المُسَجَّلَة، لا قيمَةً نائِبَة،
    /// ولا ظِلَّ إنجليزِيّاً — كَأَخَوَيه في البَوّابَة.</summary>
    [Fact]
    public void The_browse_key_lives_in_the_arabic_lexicon_with_the_logged_wording()
    {
        Assert.Contains(BrowseKey, LocaleCatalog.Lexicon);
        Assert.Equal(BrowseText, LocaleCatalog.Find(LocaleCatalog.Arabic, BrowseKey));
        Assert.False(LocaleCatalog.IsPlaceholderKey(LocaleCatalog.Arabic, BrowseKey));

        foreach (var key in new[] { BrowseKey, "roles.landing.have_account", "roles.landing.pick_hint" })
            Assert.Null(LocaleCatalog.Find("en", key));
    }

    /// <summary>
    /// <para><b>النَصُّ لا يُكتَبُ في الواجِهَة</b>: لا <c>.razor</c> يَحمِلُه حَرفِيّاً، والمِفتاحُ
    /// يَقرَؤُه مِلَفٌّ واحِدٌ هُوَ <c>RoleLanding.razor</c> (القاعِدَة ١١). والسَطرُ الَّذي تَحتَه
    /// — «لَدَيكَ حِساب» — بِقيمَتِه لَم يَتَبَدَّل.</para>
    /// </summary>
    [Fact]
    public void The_text_is_read_from_the_dictionary_by_the_role_landing_alone()
    {
        var components = Path.Combine(RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "Components");
        var razors = Directory.GetFiles(components, "*.razor", SearchOption.AllDirectories);
        Assert.True(razors.Length > 50, $"أَداةٌ عَمياء: {razors.Length} مِلَفّاً فَقَط.");

        var readers = razors.Where(f => File.ReadAllText(f).Contains($"\"{BrowseKey}\"", StringComparison.Ordinal))
                            .Select(Path.GetFileName).ToArray();
        Assert.Equal(new[] { "RoleLanding.razor" }, readers);

        foreach (var f in razors)
            Assert.DoesNotContain(BrowseText, File.ReadAllText(f), StringComparison.Ordinal);

        Assert.Equal("لَدَيكَ حِساب بِدَور مُعَيَّن؟ سَجِّل الدُخول",
            LocaleCatalog.Find(LocaleCatalog.Arabic, "roles.landing.have_account"));
        Assert.Equal(1, Regex.Matches(RoleLandingVisitorDoorCharacterizationTests.RoleLanding(),
            Regex.Escape("roles.landing.have_account")).Count);
    }

    /// <summary>
    /// <para><b>لَقَطاتُ الأَساسِ الحَيَّةُ تَحمِلُ الرابِطَ كَما يُرسَم</b> (‏2026-10-11، مُراجَعَةُ
    /// <c>bce68ed</c>؛ ‏ADR-035 §٢‑٤ مُلحَق): التِقاطٌ حَيٌّ قَبلَ الرابِطِ وبَعدَه — ‏59 صَفحَةً عامَّة
    /// عَلى قاعِدَةٍ مَحَلِّيَّةٍ مُرمِيَّة، بِـ<c>curl</c> وَحدَه — اختَلَفَت فيه أَربَعٌ لا غَير، هي صَفَحاتُ
    /// البَوّابَة، وكُلٌّ بِسَطرَينِ (‏132 بايتاً) فَوقَ سَطرِ «لَدَيكَ حِساب». فَأُعيدَ تَثبيتُ الأَربَعِ في
    /// <c>i18n-baseline/</c> بِذلك الفَرقِ نَفسِه.</para>
    /// <para>والرابِطُ مِن جَذرِ المُستَأجِرِ حَتّى عَلى <c>/{slug}/r/{role}</c> — وسَطرُ الدُخولِ تَحتَه
    /// يَحمِلُ الدَور. واللَقطَةُ المُجَمَّدَةُ <c>baseline/</c> «ما كان» (يَقرَؤُها
    /// <c>ComponentVariantCharacterizationTests</c> تَوصيفاً لِما قَبلَ المُتَغايِرات) لا تَحمِلُه ولا تُمَسّ.</para>
    /// <para><b>قُلِبَ ‏2026-10-29 (قَرارُ المالِك د34، ‏ADR-048)</b> — وكانَ <c>The_live_baselines_of_the_role_gateway_carry_the_link_once_above_the_have_account_line</c>
    /// بِأَربَعِ لَقَطات: <c>ashare-portal</c> صارَت رَئيسِيَّةَ المُشتَري والبِطاقاتُ قِسمٌ تَحتَها بِلا الرابِط (زائِرُها يَتَصَفَّحُ الآن)،
    /// فَالحامِلاتُ ثَلاثٌ — صَفَحاتُ <c>/ashare/r/{role}</c> — وسَطرُ الدُخولِ في <c>ashare-portal</c> باقٍ مَرَّةً.</para>
    /// <para><b>وقُلِبَ ‏2026-10-30 (تَدقيقُ الموجَة ١١ CO5)</b> — وكانَ <c>…_and_the_store_root_below_the_goods_does_not</c>: <c>ashare-role-customer</c> صارَت
    /// رَئيسِيَّةَ المُشتَري والبِطاقاتُ تَحتَها بِلا الرابِط (زائِرُ تَطبيقِ المُشتَري يَتَصَفَّحُ الآن)، فَالحامِلَتانِ صَفحَتا <c>/ashare/r/host</c> و<c>/ashare/r/vendor</c>،
    /// وسَطرُ الدُخولِ باقٍ مَرَّةً في كُلٍّ مِن الصَفحَتَينِ اللَّتَينِ تَحتَ البِضاعَة.</para>
    /// </summary>
    [Fact]
    public void The_live_baselines_of_the_role_gateway_carry_the_link_once_above_the_have_account_line_and_the_pages_below_the_goods_do_not()
    {
        var appearance = Path.Combine(RepoRoot, "tests", "characterization", "appearance");
        var link = $"    <div class=\"acm-role-landing-foot\"><a href=\"/ashare/explore\">{BrowseText}</a></div>\n\n";

        foreach (var (page, login) in new[] { ("ashare-portal", "/ashare/login"), ("ashare-role-customer", "/ashare/r/customer/login") })
        {
            var below = File.ReadAllText(Path.Combine(appearance, "i18n-baseline", page + ".html"));
            Assert.Empty(Regex.Matches(below, Regex.Escape(BrowseText)));
            Assert.Single(Regex.Matches(below, Regex.Escape($"<div class=\"acm-role-landing-foot\"><a href=\"{login}\">")));
        }

        var gateway = new Dictionary<string, string>
        {
            ["ashare-role-host"]     = "/ashare/r/host/login",
            ["ashare-role-vendor"]   = "/ashare/r/vendor/login",
        };

        foreach (var (page, login) in gateway)
        {
            var html = File.ReadAllText(Path.Combine(appearance, "i18n-baseline", page + ".html"));
            Assert.Equal(1, Regex.Matches(html, Regex.Escape(link)).Count);
            Assert.Contains(link + $"    <div class=\"acm-role-landing-foot\"><a href=\"{login}\">", html, StringComparison.Ordinal);
        }

        var carriers = Directory.GetFiles(Path.Combine(appearance, "i18n-baseline"), "*.html");
        Assert.True(carriers.Length > 100, $"أَداةٌ عَمياء: {carriers.Length} لَقطَةً فَقَط.");
        Assert.Equal(
            gateway.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(),
            carriers.Where(f => File.ReadAllText(f).Contains(BrowseText, StringComparison.Ordinal))
                    .Select(f => Path.GetFileNameWithoutExtension(f)).OrderBy(k => k, StringComparer.Ordinal).ToArray());

        var frozen = Directory.GetFiles(Path.Combine(appearance, "baseline"), "*.html");
        Assert.True(frozen.Length >= 6, $"أَداةٌ عَمياء: {frozen.Length} لَقطَةً مُجَمَّدَةً فَقَط.");
        Assert.DoesNotContain(frozen, f => File.ReadAllText(f).Contains(BrowseText, StringComparison.Ordinal));
    }
}
