using System.Text;
using System.Text.RegularExpressions;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ «اليَوم» — رَفضُ `create_tenant` عِندَ حَدِّ المَتاجِرِ نَصٌّ عَرَبِيٌّ حَرفِيٌّ في `AgentService.cs` (القاعِدَتانِ ٣ و١١) ═══
//
// **المَقيس** (‏2026-10-24، عِندَ `2939229`): أَداةُ وَكيلِ الاستوديو `create_tenant` حينَ تَرُدُّ `CheckBuildAsync` تُرجِعُ
// «بَلَغتَ حَدَّ التَطبيقاتِ في باقَتِك ({Used} مِن {Limit}). اِحذِف تَطبيقاً أَو اِرفَع باقَتَك مِن /studio/billing.» — مَكتوبَةً في
// `.cs` خارِجَ `ar.json` («ب» S11·٢١، وتَصحيحاه)، ولا صَفَّ يُثَبِّتُ النَصَّ نَفسَه. وصِياغَتُه قَرارُ المالِك («ب» S11·١٨ و·٢١، مَربوطَةٌ
// بِأ‑٤) — فَيُرَحَّلُ بِحَرفِه وَحدَه، والبُرهانُ أَنّ ما يُنتَجُ لَم يَتَغَيَّر بايتاً.
//
// **ثَلاثَةُ صُفوف**: (١) النَصُّ المُنتَجُ بِحَرفِه لِوَسيطَينِ (3، 3) مَقروءاً مِن الكودِ اليَوم — يَنقَلِبُ بِاسمِه في كوميتِ التَرحيل؛
// (٢) الرَفضُ يَتبَعُ بَوّابَةَ `CheckBuildAsync` في جِسمِ `CreateTenantAsync` — يَبقى بِلا تَعديل؛ و(٣) البُرهانُ الحَيُّ
// `LiveAgentStoreLimitRefusalProofTests` يُنادي المُنَفِّذَ الحَقيقيَّ عَلى Postgres ويُقارِنُ البايتات — يَبقى بِلا تَعديل.

public class AgentStoreLimitRefusalTests(ITestOutputHelper output)
{
    /// <summary>النَصُّ بِحَرفِه كَما يَبلُغُ الوَكيلَ والمُستَخدِم — ثابِتٌ قَبلَ التَرحيلِ وبَعدَه.
    ///
    /// <para><b>‏2026-10-25 (بَندُ المُنَسِّقِ في تَيّارِ W8-3 — تَبديلٌ مَقصودٌ مَرئِيّ، يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه)</b>: سَقَطَت الجُملَةُ الثانِيَةُ
    /// <see cref="DeadDoorSentence"/> وَحدَها — بابانِ لا يُفتَحان (صَفُّ
    /// <see cref="The_store_limit_refusal_states_the_limit_and_sends_to_no_door_that_does_not_open"/>) — ولَم يُكتَب حَرفٌ جَديد.</para></summary>
    internal static string ExpectedFor(int used, int limit) =>
        $"بَلَغتَ حَدَّ التَطبيقاتِ في باقَتِك ({used} مِن {limit}).";

    /// <summary>الجُملَةُ الَّتي سَقَطَت بِحَرفِها (‏<c>f555fdf</c> وما قَبلَه) — بِمَسافَتِها الأولى؛ مِن «أ» S14·٤: «لا تُكتَبُ دَعوَةٌ لا تَكتَمِل».</summary>
    internal const string DeadDoorSentence = " اِحذِف تَطبيقاً أَو اِرفَع باقَتَك مِن /studio/billing.";

    internal static string AgentCode()
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Services", "AgentService.cs");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 20_000, $"أَداةٌ عَمياء: {path} لَم يُقرَأ.");
        return text;
    }

    /// <summary>جِسمُ <c>CreateTenantAsync</c> — مِن تَوقيعِه إلى الدالَّةِ الَّتي تَليه.</summary>
    internal static string CreateTenantBody()
    {
        var code = AgentCode();
        var start = code.IndexOf("private async Task<(bool, string)> CreateTenantAsync(", StringComparison.Ordinal);
        var end = code.IndexOf("private async Task<(bool, string)> SetCategoriesAsync(", StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "جِسمُ CreateTenantAsync لَم يوجَد.");
        return code[start..end];
    }

    /// <summary>(‏2026-10-24، كوميتُ التَرحيل — انقَلَبَ بِقَرارٍ مَرئِيّ: كانَ <c>Today_the_store_limit_refusal_is_written_in_the_agent_code_and_yields_these_bytes</c>
    /// يَقرَأُ الحَرفِيَّ مِن جِسمِ <c>CreateTenantAsync</c> ويُقارِنُ ما يُنتِجُه بِـ<see cref="ExpectedFor"/>؛ وصارَ يَقرَأُ القيمَةَ مِن القامُوسِ
    /// ويُقارِنُ ما تُنتِجُه بِالوَسيطَينِ نَفسَيهِما بِالثابِتِ نَفسِه — بايتاً — والجِسمُ بِلا الحَرفِيّ. و<see cref="ExpectedFor"/> لَم يُمَسّ.)</summary>
    [Fact]
    public void The_store_limit_refusal_is_read_from_the_dictionary_and_yields_the_same_bytes()
    {
        var key = AgentToolExecutor.StoreLimitRefusalKey;
        Assert.Contains(key, LocaleCatalog.Lexicon);
        var template = LocaleCatalog.Find(LocaleCatalog.Arabic, key);
        Assert.False(string.IsNullOrWhiteSpace(template));
        Assert.Equal(Encoding.UTF8.GetBytes(ExpectedFor(3, 3)), Encoding.UTF8.GetBytes(string.Format(template!, 3, 3)));

        var body = CreateTenantBody();
        Assert.Contains("LocaleCatalog.Text(LocaleCatalog.Arabic, StoreLimitRefusalKey), gate.Used, gate.Limit)", body, StringComparison.Ordinal);
        foreach (var literal in new[] { "بَلَغتَ حَدَّ التَطبيقاتِ", "اِرفَع باقَتَك" })
            Assert.DoesNotContain(literal, AgentCode(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_store_limit_refusal_follows_the_build_gate_inside_create_tenant()
    {
        var body = CreateTenantBody();
        var gate = body.IndexOf("await tierService.CheckBuildAsync(quotaOwner, ct);", StringComparison.Ordinal);
        var refused = body.IndexOf("if (!gate.Allowed)", StringComparison.Ordinal);
        var store = body.IndexOf("s.Store(new Tenant", StringComparison.Ordinal);
        Assert.True(gate > 0, "CreateTenantAsync لا يَسأَلُ CheckBuildAsync.");
        Assert.True(refused > gate && store > refused,
            $"التَرتيبُ: البَوّابَة {gate} ← الرَدّ {refused} ← الكِتابَة {store} — يَجِبُ أَن يَسبِقَ الرَدُّ الكِتابَة.");
    }

    // ─── بَندُ المُنَسِّقِ في تَيّارِ W8-3 (‏2026-10-25): الرَفضُ يَدعو إلى بابَينِ لا يُفتَحان ──────────────────────────────────────
    //
    // «أ» S14·٤ قاسَه في الطَريقِ ولَم يَمَسَّه: «اِحذِف تَطبيقاً» لا يُحَرِّرُ حِصَّةً — `StudioUser.StoresBuilt` لا يَكتُبُه إلّا
    // `RecordStoreBuiltAsync` (‏`StoresBuilt++`)، وحَذفُ مَتجَرٍ لا يُنقِصُه — و«اِرفَع باقَتَك مِن /studio/billing» لا يُفتَحُ ما دامَت
    // كُلُّ دَرَجَةٍ مُسَعَّرَةً و`StudioTierPurchase.SelfServiceCheckoutExists` ‏false (فَلا دَرَجَةَ `IsSelfGrantable`). القاعِدَة ٣:
    // يُثَبَّتُ «اليَوم» في كوميتٍ مُستَقِلّ، ويُقلَبُ الصَفُّ بِاسمِه في كوميتِ الصِياغَة.

    private static readonly Regex StoresBuiltWrite = new(@"StoresBuilt\s*(?:\+\+|--|[+\-]=|=(?!=))", RegexOptions.Compiled);

    /// <summary>كُلُّ كِتابَةٍ لِـ<c>StoresBuilt</c> في <c>libs</c> و<c>apps</c> (الكودُ لا التَعليق) — بِنَصِّها، مِن الماسِحِ الواحِد.</summary>
    internal static List<string> StoresBuiltWrites()
        => EntitlementContractTests.SourceFiles()
            .SelectMany(f => StoresBuiltWrite.Matches(WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(f.Text)))
                .Select(m => Regex.Replace(m.Value, @"\s+", "")))
            .ToList();

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-25، كوميتُ الصِياغَة)</b> — كانَ <c>Today_the_store_limit_refusal_sends_the_user_to_two_doors_neither_of_which_opens</c>
    /// (‏<c>f555fdf</c>): القيمَةُ تَحمِلُ «اِحذِف تَطبيقاً» و«اِرفَع باقَتَك مِن /studio/billing»، و«كِتاباتُ StoresBuilt: 1 (StoresBuilt++) · دَرَجاتٌ تُمنَحُ
    /// ذاتِيّاً: 0 مِن 4». <b>والآن القيمَةُ تَقولُ الحَدَّ وَحدَه</b> — وهي القَديمَةُ بِلا <see cref="DeadDoorSentence"/> حَرفاً (الفَرقُ مَحصورٌ فيها) — <b>والمُقَدِّماتُ
    /// تُقاسُ هُنا بِأَعيانِها</b>: يَومَ يُنقِصُ بابٌ <c>StoresBuilt</c>، أَو تُمنَحُ دَرَجَةٌ ذاتِيّاً، أَو يولَدُ بابُ حَذفٍ لِلتَطبيق، يُحمِرُّ هذا الصَفُّ
    /// — فَالجُملَةُ تَعودُ بِصِياغَةِ المالِك («ب→تَكمِلَة» S11·٢١ في سِجِلِّه) لا بِحَرفٍ مِن الوَكيل.</summary>
    [Fact]
    public void The_store_limit_refusal_states_the_limit_and_sends_to_no_door_that_does_not_open()
    {
        var template = LocaleCatalog.Find(LocaleCatalog.Arabic, AgentToolExecutor.StoreLimitRefusalKey)!;
        var writes = StoresBuiltWrites();
        var grantable = TierCatalog.All.Keys.Where(StudioTierPurchase.IsSelfGrantable).ToList();
        var appRoutes = ComplianceInspectorTests.RepoRoutes.Where(r => r.StartsWith("/studio/apps/{slug}", StringComparison.Ordinal)).ToList();
        var deleteDoors = appRoutes.Where(r => Regex.IsMatch(r, "delete|remove|archive|destroy", RegexOptions.IgnoreCase)).ToList();
        output.WriteLine($"القيمَة: {template} · كِتاباتُ StoresBuilt: {writes.Count} ({string.Join(", ", writes)}) · دَرَجاتٌ تُمنَحُ ذاتِيّاً: {grantable.Count} مِن {TierCatalog.All.Count}" +
                         $" · مَساراتُ /studio/apps/{{slug}}: {appRoutes.Count} · بابُ حَذفٍ فيها: {deleteDoors.Count}");

        // الفَرقُ مَحصورٌ في الجُملَةِ السّاقِطَة: القيمَةُ قَبلَ الكوميتِ (‏f555fdf) = القيمَةُ اليَومَ + الجُملَةُ بِحَرفِها.
        Assert.Equal("بَلَغتَ حَدَّ التَطبيقاتِ في باقَتِك ({0} مِن {1}). اِحذِف تَطبيقاً أَو اِرفَع باقَتَك مِن /studio/billing.",
                     template + DeadDoorSentence);
        foreach (var door in new[] { "اِحذِف", "اِرفَع", "/studio/billing" })
            Assert.DoesNotContain(door, template, StringComparison.Ordinal);

        Assert.Equal(new[] { "StoresBuilt++" }, writes);
        Assert.True(TierCatalog.All.Count >= 4, $"أَداةٌ عَمياء: {TierCatalog.All.Count} دَرَجَة.");
        Assert.Empty(grantable);
        Assert.False(StudioTierPurchase.SelfServiceCheckoutExists);
        Assert.True(appRoutes.Count >= 10, $"أَداةٌ عَمياء: {appRoutes.Count} مَساراً تَحتَ /studio/apps/{{slug}}.");
        Assert.Empty(deleteDoors);
    }
}
