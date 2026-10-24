using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ الوَقتُ المَعروضُ بِتَوقيتِ مَن؟ — تَوصيفُ «اليَوم» قَبلَ التَبديل (القاعِدَة ٣) ═══
//
// **المَقيسُ** (RELEASE-READINESS §١ البَند ٦ و§٧·١): `Notification.At = DateTime.UtcNow` و`Notifications.razor` يَرسُمُ
// `n.At.ToString("HH:mm")` بِلا تَحويل — ثَلاثُ ساعاتٍ خَلفَ الرياض؛ و`Chats.razor`/`ChatRoom.razor` يُحَوِّلانِ بِـ`ToLocalTime()`
// أَي بِمِنطَقَةِ **الخادِم** (UTC في الحاوِيَة). والمُستَودَعُ فيه الاصطِلاحُ بِلا مُستَهلِكِ عَرض: `ICultureContext.TimeZone`
// = ‏"Asia/Riyadh" و`NowInZone`، مُسَجَّلٌ في `Program.cs` (‏`AddCultureContext`/`UseCultureContext`) ولا تَحقِنُه صَفحَةٌ واحِدَة.
// **والقيمَةُ المُخَزَّنَةُ مَقيسَة**: ‏Marten يَكتُبُها بِـ`Z` (‏`mt_doc_notification`/`message`/`deal`: «2026-10-24T05:08:46.671891Z»)،
// فَتَعودُ `DateTimeKind.Utc` — والبُرهانُ الحَيُّ في `LiveDisplayTimeKindProofTests`.
public class DisplayTimeZoneTests(ITestOutputHelper output)
{
    private const string Pages = "libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages";

    /// <summary>لَحظَةٌ ثابِتَةٌ عِندَ حافَّةِ اليَوم: ‏21:30 بِـUTC هي ‏00:30 مِن اليَومِ التالي في الرياض — فَيَظهَرُ فَرقُ الساعَةِ والتاريخِ مَعاً.</summary>
    internal static readonly DateTime FixedUtc = new(2026, 10, 24, 21, 30, 0, DateTimeKind.Utc);

    private static string Read(string relative)
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {relative}");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 300, $"أَداةٌ عَمياء: {relative} طولُه {text.Length}.");
        return text;
    }

    /// <summary><b>تَوصيفُ «اليَوم» — يُقلَبُ بِاسمِه في كوميتِ التَبديل</b>: الإشعارُ يُرسَمُ بِساعَةِ UTC المُخَزَّنَةِ كَما هي.</summary>
    [Fact]
    public void Today_notifications_render_the_stored_utc_clock()
    {
        var razor = Read($"{Pages}/Notifications.razor");
        Assert.Contains("Time=\"@n.At.ToString(\"HH:mm\")\"", razor, StringComparison.Ordinal);
        Assert.Equal("21:30", FixedUtc.ToString("HH:mm"));
    }

    /// <summary><b>تَوصيفُ «اليَوم» — يُقلَبُ بِاسمِه في كوميتِ التَبديل</b>: المُحادَثاتُ تُحَوِّلُ بِمِنطَقَةِ الخادِم.</summary>
    [Fact]
    public void Today_chats_convert_with_the_server_zone()
    {
        Assert.Contains("at.ToLocalTime()", Read($"{Pages}/Chats.razor"), StringComparison.Ordinal);
        Assert.Contains("m.SentAt.ToLocalTime()", Read($"{Pages}/ChatRoom.razor"), StringComparison.Ordinal);
    }

    /// <summary><b>تَوصيفُ «اليَوم» — يُقلَبُ بِاسمِه في كوميتِ التَبديل</b>: الاصطِلاحُ قائِمٌ (الرياض) ولا تَحقِنُه صَفحَة.</summary>
    [Fact]
    public void Today_the_culture_zone_exists_and_no_page_reads_it()
    {
        var ctx = new ACommerce.Kit.Culture.MutableCultureContext();
        Assert.Equal("Asia/Riyadh", ctx.TimeZone);
        var root = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates");
        var razors = Directory.GetFiles(root, "*.razor", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();
        Assert.True(razors.Count > 100, $"أَداةٌ عَمياء: {razors.Count} مِلَفّ razor فَقَط.");
        var readers = razors.Count(f => File.ReadAllText(f).Contains("ICultureContext", StringComparison.Ordinal));
        output.WriteLine($"مِلَفّاتُ razor: {razors.Count} · تَحقِنُ ICultureContext: {readers}");
        Assert.Equal(0, readers);
    }

    /// <summary><b>ويَبقى بِحَرفِه</b>: مِنطَقَةُ الرياض تُحَلُّ عَلى هذا الجِهازِ وإزاحَتُها عِندَ اللَحظَةِ الثابِتَةِ ‏+03:00 — مِن
    /// بَياناتِ المِنطَقَةِ (tzdata) لا مِن رَقَمٍ مَكتوب.</summary>
    [Fact]
    public void The_riyadh_zone_resolves_here_with_its_offset_at_the_fixed_instant()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");
        var offset = tz.GetUtcOffset(FixedUtc);
        output.WriteLine($"Asia/Riyadh عِندَ {FixedUtc:O}: {offset}");
        Assert.Equal(TimeSpan.FromHours(3), offset);
        Assert.Equal("2026-10-25 00:30", TimeZoneInfo.ConvertTimeFromUtc(FixedUtc, tz).ToString("yyyy-MM-dd HH:mm"));
    }

    /// <summary><b>ويَبقى بِحَرفِه</b>: عَدَدُ راسِمي الوَقتِ في القَوالِبِ مَطبوعٌ لا مُفتَرَض (القاعِدَة ١٠) — الصيَغُ الثَلاث الَّتي يُقرَأُ بِها.</summary>
    [Fact]
    public void The_time_renderers_under_templates_are_counted()
    {
        var root = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates");
        var rx = new Regex(@"ToString\(""(HH:mm|yyyy|MM-dd|dd/MM)|ToLocalTime\(\)");
        var files = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".razor") || f.EndsWith(".cs"))
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
        var hits = files.Sum(f => rx.Matches(File.ReadAllText(f)).Count);
        output.WriteLine($"راسِمو الوَقتِ تَحتَ libs/templates: {hits}");
        Assert.True(hits > 0, "أَداةٌ عَمياء: صِفرُ راسِمٍ لِلوَقت.");
    }
}
