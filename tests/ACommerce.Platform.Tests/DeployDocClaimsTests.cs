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

    // ─── lens-docs-vs-code-9 — كُلُّ مُتَغَيِّرٍ يَقرَؤُه الكودُ بِاسمِه في دَليلِ النَشر ─────────────────────────────────────────

    private static readonly Regex EnvRead = new(@"GetEnvironmentVariable\(""(?<n>[A-Za-z_][A-Za-z0-9_]*)""\)", RegexOptions.Compiled);
    private static readonly Regex EnvConst = new(@"""(?<n>(?:ACOMMERCE|PLATFORM)_[A-Z0-9_]+)""", RegexOptions.Compiled);

    /// <summary>أَسماءُ مُتَغَيِّراتِ البيئَةِ الَّتي يَقرَؤُها مَصدَرٌ بِحَرفِها — <c>GetEnvironmentVariable("…")</c> وكُلُّ حَرفِيَّةٍ <c>ACOMMERCE_*</c>/<c>PLATFORM_*</c> —
    /// بِلا أَسطُرِ التَعليق (<c>//</c> و<c>///</c>).</summary>
    internal static IEnumerable<string> EnvironmentNamesIn(string source)
    {
        foreach (var raw in source.Split('\n'))
        {
            var line = raw.TrimStart();
            if (line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("*", StringComparison.Ordinal)) continue;
            foreach (Match m in EnvRead.Matches(line)) yield return m.Groups["n"].Value;
            foreach (Match m in EnvConst.Matches(line)) yield return m.Groups["n"].Value;
        }
    }

    /// <summary>كُلُّ مِلَفِّ <c>.cs</c> تَحتَ <c>libs/</c> و<c>apps/</c> (بِلا <c>bin/</c> و<c>obj/</c>) — الكودُ الَّذي يُشحَن.</summary>
    private static IEnumerable<string> ShippedSources()
        => new[] { "libs", "apps" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(RepoRoot, d), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>ما يَغيبُ عَن الدَليلِ مِن تِلكَ الأَسماء — بِحَرفِها بَينَ عَلامَتَي <c>`</c> أَو في أَوَّلِ <c>`NAME=</c>.</summary>
    internal static IReadOnlyList<string> UndocumentedEnvironmentNames(IEnumerable<string> names, string deploy)
        => names.Distinct().Order(StringComparer.Ordinal)
            .Where(n => !deploy.Contains($"`{n}`", StringComparison.Ordinal) && !deploy.Contains($"`{n}=", StringComparison.Ordinal))
            .ToList();

    // ─── مُراجَعَةُ تَيّارِ W8-6 الكُبرى ٢ (‏2026-10-25) — القِراءَةُ عَبرَ ثابِت ─────────────────────────────────────────
    //
    // ‏الاسمُ يَقولُ «كُلُّ مُتَغَيِّرٍ يَقرَؤُه الكودُ بِاسمِه»، والماسِحُ أَعلاه لا يَرى إلّا `GetEnvironmentVariable("حَرفِيَّة")` وحَرفِيّاتِ
    // `ACOMMERCE_*`/`PLATFORM_*` — فَقِراءَةٌ عَبرَ ثابِتٍ بِبادِئَةٍ أُخرى خَفِيَّة. وواحِدَةٌ قائِمَةٌ ولا يُسَمّيها الدَليل:
    // `Environment.GetEnvironmentVariable(AppearanceBaselineSeeder.EnvVar)` في `Program.cs` و`EnvVar = "APPEARANCE_BASELINE_SEED"` — بَذّارٌ رابِعٌ
    // يَكتُبُ في القاعِدَة، وخارِجَ Development يَرمي `InvalidOperationException` عِندَ الإقلاع. و`Providers__Kek__v1` يُسَمّيه
    // `ProviderVault.KekEnvironmentVariable` ويُقرَأُ عَبرَ `IConfiguration` — يَمُرُّ لِأَنّه مُسَمّىً في مَوضِعٍ آخَرَ لا لِأَنّ الماسِحَ رَآه.
    // فَصارَ الوَسيطُ يُحَلُّ: كُلُّ `const string X = "…"` (أَو `= Y.Z` يُتبَعُ) في `libs/` و`apps/`، و`GetEnvironmentVariable(X)` و`(T.X)` تُرَدُّ إلى
    // قيمَتِها — وما لا يُحَلُّ يُعَدُّ ويُحمِرّ؛ وثابِتٌ يُسَمّي نَفسَه مُتَغَيِّرَ بيئَةٍ (`…EnvironmentVariable`/`…EnvVar`/`…EnvVarName`) يُعَدُّ قِراءَة.

    private static readonly Regex ConstDecl = new(
        @"\bconst\s+string\s+(?<n>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<v>""[^""\\]*""|[A-Za-z_][A-Za-z0-9_.]*)\s*;", RegexOptions.Compiled);
    private static readonly Regex TypeDecl = new(@"\b(?:class|record|struct|interface)\s+(?<t>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
    private static readonly Regex EnvReadThrough = new(@"GetEnvironmentVariable\(\s*(?<e>[A-Za-z_][A-Za-z0-9_.]*)\s*\)", RegexOptions.Compiled);
    private static readonly Regex SelfNamedEnvConst = new(@"(?:EnvironmentVariable|EnvVar|EnvVarName)$", RegexOptions.Compiled);

    /// <summary>ثابِتٌ نَصِّيٌّ بِمِلَفِّه وأَنواعِ مِلَفِّه — قيمَتُه حَرفِيَّةٌ أَو إحالَةٌ إلى ثابِتٍ آخَر.</summary>
    internal sealed record StringConstant(string File, IReadOnlySet<string> FileTypes, string Name, string Value);

    /// <summary>ما يَخرُجُ بِه ماسِحُ الثَوابِت: الأَسماءُ المَحلولَة، وكُلُّ قِراءَةٍ بِمَوضِعِها وما حُلَّت إلَيه، وما لَم يُحَلّ.</summary>
    internal sealed record ConstantEnvScan(IReadOnlyList<string> Names, IReadOnlyList<string> Reads, IReadOnlyList<string> SelfNamed, IReadOnlyList<string> Unresolved);

    private static IEnumerable<(int No, string Line)> CodeLines(string source)
        => source.Split('\n').Select((l, i) => (i + 1, l.TrimStart()))
            .Where(x => !x.Item2.StartsWith("//", StringComparison.Ordinal) && !x.Item2.StartsWith("*", StringComparison.Ordinal));

    /// <summary>يَحُلُّ قِراءاتِ <c>GetEnvironmentVariable(X)</c> و<c>(T.X)</c> عَبرَ ثَوابِتِ المَصادِرِ كُلِّها، ويَعُدُّ الثَوابِتَ الَّتي تُسَمّي نَفسَها مُتَغَيِّرَ بيئَة.</summary>
    internal static ConstantEnvScan ConstantEnvironmentReads(IReadOnlyList<(string File, string Text)> sources)
    {
        var constants = new List<StringConstant>();
        foreach (var (file, text) in sources)
        {
            var types = CodeLines(text).SelectMany(x => TypeDecl.Matches(x.Line)).Select(m => m.Groups["t"].Value).ToHashSet(StringComparer.Ordinal);
            foreach (var (_, line) in CodeLines(text))
                foreach (Match m in ConstDecl.Matches(line))
                    constants.Add(new StringConstant(file, types, m.Groups["n"].Value, m.Groups["v"].Value));
        }

        string? Resolve(string expr, string file, int depth)
        {
            if (expr.StartsWith('"')) return expr.Trim('"');
            if (depth > 6) return null;
            var parts = expr.Split('.');
            var name = parts[^1];
            var type = parts.Length > 1 ? parts[^2] : null;
            var hits = type is not null
                ? constants.Where(c => c.Name == name && c.FileTypes.Contains(type)).ToList()
                : constants.Where(c => c.Name == name && c.File == file).ToList();
            if (hits.Count == 0 && type is null) hits = constants.Where(c => c.Name == name).ToList();
            var values = hits.Select(c => Resolve(c.Value, c.File, depth + 1)).Distinct().ToList();
            return values.Count == 1 ? values[0] : null;
        }

        var names = new List<string>();
        var reads = new List<string>();
        var unresolved = new List<string>();
        foreach (var (file, text) in sources)
            foreach (var (no, line) in CodeLines(text))
                foreach (Match m in EnvReadThrough.Matches(line))
                {
                    var expr = m.Groups["e"].Value;
                    var value = Resolve(expr, file, 0);
                    if (value is null) { unresolved.Add($"{file}:{no} {expr}"); continue; }
                    names.Add(value);
                    reads.Add($"{expr}→{value}");
                }
        var selfNamed = new List<string>();
        foreach (var c in constants.Where(c => SelfNamedEnvConst.IsMatch(c.Name)))
        {
            var value = Resolve(c.Value, c.File, 0);
            if (value is null) { unresolved.Add($"{c.File} const {c.Name}"); continue; }
            names.Add(value);
            selfNamed.Add($"{c.Name}→{value}");
        }
        return new ConstantEnvScan(names.Distinct().Order(StringComparer.Ordinal).ToList(), reads.Distinct().Order(StringComparer.Ordinal).ToList(),
            selfNamed.Distinct().Order(StringComparer.Ordinal).ToList(), unresolved);
    }

    [Fact]
    public void Every_environment_variable_the_code_reads_by_name_is_named_in_the_deploy_guide()
    {
        var sources = ShippedSources().Select(f => (Path.GetRelativePath(RepoRoot, f), File.ReadAllText(f))).ToList();
        var literal = sources.SelectMany(x => EnvironmentNamesIn(x.Item2)).Distinct().Order(StringComparer.Ordinal).ToList();
        var scan = ConstantEnvironmentReads(sources);
        var names = literal.Concat(scan.Names).Distinct().Order(StringComparer.Ordinal).ToList();
        output.WriteLine($"مُتَغَيِّراتٌ يَقرَؤُها الكودُ بِاسمِها: {names.Count} — {string.Join(" · ", names)}");
        output.WriteLine($"  مِنها بِحَرفِيَّة: {literal.Count}؛ وقِراءاتٌ عَبرَ ثابِت: {scan.Reads.Count} — {string.Join(" · ", scan.Reads)}");
        output.WriteLine($"  وثَوابِتُ تُسَمّي نَفسَها مُتَغَيِّرَ بيئَة: {scan.SelfNamed.Count} — {string.Join(" · ", scan.SelfNamed)}");
        // ‏(‏2026-10-25، مُراجَعَةُ تَيّارِ W8-6 الكُبرى ٢): الأَرضِيَّةُ المَقيسَةُ يَومَها — 18 اسماً (13 بِحَرفِيَّة)، و9 قِراءاتٍ عَبرَ ثابِت،
        // و8 ثَوابِتَ تُسَمّي نَفسَها؛ وكانَت 10 لِلحَرفِيّاتِ وَحدَها.
        Assert.True(names.Count >= 18, $"أَداةٌ عَمياء: {names.Count} اسماً (المَقيسُ 18 يَومَ الإلحاق).");
        Assert.True(scan.Reads.Count >= 9, $"أَداةٌ عَمياء: {scan.Reads.Count} قِراءَةً عَبرَ ثابِت (المَقيسُ 9 يَومَ الإلحاق).");
        Assert.True(scan.SelfNamed.Count >= 8, $"أَداةٌ عَمياء: {scan.SelfNamed.Count} ثابِتاً يُسَمّي نَفسَه (المَقيسُ 8 يَومَ الإلحاق).");
        Assert.True(scan.Unresolved.Count == 0, "قِراءَةُ بيئَةٍ بِوَسيطٍ لا يُحَلُّ إلى ثابِت: " + string.Join("، ", scan.Unresolved));
        foreach (var known in new[] { "ACOMMERCE_AUTH_SECRET", "ACOMMERCE_BASE_DOMAIN", "ACOMMERCE_FORCE_INSECURE_COOKIES", "PLATFORM_ADMIN_BOOTSTRAP", "CART_SAMPLE_PHONE",
                                      "APPEARANCE_BASELINE_SEED", "Providers__Kek__v1", "ACOMMERCE_OTP_SENDS_PER_HOUR" })
            Assert.Contains(known, names);
        var missing = UndocumentedEnvironmentNames(names, Deploy());
        Assert.True(missing.Count == 0, "يَقرَؤُها الكودُ ولا يُسَمّيها DEPLOY.md: " + string.Join("، ", missing));
        // ‏والمُضعِفُ لِلكوكي في قائِمَةِ ما لا يوضَعُ في الإنتاج.
        var never = Deploy()[Deploy().IndexOf("**ويُضَمُّ إلى «لا تُوضَع في الإنتاج أَبَداً» أَعلاه**", StringComparison.Ordinal)..];
        Assert.Contains("`ACOMMERCE_FORCE_INSECURE_COOKIES`", never[..400], StringComparison.Ordinal);
        // ‏وبَذّارُ اللَقطَةِ رابِعُ «لا تُوضَع في الإنتاج أَبَداً»، والسَطرُ يَقولُ إنّه يَرمي عِندَ الإقلاعِ خارِجَ Development.
        var fourth = Deploy().IndexOf("ورابِعُ بَذّارٍ يُضَمُّ إلى «لا تُوضَع في الإنتاج أَبَداً»**", StringComparison.Ordinal);
        Assert.True(fourth > 0, "سَطرُ APPEARANCE_BASELINE_SEED تَحتَ «لا تُوضَع في الإنتاج أَبَداً» غائِب.");
        var row = Deploy()[fourth..(fourth + 700)];
        Assert.Contains("`APPEARANCE_BASELINE_SEED`", row, StringComparison.Ordinal);
        Assert.Contains("`InvalidOperationException`", row, StringComparison.Ordinal);
    }

    /// <summary><b>حَقنُ عَيب</b> (‏2026-10-25، مُراجَعَةُ تَيّارِ W8-6 الكُبرى ٢): ثابِتٌ بِلا بادِئَةٍ يُقرَأُ عَبرَ إحالَتَين في مِلَفٍّ ثالِث —
    /// الماسِحُ الحَرفِيُّ أَعلاه أَعمى عَنه، وماسِحُ الثَوابِتِ يَحُلُّه ويَعُدُّه غائِباً؛ ووَسيطٌ لا يُحَلُّ يُعَدُّ ولا يُبتلَع.</summary>
    [Fact]
    public void The_constant_scanner_resolves_an_unprefixed_read_through_indirection()
    {
        var sources = new List<(string, string)>
        {
            ("Seed/SampleSwitch.cs", "public static class SampleSwitch\n{\n    public const string Flag = \"WASAYEL_INJECTED_SEED\";\n}\n"),
            ("Seed/Alias.cs", "public static class Alias\n{\n    public const string Var = SampleSwitch.Flag;\n}\n"),
            ("Program.cs", "if (Environment.GetEnvironmentVariable(Alias.Var) == \"1\") { }\n" +
                           "var x = Environment.GetEnvironmentVariable(localName);\n" +
                           "// Environment.GetEnvironmentVariable(Alias.InAComment)\n"),
            ("Vault.cs", "public static class Vault\n{\n    public const string KeyEnvironmentVariable = \"Wasayel__Injected__Key\";\n}\n"),
        };
        Assert.Empty(sources.SelectMany(x => EnvironmentNamesIn(x.Item2)));
        var scan = ConstantEnvironmentReads(sources);
        Assert.Equal(new[] { "Alias.Var→WASAYEL_INJECTED_SEED" }, scan.Reads);
        Assert.Equal(new[] { "KeyEnvironmentVariable→Wasayel__Injected__Key" }, scan.SelfNamed);
        Assert.Equal(new[] { "Program.cs:2 localName" }, scan.Unresolved);
        Assert.Equal(new[] { "WASAYEL_INJECTED_SEED", "Wasayel__Injected__Key" }, UndocumentedEnvironmentNames(scan.Names, "لا شَيء"));
        Assert.Equal(new[] { "Wasayel__Injected__Key" }, UndocumentedEnvironmentNames(scan.Names, "`WASAYEL_INJECTED_SEED=1`"));
    }

    /// <summary><b>حَقنُ عَيب</b>: مَصدَرٌ مَصنوعٌ يَقرَأُ مُتَغَيِّرَينِ جَديدَين — بِـ<c>GetEnvironmentVariable</c> وبِثابِت — يُجمَعانِ ويُعَدّانِ غائِبَين، وسَطرُ التَعليقِ
    /// لا يُعَدّ؛ ودَليلٌ يُسَمّي أَحَدَهُما يُنقِصُ الغائِبَ واحِداً.</summary>
    [Fact]
    public void The_environment_name_scanner_goes_red_on_an_injected_undocumented_read()
    {
        const string source =
            "var a = Environment.GetEnvironmentVariable(\"WASAYEL_INJECTED_FLAG\");\n" +
            "public const string Var = \"ACOMMERCE_INJECTED_LIMIT\";\n" +
            "// Environment.GetEnvironmentVariable(\"ACOMMERCE_ONLY_IN_A_COMMENT\")\n";
        var names = EnvironmentNamesIn(source).ToList();
        Assert.Equal(new[] { "WASAYEL_INJECTED_FLAG", "ACOMMERCE_INJECTED_LIMIT" }, names);
        Assert.Equal(2, UndocumentedEnvironmentNames(names, "لا شَيء").Count);
        Assert.Equal(new[] { "WASAYEL_INJECTED_FLAG" }, UndocumentedEnvironmentNames(names, "| `ACOMMERCE_INJECTED_LIMIT` |"));
    }

    // ─── brief-P3-8 / lens-docs-vs-code-8 — §٦ بِأَرقامٍ مُؤَرَّخَةٍ ووَسمَينِ مُثَبَّتَين، وجَدوَلُ «الاختِيارِيَّة» بِلا سِرٍّ إلزامِيّ ─────────────

    /// <summary>وَسما <c>FROM</c> في الـ<c>Dockerfile</c> بِحَرفِهِما — <c>sdk:X</c> و<c>aspnet:Y</c>.</summary>
    internal static string[] DockerfileTags()
        => Regex.Matches(Read("Dockerfile"), @"^FROM mcr\.microsoft\.com/dotnet/(?<t>(?:sdk|aspnet):[0-9.]+)", RegexOptions.Multiline)
            .Select(m => m.Groups["t"].Value).ToArray();

    /// <summary>صُفوفُ جَدوَلِ §٢ الخُطوَة ٦ «مُتَغَيِّرات اختِيارِيَّة» — مِن عُنوانِها إلى أَوَّلِ سَطرٍ لَيسَ صَفّاً بَعدَ الجَدوَل.</summary>
    internal static string[] OptionalTableRows(string deploy)
    {
        var section = Section(deploy, "### ٦) مُتَغَيِّرات اختِيارِيَّة في الـSpace", "### ");
        var lines = section.Split('\n');
        var start = Array.FindIndex(lines, l => l.StartsWith("| المُتَغَيِّر |", StringComparison.Ordinal));
        Assert.True(start > 0, "جَدوَلُ «مُتَغَيِّرات اختِيارِيَّة» غائِب.");
        return lines.Skip(start + 2).TakeWhile(l => l.StartsWith('|')).ToArray();
    }

    [Fact]
    public void Section_six_dates_its_figures_and_names_the_pinned_tags_and_the_optional_table_holds_no_mandatory_secret()
    {
        var tags = DockerfileTags();
        output.WriteLine($"وَسما الـDockerfile: {string.Join(" · ", tags)}");
        Assert.Equal(2, tags.Length);
        var deploy = Deploy();
        var six = Section(deploy, "## ٦. حُدودُ ما تُحُقِّقَ مِنه", "## ");
        var fix = six[six.IndexOf("brief-P3-8", StringComparison.Ordinal)..];
        foreach (var t in tags) Assert.Contains($"`mcr.microsoft.com/dotnet/{t}`", fix, StringComparison.Ordinal);
        var runBlocks = Regex.Matches(Read(".github", "workflows", "deploy-hf.yml"), @"^\s*run: \|\s*$", RegexOptions.Multiline).Count;
        output.WriteLine($"كُتَلُ run: | في deploy-hf.yml: {runBlocks}");
        Assert.True(runBlocks > 0, "أَداةٌ عَمياء: صِفرُ كُتلَة run.");
        Assert.Contains(runBlocks switch { 7 => "سَبعٌ", _ => $"{runBlocks}" }, fix, StringComparison.Ordinal);
        Assert.Contains("‏2026-08-22", fix, StringComparison.Ordinal);

        var rows = OptionalTableRows(deploy);
        output.WriteLine($"صُفوفُ «الاختِيارِيَّة»: {rows.Length}");
        Assert.True(rows.Length >= 5, $"أَداةٌ عَمياء: {rows.Length} صَفّاً.");
        Assert.DoesNotContain(rows, r => r.Contains("**إلزاميٌّ خارِجَ Development**", StringComparison.Ordinal));
        Assert.DoesNotContain(rows, r => r.StartsWith("| `ACOMMERCE_AUTH_SECRET` |", StringComparison.Ordinal));
        // ‏وصَفُّه في الخُطوَةِ ٤ قائِمٌ — الإحالَةُ لا تُشيرُ إلى فَراغ.
        Assert.Contains("| `ACOMMERCE_AUTH_SECRET` | سِرُّ تَوقيعِ كوكي الجَلسَة", Section(deploy, "### ٤) سِرّا الإنتاجِ الإلزامِيّان", "### "), StringComparison.Ordinal);
    }

    /// <summary><b>حَقنُ عَيب</b>: جَدوَلٌ مَصنوعٌ بِالصَفِّ القَديمِ تَحتَ العُنوانِ نَفسِه — قارِئُ الصُفوفِ يَجِدُه بِعَلامَتِه، فَالصَفُّ أَعلاه لا يَمُرُّ أَعمى.</summary>
    [Fact]
    public void The_optional_table_reader_finds_the_old_mandatory_secret_row()
    {
        const string sample =
            "### ٦) مُتَغَيِّرات اختِيارِيَّة في الـSpace\nمَقيسَة:\n\n| المُتَغَيِّر | الأَثَر | المَوضِع |\n|---|---|---|\n" +
            "| `PLATFORM_ADMIN_PHONE` | يَمنَح | x |\n| `ACOMMERCE_AUTH_SECRET` | **إلزاميٌّ خارِجَ Development** — سِرّ | y |\n\nبَعدَه\n### ٧) تالٍ\n";
        var rows = OptionalTableRows(sample);
        Assert.Equal(2, rows.Length);
        Assert.Contains(rows, r => r.Contains("**إلزاميٌّ خارِجَ Development**", StringComparison.Ordinal));
    }

    // ─── brief-P2-3 — §٢·ج «ويَراهُ صاحِبُه في الاستوديو أَيضاً» ─────────────────────────────────────────────────────────

    /// <summary>أَيُرسَمُ زِرُّ PayPal في الاستوديو لِلسارِيَةِ أَيضاً؟ — يُقرَأُ مِن <c>StudioHome.razor</c>: الفَرعُ <c>PayPalUrl(…)</c> داخِلَ حَلقَةِ
    /// <c>Renewable</c>، و<c>Renewable</c> لا يَسأَلُ الرابِط ⇒ لا.</summary>
    internal static bool StudioDrawsPayPalOnlyWhenRenewable(string razor)
    {
        var loop = razor.IndexOf("@foreach (var t in myApps.Where(a => Renewable(a.Slug)))", StringComparison.Ordinal);
        var list = razor.IndexOf("<div class=\"studio-list\">", loop < 0 ? 0 : loop, StringComparison.Ordinal);
        var button = razor.IndexOf("PayPalUrl(t.Slug) is { } payUrl", StringComparison.Ordinal);
        var renewable = razor.IndexOf("bool Renewable(string slug)", StringComparison.Ordinal);
        var body = renewable < 0 ? "" : razor[renewable..razor.IndexOf(';', renewable)];
        return loop >= 0 && list > loop && button > loop && button < list && !body.Contains("PayPalUrl", StringComparison.Ordinal);
    }

    [Fact]
    public void The_paypal_section_says_the_studio_draws_the_link_only_once_the_plan_leaves_active()
    {
        var razor = Read("libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "StudioHome.razor");
        Assert.True(StudioDrawsPayPalOnlyWhenRenewable(razor), "زِرُّ PayPal في الاستوديو لَم يَعُد مَشروطاً بِـRenewable — صَحِّح سَطرَ §٢·ج.");
        var section = Section(Deploy(), "## ٢·ج. PayPal", "## ");
        var claim = section.IndexOf("(ويَراهُ صاحِبُه في الاستوديو أَيضاً)", StringComparison.Ordinal);
        Assert.True(claim > 0, "سَطرُ «ويَراهُ صاحِبُه في الاستوديو» غائِب.");
        var after = section[claim..];
        Assert.Contains("brief-P2-3", after, StringComparison.Ordinal);
        Assert.Contains("فَما دامَت الباقَةُ سارِيَةً لا يَرى صاحِبُ المَتجَرِ الرابِطَ", after, StringComparison.Ordinal);
    }

    /// <summary><b>حَقنُ عَيب</b>: صَفحَةٌ مَصنوعَةٌ يَسأَلُ فيها <c>Renewable</c> الرابِطَ — المُتَحَقِّقُ يَقولُ «لا» فَيَحمَرُّ الصَفُّ أَعلاه حينَ يَتَبَدَّلُ السُلوك.</summary>
    [Fact]
    public void The_paypal_studio_reader_goes_red_when_renewable_asks_the_link()
    {
        const string changed =
            "@foreach (var t in myApps.Where(a => Renewable(a.Slug)))\n{ else if (PayPalUrl(t.Slug) is { } payUrl) { } }\n<div class=\"studio-list\">\n" +
            "bool Renewable(string slug) => PlanState(slug) is Grace || PayPalUrl(slug) is not null;\n";
        Assert.False(StudioDrawsPayPalOnlyWhenRenewable(changed));
        Assert.True(StudioDrawsPayPalOnlyWhenRenewable(changed.Replace("|| PayPalUrl(slug) is not null", "", StringComparison.Ordinal)));
    }

    // ─── brief-P2-2 — §٢·هـ البَند ٧: «وهي قائِمَةٌ» والوَثائِقُ الخَمسُ نَوائِب ─────────────────────────────────────────────

    /// <summary>البَندُ ٧ مِن «ما لَم يُبرهَن» في §٢·هـ (‏Paddle) — مِن رَأسِه إلى رَأسِ البَندِ ٨.</summary>
    internal static string PaddleLiveApprovalItem(string deploy)
    {
        var section = Section(deploy, "## ٢·هـ. Paddle", "## ");
        var a = section.IndexOf("7. **اعتِمادُ الوَضعِ المُباشِر**", StringComparison.Ordinal);
        Assert.True(a > 0, "البَندُ ٧ مِن §٢·هـ غائِب — الأَداةُ عَمياء.");
        var b = section.IndexOf("\n8. ", a, StringComparison.Ordinal);
        Assert.True(b > a, "البَندُ ٨ مِن §٢·هـ غائِب — الأَداةُ عَمياء.");
        return section[a..b];
    }

    /// <summary>أَيَقولُ البَندُ إنّ الوَثائِقَ الخَمسَ ما زالَت نَوائِبَ، ويُحيلُ إلى خُطوَةِ مَلئِها ومَفاتيحِها؟</summary>
    internal static bool SaysTheDocumentsStillHoldPlaceholders(string item)
        => item.Contains("brief-P2-2", StringComparison.Ordinal)
           && item.Contains("`[[ … يَملَؤُه المالِك ]]`", StringComparison.Ordinal)
           && item.Contains("`PlatformDocFields.EntityKeys`", StringComparison.Ordinal)
           && item.Contains("§٢ الخُطوَة ٧", StringComparison.Ordinal);

    /// <summary>
    /// <para><b>«وهي قائِمَةٌ» في §٢·هـ البَند ٧ لا تَمُرُّ وَحدَها ما دامَ مِفتاحُ كِيانٍ نائِباً</b> (‏2026-10-25، تَدقيقُ الإطلاق brief-P2-2): البَندُ
    /// يَعُدُّ الوَثائِقَ الخَمسَ شَرطَ اعتِمادِ Paddle المُباشِرِ ويَقولُها «قائِمَة»، وقيَمُها في القاموسِ نَوائِب. والعَدُّ مِن
    /// <c>PlatformDocFields.UnfilledCount</c> — ما يَعُدُّه <c>/admin</c> — لا مَكتوبٌ هُنا؛ فَيَومَ تُملَأُ الخَمسَةُ يَصيرُ السَطرُ سِجِلّاً
    /// مُؤَرَّخاً ولا يَحمَرُّ شَيء، وما دامَ نائِبٌ واحِدٌ فَغِيابُ السَطرِ أَحمَر. والنَصُّ الأَوَّلُ باقٍ بِحَرفِه (إلحاقٌ لا إعادَةُ كِتابَة).</para>
    /// </summary>
    [Fact]
    public void The_paddle_live_approval_item_says_the_five_documents_still_hold_placeholders_while_any_does()
    {
        var unfilled = ACommerce.Templates.Customer.Marketplace.Services.PlatformDocFields.UnfilledCount(ACommerce.Platform.I18n.LocaleCatalog.Arabic);
        var keys = ACommerce.Templates.Customer.Marketplace.Services.PlatformDocFields.EntityKeys.Count;
        var item = PaddleLiveApprovalItem(Deploy());
        var says = SaysTheDocumentsStillHoldPlaceholders(item);
        output.WriteLine($"مَفاتيحُ الكِيان: {keys} · نائِبٌ مِنها: {unfilled} · البَندُ ٧ ({item.Length} مِحرَفاً) يَقولُ النَوائِب: {says}");
        Assert.True(keys >= 5, $"أَداةٌ عَمياء: {keys} مِفتاحَ كِيان.");
        Assert.Contains("وهي قائِمَةٌ ويَبلُغُها الزائِرُ مِن التَذييل", item, StringComparison.Ordinal);
        if (unfilled > 0)
            Assert.True(says, $"{unfilled} مِن مَفاتيحِ الكِيانِ نائِبٌ، والبَندُ ٧ في §٢·هـ يَقولُ «وهي قائِمَة» بِلا سَطرِ brief-P2-2.");
    }

    /// <summary><b>حَقنُ عَيب</b>: البَندُ ٧ بِحَرفِه عِندَ <c>dace7cd</c> (قَبلَ السَطرِ المُؤَرَّخ) — القارِئُ يَجِدُه ويَقولُ «لا»، فَالصَفُّ أَعلاه
    /// يَحمَرُّ عَلَيه ما دامَ نائِبٌ، ولا يَمُرُّ أَعمى.</summary>
    [Fact]
    public void The_placeholder_reader_goes_red_on_the_approval_item_as_it_was()
    {
        const string asItWas =
            "7. **اعتِمادُ الوَضعِ المُباشِر** — نِطاقاً ومُنتَجاً — قَرارُ Paddle\n   لا قَرارُنا. والوَثائِقُ الخَمسُ شَرطٌ مُعلَنٌ في مَركَزِ\n" +
            "   مُساعَدَتِها، وهي قائِمَةٌ ويَبلُغُها الزائِرُ مِن التَذييل — **ولا\n   يُدَّعى أَنّ ذلك يَكفي**.\n";
        var deploy = "## ٢·هـ. Paddle — عَيِّنَة\n\n" + asItWas + "8. **تالٍ**\n\n## ٢·و. تالٍ\n";
        var item = PaddleLiveApprovalItem(deploy);
        Assert.Contains("وهي قائِمَةٌ ويَبلُغُها الزائِرُ مِن التَذييل", item, StringComparison.Ordinal);
        Assert.False(SaysTheDocumentsStillHoldPlaceholders(item));
        Assert.True(SaysTheDocumentsStillHoldPlaceholders(item +
            "   (brief-P2-2) نَوائِبَ `[[ … يَملَؤُه المالِك ]]` — `PlatformDocFields.EntityKeys` — §٢ الخُطوَة ٧\n"));
    }

    // ─── W8-8 الجَولَة ١ — §٢·هـ البَند ٧: «الوَثائِقُ الخَمسُ تُرسَمُ بِنَوائِبِها» أَربَعٌ مِنها ────────────────────────────────

    /// <summary>صَفَحاتُ الوَثائِقِ الخَمسِ بِمَساراتِها — كَما يُعَدِّدُها البَندُ ٧ (الشُروط، الخُصوصِيَّة، الاستِرداد، الأَسعار، التَواصُل).</summary>
    internal static readonly (string Route, string File)[] PlatformDocPages =
    {
        ("/terms", "PlatformTerms.razor"), ("/privacy", "PlatformPrivacy.razor"), ("/refunds", "PlatformRefunds.razor"),
        ("/pricing", "PlatformPricing.razor"), ("/contact", "PlatformContact.razor"),
    };

    /// <summary>أَيَرسُمُ المَصدَرُ مِفتاحاً مِن <paramref name="entityKeys"/> — بِـ<c>.Markup("…")</c> أَو بِالمُفَهرِسِ <c>["…"]</c>؛ والسُؤالُ
    /// <c>IsPlaceholder("…")</c> وَحدَه لا يَرسُمُ شَيئاً.</summary>
    internal static bool DrawsEntityKey(string razor, IEnumerable<string> entityKeys)
        => entityKeys.Any(k => Regex.IsMatch(razor, @"(?:\.Markup\(\s*|\[\s*)""" + Regex.Escape(k) + @"""", RegexOptions.CultureInvariant));

    /// <summary>
    /// <para><b>«الخَمسُ تُرسَمُ بِنَوائِبِها» أَربَعٌ مِنها</b> (‏2026-10-25، مُراجَعَةُ تَيّارِ W8-8 الجَولَة ١): الشُروطُ والخُصوصِيَّةُ والاستِردادُ
    /// والتَواصُلُ تَرسُمُ مِفتاحَ كِيانٍ، و<c>/pricing</c> يَسأَلُ <c>IsPlaceholder</c> لِيُخفيَ بابَ التَواصُل ولا يَرسُمُ مِفتاحاً. والبَندُ ٧ يَحمِلُ
    /// الدِقَّةَ ما دامَ العَدُّ كَذلِك — ويَحمَرُّ يَومَ يَرسُمُ <c>/pricing</c> نائِباً أَو تَكُفُّ صَفحَةٌ عَن رَسمِه، فَيُعادُ القِياس.</para>
    /// </summary>
    [Fact]
    public void Four_of_the_five_documents_draw_entity_placeholders_and_pricing_hides_its_contact_door_instead()
    {
        var keys = ACommerce.Templates.Customer.Marketplace.Services.PlatformDocFields.EntityKeys;
        var drawn = new List<string>();
        foreach (var (route, file) in PlatformDocPages)
        {
            var razor = Read("libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "Platform", file);
            Assert.Contains($"@page \"{route}\"", razor, StringComparison.Ordinal);
            if (DrawsEntityKey(razor, keys)) drawn.Add(route);
        }
        output.WriteLine($"صَفَحاتُ الوَثائِق: {PlatformDocPages.Length} · تَرسُمُ مِفتاحَ كِيان: {drawn.Count} ({string.Join("، ", drawn)})");
        Assert.True(keys.Count >= 5, $"أَداةٌ عَمياء: {keys.Count} مِفتاحَ كِيان.");
        Assert.Equal(new[] { "/terms", "/privacy", "/refunds", "/contact" }, drawn);

        var pricing = Read("libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "Platform", "PlatformPricing.razor");
        Assert.Contains("L.IsPlaceholder(\"platform.doc.contact_email\")", pricing, StringComparison.Ordinal);

        var item = PaddleLiveApprovalItem(Deploy());
        Assert.Contains("مُراجَعَةُ تَيّارِ W8-8 الجَولَة ١", item, StringComparison.Ordinal);
        Assert.Contains("**أَربَعٌ مِن الخَمسِ** (الشُروط، الخُصوصِيَّة، الاستِرداد، التَواصُل)", item, StringComparison.Ordinal);
        Assert.Contains("`/pricing` لا\n   يَرسُمُ نائِباً بَل يُخفي بابَ التَواصُل", item, StringComparison.Ordinal);
    }

    /// <summary><b>حَقنُ عَيب</b>: <c>/pricing</c> يَرسُمُ البَريدَ بِـ<c>Markup</c> أَو بِالمُفَهرِس ⇒ يُعَدّ؛ والسُؤالُ وَحدَه ومِفتاحٌ لَيسَ مِن الكِيان ⇒ لا.</summary>
    [Fact]
    public void The_entity_key_drawer_reader_sees_markup_and_indexer_and_not_a_bare_placeholder_question()
    {
        var keys = ACommerce.Templates.Customer.Marketplace.Services.PlatformDocFields.EntityKeys;
        Assert.True(DrawsEntityKey("<p>@L.Markup(\"platform.doc.contact_email\")</p>", keys));
        Assert.True(DrawsEntityKey("<a href=\"@($\"mailto:{L[\"platform.doc.contact_email\"]}\")\">x</a>", keys));
        Assert.True(DrawsEntityKey("<span>@doc.Markup(\"platform.doc.entity_country\")</span>", keys));
        Assert.False(DrawsEntityKey("@if (!L.IsPlaceholder(\"platform.doc.contact_email\")) { <a href=\"/contact\">x</a> }", keys));
        Assert.False(DrawsEntityKey("<p>@L.Markup(\"platform.doc.placeholder_note\")</p>", keys));
    }

    // ─── W8-8 الجَولَة ١ — لافِتَةُ «الحُقولُ النائِبَة» تُسَمّى بِمِفتاحِها وعُنوانِها المَرسوم ───────────────────────────────

    internal const string DocFieldsBannerKey = "admin.home.doc_fields_title";

    /// <summary>كَم فِقرَةً يَقتَبِسُ فيها الدَليلُ <paramref name="title"/> بَينَ «» بِجِوارِ اسمِ مِفتاحِه — السَطرُ الَّذي يُسَمّي اللافِتَةَ بِما يُرسَم.</summary>
    internal static int QuotesBannerByKeyAndTitle(string deploy, string key, string title)
        => deploy.Split("\n\n").Count(p => Names(p, key, title));

    private static bool Names(string paragraph, string key, string title)
        => paragraph.Contains($"`{key}`", StringComparison.Ordinal) && paragraph.Contains($"«{title}»", StringComparison.Ordinal);

    /// <summary>فِقَراتُ الدَليلِ الَّتي تُسَمّي اللافِتَةَ بِاسمِها «الحُقولُ النائِبَة» ولا تُسَمّيها بِمِفتاحِها وعُنوانِها، ولا في الفِقرَتَينِ التالِيَتَين
    /// سَطرٌ يُسَمّيها — ما يَبحَثُ عَنه مالِكٌ في <c>/admin</c> فَلا يَجِدُه.</summary>
    internal static IReadOnlyList<int> NamedWithoutTitle(string deploy, string key, string title)
    {
        var ps = deploy.Split("\n\n");
        var bare = new List<int>();
        for (var i = 0; i < ps.Length; i++)
            if (ps[i].Contains("«الحُقولُ النائِبَة»", StringComparison.Ordinal) && !Names(ps[i], key, title)
                && !ps.Skip(i + 1).Take(2).Any(p => Names(p, key, title)))
                bare.Add(i);
        return bare;
    }

    /// <summary>
    /// <para><b>«الحُقولُ النائِبَة» اسمٌ لا نَصّ</b> (‏2026-10-25، مُراجَعَةُ تَيّارِ W8-8 الجَولَة ١): مالِكٌ يَبحَثُ في <c>/admin</c> عَن المُقتَبَسِ لا يَجِدُه —
    /// عُنوانُ اللافِتَةِ قيمَةُ <see cref="DocFieldsBannerKey"/>. فَمَوضِعا الاسمِ في الدَليل (§٢ الخُطوَة ٧ و§٢·هـ البَند ٧) يَحمِلُ كُلٌّ مِنهُما سَطراً
    /// يُسَمّيها بِمِفتاحِها وعُنوانِها بِحَرفِه مِن <c>ar.json</c> — فَيَحمَرُّ يَومَ يَتَغَيَّرُ العُنوانُ ولا يَتبَعُه الدَليل.</para>
    /// </summary>
    [Fact]
    public void The_doc_fields_banner_is_named_in_the_guide_by_its_key_and_its_drawn_title()
    {
        var title = ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, DocFieldsBannerKey);
        Assert.True(title.Length > 10, $"أَداةٌ عَمياء: عُنوانُ اللافِتَةِ «{title}».");
        var admin = Read("libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "Admin", "AdminHome.razor");
        Assert.Contains($"Title=\"@(L[\"{DocFieldsBannerKey}\"])\"", admin, StringComparison.Ordinal);

        var deploy = Deploy();
        var named = Regex.Matches(deploy, "«الحُقولُ النائِبَة»").Count;
        var quoted = QuotesBannerByKeyAndTitle(deploy, DocFieldsBannerKey, title);
        var bare = NamedWithoutTitle(deploy, DocFieldsBannerKey, title);
        output.WriteLine($"«الحُقولُ النائِبَة» في الدَليل: {named} · فِقراتٌ تُسَمّيها بِمِفتاحِها وعُنوانِها: {quoted} · اسمٌ بِلا عُنوانٍ قَريب: {bare.Count} · العُنوان: «{title}»");
        Assert.True(named >= 2 && quoted >= 2, $"أَداةٌ عَمياء: {named} مَوضِعاً لِلاسمِ و{quoted} فِقرَةً بِالعُنوان.");
        Assert.True(bare.Count == 0, $"فِقَرٌ تُسَمّي اللافِتَةَ «الحُقولُ النائِبَة» بِلا عُنوانِها المَرسومِ قَريباً: {string.Join("، ", bare)}");
        Assert.True(QuotesBannerByKeyAndTitle(PaddleLiveApprovalItem(deploy), DocFieldsBannerKey, title) >= 1, "البَندُ ٧ مِن §٢·هـ بِلا سَطرِ العُنوان.");
    }

    /// <summary><b>حَقنُ عَيب</b>: فِقرَةٌ تَقتَبِسُ عُنواناً قَديماً، أَو تُسَمّي المِفتاحَ بِلا العُنوان، أَو العُنوانَ بِلا المِفتاح ⇒ لا تُعَدّ.</summary>
    [Fact]
    public void The_banner_title_reader_goes_red_on_a_stale_title_or_a_missing_key()
    {
        const string title = "عُنوانٌ مَرسوم";
        Assert.Equal(1, QuotesBannerByKeyAndTitle($"أ\n\nمِفتاحُ `{DocFieldsBannerKey}`: «{title}»\n\nب", DocFieldsBannerKey, title));
        Assert.Equal(0, QuotesBannerByKeyAndTitle($"مِفتاحُ `{DocFieldsBannerKey}`: «عُنوانٌ قَديم»", DocFieldsBannerKey, title));
        Assert.Equal(0, QuotesBannerByKeyAndTitle($"اللافِتَةُ «{title}»", DocFieldsBannerKey, title));
        Assert.Equal(0, QuotesBannerByKeyAndTitle($"`{DocFieldsBannerKey}`\n\n«{title}»", DocFieldsBannerKey, title));
        // ‏والفِقرَةُ عِندَ 6167b9f بِحَرفِ اسمِها وَحدَه — بِلا سَطرِ العُنوانِ بَعدَها ⇒ حَمراء؛ وبِه ⇒ لا.
        const string asItWas = "ولافِتَةُ «الحُقولُ النائِبَة» في `/admin` تَكُفُّ عِندَ الخَمسَة.";
        Assert.Equal(new[] { 1 }, NamedWithoutTitle($"أ\n\n{asItWas}\n\nب\n\nج\n\nمِفتاحُ `{DocFieldsBannerKey}`: «{title}»", DocFieldsBannerKey, title));
        Assert.Empty(NamedWithoutTitle($"أ\n\n{asItWas}\n\nمِفتاحُ `{DocFieldsBannerKey}`: «{title}»", DocFieldsBannerKey, title));
    }
}
