using System.Text.RegularExpressions;
using ACommerce.Kit.Auth;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Gates;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>إعادَةُ قَبولِ الشُروطِ تَقولُ ما تَغَيَّر</b> (‏2026-10-30، تَدقيقُ الموجَة ١١ CO9). <b>تَوصيفُ «اليَوم» قَبلَ التَبديل</b> (القاعِدَة ٣): رُفِعَ
/// الإصدارُ إلى 2 بَعدَ تَبديلِ بَندِ الخُصوصِيَّةِ (<c>c3982f9</c>)، فَيُعيدُ <c>TermsFilter</c> عُضوَ الإصدارِ 1 إلى <c>/{slug}/terms</c> عِندَ أَوَّلِ كِتابَة — وتَرسُمُ
/// الصَفحَةُ الشُروطَ كامِلَةً بِـ«إصدار 2» ولا تَقولُ أَيُّ بَندٍ تَبَدَّل: لا تَقرَأُ وَثيقَةَ العُضوِ أَصلاً، ولا سَطرَ في القامُوسِ لِإصدار. فَيُطلَبُ مِنه قَبولُ ما لا
/// يَعرِفُ ما الجَديدُ فيه، والبَندُ الرابِعُ يَعِدُه «سَنُخطِركَ وَنَطلُب القَبول مَرَّة أُخرى لِلإصدار الجَديد».</para>
/// </summary>
public class TermsChangesTests(ITestOutputHelper output)
{
    private static readonly string PagePath = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
        "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "Terms.razor");

    private static readonly Regex VersionLine = new(@"^terms\.changes\.v(?<n>\d+)$", RegexOptions.Compiled);

    /// <summary>مَفاتيحُ «ما تَغَيَّرَ في الإصدارِ N» في القامُوسِ العَرَبِيّ — مِن المِلَفِّ لا مِن قائِمَةٍ مَكتوبَة.</summary>
    internal static IReadOnlyList<int> VersionLines()
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "core", "ACommerce.Platform.I18n", "Locales", "ar.json");
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.EnumerateObject().Select(p => VersionLine.Match(p.Name)).Where(m => m.Success)
            .Select(m => int.Parse(m.Groups["n"].Value)).OrderBy(n => n).ToList();
    }

    [Fact]
    public void Today_the_terms_page_asks_a_version_one_member_to_re_accept_without_saying_what_changed()
    {
        var page = WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(PagePath));
        var lines = VersionLines();
        output.WriteLine($"الإصدارُ الحالِيّ: {TermsPolicy.CurrentVersion} · أَسطُرُ «ما تَغَيَّر» في القامُوس: {lines.Count} · الصَفحَةُ تَقرَأُ وَثيقَةَ العُضو: " +
                         $"{page.Contains("ProfileAsync", StringComparison.Ordinal)} · تَذكُرُ terms.changes.v: {page.Contains("terms.changes.v", StringComparison.Ordinal)}");

        Assert.Equal(2, TermsPolicy.CurrentVersion);
        Assert.Empty(lines);
        Assert.DoesNotContain("ProfileAsync", page, StringComparison.Ordinal);
        Assert.DoesNotContain("AcceptedTermsVersion", page.Split("@code")[0].Replace("AcceptedTermsAt + AcceptedTermsVersion", ""), StringComparison.Ordinal);
        Assert.DoesNotContain("terms.changes.v", page, StringComparison.Ordinal);
        var v1 = new User { AcceptedTermsAt = DateTime.UtcNow.AddDays(-30), AcceptedTermsVersion = 1 };
        Assert.False(TermsPolicy.IsAccepted(v1));
    }
}
