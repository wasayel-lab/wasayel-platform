using System.Text.RegularExpressions;
using ACommerce.Platform.I18n;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ ما يَصِلُ مُزَوِّدَ رَسائِلِ الدُخولِ وبَريدِه غَيرَ الرَمز — تَوصيفٌ قَبلَ التَبديل (‏2026-11-04، الموجَة ١٢ G1 · ٩؛ H-SEC-W12-2) ══════════════════════
//
// **ما يُثَبَّتُ هُنا لِيَبقى** (القاعِدَة ٣): سَطرُ `platform.privacy.share_otp` بِالعَرَبِيَّةِ والإنكليزِيَّةِ يُسَمّي المُزَوِّدَ بِوَسيطِه (`{0}` — يُقرَأُ مِن الإعدادِ الَّذي يَختارُه، د20)
// ويَقولُ إنّه يَستَلِمُ الوَسيلَةَ مَعَ الرَمز؛ ونِداءاتُ `SendNoticeAsync` في الكودِ الحَيِّ تُعَدُّ ولا تَكونُ صِفراً (الأَداةُ مَقيسَة).
// **وما يُطبَعُ ولا يُثَبَّت**: عَدَدُ النِداءاتِ ومَوضِعُها، وأَنّ السَطرَ اليَومَ لا يَذكُرُ إشعاراتِ أَمانِ الحِسابِ ولا بَريدَ صاحِبِ المَتجَرِ بِالطَلَباتِ والمال — وكُتِبَ (`88e0f3a`) قَبلَ
// مُرسِلَيهِما (`3ea4b80`، `c6e84ae`). وهذا يَتَغَيَّرُ في كوميتِ التَبديل؛ والمِلَفُّ يَبقى بِبَصمَتِه.

public class PrivacyShareOtpNoticeTests(ITestOutputHelper output)
{
    private static readonly Regex NoticeCall = new(@"\.SendNoticeAsync\(", RegexOptions.Compiled);

    /// <summary>مَواضِعُ نِداءِ <c>SendNoticeAsync</c> في كودِ <c>libs/</c> و<c>apps/</c> الحَيِّ (بِلا تَعليقات) — لا تَعريفُ الواجِهَةِ ولا تَنفيذُ القَنَوات.</summary>
    internal static IReadOnlyList<string> NoticeCallers()
        => EntitlementContractTests.SourceFiles()
            .Where(f => f.File.EndsWith(".cs", StringComparison.Ordinal))
            .SelectMany(f => NoticeCall.Matches(WriteEndpointGuardTests.StripComments(f.Text)).Select(_ => Path.GetFileName(f.File)))
            .OrderBy(n => n, StringComparer.Ordinal).ToList();

    [Fact]
    public void The_sign_in_provider_line_names_the_provider_and_the_contact_and_the_notice_callers_are_counted()
    {
        var callers = NoticeCallers();
        var ar = LocaleCatalog.Find(LocaleCatalog.Arabic, "platform.privacy.share_otp")!;
        var en = LocaleCatalog.Find("en", "platform.privacy.share_otp")!;
        output.WriteLine($"نِداءاتُ SendNoticeAsync: {callers.Count} [{string.Join(", ", callers)}] · السَطرُ يَذكُرُ الإشعارات: ar {ar.Contains("إشعار")} · en {en.Contains("notice", StringComparison.OrdinalIgnoreCase)}");
        output.WriteLine($"ar: {ar}");
        output.WriteLine($"en: {en}");
        Assert.True(callers.Count > 0, "أَداةٌ عَمياء: صِفرُ نِداء.");
        Assert.Contains("{0}", ar, StringComparison.Ordinal);
        Assert.Contains("{0}", en, StringComparison.Ordinal);
        Assert.Contains("الرَمز", ar, StringComparison.Ordinal);
        Assert.Contains("code", en, StringComparison.Ordinal);
    }
}
