using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>قِراءَةُ المالِكِ بَعدَ النَشرِ هِيَ ما يُرسِلُه البُرهانُ حَرفاً</b> (‏2026-11-08، مُراجَعَةُ C2c الجَولَةُ الثانِيَة · C2c-P2-01؛
/// <c>docs/DEPLOY.md</c> §٢·ي، قِسمُ Cloudflare، الخُطوَةُ ٠).</para>
///
/// <para><b>العِلَّةُ المَقيسَة</b>: الخُطوَةُ ٠ كانَت تَقولُ <c>curl … -X POST -H 'Origin: https://&lt;host&gt;' …/auth/phone/login</c> ← ‏302،
/// والبابُ يَحمِلُ <c>.RequireFormBody()</c> — فَطَلَبٌ بِلا جِسمٍ يُجابُ ‏415 بَعدَ فَحصِ الأَصلِ وقَبلَ الباب. مَقيساً عَلى خادِمٍ بارِدٍ بِلا نِطاق:
/// الأَصلُ الغَريبُ 403، وأَصلُه 415 (ومَعَ <c>X-Forwarded-Proto: https</c> ‏415)، والطَلَبُ نَفسُه بِـ<c>-d ''</c> ‏302. والـ302 في تَقريرِ التَيّارِ
/// جاءَ مِن صُفوفِ البُرهانِ الَّتي تُضيفُ نَوعَ الاستِمارَةِ وجِسماً فارِغاً — فَالأَمرُ الَّذي يُقالُ لِلمالِكِ لَم يُقَس قَطّ (القاعِدَة ١٠). وهذِه القِراءَةُ
/// فَحصُه لِلعُطلِ الكُلِّيّ: وَسيطٌ لا يُمَرِّرُ <c>X-Forwarded-Proto</c> يَرُدُّ كُلَّ استِمارَةٍ 403.</para>
///
/// <para><b>والحَدُّ مَقيسٌ آلِيّاً</b> (القاعِدَة ٢): أَعلامُ الأَمرِ في الخُطوَةِ ٠ هِيَ <c>OWNER_READ</c> في <c>scripts/prove-store-hosts.sh</c> حَرفاً،
/// والمَجموعَةُ ١٢ تُرسِلُها حَيّاً وتَنتَظِرُ الرَمزَينِ المَكتوبَين؛ والأَعلامُ تَحمِلُ جِسماً، لِأَنّ البابَ يَحمِلُ بَوّابَةَ الاستِمارَة.</para>
/// </summary>
public class OwnerOriginReadTests(ITestOutputHelper output)
{
    private const string LoginDoor = "/{slug}/auth/phone/login";

    private static string Read(string relative)
        => File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, relative));

    /// <summary>الخُطوَةُ ٠ مِن قِسمِ Cloudflare — مِن سَطرِها إلى سَطرِ الخُطوَةِ ١.</summary>
    private static string Step0()
    {
        var doc = Read("docs/DEPLOY.md");
        var section = doc.IndexOf("### Cloudflare والمُضيفاتُ مِن أَوَّلِها إلى آخِرِها", StringComparison.Ordinal);
        Assert.True(section > 0, "قِسمُ Cloudflare غائِبٌ عَن دَليلِ النَشر — الأَداةُ عَمياء.");
        var start = doc.IndexOf("\n0. **البِناءُ يَحمِلُ التَحصين**", section, StringComparison.Ordinal);
        var end = doc.IndexOf("\n1. **DNS**", start + 1, StringComparison.Ordinal);
        Assert.True(start > section && end > start, "الخُطوَةُ ٠ غائِبَة — الأَداةُ عَمياء.");
        return doc[start..end];
    }

    private static readonly Regex DocCommand = new(
        @"`curl (?<flags>[^`]*?) -H 'Origin: (?<origin>[^']+)' https://<host>/<store>/auth/phone/login`", RegexOptions.CultureInvariant);

    private static readonly Regex ScriptFlags = new(@"^OWNER_READ=\((?<flags>[^)\n]*)\)$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex SendsABody = new(@"(^|\s)(-d|--data|--data-raw|--data-urlencode)\s", RegexOptions.CultureInvariant);

    [Fact]
    public void Step_0_sends_the_flags_the_host_proof_sends()
    {
        var step = Step0();
        var commands = DocCommand.Matches(step).Select(m => (Flags: m.Groups["flags"].Value, Origin: m.Groups["origin"].Value)).ToList();
        var script = ScriptFlags.Matches(Read("scripts/prove-store-hosts.sh")).Select(m => m.Groups["flags"].Value).ToList();
        output.WriteLine($"أَوامِرُ الخُطوَةِ ٠: {commands.Count} ({string.Join(" · ", commands.Select(c => $"[{c.Flags}] {c.Origin}"))}) · OWNER_READ في البُرهان: {script.Count}");

        Assert.NotEmpty(commands);                    // صِفرُ أَمرٍ = أَداةٌ عَمياء (القاعِدَة ١٠)
        Assert.Single(script);
        Assert.All(commands, c => Assert.Equal(script[0], c.Flags));
    }

    /// <summary>والأَعلامُ تَحمِلُ جِسماً — والبابُ يَرُدُّ ما سِواه 415 قَبلَ أَن يَبلُغَه: موجِبٌ (بِجِسمٍ) وسالِبٌ (<c>-X POST</c> وَحدَه).</summary>
    [Fact]
    public void Step_0_carries_a_form_body_because_the_door_answers_415_without_one()
    {
        var door = WriteEndpointGuardTests.AllMinimalApiEndpoints().Where(e => e.Route == LoginDoor && e.Body.StartsWith(".MapPost", StringComparison.Ordinal)).ToList();
        var commands = DocCommand.Matches(Step0()).Select(m => m.Groups["flags"].Value).ToList();
        output.WriteLine($"البابُ {LoginDoor}: {door.Count} · بِبَوّابَةِ الاستِمارَة: {door.Count(e => e.Body.Contains(".RequireFormBody(", StringComparison.Ordinal))} · أَوامِرُ الخُطوَةِ ٠: {commands.Count}");

        Assert.Single(door);
        Assert.Contains(".RequireFormBody(", door[0].Body, StringComparison.Ordinal);
        Assert.NotEmpty(commands);
        Assert.All(commands, f => Assert.Matches(SendsABody, f));
        Assert.DoesNotMatch(SendsABody, "-s -o /dev/null -w '%{http_code}\\n' -X POST");   // ما كانَ مَكتوباً: لا جِسم
    }

    /// <summary>والخُطوَةُ تَقولُ الرُموزَ الثَلاثَةَ الَّتي يَقيسُها البُرهان: الغَريبُ 403، والأَصلُ 302، و415 بِلا جِسمٍ لا يَحكُمُ عَلى شَيء.</summary>
    [Fact]
    public void Step_0_names_the_three_codes_the_host_proof_measures()
    {
        var step = Step0();
        var script = Read("scripts/prove-store-hosts.sh");
        Assert.Contains("evil.example' https://<host>/<store>/auth/phone/login` ← **403**", step, StringComparison.Ordinal);
        Assert.Contains("`-H 'Origin: https://<host>'` ← **302**", step, StringComparison.Ordinal);
        Assert.Contains("**415**", step, StringComparison.Ordinal);
        Assert.Contains("owner_read OWNER_READ      \"https://evil.example\" 403", script, StringComparison.Ordinal);
        Assert.Contains("owner_read OWNER_READ      \"$SCHEME://$R\"         302", script, StringComparison.Ordinal);
        Assert.Contains("owner_read OWNER_READ_BARE \"$SCHEME://$R\"         415", script, StringComparison.Ordinal);
    }
}
