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

public class ListingOwnerCountScriptTests(Xunit.Abstractions.ITestOutputHelper output)
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

    // ─── الحَقلُ الشَخصِيُّ لا يُقرَأُ إلّا في المُرَشِّحَينِ المُسَمَّيَين (‏2026-10-24 — مُراجَعَةُ الموجَة ٤، بَندُ F) ─────────────
    //
    // **المَقيسُ قَبل**: الصَفُّ أَعلاه لا يَقرَأُ إلّا الأَسطُرَ الَّتي فيها `Console.` — فَحَقلٌ يُسنَدُ إلى مُتَغَيِّرٍ ثُمَّ يُطبَعُ
    // المُتَغَيِّرُ في سَطرٍ آخَر (`var who = studioUser.Phone;` ثُمَّ `Console.WriteLine($"… {who}")`) يَمُرّ، وكَذا سَطرٌ يَحمِلُ الحَقلَ
    // في إسنادٍ إلى `storeOwnerLine` الَّذي يُطبَعُ بَعدَه. والسكربتُ يُشَغَّلُ عَلى نُسخَةٍ تَحمِلُ هَواتِفَ المُستَخدِمينَ وبَريدَهُم.

    /// <summary>المُرَشِّحانِ المُسَمَّيانِ بِحَرفِهِما بَعدَ طَيِّ المَسافات — يُقارِنانِ هاتِفَ مالِكِ الاستوديو أَو بَريدَه داخِلَ
    /// الاستِعلامِ ولا يُخرِجانِه (رَأسُ السكربت، «ما لا يَفعَلُه» ٢). ولا مَوضِعَ ثالِثَ يَقرَأُ حَقلاً شَخصِيّاً.</summary>
    internal static readonly string[] NamedPreFilters =
    {
        "if (!string.IsNullOrWhiteSpace(studioUser.Phone)) front.AddRange(await tq.Query<User>().Where(u => u.Phone == studioUser.Phone).ToListAsync());",
        "if (!string.IsNullOrWhiteSpace(studioUser.Email)) front.AddRange(await tq.Query<User>().Where(u => u.Email == studioUser.Email).ToListAsync());",
    };

    /// <summary>ما يَقرَأُ <c>.Phone</c> أَو <c>.Email</c> أَو <c>.NationalId</c> أَو <c>.FullName</c> في الكودِ بِلا تَعليقاتِه خارِجَ
    /// المُرَشِّحَينِ المُسَمَّيَين — وكُلُّ مُرَشِّحٍ يَرِدُ مَرَّةً بِحَرفِه وإلّا عُدَّ خَرقاً (فَنَسخُه أَو تَغييرُه لا يَمُرّ).</summary>
    internal static IReadOnlyList<string> PersonalFieldReads(string code)
    {
        var folded = Regex.Replace(WriteEndpointGuardTests.StripComments(code), @"\s+", " ");
        var v = new List<string>();
        foreach (var filter in NamedPreFilters)
        {
            var n = Regex.Matches(folded, Regex.Escape(filter)).Count;
            if (n != 1) v.Add($"المُرَشِّحُ المُسَمّى يَرِدُ {n} مَرَّة: {filter}");
            folded = folded.Replace(filter, " ", StringComparison.Ordinal);
        }
        foreach (Match m in Regex.Matches(folded, @"\.(?:Phone|Email|NationalId|FullName)\b"))
            v.Add(folded.Substring(Math.Max(0, m.Index - 40), Math.Min(folded.Length, m.Index + 40) - Math.Max(0, m.Index - 40)).Trim());
        return v;
    }

    /// <summary><b>ولا يَقرَأُ حَقلاً شَخصِيّاً خارِجَ المُرَشِّحَين</b> — في أَيِّ سَطر، لا في أَسطُرِ الطِباعَةِ وَحدَها.</summary>
    [Fact]
    public void TheScript_ReadsNoPersonalFieldOutsideTheTwoNamedPreFilters()
    {
        var code = Code();
        var reads = PersonalFieldReads(code);
        var mentions = Regex.Matches(WriteEndpointGuardTests.StripComments(code), @"\.(?:Phone|Email|NationalId|FullName)\b").Count;
        output.WriteLine($"مَواضِعُ الحَقلِ الشَخصِيِّ في الكود: {mentions} · مُرَشِّحانِ مُسَمَّيانِ: {NamedPreFilters.Length} (ثَلاثَةُ مَواضِعَ في كُلٍّ) · خارِجَهُما: {reads.Count}");
        Assert.True(mentions >= 6, $"أَداةٌ عَمياء: {mentions} مَوضِعاً — في المُرَشِّحَينِ وَحدَهُما سِتَّة.");
        Assert.True(reads.Count == 0, $"حَقلٌ شَخصِيٌّ خارِجَ المُرَشِّحَينِ المُسَمَّيَين ({reads.Count}):\n  " + string.Join("\n  ", reads));
    }

    /// <summary><b>حَقنُ عَيب — فاحِصُ الحَقلِ الشَخصِيِّ يُحمِرُّ فِعلاً</b> (‏2026-10-24): الكودُ الحَيُّ تُحقَنُ فيه قِراءاتٌ لا يَراها
    /// فاحِصُ أَسطُرِ الطِباعَة — إسنادٌ ثُمَّ طِباعَة، وإسنادٌ إلى سَطرٍ يُطبَعُ لاحِقاً، ومُرَشِّحٌ ثالِث، ونُسخَةٌ ثانِيَةٌ مِن مُرَشِّحٍ مُسَمّى —
    /// ويُشتَرَطُ رَفضُها؛ والحَيُّ وتَعليقٌ يَذكُرُ الحَقلَ يُقبَلان.</summary>
    [Fact]
    public void ThePersonalFieldChecker_CanActuallyGoRed()
    {
        var code = Code();
        const string Anchor = "storeOwnerSells = live.Any(";
        Assert.Single(Regex.Matches(code, Regex.Escape(Anchor)));
        string With(string line) => code.Replace(Anchor, line + "\n            " + Anchor, StringComparison.Ordinal);

        var accepted = new (string Name, string Code)[]
        {
            ("الحَيّ", code),
            ("تَعليقٌ يَذكُرُ الحَقل", With("// ‏يُقارَنُ u.Phone داخِلَ الاستِعلامِ وَحدَه")),
        };
        var rejected = new (string Name, string Code)[]
        {
            ("إسنادٌ ثُمَّ طِباعَة", With("var who = studioUser.Phone; Console.WriteLine($\"  مالِك: {who}\");")),
            ("إسنادٌ إلى سَطرٍ يُطبَع", With("storeOwnerLine = studioUser.FullName;")),
            ("بَريدٌ في سَطرِ خَطَأ", With("Console.Error.WriteLine(studioUser.Email);")),
            ("هُوِيَّةٌ وَطَنِيَّة", With("var nid = live.First().NationalId;")),
            ("مُرَشِّحٌ ثالِث", With("front.AddRange(await tq.Query<User>().Where(u => u.Phone == t.Id).ToListAsync());")),
            ("نُسخَةٌ ثانِيَةٌ مِن مُرَشِّحٍ مُسَمّى", With(NamedPreFilters[0])),
        };

        var wrong = accepted.Where(x => PersonalFieldReads(x.Code).Count != 0)
                .Select(x => $"رُفِضَ حَيٌّ «{x.Name}»: {string.Join("؛ ", PersonalFieldReads(x.Code))}")
            .Concat(rejected.Where(x => PersonalFieldReads(x.Code).Count == 0).Select(x => $"قُبِلَ «{x.Name}»"))
            .ToList();
        // ‏وفاحِصُ أَسطُرِ الطِباعَةِ وَحدَه (الصَفُّ أَعلاه) يَقبَلُ مِنها:
        var personal = new[] { ".Phone", ".Email", ".NationalId", "OwnerUserId}", "u.Id}", ".FullName" };
        var printOnly = rejected.Count(x => !x.Code.Split('\n')
            .Any(l => l.Contains("Console.", StringComparison.Ordinal) && personal.Any(p => l.Contains(p, StringComparison.Ordinal))));
        output.WriteLine($"حُقِنَ {rejected.Length} شَكلاً و{accepted.Length} حَيّاً · يَقبَلُ فاحِصُ أَسطُرِ الطِباعَةِ وَحدَه {printOnly} مِن المَرفوضات");
        Assert.True(printOnly >= 4, $"العَيِّنَةُ لا تَقيسُ ما سَدَّه الفاحِصُ الجَديد: {printOnly}.");
        Assert.True(wrong.Count == 0,
            $"حُقِنَ {rejected.Length} شَكلاً و{accepted.Length} حَيّاً، وأَخطَأَ الفاحِصُ في {wrong.Count}:\n  " + string.Join("\n  ", wrong));
    }
}
