using System.Text.RegularExpressions;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ عَدّادُ مالِكي الإعلانات (`scripts/count-listing-owners.cs`) — قِراءَةٌ فَقَط، ويَفشَلُ عَلى الصِفر (‏ADR-032 §٦‑ج‑1) ═══
//
// **لِماذا وُلِدَ السكربت**: عَتَبَةُ الرُجوعِ عَن «المَتجَرُ المُفرَدُ حالَةٌ مِن المُتَعَدِّد» (‏§٦‑ز) رَقمٌ يَكتُبُه المالِكُ
// عَلى عَدٍّ مَقيس — و`PlatformStatsQueries.ListingCountsAsync` يَعُدُّ الإعلاناتِ لا مالِكيها. والسكربتُ يُشَغَّلُ عَلى فَرعِ
// Neon مَنسوخٍ تَحمِلُه بَياناتُ الإنتاج، فَحَدّاه **«لا يَكتُبُ حَرفاً»** و**«لا يَطبَعُ صِفراً لِما لَم يَقرَأ»**.
//
// **والحَدّانِ يَحرُسُهُما هذا المِلَفُّ لا تَعليقُ السكربت** (القاعِدَة ٢): بابُ كِتابَةٍ يَظهَرُ فيه، أَو حارِسُ العَمى يَسقُط،
// أَو نُسخَةٌ ثانِيَةٌ مِن اشتِقاقِ النَمَطِ تُكتَبُ فيه بَدَلَ دالَّةِ الواجِهَة — يَحمَرُّ هُنا. والبُرهانُ الحَيُّ (قاعِدَةٌ مَبذورَة،
// وعَدّادُ كِتاباتِ Postgres قَبلُ وبَعدُ، وطَفرَةٌ مَحقونَةٌ تُحمِرُّ المُقارَنَةَ بِالعَدّادِ القائِم) في رِسالَةِ كوميتِه.

public class ListingOwnerCountScriptTests
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static string Script()
    {
        var path = Path.Combine(RepoRoot, "scripts", "count-listing-owners.cs");
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path}");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 2000, $"أَداةٌ عَمياء: {path} طولُه {text.Length} — لَم يُقرَأ.");
        return text;
    }

    /// <summary>السكربتُ بِلا تَعليقاتِه ولا تَوجيهاتِ <c>#:</c> — فَما يُفحَصُ «لا يَكتُبُ» هُوَ الكودُ لا شَرحُه
    /// (الرَأسُ يَذكُرُ <c>Store</c> و<c>SaveChanges</c> لِيَقولَ إنَّه لا يَملِكُهُما).</summary>
    private static string Code() => string.Join('\n', Script().Split('\n')
        .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)
                 && !l.StartsWith("#", StringComparison.Ordinal)));

    /// <summary><b>لا يَفتَحُ إلّا <c>IQuerySession</c> ولا يَمَسُّ المُخَطَّط</b> — كُلُّ بابِ كِتابَةٍ يَعرِفُه Marten وNpgsql
    /// مَحظورٌ بِاسمِه، والجَلَساتُ المَفتوحَةُ تُعَدُّ وتَفشَلُ عَلى الصِفر.</summary>
    [Fact]
    public void TheScript_OpensQuerySessionsOnly_AndNeverTouchesTheSchema()
    {
        var code = Code();

        Assert.Contains("AutoCreateSchemaObjects = AutoCreate.None", code, StringComparison.Ordinal);
        var opened = Regex.Matches(code, @"\.QuerySession\(").Count;
        Assert.True(opened >= 3, $"أَداةٌ عَمياء: {opened} جَلسَةَ قِراءَةٍ مَعدودَة.");

        var writes = new[]
        {
            "LightweightSession", "IdentitySession", "DirtyTrackedSession", "OpenSession", "SaveChanges",
            ".Store(", ".Insert(", ".Update(", ".Delete(", "HardDelete", ".Patch<", "Events.Append", "StartStream",
            "ExecuteNonQuery", "ApplyAllConfiguredChanges", "AutoCreate.All", "AutoCreate.CreateOrUpdate", "AutoCreate.CreateOnly",
        };
        var found = writes.Where(w => code.Contains(w, StringComparison.Ordinal)).ToList();
        Assert.True(found.Count == 0,
            $"فُحِصَ {writes.Length} بابَ كِتابَة، ووُجِدَ في السكربتِ {found.Count}: {string.Join(", ", found)}");
    }

    /// <summary><b>يَطبَعُ عَدَدَ ما فَحَصَه ويَخرُجُ بِـ1 عَلى الصِفر</b> (القاعِدَة ١٠) — ويَخرُجُ بِـ1 إن لَم يُطابِق عَدُّه
    /// لِلإعلاناتِ عَدّادَ الواجِهَةِ القائِمَ الَّذي يُناديه بِعَينِه (القاعِدَة ٨).</summary>
    [Fact]
    public void TheScript_PrintsWhatItChecked_AndFailsOnZeroAndOnACounterMismatch()
    {
        var code = Code();

        var blind = Regex.Match(code, @"if \(tenants\.Count == 0\)\s*\{(?<body>[^}]*)\}");
        Assert.True(blind.Success, "حارِسُ العَمى غائِب: لا فَرعَ لِصِفرِ مُستَأجِرٍ مَفحوص.");
        Assert.Contains("return 1;", blind.Groups["body"].Value, StringComparison.Ordinal);
        Assert.Contains("BLIND CHECK", blind.Groups["body"].Value, StringComparison.Ordinal);

        Assert.Contains("فُحِصَ: {tenants.Count} مَتجَراً", code, StringComparison.Ordinal);

        Assert.Contains("new PlatformStatsQueries(store)", code, StringComparison.Ordinal);
        Assert.Contains(".ListingCountsAsync(", code, StringComparison.Ordinal);
        var mismatch = Regex.Match(code, @"if \(mismatches\.Count > 0\)\s*\{(?<body>(?:[^{}]|\{[^{}]*\})*)\}");
        Assert.True(mismatch.Success, "لا فَرعَ لِعَدٍّ لا يُطابِقُ العَدّادَ القائِم.");
        Assert.Contains("return 1;", mismatch.Groups["body"].Value, StringComparison.Ordinal);
    }

    /// <summary><b>ما يُطبَعُ نَمَطاً هُوَ ما يَراه الزائِر</b>: يُقرَأُ بِـ<c>PatternProfileResolver.PatternOf</c> بِعَينِها،
    /// والنَمَطُ المُقتَرَحُ بِـ<c>TenantFromAnalysisFactory.NormalizePattern</c> الَّتي يُناديها المَصنَع، والبائِعُ بِـ
    /// <c>SelfGrantPolicy.SelfGrantable</c> — ولا يَكتُبُ السكربتُ اشتِقاقاً مِن عِندِه (نُسخَةُ <c>MainLayout</c> عِبرَةٌ قائِمَة:
    /// قارِئانِ لِلكِندِ لا واحِد — ‏ADR-034 §٣‑٣).</summary>
    [Fact]
    public void TheScript_ReadsPatternAndSellersWithTheProductsOwnFunctions()
    {
        var code = Code();

        Assert.Contains("PatternProfileResolver.PatternOf(t)", code, StringComparison.Ordinal);
        Assert.Contains("TenantFromAnalysisFactory.NormalizePattern(", code, StringComparison.Ordinal);
        Assert.Contains("SelfGrantPolicy.SelfGrantable(t.Roles)", code, StringComparison.Ordinal);
        Assert.Contains("Attributes.TryGetValue(\"owner_id\"", code, StringComparison.Ordinal);

        foreach (var copy in new[] { "\"commercial\"", "\"residential\"", "\"roommate\"", "AppPattern.Trip", "\"rider\"" })
            Assert.DoesNotContain(copy, code, StringComparison.Ordinal);

        var projects = Regex.Matches(Script(), @"^#:project (?<p>\S+)$", RegexOptions.Multiline)
            .Select(m => m.Groups["p"].Value).ToList();
        Assert.Single(projects);
        Assert.True(File.Exists(Path.GetFullPath(Path.Combine(RepoRoot, "scripts", projects[0]))),
            $"#:project يُحيلُ إلى مَفقود: {projects[0]}");
        Assert.Contains("#:property PublishAot=false", Script(), StringComparison.Ordinal);
    }

    /// <summary><b>ولا يَطبَعُ هاتِفاً ولا بَريداً ولا مُعَرِّفَ مُستَخدِم</b>: لا سَطرَ طِباعَةٍ يَحمِلُ حَقلاً شَخصِيّاً، والمُطابَقَةُ
    /// تُقارِنُ داخِلَ الاستِعلامِ وتُطبَعُ «نَعَم/لا».</summary>
    [Fact]
    public void TheScript_PrintsNoPhoneEmailOrUserId()
    {
        var printLines = Code().Split('\n')
            .Where(l => l.Contains("Console.", StringComparison.Ordinal))
            .ToList();
        Assert.True(printLines.Count > 5, $"أَداةٌ عَمياء: {printLines.Count} سَطرَ طِباعَة.");

        var personal = new[] { ".Phone", ".Email", ".NationalId", "OwnerUserId}", "u.Id}", ".FullName" };
        var leaks = printLines.Where(l => personal.Any(p => l.Contains(p, StringComparison.Ordinal))).ToList();
        Assert.True(leaks.Count == 0,
            $"فُحِصَ {printLines.Count} سَطرَ طِباعَة، ومِنها {leaks.Count} يَطبَعُ حَقلاً شَخصِيّاً: {string.Join(" | ", leaks)}");
    }
}
