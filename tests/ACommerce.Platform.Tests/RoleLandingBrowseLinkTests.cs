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
}
