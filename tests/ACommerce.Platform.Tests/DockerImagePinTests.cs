using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

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
//
// ‏(‏2026-10-25 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه؛ تَدقيقُ الإطلاق lens-checkers-ci-1 (حاجِبَة) وbrief-P3-3/lens-checkers-ci-11) **«لا يُقارَنُ
// بِـSDK العامِلِ في CI» قُلِب**: ثَمَنُه قيسَ — الوَسمانِ ‏10.0.401/10.0.12 تَخَلَّفا عَن إصدارِ ‏2026-10-13 الأَمنيّ (حَرِجَتانِ عَلى وَقتِ التَشغيل)
// وCI أَخضَرُ بِقَناةٍ لا تَقولُ ما شُحِن. فَصارَ CI يَشتَقُّ الـSDK مِن وَسمِ الـ`Dockerfile` نَفسِه (`scripts/ci-sdk.sh` ← `global.json` لِلتَشغيل
// ← `setup-dotnet` بِـ`global-json-file` ← `dotnet --version`)، فَلا رَقَمَ ثانٍ يُنسى ولا صَفَّ يَحمَرُّ بِإصدارِ مايكروسوفت. والزَوجُ
// (‏SDK ووَقتُ تَشغيلِه) يُقرَأُ مِن جَدوَلٍ مَنقولٍ عَن `release-metadata/10.0/releases.json` — فَلا يُرفَعُ أَحَدُهُما وَحدَه.

public class DockerImagePinTests(ITestOutputHelper output)
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
        // ‏(‏2026-10-25) كانَ «`dotnet-version: 10.0.x` في ci.yml» — وصارَت قَناةُ CI قَناةَ الوَسمِ بِالبِناء: يَشتَقُّه `ci-sdk.sh` مِنه.
        Assert.Contains("./scripts/ci-sdk.sh write", Read(".github", "workflows", "ci.yml"), StringComparison.Ordinal);
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
    // ─── ‏2026-10-25: CI بِـSDK الصورَةِ نَفسِه — والزَوجُ مِن إصدارٍ واحِد ───────────────────────────────────────────────

    /// <summary><b>الزَوجُ المَقيس</b>: كُلُّ SDK في شَريطِ ‏4xx ووَقتُ التَشغيلِ الَّذي يَحمِلُه — مَنقولٌ بِحَرفِه عَن
    /// <c>https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json</c> (‏<c>sdks[].version</c> ←
    /// <c>runtime-version</c>، ‏2026-10-25). <b>والتَرقِيَةُ تُضيفُ صَفّاً مِن هُناك</b> — لا يُخمَّنُ رَقَمُ رُقعَة.</summary>
    internal static readonly IReadOnlyDictionary<string, string> RuntimeOfSdk = new Dictionary<string, string>
    {
        ["10.0.401"] = "10.0.12",   // ‏إصدارُ ‏2026-09-08
        ["10.0.402"] = "10.0.13",   // ‏إصدارُ ‏2026-10-13 (`security: true`)
    };

    /// <summary><c>null</c> إن كانَ الزَوجُ مِن إصدارٍ واحِد، وإلّا رَمزُ الخَرق: <c>sdk_unknown</c> (لا صَفَّ — يُضافُ مِن releases.json) أَو
    /// <c>runtime_mismatch</c> (رُفِعَ أَحَدُهُما وَحدَه).</summary>
    internal static string? PairBreach(string sdk, string aspnet) =>
        !RuntimeOfSdk.TryGetValue(sdk, out var runtime) ? "sdk_unknown"
        : runtime != aspnet ? "runtime_mismatch"
        : null;

    [Fact]
    public void The_runtime_image_is_the_runtime_that_the_pinned_sdk_carries()
    {
        var tags = Tags();
        output.WriteLine($"الزَوجُ: sdk:{tags["sdk"]} · aspnet:{tags["aspnet"]} · صُفوفُ الجَدوَل {RuntimeOfSdk.Count}");
        Assert.True(RuntimeOfSdk.Count > 0, "أَداةٌ عَمياء: جَدوَلُ الأَزواجِ فارِغ.");
        Assert.Null(PairBreach(tags["sdk"], tags["aspnet"]));

        // ‏حَقنُ عَيب: رَفعُ أَحَدِهِما وَحدَه يُحمِرّ — الصورَةُ القَديمَةُ بِالـSDK الجَديدِ والعَكس، ورُقعَةٌ لا صَفَّ لَها.
        Assert.Equal("runtime_mismatch", PairBreach("10.0.402", "10.0.12"));
        Assert.Equal("runtime_mismatch", PairBreach("10.0.401", "10.0.13"));
        Assert.Equal("sdk_unknown", PairBreach("10.0.403", "10.0.14"));
    }

    /// <summary>خُروقُ «CI بِـSDK الصورَة» في سَيرِ عَمَلٍ واحِد، ومَعَها عَدَدُ خُطُواتِ <c>setup-dotnet</c> المَقروءَة. الأَسطُرُ المُعَلَّقَةُ لا تُعَدّ.
    /// الرُموز: <c>dotnet_version_key</c> (رَقَمٌ أَو قَناةٌ ثانِيَةٌ تُكتَبُ بِاليَد) · <c>no_global_json</c> (الخُطوَةُ لا تَقرَأُ <c>global.json</c>) ·
    /// <c>not_written_before</c> (لا <c>ci-sdk.sh write</c> قَبلَها في الوَظيفَة) · <c>not_verified_after</c> (لا <c>ci-sdk.sh verify</c> بَعدَها
    /// وقَبلَ أَوَّلِ استِعمالٍ لِـ<c>dotnet</c>).</summary>
    internal static IReadOnlyList<string> CiSdkBreaches(string workflow, out int setups)
    {
        var lines = workflow.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Select((text, i) => (Text: text, I: i))
            .Where(l => !l.Text.TrimStart().StartsWith('#'))
            .ToList();
        var breaches = new List<string>();
        if (lines.Any(l => Regex.IsMatch(l.Text, @"^\s*dotnet-version\s*:"))) breaches.Add("dotnet_version_key");

        var jobStarts = lines.Where(l => Regex.IsMatch(l.Text, @"^  [\w-]+:\s*$")).Select(l => l.I).ToList();
        int JobEnd(int at) => jobStarts.Where(j => j > at).DefaultIfEmpty(int.MaxValue).Min();
        int JobStart(int at) => jobStarts.Where(j => j < at).DefaultIfEmpty(-1).Max();

        var setupLines = lines.Where(l => l.Text.Contains("uses: actions/setup-dotnet@", StringComparison.Ordinal)).ToList();
        setups = setupLines.Count;
        foreach (var setup in setupLines)
        {
            var (from, to) = (JobStart(setup.I), JobEnd(setup.I));
            var step = lines.Where(l => l.I > setup.I && l.I < to).TakeWhile(l => !Regex.IsMatch(l.Text, @"^\s*- ")).ToList();
            if (!step.Any(l => l.Text.Trim() == "global-json-file: global.json")) breaches.Add("no_global_json");
            if (!lines.Any(l => l.I > from && l.I < setup.I && l.Text.Contains("./scripts/ci-sdk.sh write", StringComparison.Ordinal)))
                breaches.Add("not_written_before");

            var verify = lines.FirstOrDefault(l => l.I > setup.I && l.I < to && l.Text.Contains("./scripts/ci-sdk.sh verify", StringComparison.Ordinal));
            var use = lines.FirstOrDefault(l => l.I > setup.I && l.I < to && !l.Text.Contains("ci-sdk.sh", StringComparison.Ordinal)
                && (Regex.IsMatch(l.Text, @"\bdotnet (build|test|publish|run|restore)\b") || l.Text.Contains("verify-production-boot.sh", StringComparison.Ordinal)));
            if (verify.Text is null || (use.Text is not null && use.I < verify.I)) breaches.Add("not_verified_after");
        }
        return breaches;
    }

    private static IReadOnlyList<(string File, string Text)> Workflows()
    {
        var dir = Path.Combine(RepoRoot, ".github", "workflows");
        return Directory.EnumerateFiles(dir, "*.yml").Concat(Directory.EnumerateFiles(dir, "*.yaml"))
            .OrderBy(f => f, StringComparer.Ordinal).Select(f => (Path.GetFileName(f), File.ReadAllText(f))).ToList();
    }

    /// <summary><b>كُلُّ وَظيفَةٍ تُثَبِّتُ dotnet تَأخُذُ SDK الصورَةِ مِن الـ<c>Dockerfile</c></b> — في كُلِّ سُيَرِ العَمَل، ويُطبَعُ العَدَد.</summary>
    [Fact]
    public void Every_job_that_sets_up_dotnet_builds_with_the_images_sdk_taken_from_the_dockerfile()
    {
        var total = 0;
        var wrong = new List<string>();
        foreach (var (file, text) in Workflows())
        {
            var breaches = CiSdkBreaches(text, out var setups);
            total += setups;
            output.WriteLine($"{file}: خُطُواتُ setup-dotnet {setups} · خُروق {breaches.Count}{(breaches.Count > 0 ? " — " + string.Join(", ", breaches) : "")}");
            wrong.AddRange(breaches.Select(b => $"{file}: {b}"));
        }
        Assert.True(total >= 2, $"أَداةٌ عَمياء: {total} خُطوَةَ setup-dotnet مَقروءَة (المُنتَظَرُ وَظيفَتا build-and-test وproduction-boot).");
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    /// <summary><b>حَقنُ عَيب — الفاحِصُ أَعلاه يُحمِرُّ فِعلاً</b>: عَودَةُ القَناة، وحَذفُ الكِتابَة، وحَذفُ التَحَقُّق — كُلٌّ بِرَمزِه؛ وتَعليقٌ يَذكُرُ القَناةَ لا يُعَدّ.</summary>
    [Fact]
    public void The_ci_sdk_checker_can_actually_go_red()
    {
        var ci = Read(".github", "workflows", "ci.yml").Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Empty(CiSdkBreaches(ci, out var live));
        Assert.Contains("# كانَ `dotnet-version: 10.0.x`", ci, StringComparison.Ordinal);   // ‏التَعليقُ اليَومَ يَذكُرُها — ولا يُعَدّ

        static string Once(string text, string from, string to)
        {
            var at = text.IndexOf(from, StringComparison.Ordinal);
            Assert.True(at >= 0, $"عَيِّنَةُ الحَقنِ غائِبَة: {from}");
            return text[..at] + to + text[(at + from.Length)..];
        }
        var cases = new (string Name, string Text, string Code)[]
        {
            ("القَناةُ تَعود", Once(ci, "global-json-file: global.json", "dotnet-version: 10.0.x"), "dotnet_version_key"),
            ("الخُطوَةُ بِلا global.json", Once(ci, "global-json-file: global.json", "dotnet-version: 10.0.402"), "no_global_json"),
            ("لا كِتابَةَ قَبلَها", Once(ci, "run: ./scripts/ci-sdk.sh write", "run: true"), "not_written_before"),
            ("لا تَحَقُّقَ بَعدَها", Once(ci, "run: ./scripts/ci-sdk.sh verify", "run: true"), "not_verified_after"),
        };
        var missed = new List<string>();
        foreach (var c in cases)
        {
            var got = CiSdkBreaches(c.Text, out var setups);
            output.WriteLine($"«{c.Name}» → setup-dotnet {setups} · {string.Join(", ", got)}");
            if (!got.Contains(c.Code)) missed.Add($"«{c.Name}»: لا {c.Code} ({string.Join(", ", got)})");
        }
        output.WriteLine($"حُقِنَ {cases.Length} شَكلاً عَلى {live} خُطوَةً حَيَّة؛ فاتَ الفاحِصَ {missed.Count}");
        Assert.True(missed.Count == 0, string.Join("\n", missed));
    }

    private bool NoBash()
    {
        if (!OperatingSystem.IsWindows() && File.Exists("/bin/bash")) return false;
        output.WriteLine("لا /bin/bash هُنا (Windows) — يُقاسُ هذا الصَفُّ في CI (‏ubuntu).");
        return true;
    }

    private static (int Code, string Out) Bash(string workDir, IDictionary<string, string>? env, params string[] args)
    {
        var psi = new ProcessStartInfo("/bin/bash", args)
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };
        psi.Environment.Remove("GITHUB_OUTPUT");
        if (env is not null) foreach (var (k, v) in env) psi.Environment[k] = v;
        using var p = Process.Start(psi)!;
        var text = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        Assert.True(p.WaitForExit(20_000), "ci-sdk.sh لَم يَنتَهِ في عِشرينَ ثانِيَة.");
        return (p.ExitCode, text.Trim());
    }

    private static string Script => Path.Combine(RepoRoot, "scripts", "ci-sdk.sh");

    /// <summary><b><c>ci-sdk.sh write</c> مُشَغَّلاً</b>: الـ<c>Dockerfile</c> الحَقيقيُّ يُعطي <c>global.json</c> بِوَسمِه و<c>rollForward: disable</c>
    /// وسَطرَ <c>version=</c> في <c>$GITHUB_OUTPUT</c>؛ والوَسمُ المُتَحَرِّكُ والمُكَرَّرُ والغائِبُ تُرَدُّ بِـ1 ولا تَكتُبُ شَيئاً.</summary>
    [Fact]
    public void The_sdk_script_writes_the_dockerfile_tag_and_refuses_a_moving_doubled_or_missing_one()
    {
        if (NoBash()) return;
        var tags = Tags();
        var dir = Directory.CreateTempSubdirectory("wasayel-ci-sdk-");
        try
        {
            var outFile = Path.Combine(dir.FullName, "out.txt");
            var (code, text) = Bash(dir.FullName, new Dictionary<string, string> { ["GITHUB_OUTPUT"] = outFile },
                Script, "write", Path.Combine(RepoRoot, "Dockerfile"), Path.Combine(dir.FullName, "global.json"));
            output.WriteLine($"[الحَقيقيّ] {code} · {text}");
            Assert.Equal(0, code);
            using (var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir.FullName, "global.json"))))
            {
                Assert.Equal(tags["sdk"], json.RootElement.GetProperty("sdk").GetProperty("version").GetString());
                Assert.Equal("disable", json.RootElement.GetProperty("sdk").GetProperty("rollForward").GetString());
            }
            Assert.Equal($"version={tags["sdk"]}", File.ReadAllText(outFile).Trim());

            var line = $"FROM mcr.microsoft.com/dotnet/sdk:{tags["sdk"]} AS build\n";
            var cases = new (string Name, string Dockerfile)[]
            {
                ("قَناةٌ مُتَحَرِّكَة", "FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build\n"),
                ("قَناةُ ‏10.0.x", "FROM mcr.microsoft.com/dotnet/sdk:10.0.x AS build\n"),
                ("سَطرانِ لِلبِناء", line + line),
                ("لا سَطرَ لِلبِناء", "FROM mcr.microsoft.com/dotnet/aspnet:10.0.13 AS runtime\n"),
            };
            foreach (var c in cases)
            {
                var docker = Path.Combine(dir.FullName, "Dockerfile.case");
                var global = Path.Combine(dir.FullName, "global.case.json");
                File.WriteAllText(docker, c.Dockerfile, new UTF8Encoding(false));
                var (rc, said) = Bash(dir.FullName, null, Script, "write", docker, global);
                output.WriteLine($"«{c.Name}» → {rc} · {said.Split('\n').Last()}");
                Assert.True(rc == 1, $"«{c.Name}»: رَمزُ {rc} لا 1 — {said}");
                Assert.False(File.Exists(global), $"«{c.Name}»: كُتِبَ global.json مِن وَسمٍ مَرفوض.");
                Assert.Contains("::error::", said, StringComparison.Ordinal);
            }
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    /// <summary><b><c>ci-sdk.sh verify</c> مُشَغَّلاً</b> بِـ<c>dotnet</c> مَصنوعٍ أَوَّلَ <c>PATH</c>: الوَسمُ نَفسُه يَمُرّ، ورُقعَةٌ أَحدَثُ تُرَدّ،
    /// وSDK غائِبٌ (رَمزٌ غَيرُ الصِفرِ وقائِمَةٌ لا رَقَم) يُرَدّ.</summary>
    [Fact]
    public void The_sdk_script_verify_refuses_any_sdk_but_the_images()
    {
        if (NoBash()) return;
        var tags = Tags();
        var dir = Directory.CreateTempSubdirectory("wasayel-ci-sdk-verify-");
        try
        {
            var fake = Path.Combine(dir.FullName, "dotnet");
            int Verify(string body)
            {
                File.WriteAllText(fake, "#!/bin/bash\n" + body + "\n", new UTF8Encoding(false));
                File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                var path = dir.FullName + ":" + (Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin");
                var (rc, said) = Bash(dir.FullName, new Dictionary<string, string> { ["PATH"] = path },
                    Script, "verify", Path.Combine(RepoRoot, "Dockerfile"));
                output.WriteLine($"[{body}] → {rc} · {said.Split('\n').Last()}");
                return rc;
            }
            Assert.Equal(0, Verify($"echo {tags["sdk"]}"));
            Assert.Equal(1, Verify("echo 10.0.499"));
            Assert.Equal(1, Verify("echo 'A compatible .NET SDK was not found.'; echo '10.0.401 [/usr/share/dotnet/sdk]'; exit 145"));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
