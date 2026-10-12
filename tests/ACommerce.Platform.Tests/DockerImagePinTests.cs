using System.Text.RegularExpressions;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ صورَتا النَشرِ بِوَسمٍ دَقيقٍ لا مُتَحَرِّك (‏2026-10-12، موجزُ الفجوات P3-3) ═══════════════════════════════════════
//
// **المَقيسُ قَبل**: `FROM mcr.microsoft.com/dotnet/sdk:10.0` و`FROM mcr.microsoft.com/dotnet/aspnet:10.0` — وَسمانِ يَنتَقِلانِ
// مَعَ كُلِّ تَرقِيَةٍ شَهرِيَّة، فَيُبنى إيداعٌ واحِدٌ بِـSDK غَيرِ الَّذي قيسَ عَلَيه، و`DEPLOY.md` §٥ يُسَمّي العَطَبَ وعِلاجَه
// («تُثَبَّتُ النُسَخُ بِوَسمٍ دَقيق») بِلا تَنفيذ. **والوَسمانِ اليَومَ** زَوجٌ مَقيس: `dotnet --version` = ‏10.0.401 و`--list-runtimes`
// = ‏10.0.12 حَيثُ قيسَ كُلُّ شَيء، وكِلاهُما في قائِمَةِ وُسومِ MCR المَجلوبَةِ يَومَ التَثبيت (رِسالَةُ الكوميت).
//
// **والحَدُّ المَحروسُ هُنا شَكلُه لا رَقَمُه**: لا وَسمَ مُتَحَرِّكٌ يَعود، والقَناةُ (‏10.0) قَناةُ `net10.0` وقَناةُ `ci.yml`، و§٥
// يُسَمّي الوَسمَينِ بِحَرفِهِما. **ولا يُقارَنُ بِـSDK العامِلِ في CI** — `10.0.x` فيها يَتَقَدَّمُ شَهرِيّاً، وصَفٌّ يَحمَرُّ بِكُلِّ إصدارٍ
// مِن مايكروسوفت ضَجيجٌ لا حارِس؛ والتَرقِيَةُ قَرارٌ يُتَّخَذُ بِالوَسمَينِ مَعاً كَما في رَأسِ الـ`Dockerfile`.

public class DockerImagePinTests
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static string Read(params string[] parts)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, Path.Combine(parts)));
        // ‏أَقصَرُها `Directory.Build.props` (مِئاتُ البايتات) — والحَدُّ يُمَيِّزُ «قُرِئَ» مِن «فارِغ» لا أَكثَر.
        Assert.True(text.Length > 100, $"أَداةٌ عَمياء: {Path.Combine(parts)} لَم يُقرَأ.");
        return text;
    }

    private static readonly Regex From = new(@"^FROM mcr\.microsoft\.com/dotnet/(?<image>sdk|aspnet):(?<tag>\S+) AS (?<stage>\w+)\s*$",
        RegexOptions.Multiline);

    private static Dictionary<string, string> Tags()
    {
        var froms = From.Matches(Read("Dockerfile")).ToDictionary(m => m.Groups["image"].Value, m => m.Groups["tag"].Value);
        Assert.Equal(2, froms.Count);
        return froms;
    }

    [Fact]
    public void Both_images_are_pinned_to_an_exact_patch_not_a_moving_channel()
    {
        var tags = Tags();
        Assert.Matches(new Regex(@"^10\.0\.\d{3}$"), tags["sdk"]);      // ‏شَريطُ ميزاتٍ ورُقعَة: 10.0.401
        Assert.Matches(new Regex(@"^10\.0\.\d{1,2}$"), tags["aspnet"]); // ‏رُقعَةُ وَقتِ التَشغيل: 10.0.12

        // ‏والشَكلُ المُتَحَرِّكُ لا يَعودُ في أَيِّ سَطرِ FROM.
        Assert.DoesNotMatch(new Regex(@"^FROM mcr\.microsoft\.com/dotnet/\w+:10\.0\s", RegexOptions.Multiline), Read("Dockerfile"));
    }

    /// <summary>القَناةُ واحِدَةٌ في المَواضِعِ الثَلاثَة: إطارُ المَشاريع (`net10.0`)، و`dotnet-version` في CI، والوَسمان.</summary>
    [Fact]
    public void The_pinned_channel_is_the_projects_framework_and_cis_channel()
    {
        var tags = Tags();
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", Read("Directory.Build.props"), StringComparison.Ordinal);
        Assert.Contains("dotnet-version: 10.0.x", Read(".github", "workflows", "ci.yml"), StringComparison.Ordinal);
        Assert.StartsWith("10.0.", tags["sdk"], StringComparison.Ordinal);
        Assert.StartsWith("10.0.", tags["aspnet"], StringComparison.Ordinal);
    }

    /// <summary>و§٥ يَقولُ ما في الـ`Dockerfile` حَرفاً — فَتَرقِيَةُ أَحَدِهِما بِلا الآخَرِ تَحمَرّ.</summary>
    [Fact]
    public void Deploy_doc_names_the_two_pinned_tags()
    {
        var tags = Tags();
        var deploy = Read("docs", "DEPLOY.md");
        Assert.Contains($"`sdk:{tags["sdk"]}`", deploy, StringComparison.Ordinal);
        Assert.Contains($"`aspnet:{tags["aspnet"]}`", deploy, StringComparison.Ordinal);
    }
}
