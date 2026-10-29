using System.Text.RegularExpressions;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ «التكاملات الجاهزة» في مَرجِعِ القُدُراتِ تَعِدُ المُحَلِّلَ بِثَلاثَةٍ لا يَحمِلُها الكود — تَوصيفُ «اليَوم» ═══
//
// قَرارُ المالِك د21 (+د48) (‏2026-10-29): «تُحذف من «التكاملات الجاهزة» كل ما لا يحمله الكود: الفوترة الإلكترونيّة ZATCA، وسطر «منصّة إيجار /
// REGA»، و«وضمانه» في سطر تحصيل الدفع؛ وسطر الرسائل عبر Unifonic/Taqnyat يُستبدل بالمدمج فعلًا: Twilio لرسائل رمز الدخول وحدها. ويُضاف إلى
// «المحدوديات» سطر صادق». **قَبلَ التَبديل** (القاعِدَة ٣) يُثَبَّتُ ما يَبلُغُ مُوَجِّهَ التَحليلِ اليَوم مِن المَورِدِ المُضَمَّنِ بِعَينِه
// (`SaudiDataProvider.CapabilitiesMarkdown`) — والطَرَفُ المُقابِل: صِفرُ ذِكرٍ لِـUnifonic وTaqnyat وZATCA وREGA في كودِ `libs`/`apps`
// (التَعليقاتُ مَنزوعَة، ومُجَلَّدُ `Data/` الَّذي يَحمِلُ المَرجِعَ مُستَثنى)، والمَدمَجُ لِلرَسائِلِ `TwilioSmsChannel`. صَفُّ «Today_» يُقلَبُ
// بِاسمِه في كوميتِ التَبديل، وصَفّا الكودِ يَبقَيانِ بِلا تَعديلِ حَرف.
public class CapabilitiesIntegrationsCharacterizationTests(ITestOutputHelper output)
{
    internal const string UnifonicLineOld = "- **Unifonic / Taqnyat**: رسائل OTP والإشعارات.";
    internal const string ZatcaLineOld = "- **ZATCA**: الفوترة الإلكترونية (فاتورة) — متاح كتكامل.";
    internal const string RegaLineOld = "- **منصة إيجار / REGA**: للقطاع العقاري.";
    internal const string ClassifiedsContraLineOld = "- **متى لا يُستخدم**: تحتاج تحصيل الدفع وضمانه داخل المنصة (استخدم marketplace).";

    internal static string[] CapabilityLines() => new SaudiDataProvider().CapabilitiesMarkdown.Replace("\r\n", "\n").Split('\n');

    private static readonly Regex Promised = new(@"\b(Unifonic|Taqnyat|ZATCA|REGA)\b", RegexOptions.Compiled);

    /// <summary>كُلُّ ذِكرٍ لِلأَسماءِ الأَربَعَةِ في كودِ <c>libs</c> و<c>apps</c> — بِلا تَعليقاتٍ، وبِلا <c>Data/</c> ولا <c>bin</c>/<c>obj</c>.</summary>
    internal static IReadOnlyList<string> PromisedNamesInCode(out int scanned)
    {
        var hits = new List<string>();
        scanned = 0;
        foreach (var root in new[] { "libs", "apps" })
            foreach (var f in Directory.EnumerateFiles(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, root), "*.*", SearchOption.AllDirectories))
            {
                var sep = Path.DirectorySeparatorChar;
                if (f.Contains($"{sep}bin{sep}") || f.Contains($"{sep}obj{sep}") || f.Contains($"{sep}Data{sep}")) continue;
                if (!(f.EndsWith(".cs") || f.EndsWith(".razor") || f.EndsWith(".json"))) continue;
                scanned++;
                var text = File.ReadAllText(f);
                if (!f.EndsWith(".json")) text = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(text));
                foreach (Match m in Promised.Matches(text))
                    hits.Add($"{Path.GetRelativePath(ThemeZeroEquivalenceTests.RepoRoot, f)}: {m.Value}");
            }
        return hits;
    }

    [Fact]
    public void No_code_outside_the_reference_carries_unifonic_taqnyat_zatca_or_rega()
    {
        var hits = PromisedNamesInCode(out var scanned);
        output.WriteLine($"مِلَفّاتٌ مَفحوصَة: {scanned} · ذِكرٌ في الكود: {hits.Count}");
        Assert.True(scanned > 500, $"أَداةٌ عَمياء: {scanned} مِلَفّاً.");
        Assert.Empty(hits);
    }

    [Fact]
    public void The_integrated_sms_channel_is_twilio_for_the_sign_in_code()
    {
        var twilio = UpgradePromptPriceTests.Code("libs", "kits", "Auth", "ACommerce.Kit.Auth.Providers.Twilio", "TwilioSmsChannel.cs");
        Assert.Contains("class TwilioSmsChannel : IOtpChannel", twilio, StringComparison.Ordinal);
        Assert.Contains("public string ChannelName => \"Twilio\";", twilio, StringComparison.Ordinal);
    }

    [Fact]
    public void Today_the_integrations_list_promises_zatca_rega_and_unifonic_and_the_classifieds_line_a_payment_guarantee()
    {
        var lines = CapabilityLines();
        output.WriteLine($"أَسطُرُ المَرجِع: {lines.Length}");
        Assert.Contains(UnifonicLineOld, lines);
        Assert.Contains(ZatcaLineOld, lines);
        Assert.Contains(RegaLineOld, lines);
        Assert.Contains(ClassifiedsContraLineOld, lines);
        Assert.DoesNotContain(lines, l => l.Contains("Twilio", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("الفوترة الإلكترونية والربط بمنصة إيجار", StringComparison.Ordinal));
    }
}
