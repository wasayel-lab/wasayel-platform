using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ G6 — تَوأَمُ الطَبَقَةِ ٨-ج: لا عَميلَ صادِراً خامّاً في libs/ خارِجَ قائِمَتِه، ولا فِعلَ SDK خارِجَ المُستَدعي المَعدود (‏2026-11-12، W13-6·١١ — ADR-051 §١٨) ═══
//
// المُصَنِّفُ نَفسُه الَّذي في `scripts/verify-architecture.sh` (الطَبَقَة ٨-ج) بِـC#، عَلى القائِمَةِ نَفسِها (`scripts/verify-outbound-allowlist.txt`): التَعليقاتُ
// والنُصوصُ مَنزوعَة، ثُمَّ رُموزُ أَنواعِ العَميلِ الخامّ؛ وفي مِلَفٍّ يُسَمّي سَطرُه حَقلَ SDK، كُلُّ وُصولٍ إلى عُضوٍ فيه (عَدا Dispose) داخِلَ وُسَطاءِ
// `InvokeAsync(` أَو `HelperAsync(` بِعُمقِ الأَقواس — بِلا Roslyn (المَشروعُ لا يُرَجِّعُه). يَطبَعُ ما عَدّ، ويَفشَلُ عَلى العَمى، والحَقنُ يُحَمِّرُه.

public class OutboundInvokerBoundaryTests(ITestOutputHelper output)
{
    /// <summary><b>سَقفُ القائِمَة</b> — تَتَقَلَّصُ ولا تَنمو: 12 عِندَ البَند ١١ (خَرَجَ Twilio ودَخَلَ <c>ProviderHttp.cs</c>)، ويَنزِلُ مَعَ كُلِّ عُدَّةٍ تُرَحَّل.
    /// ‏(‏2026-11-12، W13-6·١٢) <b>11</b>: خَرَجَ Brevo (عَميلُه <c>ProviderHttp</c>)، وبَقِيَ SMTP بِحَقلِه <c>client</c> — أَوَّلُ سَطرٍ تَقيسُه قاعِدَةُ الفِعل.
    /// ‏(‏2026-11-12، W13-6·١٣) <b>10</b>: خَرَجَ نَفاذ (عَميلُه <c>ProviderHttp</c>)، وصارَ لِـS3 والإشعارِ حَقلُهُما <c>_client</c> — فَقاعِدَةُ الفِعلِ تَقيسُ ثَلاثَةَ أَسطُر.
    /// ‏(‏2026-11-12، W13-6·١٤) <b>7</b>: خَرَجَ ميسّر وPayPal وPaddle (عُمَلاؤُها <c>ProviderHttp</c>).
    /// ‏(‏2026-11-12، W13-6·١٥) <b>5</b>: خَرَجَ OTO والخَلفِيّاتُ الثَلاث (عَميلُهُما <c>ProviderHttp</c>) — وبَقِيَ ما سَمّاه التَصميم (§6.4): المُستَدعي نَفسُه وعُدَدُ الـSDK الثَلاث ومَركَزُنا.</summary>
    internal const int AllowListCeiling = 5;

    internal static readonly Regex RawTypes = new(
        @"\b(?:HttpClient|HttpMessageInvoker|IHttpClientFactory|SocketsHttpHandler|HttpClientHandler|AmazonS3Client|IAmazonS3|SmtpClient|WebPushClient|HubConnectionBuilder|ClientWebSocket|TcpClient|GrpcChannel)\b|\.CreateClient\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Lexemes = new(
        "(\"\"\"[\\s\\S]*?\"\"\"|@\"(?:\"\"|[^\"])*\"|\\$?\"(?:\\\\.|[^\"\\\\\\n])*\"|'(?:\\\\.|[^'\\\\\\n])'|//[^\\n]*|/\\*[\\s\\S]*?\\*/)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex InvokerBefore = new(@"\b(?:InvokeAsync|HelperAsync)\s*(?:<[^<>()]*>)?\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>النَصُّ بِلا تَعليقاتٍ (مَسافَة) وبِلا نُصوصٍ حَرفِيَّة (<c>""</c>) — كَما يَنزِعُها مُصَنِّفُ الطَبَقَة.</summary>
    internal static string Strip(string code)
        => Lexemes.Replace(code, m => m.Value.StartsWith("//", StringComparison.Ordinal) || m.Value.StartsWith("/*", StringComparison.Ordinal) ? " " : "\"\"");

    /// <summary>أَيَقَعُ المَوضِعُ داخِلَ وُسَطاءِ <c>InvokeAsync(</c> أَو <c>HelperAsync(</c>؟ — بِعُمقِ الأَقواسِ إلى الخارِج.</summary>
    internal static bool InsideInvoker(string s, int at)
    {
        var depth = 0;
        for (var i = at - 1; i >= 0; i--)
        {
            if (s[i] == ')') depth++;
            else if (s[i] == '(')
            {
                if (depth > 0) { depth--; continue; }
                if (InvokerBefore.IsMatch(s[..i])) return true;
            }
        }
        return false;
    }

    internal sealed record Scan(string File, int Tokens, int Accesses, int Outside);

    internal static Scan Classify(string file, string code, string? field)
    {
        var s = Strip(code);
        var tokens = RawTypes.Matches(s).Count;
        int accesses = 0, outside = 0;
        if (!string.IsNullOrEmpty(field) && field != "-")
            foreach (Match m in new Regex($@"\b{Regex.Escape(field)}\s*\.\s*(\w+)").Matches(s))
            {
                if (m.Groups[1].Value == "Dispose") continue;
                accesses++;
                if (!InsideInvoker(s, m.Index)) outside++;
            }
        return new Scan(file, tokens, accesses, outside);
    }

    internal sealed record Allowed(string File, string Field, string Reason);

    internal static IReadOnlyList<Allowed> AllowList(IEnumerable<string>? lines = null)
        => (lines ?? File.ReadAllLines(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "scripts", "verify-outbound-allowlist.txt")))
            .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#'))
            .Select(l => l.Split('|')).Select(p => new Allowed(p[0], p.Length > 1 ? p[1] : "-", p.Length > 2 ? p[2] : "")).ToList();

    internal static IReadOnlyList<(string File, string Code)> LibsSources()
    {
        var root = ThemeZeroEquivalenceTests.RepoRoot;
        var sep = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(Path.Combine(root, "libs"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}bin{sep}", StringComparison.Ordinal) && !f.Contains($"{sep}obj{sep}", StringComparison.Ordinal))
            .Select(f => (Path.GetRelativePath(root, f).Replace('\\', '/'), File.ReadAllText(f))).OrderBy(x => x.Item1, StringComparer.Ordinal).ToList();
    }

    /// <summary>خُروقُ الطَبَقَة: عَميلٌ خامٌّ خارِجَ القائِمَة، وفِعلُ SDK خارِجَ المُستَدعي، وإعفاءٌ مَيِّت.</summary>
    internal static IReadOnlyList<string> Violations(IReadOnlyList<Scan> scans, IReadOnlyList<Allowed> allowed)
    {
        var listed = allowed.Select(a => a.File).ToHashSet(StringComparer.Ordinal);
        var v = new List<string>();
        v.AddRange(scans.Where(s => s.Tokens > 0 && !listed.Contains(s.File)).Select(s => $"raw_client_outside_allow_list:{s.File}"));
        v.AddRange(scans.Where(s => s.Outside > 0).Select(s => $"sdk_verb_outside_invoker:{s.File} ({s.Outside}/{s.Accesses})"));
        var naming = scans.Where(s => s.Tokens > 0).Select(s => s.File).ToHashSet(StringComparer.Ordinal);
        v.AddRange(allowed.Where(a => !naming.Contains(a.File)).Select(a => $"dead_exemption:{a.File}"));
        return v;
    }

    private static IReadOnlyList<Scan> ScanAll(IReadOnlyList<(string File, string Code)> sources, IReadOnlyList<Allowed> allowed)
    {
        var fields = allowed.ToDictionary(a => a.File, a => a.Field, StringComparer.Ordinal);
        return sources.Select(s => Classify(s.File, s.Code, fields.GetValueOrDefault(s.File))).ToList();
    }

    [Fact]
    public void Every_raw_client_in_libs_is_allow_listed_and_every_listed_file_still_names_one()
    {
        var sources = LibsSources();
        var allowed = AllowList();
        var scans = ScanAll(sources, allowed);
        var naming = scans.Where(s => s.Tokens > 0).ToList();
        var violations = Violations(scans, allowed);
        output.WriteLine($"[8-ج] مِلَفّاتٌ مَفحوصَة {scans.Count} · تُسَمّي عَميلاً خامّاً {naming.Count} · رُموز {scans.Sum(s => s.Tokens)} · "
                         + $"وُصولُ SDK مَفحوص {scans.Sum(s => s.Accesses)} · في القائِمَة {allowed.Count} (السَقف {AllowListCeiling}) · خُروق {violations.Count}");
        foreach (var s in naming) output.WriteLine($"  {s.File} · {s.Tokens}");
        Assert.True(scans.Count >= 300 && scans.Sum(s => s.Tokens) > 0, $"أَداةٌ عَمياء: {scans.Count} مِلَفّاً و{scans.Sum(s => s.Tokens)} رَمزاً.");
        Assert.True(allowed.Count <= AllowListCeiling, $"القائِمَةُ نَمَت: {allowed.Count} فَوقَ السَقف {AllowListCeiling} — تَتَقَلَّصُ ولا تَنمو.");
        Assert.Contains(allowed, a => a.File.EndsWith("/Metering/ProviderHttp.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(allowed, a => a.File.EndsWith("/TwilioSmsChannel.cs", StringComparison.Ordinal));
        // ‏(‏2026-11-12، W13-6·١٢) Brevo خارِجَها، وSMTP فيها بِحَقلِه: أَفعالُ MailKit الأَربَعَةُ مَفحوصَةٌ ولا واحِدَ خارِجَ المُستَدعي.
        Assert.DoesNotContain(allowed, a => a.File.EndsWith("/BrevoEmailChannel.cs", StringComparison.Ordinal));
        var smtp = scans.Single(x => x.File.EndsWith("/SmtpEmailChannel.cs", StringComparison.Ordinal));
        output.WriteLine($"  SMTP · الحَقل {allowed.Single(a => a.File == smtp.File).Field} · وُصول {smtp.Accesses} · خارِجَ المُستَدعي {smtp.Outside}");
        Assert.Equal(("client", 4, 0), (allowed.Single(a => a.File == smtp.File).Field, smtp.Accesses, smtp.Outside));
        // ‏(‏2026-11-12، W13-6·١٣) نَفاذ خارِجَها، وS3 والإشعارُ فيها بِحَقلِ كُلٍّ: أَفعالُ S3 الأَربَعَة (الرَفعُ مَعدوداً، والجَلبُ والوُجودُ والحَذفُ مُساعِدَة)
        // وفِعلُ الإشعارِ الواحِدُ مَفحوصَةٌ ولا واحِدَ خارِجَ المُستَدعي.
        Assert.DoesNotContain(allowed, a => a.File.EndsWith("/NafathChannel.cs", StringComparison.Ordinal));
        foreach (var (name, accesses) in new[] { ("/S3FileStorage.cs", 4), ("/WebPushService.cs", 1) })
        {
            var scan = scans.Single(x => x.File.EndsWith(name, StringComparison.Ordinal));
            var field = allowed.Single(a => a.File == scan.File).Field;
            output.WriteLine($"  {name.TrimStart('/')} · الحَقل {field} · وُصول {scan.Accesses} · خارِجَ المُستَدعي {scan.Outside}");
            Assert.Equal(("_client", accesses, 0), (field, scan.Accesses, scan.Outside));
        }
        Assert.All(allowed, a => Assert.False(string.IsNullOrWhiteSpace(a.Reason), $"سَطرٌ بِلا سَبَب: {a.File}"));
        Assert.True(violations.Count == 0, "الطَبَقَةُ ٨-ج حَمراء: " + string.Join(" | ", violations));
    }

    /// <summary>الأَداةُ تَرى ما تَعُدّ (القاعِدَة ١٠): النَوعُ في الكودِ يُعَدّ، وفي التَعليقِ والنَصِّ وفي أَسماءٍ تَحويه (<c>AddHttpClient</c>، <c>IHttpClientBuilder</c>) لا.</summary>
    [Theory]
    [InlineData("private readonly HttpClient _http;", 1)]
    [InlineData("private static readonly System.Net.Http.HttpClient Raw = new();", 1)]
    [InlineData("var c = factory.CreateClient(name); IHttpClientFactory f;", 2)]
    [InlineData("using var smtp = new SmtpClient(); IAmazonS3 s3; WebPushClient w;", 3)]
    [InlineData("services.AddHttpClient<OtoClient>(); IHttpClientBuilder b; ConfigureHttpClientDefaults(x);", 0)]
    [InlineData("// HttpClient في تَعليق\n/* SmtpClient */ var s = \"HttpClient\"; var t = @\"IAmazonS3\";", 0)]
    [InlineData("ProviderHttp<TwilioSmsChannel> _http;", 0)]
    public void The_type_rule_sees_what_it_counts(string code, int tokens)
        => Assert.Equal(tokens, Classify("x.cs", code, null).Tokens);

    /// <summary><b>قاعِدَةُ الفِعلِ بِعُمقِ الأَقواس</b>: داخِلَ وُسَطاءِ <c>InvokeAsync(</c> (ولو في لامبدا بِأَقواسٍ مُتَداخِلَة) و<c>HelperAsync&lt;T&gt;(</c> يَمُرّ، وخارِجَها خَرق،
    /// و<c>Dispose</c> مُثَبَّت، والتَعليقُ والقَوسُ في نَصٍّ لا يُعَدّان.</summary>
    [Fact]
    public void The_verb_rule_tracks_paren_depth()
    {
        const string code = """
            class A { private readonly IAmazonS3 _s3; // _s3.Comment()
              public A(IAmazonS3 s3) { _s3 = s3; }
              public async Task Put() { await _calls.InvokeAsync(call, async t => { var r = await _s3.PutObjectAsync(Make(req, (1)), t); return r; }, r => ok, ct); }
              public async Task Get() { var s = "x)("; await _s3.GetObjectAsync(req, ct); }
              public void Dispose() => _s3.Dispose();
              public Task H() => _calls.HelperAsync<int>(helper, t => _s3.DeleteObjectAsync(d, t), ct);
            }
            """;
        var scan = Classify("A.cs", code, "_s3");
        output.WriteLine($"[8-ج] الفِعل: رُموز {scan.Tokens} · وُصول {scan.Accesses} · خارِجَ المُستَدعي {scan.Outside}");
        Assert.Equal(new Scan("A.cs", 2, 3, 1), scan);
    }

    /// <summary><b>الحَقنُ المَقيس</b>: نُسخَةٌ مِن قَناةِ Twilio في مِجَلَّدِ عُدَّةٍ تَحمِلُ <c>new HttpClient()</c> ⇐ خَرق؛ وسَطرٌ في القائِمَةِ لِمِلَفٍّ لا يُسَمّي نَوعاً خامّاً
    /// ⇐ إعفاءٌ مَيِّت؛ وحَقلُ SDK يُسَمّى لِمِلَفٍّ لَم يُلَفَّ فِعلُه ⇐ فِعلٌ خارِجَ المُستَدعي.</summary>
    /// <remarks>‏(‏2026-11-12، W13-6·١٣) كانَ صَفُّ الحَقلِ يُسَمّي <c>_client</c> لِـS3 «اليَوم» — ولُفَّ فِعلُه في هذا البَندِ فَصارَ الحَقلُ في القائِمَةِ نَفسِها.
    /// فَصارَ الحَقنُ فِعلاً قَبلَ المُستَدعي في نُسخَةٍ مِن S3 الحَقيقِيِّ ومِن خِدمَةِ الإشعارِ الحَقيقِيَّة، كَصَفِّ SMTP.</remarks>
    [Fact]
    public void Each_injected_breach_turns_the_layer_red()
    {
        var sources = LibsSources().ToList();
        var allowed = AllowList();
        var twilio = sources.Single(s => s.File.EndsWith("/TwilioSmsChannel.cs", StringComparison.Ordinal));
        var copy = (twilio.File.Replace("TwilioSmsChannel.cs", "InjectedCopy.cs", StringComparison.Ordinal),
                    twilio.Code.Replace("public sealed class TwilioSmsChannel : IOtpChannel",
                        "public sealed class InjectedCopy { private static readonly HttpClient Raw = new(); }\npublic sealed class X : IOtpChannel", StringComparison.Ordinal));
        Assert.NotEqual(twilio.Code, copy.Item2);
        var raw = Violations(ScanAll([.. sources, copy], allowed), allowed);
        Assert.Equal([$"raw_client_outside_allow_list:{copy.Item1}"], raw);

        var dead = allowed.Append(new Allowed(twilio.File, "-", "injected")).ToList();
        Assert.Equal([$"dead_exemption:{twilio.File}"], Violations(ScanAll(sources, dead), dead));

        // ‏(‏2026-11-12، W13-6·١٣) فِعلُ S3 يُنادى قَبلَ المُستَدعي في نُسخَةٍ مِن المَخزَنِ الحَقيقِيّ، وفِعلُ الإشعارِ قَبلَ مُستَدعيه ⇐ خَرقٌ عَلى سَطرِ كُلٍّ بِعَدَدِه.
        foreach (var (name, verbAnchor, before, count) in new[]
                 {
                     ("/S3FileStorage.cs", "await _calls.InvokeAsync(counted,", "await _client.ListBucketsAsync(ct);\n        ", "1/5"),
                     ("/WebPushService.cs", "var refused = await _calls.InvokeAsync<", "_client.SetVapidDetails(_vapid);\n                ", "1/2"),
                 })
        {
            var file = sources.Single(x => x.File.EndsWith(name, StringComparison.Ordinal));
            Assert.Single(Regex.Matches(file.Code, Regex.Escape(verbAnchor)));
            var injected = sources.Select(x => x == file ? (x.File, x.Code.Replace(verbAnchor, before + verbAnchor, StringComparison.Ordinal)) : x).ToList();
            var verbBreach = Violations(ScanAll(injected, allowed), allowed);
            output.WriteLine($"[8-ج] حَقنُ {name.TrimStart('/')}: {string.Join(" · ", verbBreach)}");
            Assert.Equal([$"sdk_verb_outside_invoker:{file.File} ({count})"], verbBreach);
        }
        output.WriteLine($"[8-ج] الحَقن: {string.Join(" · ", raw.Concat(Violations(ScanAll(sources, dead), dead)))}");

        // ‏(‏2026-11-12، W13-6·١٢) وفِعلُ MailKit يُنادى قَبلَ المُستَدعي في نُسخَةٍ مِن قَناةِ SMTP الحَقيقِيَّة ⇐ خَرقٌ عَلى سَطرِها بِعَدَدِه.
        var smtp = sources.Single(x => x.File.EndsWith("/SmtpEmailChannel.cs", StringComparison.Ordinal));
        const string anchor = "await _calls.InvokeAsync(";
        Assert.Single(Regex.Matches(smtp.Code, Regex.Escape(anchor)));
        var early = sources.Select(x => x == smtp ? (x.File, x.Code.Replace(anchor, "await client.NoOpAsync(cts.Token);\n            " + anchor, StringComparison.Ordinal)) : x).ToList();
        var mailBreach = Violations(ScanAll(early, allowed), allowed);
        output.WriteLine($"[8-ج] حَقنُ SMTP: {string.Join(" · ", mailBreach)}");
        Assert.Equal([$"sdk_verb_outside_invoker:{smtp.File} (1/5)"], mailBreach);
    }

    /// <summary>والطَبَقَةُ في السكربتِ والتَوأَمُ هُنا يَقرَآنِ القائِمَةَ نَفسَها والأَنواعَ نَفسَها — نَصٌّ في السكربتِ لا يَتَباعَدُ عَن هذا المِلَفِّ صامِتاً.</summary>
    [Fact]
    public void The_script_and_its_twin_read_one_allow_list_with_one_type_rule()
    {
        var script = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "scripts", "verify-architecture.sh"));
        Assert.Contains("verify-outbound-allowlist.txt", script, StringComparison.Ordinal);
        foreach (var type in Regex.Match(RawTypes.ToString(), @"\(\?:([^)]*)\)").Groups[1].Value.Split('|'))
            Assert.Contains(type, script, StringComparison.Ordinal);
        Assert.Contains(@"\.CreateClient\(", script, StringComparison.Ordinal);
        Assert.Contains("InvokeAsync|HelperAsync", script, StringComparison.Ordinal);
        Assert.Contains("Layer 8c", script, StringComparison.Ordinal);
    }
}
