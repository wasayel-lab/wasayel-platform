using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ سكربتُ «ما حُذِفَ قَبلَ السَحب» — تَوصيفٌ قَبلَ التَبديل (‏2026-11-04، الموجَة ١٢ G1 · ١١؛ B-1 + B-4 + G-DOCS-4) ════════════════════════════════════
//
// **ما يُثَبَّتُ هُنا لِيَبقى** (القاعِدَة ٣): `scripts/withdraw-erased-owner-listings.cs` يَعرِضُ افتِراضاً ويَخرُجُ بِـ2، ولا يَكتُبُ إلّا بِـ`--apply` وعَدَدٍ يُطابِقُ العَرض،
// ويَكتُبُ بَيانَ ما سيُمَسُّ قَبلَ أَوَّلِ حِفظ، ويَفشَلُ عَلى صِفرِ مَتجَر، ولا يَمَسُّ المُخَطَّط، ويُنادي `AccountErasure.CloseIncomingAsync`، ويَكتُبُ سَطرَ تَدقيقِ
// `listing.withdraw_erased_owner` لِكُلِّ حِسابٍ يَمَسُّه — ورُموزُ خُروجِه 0/1/2.
// **وما يُطبَعُ ولا يُثَبَّت**: اليَومَ لا يُنادي `EraseCopiesAsync` (فَتَبقى نُسَخُ الاسمِ كُلُّها لِحِسابٍ حُذِفَ قَبلَ `548ea4d`)، ولا يَكتُبُ إشعارَ جَرَسٍ لِمُشتَري الصَفقَةِ المُلغاة
// (ورَأسُه يَقولُ إنّ السَبَبَ «SignalR»)، وسَطرُ تَدقيقِه هَدَفُه `User` المَمحُوّ فَيُخفي `HideErasedActors` حاشِيَتَه كُلَّها، ولا يَحذِفُ مِلَفّاً. مَقيساً بِتَشغيلِه عَلى حِسابٍ
// مَبذورٍ (رِسالَةُ الكوميت). وهذا يَتَغَيَّرُ في كوميتِ التَبديل؛ والمِلَفُّ يَبقى بِبَصمَتِه.

public class ErasedOwnerListingScriptTests(ITestOutputHelper output)
{
    internal static string ScriptText()
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "scripts", "withdraw-erased-owner-listings.cs");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 3000, $"أَداةٌ عَمياء: {path} طولُه {text.Length}.");
        return text;
    }

    [Fact]
    public void The_script_keeps_its_show_first_confirm_backup_contract_and_prints_what_it_does_not_do_yet()
    {
        var text = ScriptText();
        var code = WriteEndpointGuardTests.StripComments(text);
        foreach (var n in new[]
                 {
                     "if (!apply)", "return 2;", "if (confirm is null || confirm != changes)", "--apply --confirm {changes}", "AutoCreateSchemaObjects = AutoCreate.None",
                     "✗ BLIND CHECK", "AccountErasure.CloseIncomingAsync(", "File.WriteAllTextAsync(manifestPath", "\"listing.withdraw_erased_owner\"", "return 0;", "return 1;",
                 })
            Assert.Contains(n, code, StringComparison.Ordinal);
        var gate = code.IndexOf("if (confirm is null || confirm != changes)", StringComparison.Ordinal);
        var manifest = code.IndexOf("File.WriteAllTextAsync(manifestPath", StringComparison.Ordinal);
        var firstSave = code.IndexOf("SaveChangesAsync", StringComparison.Ordinal);
        Assert.True(code.IndexOf("if (!apply)", StringComparison.Ordinal) < gate && gate < manifest && manifest < firstSave, "كِتابَةٌ قَبلَ العَرضِ أَو التَأكيدِ أَو البَيان.");
        output.WriteLine($"يُنادي EraseCopiesAsync: {code.Contains("AccountErasure.EraseCopiesAsync(", StringComparison.Ordinal)} · يَكتُبُ إشعاراً: {code.Contains("ClosedByErasureFor(", StringComparison.Ordinal)} · "
            + $"هَدَفُ التَدقيق: {(code.Contains("\"listing.withdraw_erased_owner\", \"User\"", StringComparison.Ordinal) ? "User" : "غَيرُه")} · يَحذِفُ مِلَفّات: {code.Contains("DeleteFilesAsync(", StringComparison.Ordinal)} · "
            + $"الرَأسُ يَقولُ SignalR: {text.Contains("SignalR", StringComparison.Ordinal)}");
    }
}
