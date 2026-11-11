using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>HTTP يُغلَقُ عِندَ الحافَّةِ قَبلَ أَن يُعتَمَدَ عَلى <c>__Host-</c></b> (‏2026-11-11، W13-4·C2z·1 — مُراجَعَةُ C2c · SEC-C2c-R3-1 وSEC-C2c-R2-2؛
/// <c>docs/DEPLOY.md</c> §٢·ي، قِسمُ Cloudflare، الخُطوَةُ ٣·ب؛ ADR-050 §١٥).</para>
///
/// <para><b>العِلَّةُ المَقيسَة</b>: اسمُ كَعكَةِ الجَلسَةِ قَرارٌ لِكُلِّ طَلَبٍ بِـ<c>AuthSession.SecureFor</c> — فَعَلى <c>http://</c> الأَسماءُ القَديمَةُ بِلا
/// <c>Secure</c>، وهي ما يَزرَعُه مُضيفٌ شَقيقٌ بِـ<c>Domain=R</c>. وصِفرُ تَحويلٍ إلى HTTPS في الكود، وخُطُواتُ Cloudflare كانَت لا تَذكُرُ «Always Use HTTPS»
/// (صِفرُ مَوضِعٍ في الدَليل) وHSTS آخِرُها. <b>والحَدُّ مَقيسٌ آلِيّاً</b> (القاعِدَة ٢): الخُطوَةُ في القِسمِ وقَبلَ الخُطوَةِ ٦، وقِراءَتُها لِلعائِلاتِ الثَلاث،
/// وتَراجُعُها، وصَفُّ البُرهانِ الَّذي يَقرَؤُها بَعدَ النَشر — وغِيابُ أَيٍّ مِنها يُحمِرّ.</para>
/// </summary>
public class DeployEdgeHttpsTests(ITestOutputHelper output)
{
    private static string Read(string relative)
        => File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, relative));

    private static string CloudflareSection()
    {
        var doc = Read("docs/DEPLOY.md");
        var start = doc.IndexOf("### Cloudflare والمُضيفاتُ مِن أَوَّلِها إلى آخِرِها", StringComparison.Ordinal);
        var end = doc.IndexOf("\n## ٣.", start + 1, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "قِسمُ Cloudflare غائِب — الأَداةُ عَمياء.");
        return doc[start..end];
    }

    [Fact]
    public void The_edge_closes_http_before_the_domain_is_set()
    {
        var s = CloudflareSection();
        var step = s.IndexOf("\n3·ب.", StringComparison.Ordinal);
        var step4 = s.IndexOf("\n4. **شَهادَةُ الأَصل**", StringComparison.Ordinal);
        var step6 = s.IndexOf("\n6. **المُتَغَيِّرُ عِندَ المُستَضيف**", StringComparison.Ordinal);
        output.WriteLine($"الخُطوَةُ ٣·ب عِندَ {step} · الخُطوَةُ ٤ عِندَ {step4} · الخُطوَةُ ٦ عِندَ {step6}");
        Assert.True(step > 0, "خُطوَةُ «Always Use HTTPS» غائِبَة.");
        Assert.True(step4 > step && step6 > step, "الخُطوَةُ ٣·ب يَجِبُ أَن تَسبِقَ الخُطوَةَ ٦ (ضَبطُ النِطاق).");
        var body = s[step..step4];
        Assert.Contains("Always Use HTTPS: On", body, StringComparison.Ordinal);
        Assert.Contains("always-use-https", body, StringComparison.Ordinal);
    }

    private static readonly Regex Read_ = new(@"`curl -sI http://(?<host>[^/`]+)/ \| grep -iE '\^\(HTTP\|location\)'`", RegexOptions.CultureInvariant);

    [Fact]
    public void Its_read_covers_the_apex_the_store_and_the_role_host_over_plain_http()
    {
        var s = CloudflareSection();
        var step = s[s.IndexOf("\n3·ب.", StringComparison.Ordinal)..s.IndexOf("\n4. **شَهادَةُ الأَصل**", StringComparison.Ordinal)];
        var hosts = Read_.Matches(step).Select(m => m.Groups["host"].Value).ToList();
        output.WriteLine($"قِراءاتُ http: {hosts.Count} ({string.Join(" · ", hosts)})");
        Assert.Equal(["R", "<slug>.R", "<slug>-<role>.R"], hosts);
        Assert.Contains("location: https://", step, StringComparison.Ordinal);
    }

    [Fact]
    public void Its_undo_says_what_turning_it_off_reopens()
    {
        var s = CloudflareSection();
        var undo = s[s.IndexOf("#### كَيفَ يُتَراجَع", StringComparison.Ordinal)..];
        Assert.Contains("**Always Use HTTPS** ← Off", undo, StringComparison.Ordinal);
    }

    [Fact]
    public void The_host_proof_reads_it_after_the_deploy()
    {
        var script = Read("scripts/prove-store-hosts.sh");
        Assert.Contains("١٦) HTTP يُحَوَّلُ إلى HTTPS عِندَ الحافَّةِ", script, StringComparison.Ordinal);
        Assert.Contains("if [ \"$DIRECT\" = 1 ] && [ \"$SCHEME\" = https ]; then", script, StringComparison.Ordinal);
        Assert.Contains("\"http://$h/\"", script, StringComparison.Ordinal);
    }
}
