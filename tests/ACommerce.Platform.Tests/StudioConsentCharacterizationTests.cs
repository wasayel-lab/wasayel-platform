using System.Text.RegularExpressions;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ مُوافَقَةِ الاستوديو — قَبلَ تَبديلِ نَصِّها ورَفعِ إصدارِها ═══════════
//
// **المَقيسُ** (موجَزُ الفَجَوات P2-1، ‏2026-10-10): صَفحَةُ `/studio/consent` «تَظهَر
// قَبل أَوَّل تَحليل … لا يُمكِن المُتابَعَة بِدونها»، وتَرسُمُ خَمسَةَ بُنودٍ مِن القامُوس
// وتَكتُبُ `ConsentRecord` بِـ`ConsentPolicy.CurrentVersion`. وثَلاثَةُ مَواضِعَ تَقرَأُ
// الإصدارَ أَو تَكتُبُه: بابُ الصَفحَةِ (`StudioQueries.HasConsentedAsync`)، ونُقطَةُ
// القَبولِ (`POST /studio/consent/accept` تَكتُبُ السِجِلّ)، ونُقطَةُ استِئنافِ
// المُطالَبَة (`ResumeStudioPromptAsync` تَفحَصُ قَبلَ أَوَّلِ تَحليل). وكُلُّها تُسَمّي
// الثابِتَ لا رَقَماً — مَقيساً: صِفرُ `Version == <رقم>` في المِلَفّاتِ الأَربَعَة.
//
// **ولَقطَةُ أَساسِ `studio-consent.html`** تَحمِلُ البُنودَ الخَمسَةَ بِحَرفِ القامُوسِ
// وسَطرَ «إصدار 1» — فَهِيَ طَرَفُ مُقارَنَةٍ بايتِيٍّ لِلنَصِّ الَّذي سَيَتَبَدَّل. والمَوجَزُ
// يَشتَرِطُ أَن يُحَدَّثَ خَطُّ الأَساسِ **مُعلَناً لا صامِتاً**؛ فَالفَحصُ هُنا يَربِطُه
// بِالقامُوسِ والثابِتِ مَعاً: مَن بَدَّلَ النَصَّ أَو الإصدارَ ولَم يُبَدِّل اللَقطَةَ يَحمَرّ،
// ومَن بَدَّلَ اللَقطَةَ وَحدَها يَحمَرّ.
//
// **ما يُثَبِّتُه هذا المِلَفُّ يَبقى أَخضَرَ بِلا تَعديلِ حَرفٍ بَعدَ الكوميتِ التالي
// (القاعِدَة ٣)**: المَواضِعُ الثَلاثَةُ وثابِتُها، وغِيابُ الرَقَمِ الحَرفِيّ، وشَكلُ السِجِلّ،
// والرَبطُ بَينَ اللَقطَةِ والقامُوسِ والثابِت. وأَمّا نَصُّ البَندِ الرابِعِ وقيمَةُ الإصدارِ
// وشَرطُ القَبولِ فَفي `StudioConsentPolicyTests` — مِلَفٌّ مُستَقِلّ، لِأَنَّها ما يَتَبَدَّل.

public class StudioConsentCharacterizationTests
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    internal static string Source(params string[] parts)
    {
        var path = Path.Combine(RepoRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياءُ بِلا طَرَفٍ مَقروء.");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 300, $"أَداةٌ عَمياء: {path} طولُه {text.Length} مِحرَفاً — لَم يُقرَأ.");
        return text;
    }

    private const string Template = "ACommerce.Templates.Customer.Marketplace";

    internal static string ConsentPage() => Source("libs", "templates", Template, "Components", "Pages", "StudioConsent.razor");
    internal static string Queries()     => Source("libs", "templates", Template, "Services", "Queries", "StudioQueries.cs");
    internal static string Endpoints()   => Source("libs", "templates", Template, "MarketplaceTemplateExtensions.cs");
    internal static string Policy()      => Source("libs", "templates", Template, "Services", "Incubator", "ConsentRecord.cs");
    internal static string Baseline()    => Source("tests", "characterization", "appearance", "i18n-baseline", "studio-consent.html");

    private static string Ar(string key)
    {
        var v = LocaleCatalog.Find(LocaleCatalog.Arabic, key);
        Assert.False(string.IsNullOrWhiteSpace(v), $"«{key}» بِلا قيمَةٍ عَرَبِيَّة.");
        return v!;
    }

    // ─── ثَلاثَةُ مَواضِعَ وثابِتٌ واحِد ─────────────────────────────────────

    /// <summary>
    /// <para><b>الكاتِبُ والقارِئانِ يُسَمّونَ <c>ConsentPolicy.CurrentVersion</c></b>:
    /// نُقطَةُ القَبولِ تَكتُبُه في السِجِلّ، والصَفحَةُ تَعرِضُه وتَسأَلُ بِه، ونُقطَةُ
    /// الاستِئنافِ تَفحَصُ بِه قَبلَ أَوَّلِ تَحليل. فَرَفعُ الثابِتِ يَبلُغُ الثَلاثَةَ
    /// مَعاً بِلا تَعديلِ مَوضِع.</para>
    /// </summary>
    [Fact]
    public void The_writer_and_both_readers_name_the_single_policy_constant()
    {
        var endpoints = Endpoints();
        Assert.Contains("Version = Services.Incubator.ConsentPolicy.CurrentVersion,", endpoints, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(endpoints, Regex.Escape("ConsentPolicy.CurrentVersion")).Count);

        var page = ConsentPage();
        Assert.Contains("@ConsentPolicy.CurrentVersion", page, StringComparison.Ordinal);
        Assert.Contains("Studio.HasConsentedAsync(", page, StringComparison.Ordinal);
        Assert.Contains("Auth.UserId!.Value, ConsentPolicy.CurrentVersion);", page, StringComparison.Ordinal);

        // والاستِعلامُ يَأخُذُ الإصدارَ وَسيطاً لا يَعرِفُ الثابِتَ بِنَفسِه.
        Assert.Contains("Task<bool> HasConsentedAsync(", Queries(), StringComparison.Ordinal);
        Assert.Contains("Guid userId, int version", Queries(), StringComparison.Ordinal);
    }

    /// <summary><b>ولا رَقَمَ إصدارٍ حَرفِيّاً خارِجَ السِياسَة</b> — مَقيساً عَلى
    /// المِلَفّاتِ الأَربَعَة: الثابِتُ يُعَرَّفُ في <c>ConsentRecord.cs</c> وَحدَه.</summary>
    [Fact]
    public void No_consent_version_literal_lives_outside_the_policy()
    {
        var literal = new Regex(@"\bVersion\s*==?\s*[0-9]", RegexOptions.Compiled);
        foreach (var (name, text) in new[]
        {
            ("StudioQueries.cs", Queries()),
            ("StudioConsent.razor", ConsentPage()),
            ("MarketplaceTemplateExtensions.cs", Endpoints()),
        })
            Assert.False(literal.IsMatch(text), $"{name} يُقارِنُ أَو يَكتُبُ إصداراً رَقَماً حَرفِيّاً.");

        var policy = Policy();
        Assert.Matches(@"public const int CurrentVersion = [0-9]+;", policy);
        Assert.Equal(1, Regex.Matches(policy, "const int CurrentVersion").Count);
    }

    // ─── شَكلُ السِجِلّ ──────────────────────────────────────────────────────

    /// <summary><b>السِجِلُّ لِكُلِّ (مُستَخدِم، إصدار)</b>: مُعَرِّفٌ ومُستَخدِمٌ وإصدارٌ
    /// صَحيحٌ ووَقتٌ وأَثَرُ طَلَب — فَإصدارٌ جَديدٌ بِلا سِجِلٍّ حَتّى يُقبَل.</summary>
    [Fact]
    public void A_consent_record_is_one_row_per_user_and_version()
    {
        var props = typeof(ConsentRecord).GetProperties().Select(p => (p.Name, p.PropertyType)).ToHashSet();
        Assert.Contains((nameof(ConsentRecord.UserId), typeof(Guid)), props);
        Assert.Contains((nameof(ConsentRecord.Version), typeof(int)), props);
        Assert.Contains((nameof(ConsentRecord.At), typeof(DateTime)), props);
        Assert.Contains((nameof(ConsentRecord.Ip), typeof(string)), props);
        Assert.Contains((nameof(ConsentRecord.UserAgent), typeof(string)), props);
        Assert.True(ConsentPolicy.CurrentVersion >= 1);
    }

    // ─── الصَفحَةُ تَرسُمُ البُنودَ مِن القامُوس ─────────────────────────────

    /// <summary><b>البُنودُ الخَمسَةُ تُرسَمُ مِن القامُوسِ عُنواناً وجِسماً</b>
    /// (القاعِدَة ١١) — فَتَبديلُ البَندِ الرابِعِ سَطرٌ في <c>ar.json</c> لا في الصَفحَة.</summary>
    [Fact]
    public void The_five_items_are_rendered_from_the_dictionary()
    {
        var page = ConsentPage();
        for (var i = 1; i <= 5; i++)
        {
            Assert.Contains($"@L.Markup(\"studio.consent.item{i}_title\")", page, StringComparison.Ordinal);
            Assert.Contains($"@L.Markup(\"studio.consent.item{i}_body\")", page, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("لا يَتِم تَحصيل", page, StringComparison.Ordinal);
    }

    // ─── اللَقطَةُ مَربوطَةٌ بِالقامُوسِ والثابِت ─────────────────────────────

    /// <summary>
    /// <para><b>لَقطَةُ الأَساسِ تَحمِلُ البُنودَ بِحَرفِ القامُوسِ وسَطرَ الإصدارِ
    /// بِقيمَةِ الثابِت.</b> هذا هُوَ ما يَجعَلُ تَحديثَ اللَقطَةِ مُعلَناً: نَصٌّ يَتَبَدَّلُ
    /// في القامُوسِ أَو إصدارٌ يُرفَعُ ولَم تَلحَقْه اللَقطَةُ يَحمَرُّ هُنا — واللَقطَةُ لا
    /// تُبَدَّلُ وَحدَها. والعَدُّ يُطبَع: خَمسَةُ بُنودٍ وسَطرٌ.</para>
    /// </summary>
    [Fact]
    public void The_i18n_baseline_carries_the_live_consent_items_and_the_live_version()
    {
        var baseline = Baseline();
        Assert.True(baseline.Length > 5000, "أَداةٌ عَمياء: لَقطَةُ الأَساسِ لَم تُقرَأ.");

        var checked_ = 0;
        for (var i = 1; i <= 5; i++)
        {
            var expected = $"<p><strong>{Ar($"studio.consent.item{i}_title")}</strong> {Ar($"studio.consent.item{i}_body")}</p>";
            Assert.True(baseline.Contains(expected, StringComparison.Ordinal),
                $"البَندُ {i} في اللَقطَةِ لا يُطابِقُ القامُوسَ حَرفاً — حَدِّث اللَقطَةَ مُعلَناً أَو أَعِد النَصّ.");
            checked_++;
        }

        var versionLine = $"<p class=\"auth-sub\">{Ar("studio.consent.version")} {ConsentPolicy.CurrentVersion}</p>";
        Assert.True(baseline.Contains(versionLine, StringComparison.Ordinal),
            $"سَطرُ الإصدارِ في اللَقطَةِ لا يُطابِقُ `ConsentPolicy.CurrentVersion` = {ConsentPolicy.CurrentVersion}.");
        checked_++;

        Assert.Equal(6, checked_);
    }
}
