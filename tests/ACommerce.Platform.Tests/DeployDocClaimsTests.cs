using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>ادِّعاءاتُ دَليلِ النَشرِ تُقرَأُ مِن الكود</b> (‏2026-10-25، تَدقيقُ الإطلاق، تَيّارُ W8-6 — نَسَقُ <see cref="OtoDeployDocTests"/>): كُلُّ
/// صَفٍّ هُنا يَحمِلُ مُلاحَظَةً صَحَّحَت سَطراً في <c>docs/DEPLOY.md</c> بِإلحاقٍ مُؤَرَّخ، ويَحمَرُّ إن عادَ السَطرُ القَديمُ بِلا شَطبٍ أَو افتَرَقَ
/// الدَليلُ عَمّا يَفعَلُه الكود. والقيَمُ مِن الكودِ والسكربتاتِ لا مَنسوخَة.</para>
/// </summary>
public class DeployDocClaimsTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray()));

    private static string Deploy()
    {
        var text = Read("docs", "DEPLOY.md");
        Assert.True(text.Length > 50_000, "أَداةٌ عَمياء: docs/DEPLOY.md لَم يُقرَأ.");
        return text;
    }

    /// <summary>قِسمٌ مِن الدَليلِ بِعُنوانِه إلى العُنوانِ التالي مِن مُستَواه.</summary>
    internal static string Section(string doc, string heading, string nextHeadingPrefix)
    {
        var a = doc.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(a >= 0, $"عُنوانٌ غائِب: {heading}");
        var b = doc.IndexOf("\n" + nextHeadingPrefix, a + heading.Length, StringComparison.Ordinal);
        return b < 0 ? doc[a..] : doc[a..b];
    }

    /// <summary>النَصُّ بِلا ما بَينَ <c>~~…~~</c> — المَشطوبُ سِجِلٌّ لا ادِّعاء.</summary>
    internal static string Unstruck(string text) => Regex.Replace(text, "~~.*?~~", "", RegexOptions.Singleline);

    // ─── lens-docs-vs-code-5 — «مَرئِيَّةُ الـSpace» في §٥ ─────────────────────────────────────────────

    /// <summary>
    /// <para><b>الفِقرَةُ الَّتي تَقولُ «المِرآةُ تَدفَعُ الشَجَرَةَ كامِلَةً» وتَنصَحُ بِجَعلِ الـSpace خاصّاً مَشطوبَةٌ بِتَصحيحٍ مُؤَرَّخ</b>، والتَصحيحُ
    /// يُحيلُ إلى §١·ب؛ والمَحظوراتُ السِتُّ الَّتي يُسَمّيها هي <c>FORBIDDEN_PREFIXES</c> في <c>deploy-manifest.sh</c> بِحَرفِها — ومِنها
    /// <c>docs/</c> و<c>CLAUDE.md</c> اللَّذانِ قالَت الفِقرَةُ إنّهُما يُدفَعان.</para>
    /// </summary>
    [Fact]
    public void The_space_visibility_paragraph_is_struck_and_its_correction_names_the_manifests_forbidden_prefixes()
    {
        var deploy = Deploy();
        var section5 = Section(deploy, "## ٥. أَعطالٌ مَعروفَة وسَبَبُها", "## ");
        const string claim = "المِرآةُ تَدفَع **الشَجَرَةَ\nكامِلَةً**";
        Assert.Contains(claim, section5, StringComparison.Ordinal);
        Assert.DoesNotContain(claim, Unstruck(section5), StringComparison.Ordinal);
        Assert.DoesNotContain("(‏Settings → ‏Private)", Unstruck(deploy), StringComparison.Ordinal);
        Assert.Contains("lens-docs-vs-code-5", section5, StringComparison.Ordinal);
        Assert.Contains("§١·ب", Unstruck(section5), StringComparison.Ordinal);

        var manifest = Read("scripts", "deploy-manifest.sh");
        var line = manifest.Split('\n').Single(l => l.StartsWith("FORBIDDEN_PREFIXES=(", StringComparison.Ordinal));
        var prefixes = Regex.Matches(line, "\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();
        output.WriteLine($"المَحظورات: {string.Join(" ", prefixes)}");
        Assert.True(prefixes.Length >= 6, $"أَداةٌ عَمياء: {prefixes.Length} مَحظوراً.");
        Assert.Contains("docs/", prefixes);
        Assert.Contains("CLAUDE.md", prefixes);
        var correction = Unstruck(section5);
        foreach (var p in prefixes) Assert.Contains($"`{p}`", correction, StringComparison.Ordinal);
        // ‏و§١·ب نَفسُه يَقولُ إنّ الـSpace يَبقى عامّاً.
        Assert.Contains("الـSpace **عامّ**، ويَجِب أَن يَبقى عامّاً", deploy, StringComparison.Ordinal);
    }

    // ─── lens-docs-vs-code-3 — §٢·و «التَهيئَة»: مُتَغَيِّرانِ إلزامِيّانِ لا واحِد ──────────────────────────────────────

    /// <summary>أَسماءُ ما تَشتَرِطُه البَوّابَةُ قَبلَ البِناء — كُلُّ سَطرِ <c>|| MISSING_VARS="$MISSING_VARS X"</c> في السكربت.</summary>
    internal static string[] GateMandatoryVariables(string gate)
        => Regex.Matches(gate, @"\|\| MISSING_VARS=""\$MISSING_VARS (?<v>[A-Za-z_][A-Za-z0-9_]*)""")
            .Select(m => m.Groups["v"].Value).Distinct().ToArray();

    /// <summary>ما يَنقُصُ قِسمَ «التَهيئَة» مِن تِلكَ الأَسماء: في مِثالِ المِلَفِّ سَطرَ إسنادٍ لِكُلٍّ، و«الوَحيد» لا يَبقى إلّا مَشطوباً.</summary>
    internal static IReadOnlyList<string> MissingFromGateConfig(string section, IEnumerable<string> mandatory)
    {
        var missing = new List<string>();
        var fence = section.IndexOf("```bash\n# scripts/.prod-gate.env\n", StringComparison.Ordinal);
        var example = fence < 0 ? "" : section[fence..section.IndexOf("\n```", fence + 8, StringComparison.Ordinal)];
        foreach (var v in mandatory)
            if (!Regex.IsMatch(example, $@"^{Regex.Escape(v)}=", RegexOptions.Multiline)) missing.Add(v);
        if (Unstruck(section).Contains("المُتَغَيِّرُ الإلزامِيُّ الوَحيد", StringComparison.Ordinal)) missing.Add("«الوَحيد» بِلا شَطب");
        return missing;
    }

    [Fact]
    public void The_gate_configuration_section_names_every_variable_the_gate_requires_in_its_example()
    {
        var mandatory = GateMandatoryVariables(Read("scripts", "verify-production-boot.sh"));
        output.WriteLine($"ما تَشتَرِطُه البَوّابَة: {string.Join(" · ", mandatory)}");
        Assert.True(mandatory.Length >= 2, $"أَداةٌ عَمياء: {mandatory.Length} مُتَغَيِّراً إلزامِيّاً مَقروءاً مِن السكربت.");
        var section = Section(Deploy(), "### التَهيئَة\n", "### ");
        var missing = MissingFromGateConfig(section, mandatory);
        Assert.True(missing.Count == 0, "«التَهيئَة» في §٢·و يَنقُصُها: " + string.Join("، ", missing));
        Assert.Contains("lens-docs-vs-code-3", section, StringComparison.Ordinal);
    }

    /// <summary><b>حَقنُ عَيب</b>: القِسمُ كَما كانَ قَبلَ الإلحاقِ (سَطرٌ واحِدٌ في المِثالِ و«الوَحيد» بِلا شَطب) يُحمِرُّ المُتَحَقِّقَ بِالسِرِّ الغائِبِ وبِالكَلِمَة.</summary>
    [Fact]
    public void The_gate_configuration_checker_goes_red_on_the_section_as_it_was()
    {
        const string before =
            "### التَهيئَة\nالمُتَغَيِّرُ الإلزامِيُّ الوَحيد `ConnectionStrings__Postgres` — نَفسُ\n…\n\n```bash\n# scripts/.prod-gate.env\n" +
            "ConnectionStrings__Postgres=\"$(tr -d '\\r\\n' < /d/Testor/neon-connection.txt)\"\n```\n";
        var missing = MissingFromGateConfig(before, GateMandatoryVariables(Read("scripts", "verify-production-boot.sh")));
        Assert.Contains("ACOMMERCE_AUTH_SECRET", missing);
        Assert.Contains("«الوَحيد» بِلا شَطب", missing);
        Assert.DoesNotContain("ConnectionStrings__Postgres", missing);
    }
}
