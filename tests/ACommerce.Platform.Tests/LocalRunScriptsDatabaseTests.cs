using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ سكربتا التَشغيلِ المَحَلِّيِّ يُقلِعانِ Development عَلى القاعِدَةِ الَّتي فَحَصاها، لا عَلى ما في بيئَةِ المُستَدعي ═══
//
// **المَقيسُ قَبلَ الإصلاح** (‏2026-10-12، ظِلالٌ لِـ`pg_isready`/`psql`/`dotnet` تُسَجِّلُ ما تَتَلَقّاه): `run.sh` و`run.ps1`
// يَفحَصانِ `acommerce_v1` عَلى localhost ثُمَّ يُشَغِّلانِ `dotnet run` بِـ`ASPNETCORE_ENVIRONMENT=Development` و**بِسِلسِلَةِ
// Neon المُصَدَّرَةِ في الصَدَفَة** حَرفاً (المُتَغَيِّرُ يَغلِبُ `appsettings.json`). وDevelopment = `AutoCreate.All` و`PlatformSeed`
// في كُلِّ إقلاع — أَي بَذرٌ وإعادَةُ كِتابَةٍ لِمُخَطَّطِ قاعِدَةٍ بَعيدَة، وهِيَ بِعَينِها «قاعِدَةُ الإنتاجِ لا يُشَغَّلُ عَلَيها
// التَطوير» في `CLAUDE.md`، وقِسمُ «بيئَة التَطوير» فيه يُصَدِّرُ تِلكَ السِلسِلَةَ في الصَدَفَةِ نَفسِها.
//
// **والإصلاحُ أَصغَرُ الخِيارَين**: يَضبِطُ السكربتُ السِلسِلَةَ إلى القاعِدَةِ الَّتي فَحَصَها (حَرفَ `appsettings.json`)، ويَقولُ
// إن تَجاوَزَ سِلسِلَةً مَوروثَةً بِمُضيفِها وَحدَه. و`run.ps1` يُعيدُ قيمَةَ المُستَدعي في `finally` كَأُختِها `ASPNETCORE_ENVIRONMENT`.

public class LocalRunScriptsDatabaseTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static string Script(string name)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, "scripts", name));
        Assert.True(text.Length > 500, $"أَداةٌ عَمياء: {name} لَم يُقرَأ.");
        return text;
    }

    /// <summary>السِلسِلَةُ المَحَلِّيَّةُ الوَحيدَةُ في المُستَودَع — يَقرَؤُها التَطبيقُ عِندَ غِيابِ المُتَغَيِّر.</summary>
    private static string AppSettingsConnection()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "apps", "V1.App", "appsettings.json")));
        return doc.RootElement.GetProperty("ConnectionStrings").GetProperty("Postgres").GetString()!;
    }

    /// <summary><b>كُلٌّ مِنهُما يَضبِطُ سِلسِلَةَ القاعِدَةِ الَّتي فَحَصَها — حَرفَ <c>appsettings.json</c> — قَبلَ
    /// <c>dotnet run</c></b>، والفَحصُ نَفسُه يَسأَلُ المُضيفَ والمُستَخدِمَ والقاعِدَةَ الَّتي تُسَمّيها السِلسِلَة.</summary>
    [Theory]
    [InlineData("run.sh", "LOCAL_CS=\"", "export ConnectionStrings__Postgres=\"$LOCAL_CS\"")]
    [InlineData("run.ps1", "$localConnection = \"", "$env:ConnectionStrings__Postgres = $localConnection")]
    public void TheScript_PinsTheDatabaseItProbed_BeforeRunning(string script, string literal, string set)
    {
        var text = Script(script);
        var local = AppSettingsConnection();
        Assert.Contains("Host=localhost", local, StringComparison.Ordinal);

        Assert.Contains(literal + local + "\"", text, StringComparison.Ordinal);
        Assert.Contains("psql -h localhost -U acommerce -d acommerce_v1", text, StringComparison.Ordinal);
        Assert.Contains("Database=acommerce_v1;Username=acommerce", local, StringComparison.Ordinal);

        var at = text.IndexOf(set, StringComparison.Ordinal);
        var run = text.IndexOf("dotnet run --no-launch-profile", StringComparison.Ordinal);
        Assert.True(at >= 0, $"{script} لا يَضبِطُ ConnectionStrings__Postgres — يُقلِعُ Development عَلى ما في البيئَة.");
        Assert.True(run > at, $"{script} يَضبِطُ السِلسِلَةَ بَعدَ `dotnet run` أَو لا يُشَغِّلُه.");
    }

    /// <summary><b>ولا يَطبَعُ السِلسِلَة</b>: كُلُّ سَطرِ طِباعَةٍ يَذكُرُ المُضيفَ المُستَخرَجَ لا مُتَغَيِّرَ السِلسِلَة. والفاحِصُ
    /// مَقيسٌ بِسَطرٍ يُفشِلُه.</summary>
    [Theory]
    [InlineData("run.sh")]
    [InlineData("run.ps1")]
    public void TheScript_PrintsTheInheritedHostOnly_NeverTheConnectionString(string script)
    {
        var printsTheString = new Regex(
            @"(echo|Write-Host)[^\n]*(\$\{?ConnectionStrings__Postgres|\$env:ConnectionStrings__Postgres|\$LOCAL_CS|\$previousConnection|\$localConnection)");
        Assert.Matches(printsTheString, "echo \"⚠ ${ConnectionStrings__Postgres}\"");
        Assert.Matches(printsTheString, "Write-Host \"x $previousConnection\"");

        var text = Script(script);
        Assert.DoesNotMatch(printsTheString, text);
        Assert.Contains(script == "run.sh" ? "${INHERITED_HOST:-" : "$inheritedHost", text, StringComparison.Ordinal);
    }

    /// <summary><b>و<c>run.ps1</c> يُعيدُ سِلسِلَةَ المُستَدعي في <c>finally</c></b> — <c>$env:</c> يَكتُبُ في جَلسَتِه، فَبِلا
    /// إعادَةٍ تَبقى المَحَلِّيَّةُ بَعدَ السكربت (مَقيسٌ بِـpwsh 7.6.6: بَعدَ الإصلاحِ تَعودُ المَوروثَةُ، والغائِبَةُ غائِبَة).</summary>
    [Fact]
    public void RunPs1_RestoresTheCallersConnection_InFinally_AfterTheRun()
    {
        var text = Script("run.ps1");
        var save    = text.IndexOf("$previousConnection = $env:ConnectionStrings__Postgres", StringComparison.Ordinal);
        var set     = text.IndexOf("$env:ConnectionStrings__Postgres = $localConnection", StringComparison.Ordinal);
        var run     = text.IndexOf("dotnet run --no-launch-profile", StringComparison.Ordinal);
        var fin     = text.IndexOf("finally {", StringComparison.Ordinal);
        var restore = text.IndexOf("$env:ConnectionStrings__Postgres = $previousConnection", StringComparison.Ordinal);

        Assert.True(save >= 0 && save < set, "run.ps1 لا يَحفَظُ سِلسِلَةَ المُستَدعي قَبلَ أَن يَضبِطَ المَحَلِّيَّة.");
        Assert.True(run > set && fin > run && restore > fin, "run.ps1 لا يُعيدُ السِلسِلَةَ في finally بَعدَ التَشغيل.");
        Assert.Equal(2, Regex.Matches(text, Regex.Escape("$env:ConnectionStrings__Postgres =")).Count);
    }

    /// <summary><b>مُشَغَّلاً بِـbash لا مَقروءاً</b>: <c>run.sh</c> بِظِلالٍ لِـ<c>pg_isready</c> و<c>psql</c> و<c>dotnet</c>، وسِلسِلَةٌ
    /// بَعيدَةٌ في البيئَة — فَيَتَلَقّى <c>dotnet</c> المَحَلِّيَّةَ وDevelopment، ويُطبَعُ المُضيفُ البَعيدُ ولا تُطبَعُ كَلِمَةُ مُرورِه؛
    /// وبِلا سِلسِلَةٍ مَوروثَةٍ لا تَنبيه. وعَلى Windows لا bash مَعلومَ المَوضِعِ فَيُقاسُ في CI (‏ubuntu).</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RunSh_Executed_HandsDotnetTheProbedDatabase(bool remoteInherited)
    {
        if (OperatingSystem.IsWindows())
        {
            output.WriteLine("Windows — لا bash مَعلومَ المَوضِع؛ يُقاسُ هذا الصَفُّ في CI (‏ubuntu).");
            return;
        }
        Assert.True(File.Exists("/bin/bash"), "أَداةٌ غائِبَة: /bin/bash.");

        var dir = Directory.CreateTempSubdirectory("wasayel-run-sh-");
        try
        {
            var log = Path.Combine(dir.FullName, "shadow.log");
            void Shadow(string name, string body)
            {
                var path = Path.Combine(dir.FullName, name);
                File.WriteAllText(path, "#!/bin/bash\n" + body + "\n", new UTF8Encoding(false));
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            Shadow("pg_isready", "exit 0");
            Shadow("psql", "echo \"psql $*\" >> \"$SHADOW_LOG\"; exit 0");
            Shadow("dotnet", "echo \"dotnet $1 env=$ASPNETCORE_ENVIRONMENT cs=$ConnectionStrings__Postgres\" >> \"$SHADOW_LOG\"; exit 0");

            var psi = new ProcessStartInfo("/bin/bash", new[] { Path.Combine(RepoRoot, "scripts", "run.sh") })
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8,
            };
            psi.Environment["PATH"] = dir.FullName + ":" + Environment.GetEnvironmentVariable("PATH");
            psi.Environment["SHADOW_LOG"] = log;
            psi.Environment.Remove("ConnectionStrings__Postgres");
            if (remoteInherited)
                psi.Environment["ConnectionStrings__Postgres"] =
                    "Host=ep-remote-shadow.example.neon.tech;Database=neondb;Username=owner;Password=PASSWORD_MARKER;SSL Mode=Require";

            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            Assert.True(p.WaitForExit(20_000), "run.sh لَم يَنتَهِ في عِشرينَ ثانِيَة.");
            var calls = File.ReadAllText(log);
            output.WriteLine(stdout);
            output.WriteLine(calls);

            Assert.Equal(0, p.ExitCode);
            Assert.Contains("psql -h localhost -U acommerce -d acommerce_v1", calls, StringComparison.Ordinal);
            Assert.Contains($"dotnet run env=Development cs={AppSettingsConnection()}", calls, StringComparison.Ordinal);
            Assert.DoesNotContain("neon.tech;", calls, StringComparison.Ordinal);
            Assert.DoesNotContain("PASSWORD_MARKER", stdout, StringComparison.Ordinal);
            if (remoteInherited)
                Assert.Contains("ep-remote-shadow.example.neon.tech", stdout, StringComparison.Ordinal);
            else
                Assert.DoesNotContain("⚠", stdout, StringComparison.Ordinal);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
