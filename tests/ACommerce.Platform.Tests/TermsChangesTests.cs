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

    /// <summary><b>قُلِبَ بِاسمِه (‏2026-10-30، CO9)</b> — كانَ <c>Today_the_terms_page_asks_a_version_one_member_to_re_accept_without_saying_what_changed</c>
    /// (‏<c>e36cdb6</c>: «أَسطُرُ «ما تَغَيَّر» في القامُوس: 0 · الصَفحَةُ تَقرَأُ وَثيقَةَ العُضو: False»). والآن الصَفحَةُ تَقرَأُ وَثيقَتَه (<c>AccountQueries.ProfileAsync</c>)
    /// وتَرسُمُ لِكُلِّ إصدارٍ بَعدَ ما قَبِلَه سَطرَه (<see cref="TermsPolicy.ChangesSince"/> و<see cref="TermsPolicy.ChangeKey"/>) فَوقَ النَصِّ كامِلاً.</summary>
    [Fact]
    public void A_member_on_an_older_version_is_told_what_changed_in_each_later_version()
    {
        var page = WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(PagePath));
        var v1 = new User { AcceptedTermsAt = DateTime.UtcNow.AddDays(-30), AcceptedTermsVersion = 1 };
        var changes = TermsPolicy.ChangesSince(v1);
        var l = new L(new Microsoft.AspNetCore.Http.HttpContextAccessor());
        output.WriteLine($"الإصدارُ الحالِيّ: {TermsPolicy.CurrentVersion} · ما يَراه عُضوُ الإصدارِ 1: {string.Join(", ", changes)} · {l[TermsPolicy.ChangeKey(2)]}");

        Assert.Equal(new[] { 2 }, changes);
        Assert.False(TermsPolicy.IsAccepted(v1));
        Assert.Contains("me = await Accounts.ProfileAsync(Slug, Auth.UserId.Value);", page, StringComparison.Ordinal);
        Assert.Contains("changes = TermsPolicy.ChangesSince(me);", page, StringComparison.Ordinal);
        Assert.Contains("@L.Markup(TermsPolicy.ChangeKey(v))", page, StringComparison.Ordinal);
        var block = page.IndexOf("@if (changes.Count > 0)", StringComparison.Ordinal);
        var terms = page.IndexOf("terms.privacy.heading", StringComparison.Ordinal);
        Assert.True(block > 0 && terms > block, "سَطرُ «ما تَغَيَّر» لَيسَ فَوقَ النَصّ.");
        foreach (var k in new[] { "terms.changes.since_title", "terms.changes.version_label" })
        {
            Assert.Contains(k, page, StringComparison.Ordinal);
            Assert.NotNull(LocaleCatalog.Find(LocaleCatalog.Arabic, k));
        }
        Assert.Equal("ما تَغَيَّرَ مُنذُ قَبِلتَ الإصدارَ 1", l.Format("terms.changes.since_title", 1));
        Assert.False(StoreTermsPrivacyClaimTests.ClaimsNoThirdParty(l[TermsPolicy.ChangeKey(2)]));
    }

    /// <summary><b>الحَدُّ مَقيساً</b> (القاعِدَة ٢): لِكُلِّ إصدارٍ نُشِرَ — مِن 1 إلى <see cref="TermsPolicy.CurrentVersion"/> — سَطرُه في القامُوس، ولا سَطرَ لِإصدارٍ
    /// لَم يُنشَر. فَرَفعُ الإصدارِ بِلا سَطرٍ يَقولُ ما تَغَيَّرَ يُحمِرُّ هذا الصَفّ. ويَطبَعُ ما عَدَّه، ويَفشَلُ عَلى الصِفر (القاعِدَة ١٠).</summary>
    [Fact]
    public void Every_published_terms_version_has_its_what_changed_line()
    {
        var lines = VersionLines();
        var published = Enumerable.Range(1, TermsPolicy.CurrentVersion).ToList();
        var missing = published.Except(lines).ToList();
        var unpublished = lines.Except(published).ToList();
        output.WriteLine($"إصداراتٌ مَنشورَة: {published.Count} · أَسطُرٌ في القامُوس: {lines.Count} ({string.Join(", ", lines.Select(TermsPolicy.ChangeKey))}) · " +
                         $"ناقِص: {string.Join(", ", missing)} · لِإصدارٍ لَم يُنشَر: {string.Join(", ", unpublished)}");

        Assert.True(lines.Count > 0, "أَداةٌ عَمياء: صِفرُ سَطرٍ في القامُوس.");
        Assert.Empty(missing);
        Assert.Empty(unpublished);
        foreach (var v in published)
            Assert.False(string.IsNullOrWhiteSpace(LocaleCatalog.Find(LocaleCatalog.Arabic, TermsPolicy.ChangeKey(v))), $"سَطرُ الإصدارِ {v} فارِغ.");
    }

    /// <summary>عَيِّنَةٌ مَحقونَة (القاعِدَة ١٠): العادُّ يَرى المَفاتيحَ بِشَكلِها وَحدَه — ‏<c>terms.changes.v3</c> سَطر، و<c>terms.changes.heading</c>
    /// و<c>terms.changes.since_title</c> و<c>terms.changes.v2x</c> لَيسَت.</summary>
    [Theory]
    [InlineData("terms.changes.v3", true)]
    [InlineData("terms.changes.v12", true)]
    [InlineData("terms.changes.heading", false)]
    [InlineData("terms.changes.since_title", false)]
    [InlineData("terms.changes.v2x", false)]
    [InlineData("terms.changes.body", false)]
    public void The_version_line_counter_sees_only_version_keys(string key, bool counted)
        => Assert.Equal(counted, VersionLine.IsMatch(key));

    /// <summary>مُوجَبٌ وسالِبٌ لِـ<see cref="TermsPolicy.ChangesSince"/>: عُضوُ الإصدارِ الحاليِّ لا يَرى كُتلَةَ التَغيير، ولا مَن لَم يَقبَل شَيئاً بَعدُ (جَديدٌ أَو
    /// وَثيقَةٌ بِلا لَحظَةِ قَبول)، ولا مَن لا وَثيقَةَ لَه؛ وعُضوُ الإصدارِ 1 يَرى الثاني وَحدَه.</summary>
    [Fact]
    public void A_member_on_the_current_version_or_who_never_accepted_sees_no_change_block()
    {
        var current = new User { AcceptedTermsAt = DateTime.UtcNow, AcceptedTermsVersion = TermsPolicy.CurrentVersion };
        var never = new User();
        var versionWithoutInstant = new User { AcceptedTermsVersion = 1 };
        var older = new User { AcceptedTermsAt = DateTime.UtcNow.AddDays(-30), AcceptedTermsVersion = 1 };

        Assert.True(TermsPolicy.IsAccepted(current));
        Assert.Empty(TermsPolicy.ChangesSince(current));
        Assert.Empty(TermsPolicy.ChangesSince(never));
        Assert.Empty(TermsPolicy.ChangesSince(versionWithoutInstant));
        Assert.Empty(TermsPolicy.ChangesSince(null));
        Assert.Equal(new[] { 2 }, TermsPolicy.ChangesSince(older));
    }
}
