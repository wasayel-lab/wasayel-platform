using System.Reflection;
using System.Text.RegularExpressions;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ سكربتُ الرِحلَةِ يَمشي بِلا مِفتاح، ويَفحَصُ FLGAP-04 في كُلِّ تَشغيل (‏2026-11-07، الموجَة ١٣ W13-1·٩ — ADR-049 §١٠) ═══
//
// **المَقيسُ قَبلَه**: خُطوَتا `scripts/rewalk-launch-journey.sh` الأُولَيانِ تَمشِيانِ `/studio/begin` حَتّى التَحليل، وتَنتَظِرانِ نافِذَةً تُحسَبُ مِن
// مُهلَةِ نِداءِ الخَلفِيَّة (كُتلَةُ `analysis_bounds`، يَقيسُها `RewalkAnalysisBoundParseTests` بِسِتَّةِ صُفوف)، وتَسقُطانِ بِلا مِفتاحٍ إلى جَلسَةِ
// `IncubatorSampleSeeder` — فَرعُها `general` فَلا تُمَيِّزُ FLGAP-04 إلّا جَولَةٌ بِمِفتاح. و`walk-after-the-key.sh` الخُطُواتُ «بَعدَ الجِدار».
// وبَعدَ ·٥ لا بابَ تَحليلٍ يُمشى: فَالسكربتُ مَكسورٌ ما لَم يُعَد، ولا أَحَدَ مِنهُما في CI — فَانجِرافُه لا يُرى إلّا بِتَشغيلِه.
//
// **والآن**: الخُطوَتانِ ما يَنقُرُه رائِدُ الأَعمالِ مِن الصَفحَةِ الأُولى إلى الاستِمارَةِ وبابِها، والفَرعُ فَحصٌ كُلَّ تَشغيل بِكُتلَةِ `branch_of` —
// وهذا المِلَفُّ يَحُلُّ مَحَلَّ صُفوفِ `analysis_bounds` السِتَّة: يَقيسُ أَنّ السكربتَ لا يَعرِفُ التَحليل، وأَنّ تَسَلسُلَه هو تَسَلسُلُ الصَفَحاتِ في الكود،
// وأَنّ حُقولَه حُقولُ البابِ، ويُشَغِّلُ `branch_of` بِـbash عَلى عَيِّناتٍ مَعلومَةِ الجَوابِ — مِنها فَرعُ المَصنَعِ نَفسُه وقَد عادَ إلَيه «commercial».
public class RewalkKeylessTests(ITestOutputHelper output)
{
    private const string ScriptPath = "scripts/rewalk-launch-journey.sh";
    private static string Repo(string rel) => Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, rel);
    private static string Script() => File.ReadAllText(Repo(ScriptPath));

    /// <summary>أَسطُرُ الكود: كُلُّ سَطرٍ لا يَبدَأُ بِـ<c>#</c> — فَتاريخُ السكربتِ في تَعليقاتِه لا يُحمِرُّ الماسِح.</summary>
    private static IReadOnlyList<string> CodeLines()
        => Script().Split('\n').Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#')).ToList();

    private static string Code() => string.Join('\n', CodeLines());

    // ─── لا تَحليلَ ولا انتِظار ───────────────────────────────────────────

    [Fact]
    public void The_script_reaches_no_analysis_door_and_waits_for_no_key()
    {
        var lines = CodeLines();
        var forbidden = new[]
        {
            "/build", "/studio/s/", "ANALYZE_TRIES", "analysis_call_bound", "stale_after_secs", "IncubatorSampleSeeder", "INCUBATOR_SAMPLE_SEED",
            "FeasibilityAnalysisService", "AgentBackends.cs", "admin.study.analyzing_title", "studio.study.failed_title", "studio.analysis.unavailable",
        };
        var hits = forbidden.SelectMany(f => lines.Where(l => l.Contains(f, StringComparison.Ordinal)).Select(l => $"{f} ← {l.Trim()}")).ToList();
        // و`/studio/begin` مَرَّةً واحِدَةً خارِجَ نَصِّ السُطورِ المَطبوعَة: عَدُّ استِمارَتِه في الصَفحَةِ الأُولى — مِجَسٌّ يَطلُبُ الصِفر، لا طَلَبٌ إلَيه.
        var begin = lines.Where(l => l.Contains("/studio/begin", StringComparison.Ordinal) && !l.TrimStart().StartsWith('"'))
            .Select(l => l.Trim()).ToList();
        output.WriteLine($"أَسطُرُ الكودِ المَفحوصَة: {lines.Count} · المَحظورُ: {forbidden.Length} · المُخالِف: {hits.Count} · /studio/begin: {string.Join(" | ", begin)}");
        Assert.True(lines.Count > 300, $"أَداةٌ عَمياء: {lines.Count} سَطراً.");
        Assert.Empty(hits);
        Assert.Equal(new[] { "NBEGIN=\"$(count \"$F\" 'action=\"/studio/begin\"')\"" }, begin);
        Assert.Contains("[ \"$NBEGIN\" = 0 ]", Code(), StringComparison.Ordinal);
    }

    // ─── التَسَلسُلُ مِن الصَفحَةِ لا مِن السكربت ──────────────────────────

    /// <summary>الخُطوَتانِ ١ و٢ بِتَرتيبِ النَقر — وكُلُّ وِجهَةٍ تُقرَأُ مِن الصَفحَةِ الَّتي قَبلَها؛ والكودُ يُوافِقُ: دَعوَةُ الصَفحَةِ الأُولى، ووِجهَةُ ما بَعدَ
    /// الدُخول، وصَفحَةُ المُوافَقَةِ بِعَودَتِها، وبابُ الاستِمارَة.</summary>
    [Fact]
    public void Steps_one_and_two_click_from_the_first_page_to_the_create_door_in_order()
    {
        var code = Code();
        var order = new[]
        {
            "get \"$J_ENT\" / \"$F\"",
            "grep -o 'href=\"/studio/new\"' \"$F\"",
            "get \"$J_ENT\" \"$CTA\" \"$F\"",
            "post \"$J_ENT\" /studio/auth/login",
            "post \"$J_ENT\" /studio/auth/verify",
            "[ \"$LOC\" = \"$CTA\" ]",
            "location.replace('/studio/consent",
            "form_dump \"$F\" 'consent/accept'",
            "--data-urlencode \"returnUrl=$RETURN_TO\"",
            "form_dump \"$F\" 'name=\"slug\"'",
            "post \"$J_ENT\" \"$CREATE_ACT\"",
            "\"/studio/apps/$SLUG?built=1\"",
            "branch_of \"$CATS\" \"$ECOM\"",
        };
        var at = 0;
        foreach (var step in order)
        {
            var i = code.IndexOf(step, at, StringComparison.Ordinal);
            Assert.True(i >= 0, $"«{step}» غائِبٌ بَعدَ المَوضِعِ {at} — التَسَلسُلُ انكَسَر.");
            at = i + step.Length;
        }
        output.WriteLine($"{order.Length} خُطوَةً بِتَرتيبِها، آخِرُها عِندَ {at} مِن {code.Length}");

        Assert.Contains("href=\"/studio/new\"", File.ReadAllText(Repo("libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages/Landing.razor")),
            StringComparison.Ordinal);
        Assert.Equal("/studio/new", StudioStoreCreate.Page);
        Assert.Equal(StudioStoreCreate.Page, StudioStoreCreate.AfterSignIn(0));
        Assert.Equal("/studio/consent?returnUrl=" + StudioStoreCreate.Page, StudioStoreCreate.ConsentPage);
        Assert.Equal("/studio/apps/create", StudioStoreCreate.Endpoint);
    }

    /// <summary>الحُقولُ الَّتي يُرسِلُها السكربتُ هي ما يَقرَؤُه البابُ (<see cref="StudioStoreCreate.Read"/>)، والمَطلوبُ الَّذي يَعرِفُه هو مَطلوبُ الاستِمارَة —
    /// فَحَقلٌ مَطلوبٌ جَديدٌ يُوقِفُ الرِحلَةَ بِاسمِه لا يُرسَلُ فارِغاً.</summary>
    [Fact]
    public void The_script_sends_exactly_the_fields_the_create_door_reads()
    {
        var code = Code();
        var post = Regex.Match(code, @"post ""\$J_ENT"" ""\$CREATE_ACT""(?<args>(?:[^\n]*\\\n)*[^\n]*)");
        Assert.True(post.Success, "لا طَلَبَ إنشاءٍ في السكربت.");
        var sent = Regex.Matches(post.Groups["args"].Value, @"--data-urlencode ""(?<n>\w+)=").Select(m => m.Groups["n"].Value).OrderBy(n => n).ToArray();

        var door = File.ReadAllText(Repo("libs/templates/ACommerce.Templates.Customer.Marketplace/Services/Incubator/StudioStoreCreate.cs"));
        var read = Regex.Matches(door, @"f\[""(?<n>\w+)""\]").Select(m => m.Groups["n"].Value).OrderBy(n => n).ToArray();

        var page = File.ReadAllText(Repo("libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages/StudioNewStore.razor"));
        var required = Regex.Matches(page, @"<input\b[^>]*\bname=""(?<n>\w+)""[^>]*\brequired\b").Select(m => m.Groups["n"].Value).OrderBy(n => n).ToArray();
        var known = Regex.Match(code, @"UNKNOWN_REQ=""\$\(fd_kind ""\$CREATE_DUMP"" FIELD \| grep -vxE '(?<k>[^']*)'");
        Assert.True(known.Success, "لا حارِسَ لِلمَطلوبِ المَجهول.");
        var knownSet = known.Groups["k"].Value.Split('|').OrderBy(n => n).ToArray();

        output.WriteLine($"يُرسِل: {string.Join(",", sent)} · يَقرَأُ البابُ: {string.Join(",", read)} · مَطلوبُ الاستِمارَة: {string.Join(",", required)} · يَعرِفُه السكربت: {string.Join(",", knownSet)}");
        Assert.Equal(4, read.Length);
        Assert.Equal(read, sent);
        Assert.Equal(3, required.Length);
        Assert.Equal(required, knownSet);
    }

    // ─── branch_of بِـbash ─────────────────────────────────────────────────

    private const string Start = "# ─── بِدايَةُ branch_of ───";
    private const string End = "# ─── نِهايَةُ branch_of ───";

    private static string Block()
    {
        var s = Script();
        var a = s.IndexOf(Start, StringComparison.Ordinal);
        Assert.True(a > 0, "كُتلَةُ branch_of غائِبَة.");
        Assert.Equal(a, s.LastIndexOf(Start, StringComparison.Ordinal));
        var b = s.IndexOf(End, a, StringComparison.Ordinal);
        Assert.True(b > a, "عَلامَةُ نِهايَةِ branch_of غائِبَة.");
        // والمَسارُ الرَئيسُ يَحكُمُ مِنها وَحدَها، مَرَّةً واحِدَة، فَحصاً لا سَطرَ مَعلومَة.
        Assert.Equal(1, Regex.Matches(s, Regex.Escape("BRANCH_OUT=\"$(branch_of \"$CATS\" \"$ECOM\")\"")).Count);
        Assert.Contains("check \"$([ \"$BRANCH\" = ecommerce ] && [ \"$BRANCH_REST\" = 0 ] && echo 1 || echo 0)\"", s, StringComparison.Ordinal);
        return s[a..(b + End.Length)];
    }

    private string Run(string cats, string ecom)
    {
        Assert.True(File.Exists("/bin/bash"), "أَداةٌ غائِبَة: /bin/bash.");
        var dir = Directory.CreateTempSubdirectory("wasayel-rewalk-branch-");
        try
        {
            var script = Path.Combine(dir.FullName, "run.sh");
            File.WriteAllText(script, "set -uo pipefail\n" + Block() + "\nbranch_of \"$1\" \"$2\"\n", new System.Text.UTF8Encoding(false));
            var psi = new System.Diagnostics.ProcessStartInfo("/bin/bash", new[] { script, cats, ecom })
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
                StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8,
            };
            using var p = System.Diagnostics.Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEnd().Trim();
            var e = p.StandardError.ReadToEnd().Trim();
            Assert.True(p.WaitForExit(10_000));
            output.WriteLine($"branch_of ← {o} {e}");
            Assert.Equal(0, p.ExitCode);
            return o;
        }
        finally { dir.Delete(recursive: true); }
    }

    /// <summary>فِئاتُ المَتجَرِ الَّذي يولَدُ مِن الإنشاءِ بِاليَد — نَمَطٌ وقِطاعٌ فارِغان كَما يُمَرِّرُهُما البابُ — مِن المَصنَعِ نَفسِه.</summary>
    private static IReadOnlyList<Category> ManualCreateCategories()
    {
        var m = typeof(TenantFromAnalysisFactory).GetMethod("CategoriesForSector", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(m);
        return (IReadOnlyList<Category>)m!.Invoke(null, new object[] { "", TenantFromAnalysisFactory.NormalizePattern("") })!;
    }

    private static string Lines(IEnumerable<Category> cats) => string.Join('\n', cats.Select(c => $"{c.Slug}:'{c.Kind}'"));
    private static string Slugs(IEnumerable<Category> cats) => string.Join(' ', cats.Select(c => c.Slug).OrderBy(s => s, StringComparer.Ordinal));

    [Fact]
    public void The_store_the_manual_create_door_births_is_judged_ecommerce_with_empty_kinds()
    {
        var cats = ManualCreateCategories();
        Assert.True(cats.Count >= 2, $"أَداةٌ عَمياء: {cats.Count} فِئَة.");
        Assert.Equal("ecommerce 0", Run(Lines(cats), Slugs(cats)));
    }

    /// <summary>عَودَةُ FLGAP-04 تُمسَك: فِئَةٌ مِن فَرعِ المَصنَعِ بِكِندِ <c>commercial</c> (ما كانَ قَبلَ ADR-034) — الفَرعُ هو، والكِندُ غَيرُ الفارِغِ يُعَدّ.</summary>
    [Fact]
    public void A_commercial_kind_back_on_the_factory_branch_is_counted()
    {
        var cats = ManualCreateCategories().Select((c, i) => new Category { Slug = c.Slug, Kind = i == 0 ? "commercial" : c.Kind }).ToList();
        Assert.Equal("ecommerce 1", Run(Lines(cats), Slugs(ManualCreateCategories())));
    }

    [Theory]
    [InlineData("general:''", "other general")]
    [InlineData("", "other ؟")]
    public void Another_branch_or_no_category_is_not_ecommerce(string cats, string expected)
        => Assert.Equal(expected, Run(cats, Slugs(ManualCreateCategories())));

    // ─── والسكربتُ الثاني تَقاعَد ───────────────────────────────────────

    /// <summary><c>walk-after-the-key.sh</c> كانَ «الخُطُواتُ الخَمسُ الباقِيَةُ بَعدَ الجِدار» — ولا جِدارَ بَعدَ اليَوم. لا مِلَفّ، ولا سكربتَ ولا مَسارَ CI يُسَمّيه
    /// في كودِه. (‏<c>ScriptExecutableBitTests</c> يَستَعمِلُ اسمَه في عَيِّنَةٍ مُصطَنَعَةٍ مِن أَسطُرِ <c>git ls-files</c> — نَصٌّ لا مِلَفّ.)</summary>
    [Fact]
    public void The_after_the_key_walk_is_retired_and_named_by_no_script_or_workflow()
    {
        Assert.False(File.Exists(Repo("scripts/walk-after-the-key.sh")), "scripts/walk-after-the-key.sh ما زالَ قائِماً.");
        var files = Directory.EnumerateFiles(Repo("scripts"), "*", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Repo(".github"), "*", SearchOption.AllDirectories)).ToList();
        var naming = files.Where(f => File.ReadAllText(f).Split('\n')
                .Any(l => !l.TrimStart().StartsWith('#') && l.Contains("walk-after-the-key", StringComparison.Ordinal)))
            .Select(f => Path.GetRelativePath(ThemeZeroEquivalenceTests.RepoRoot, f)).ToList();
        output.WriteLine($"مِلَفّاتٌ مَفحوصَة: {files.Count} · تُسَمّيه في كودِها: {naming.Count}");
        Assert.True(files.Count > 20, $"أَداةٌ عَمياء: {files.Count} مِلَفّاً.");
        Assert.Empty(naming);
    }

    /// <summary>والسكربتُ يُعرَبُ — <c>bash -n</c>: خَطَأُ صِياغَةٍ لا يَنتَظِرُ تَشغيلاً حَيّاً لِيُرى.</summary>
    [Fact]
    public void The_script_parses()
    {
        var psi = new System.Diagnostics.ProcessStartInfo("/bin/bash", new[] { "-n", Repo(ScriptPath) })
        {
            RedirectStandardError = true, UseShellExecute = false, StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var e = p.StandardError.ReadToEnd();
        Assert.True(p.WaitForExit(10_000));
        Assert.True(p.ExitCode == 0, e);
    }
}
