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

    // ─── lens-docs-vs-code-6 — §٣ الطَبَقَةُ الثانِيَة: sha الـAPI مُسَطَّحٌ لا يُطابِقُ main ─────────────────────────────────

    private const string LayerTwoClaim = "git rev-parse main        # يَجِب أَن يُطابِق sha";

    /// <summary>أَيَصدُقُ قِسمُ الطَبَقَةِ الثانِيَة؟ — المُقارَنَةُ بِـ<c>sha</c> الـAPI غائِبَةٌ، أَو يَليها تَصحيحٌ يُسَمّي المُسَطَّحَ ويُقارِنُ <c>/health</c> <c>.commit</c>.</summary>
    internal static bool LayerTwoIsTruthful(string section)
    {
        var at = section.IndexOf(LayerTwoClaim, StringComparison.Ordinal);
        if (at < 0) return true;
        var after = section[at..];
        return after.Contains("lens-docs-vs-code-6", StringComparison.Ordinal)
               && after.Contains("الإيداعُ المُسَطَّح", StringComparison.Ordinal)
               && after.Contains("/health | jq -r .commit", StringComparison.Ordinal);
    }

    /// <summary>الوَظيفَةُ تَدفَعُ إيداعاً مُسَطَّحاً وتُقارِنُ <c>sha</c> الـAPI بِه (<c>space_sha</c>) لا بِالمَصدَر — فَالدَليلُ يَقولُ ذلكَ في الطَبَقَةِ الثانِيَة.</summary>
    [Fact]
    public void Layer_two_compares_the_flattened_space_commit_and_points_at_health_for_main()
    {
        var yml = Read(".github", "workflows", "deploy-hf.yml");
        Assert.Contains("git commit -q -m \"deploy: ${sha}\"", yml, StringComparison.Ordinal);
        Assert.Contains("if [ \"${remote_sha}\" != \"${space_sha}\" ]; then", yml, StringComparison.Ordinal);
        var section = Section(Deploy(), "### الطَبَقَة الثانِيَة — بِاليَد", "### ");
        Assert.Contains(LayerTwoClaim, section, StringComparison.Ordinal);
        Assert.True(LayerTwoIsTruthful(section), "الطَبَقَةُ الثانِيَةُ تُقارِنُ sha الـAPI بِـmain بِلا تَصحيح.");
    }

    /// <summary><b>حَقنُ عَيب</b>: القِسمُ كَما كانَ — المُقارَنَةُ بِلا تَصحيح — يُحمِرُّ المُتَحَقِّق؛ وبِلا السَطرِ أَصلاً يَمُرّ.</summary>
    [Fact]
    public void The_layer_two_checker_goes_red_on_the_section_as_it_was()
    {
        var section = Section(Deploy(), "### الطَبَقَة الثانِيَة — بِاليَد", "### ");
        var before = section[..section.IndexOf("**تَصحيحٌ ‏2026-10-25 (تَدقيقُ الإطلاق lens-docs-vs-code-6)", StringComparison.Ordinal)];
        Assert.False(LayerTwoIsTruthful(before));
        Assert.True(LayerTwoIsTruthful("### الطَبَقَة الثانِيَة — بِاليَد\nلا مُقارَنَة\n"));
    }

    // ─── lens-docs-vs-code-7 — `HF_SPACE_URL`: «بِدونِه لا يُقاسُ الحَيُّ ولا يُوقَفُ نَشر» ─────────────────────────────────

    private static readonly string[] HfSpaceUrlClaims =
    [
        "**بِدونِه لا يُقاسُ الحَيُّ ولا يُوقَفُ نَشر**",
        "**وغِيابُه لا يوقِفُ\nنَشراً**",
    ];

    /// <summary>كُلُّ ادِّعاءٍ مِن الاثنَينِ لا يَظهَرُ إلّا مَشطوباً، وفي الدَليلِ تَصحيحٌ يُسَمّي <c>.host</c> و<c>nohost</c> — وإلّا ما يَنقُص.</summary>
    internal static IReadOnlyList<string> UnqualifiedHfSpaceUrlClaims(string deploy)
    {
        var missing = new List<string>();
        var unstruck = Unstruck(deploy);
        foreach (var c in HfSpaceUrlClaims)
            if (unstruck.Contains(c, StringComparison.Ordinal)) missing.Add(c);
        foreach (var word in new[] { "lens-docs-vs-code-7", "`.host`", "`nohost`" })
            if (!unstruck.Contains(word, StringComparison.Ordinal)) missing.Add(word);
        return missing;
    }

    [Fact]
    public void Hf_space_url_absence_is_said_as_the_workflow_does_it()
    {
        var yml = Read(".github", "workflows", "deploy-hf.yml");
        Assert.Contains("host_src=\"huggingface API .host\"", yml, StringComparison.Ordinal);
        Assert.Contains("base_state=\"nohost\"", yml, StringComparison.Ordinal);
        Assert.Contains("if [ \"${state}\" != \"measured\" ] || [ -z \"${live}\" ]; then", yml, StringComparison.Ordinal);
        Assert.Contains("echo \"decision=refuse\"", yml, StringComparison.Ordinal);
        Assert.Contains("lens-docs-vs-code-7", yml, StringComparison.Ordinal);
        var missing = UnqualifiedHfSpaceUrlClaims(Deploy());
        Assert.True(missing.Count == 0, "ادِّعاءُ HF_SPACE_URL بِلا تَصحيح: " + string.Join("، ", missing));
    }

    /// <summary><b>حَقنُ عَيب</b>: الدَليلُ بِالجُملَتَينِ بِلا شَطبٍ ولا تَصحيحٍ يُحمِرُّ المُتَحَقِّق.</summary>
    [Fact]
    public void The_hf_space_url_checker_goes_red_on_the_claims_as_they_were()
    {
        var sample = "| `HF_SPACE_URL` | " + HfSpaceUrlClaims[0] + " |\n…\n" + HfSpaceUrlClaims[1] + ": تُحَذِّرُ الخُطوَةُ\n";
        var missing = UnqualifiedHfSpaceUrlClaims(sample);
        Assert.Contains(HfSpaceUrlClaims[0], missing);
        Assert.Contains(HfSpaceUrlClaims[1], missing);
        Assert.Contains("`nohost`", missing);
    }
}
