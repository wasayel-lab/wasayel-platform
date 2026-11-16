using System.Text.RegularExpressions;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ الدَرَجاتُ تَكُفُّ عَن عَرضِ التَحاليلِ والتَحسيناتِ والتَصدير — وما تَيَتَّمَ مِن حُقولِها مُثَبَّتٌ بِشَرطِ رَفعِه (‏2026-11-07، الموجَة ١٣ W13-1·٧ — ADR-049 §٧) ═══
//
// **المَقيسُ قَبل**: صَفحَةُ الباقَةِ تَرسُمُ «‏40 تَحاليل / شَهر · ‏200 تَحسينات / شَهر · تَصدير PDF / Excel»، وسَطرُ الشَريطِ الجانِبيِّ «0/40 تَحليل · 0/40 تَطبيق»،
// ومَزايا نافِذَةِ التَرقِيَةِ تَعُدُّ التَحاليلَ والتَحسيناتِ والتَصدير، وتَصديرُ الدِراسَةِ يُحجَبُ بِـ`AllowExport` فَيُقالُ «ميزَةٌ في الباقاتِ الأَعلى».
// والتَحليلُ خَرَجَ مِن الرِحلَة (·٥)، فَما يُعرَضُ يَعِدُ بِما لا يوجَد.
// **وما يُثَبَّت**: لا صَفحَةَ تَعرِضُ حَقلاً مِن الحُقولِ اليَتيمَة، ولا نُقطَةَ ولا صَفحَةَ تُنادي بَوّابَةً مِنها، ولا رَمزَ سَبَبٍ يُكتَبُ في رابِط؛ وتَصديرُ
// الدِراسَةِ القَديمَةِ بَياناتُ صاحِبِها بِلا باقَة. **والقائِمَةُ تَتَقَلَّصُ ولا تَنمو، وشَرطُ رَفعِها ADR التَسعير (W13-2)**: المالِكُ يَقولُ الرَوافِعَ الجَديدَة
// (القاعِدَة ١٦)، فَيُعادُ تَشكيلُ `TierLimits` مَرَّةً واحِدَةً هُناك لا مَرَّتَين.

public class TierOrphanFieldsTests(ITestOutputHelper output)
{
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    /// <summary>اليَتامى — اسمٌ ومَوضِعُ تَعريفِه. <b>شَرطُ رَفعِ القائِمَةِ كُلِّها: ADR التَسعير (W13-2)</b>، يُعيدُ تَشكيلَ الدَرَجاتِ بِرَوافِعِ المالِك.</summary>
    /// <remarks>‏(‏2026-11-07، مُراجَعَةُ عَدَساتِ C1 · W13-1·DD-1 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) خَرَجَ مِنها ثَلاثَة: <c>AnalysesPerMonth</c> و<c>RefinesPerMonth</c>
    /// و<c>AllowExport</c> — لَيسَت بِلا قارِئٍ في وَقتِ التَشغيل: <c>TierCatalog.Exceeds</c> يَقرَؤُها، ويُناديه <c>NextAbove</c> (سُلَّمُ نافِذَةِ التَرقِيَةِ في كُلِّ
    /// رَسم) و<c>StudioTierRepair.IsStuck</c> (مَن يُعيدُه <c>scripts/repair-studio-tier.cs</c> إلى المَجّانِيَّة). والماسِحُ لا يَراهُما: يَستَثني مِلَفَّ الدَرَجاتِ
    /// ولا يَقرَأُ <c>scripts/</c>. مَقيسٌ بِالحَقنِ في تَقريرِ العَدَسَة: <c>growth.AnalysesPerMonth</c> 10 ← 50 أَحمَرَ صَفَّينِ في <c>StudioTierRepairTests</c> و<c>UpgradeLadderTests</c>
    /// وبَقِيَت هذِه القائِمَةُ خَضراء. فَصارَت مُدخَلاتِ السُلَّمِ والإصلاحِ بِأَسمائِها في <see cref="LadderAndRepairInputsUntilThePricingAdr"/> — والقائِمَةُ تَقَلَّصَت.</remarks>
    internal static readonly (string Member, string DeclaredIn)[] OrphanedUntilThePricingAdr =
    [
        ("AnalysesUsed", "StudioUser.cs"),
        ("RefinesUsed", "StudioUser.cs"),
        ("CheckAnalyzeAsync", "StudioTier.cs"),
        ("CheckRefineAsync", "StudioTier.cs"),
        ("RecordAnalysisAsync", "StudioTier.cs"),
        ("RecordRefineAsync", "StudioTier.cs"),
        ("StudioUpgradeReason.Analyses", "StudioTier.cs"),
        ("StudioUpgradeReason.Refines", "StudioTier.cs"),
        ("StudioUpgradeReason.Export", "StudioTier.cs"),
    ];

    /// <summary>المَصادِرُ الحَيَّةُ بِلا تَعليقات — خارِجَ مِلَفَّي التَعريفِ (الدَرَجاتُ والحِساب) وخارِجَ نافِذَةِ التَرقِيَةِ الَّتي تَحمِلُ المُعجَمَ المُغلَقَ لِلأَسباب.</summary>
    private static IEnumerable<(string File, string Code)> Readers()
        => StudioAnalysisRetiredTests.Sources().Where(s =>
               !s.File.EndsWith("/Services/Incubator/StudioTier.cs", StringComparison.Ordinal)
            && !s.File.EndsWith("/Services/Incubator/StudioUser.cs", StringComparison.Ordinal)
            && !s.File.EndsWith("/Components/UpgradePrompt.razor", StringComparison.Ordinal));

    /// <summary>سَلاسِلُ C# الحَرفِيَّةُ وَحدَها — لا المُدرَجَة (<c>$"…{X}…"</c>): ما بَينَ قَوسَيها كودٌ يُقرَأ، وقَد أَمسَكَ صَفُّ القِياسِ أَدناه نُسخَةً
    /// أَولى كانَت تُبَيِّضُها فَتَعمى عَن <c>?upgrade={StudioUpgradeReason.Export}</c> بِعَينِه.</summary>
    private static readonly Regex CsStrings = new(@"(?<!\$@?)(?<!\$)@?""(?:[^""\\\n]|\\.)*""", RegexOptions.Compiled);

    public static TheoryData<string> Orphans() => new(OrphanedUntilThePricingAdr.Select(o => o.Member));

    [Theory]
    [MemberData(nameof(Orphans))]
    public void An_orphaned_tier_member_is_shown_by_no_page_and_enforced_by_no_door(string member)
    {
        var token = new Regex(member.Contains('.') ? Regex.Escape(member) + @"\b" : @"\." + Regex.Escape(member) + @"\b");
        var readers = Readers().ToList();
        // في `.cs` تُبَيَّضُ السَلاسِلُ النَصّيَّةُ قَبلَ المُطابَقَة: وَصفُ `CapabilityCatalog` يُسَمّي الحُقولَ نَصّاً ولا يَقرَؤُها؛ وفي `.razor` لا — قيمَةُ
        // خاصِّيَّةٍ بَينَ عَلامَتَي تَنصيصٍ عَرضٌ حَقيقيّ.
        var hits = readers.Where(s => token.IsMatch(s.File.EndsWith(".cs", StringComparison.Ordinal) ? CsStrings.Replace(s.Code, "\"\"") : s.Code))
            .Select(s => s.File).ToList();
        Assert.True(readers.Count > 300, $"أَداةٌ عَمياء: {readers.Count} مَصدَراً.");
        Assert.True(hits.Count == 0, $"«{member}» يَقرَؤُه وَقتَ التَشغيل: {string.Join("، ", hits)} — فَلَيسَ يَتيماً: يُرفَعُ مِن القائِمَةِ بِسَبَبِه.");
        var declaredIn = OrphanedUntilThePricingAdr.Single(o => o.Member == member).DeclaredIn;
        var decl = Directory.EnumerateFiles(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, Template), declaredIn, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")).Single();
        var name = member.Contains('.') ? member[(member.LastIndexOf('.') + 1)..] : member;
        Assert.Contains(name, File.ReadAllText(decl), StringComparison.Ordinal);
    }

    /// <summary>والماسِحُ يُقاسُ (القاعِدَة ١٠): سَطرُ صَفحَةِ الباقَةِ القَديمُ بِحَرفِه يُصاب، وبَوّابَةُ الاستِئنافِ القَديمَةُ تُصاب، ونَصُّ وَصفٍ في سِلسِلَةٍ لا يُصاب.</summary>
    [Theory]
    [InlineData("x/Billing.razor", "<li>@t.AnalysesPerMonth @L.Markup(\"studio.billing.per_analyses\")</li>", "AnalysesPerMonth", true)]
    [InlineData("x/Endpoints.cs", "var gate = await tier.CheckAnalyzeAsync(userId);", "CheckAnalyzeAsync", true)]
    [InlineData("x/Endpoints.cs", "return Results.Redirect($\"/x?upgrade={Services.Incubator.StudioUpgradeReason.Export}\");", "StudioUpgradeReason.Export", true)]
    [InlineData("x/Catalog.cs", "new(\"analysis\", \"TierLimits.AnalysesPerMonth (StudioTier.cs:30)\"),", "AnalysesPerMonth", false)]
    public void The_orphan_scanner_sees_a_read_and_ignores_a_description(string file, string code, string member, bool hit)
    {
        var token = new Regex(member.Contains('.') ? Regex.Escape(member) + @"\b" : @"\." + Regex.Escape(member) + @"\b");
        var text = file.EndsWith(".cs", StringComparison.Ordinal) ? CsStrings.Replace(code, "\"\"") : code;
        Assert.Equal(hit, token.IsMatch(text));
    }

    [Fact]
    public void The_orphan_list_only_shrinks_and_names_its_removal_condition()
    {
        output.WriteLine($"يَتامى الدَرَجاتِ حَتّى ADR التَسعير (W13-2): {OrphanedUntilThePricingAdr.Length} — "
            + string.Join("، ", OrphanedUntilThePricingAdr.Select(o => o.Member)));
        Assert.True(OrphanedUntilThePricingAdr.Length <= 9, "القائِمَةُ نَمَت — يَتيمٌ جَديدٌ يُقَرَّرُ في مُراجَعَةٍ لا يُضافُ صامِتاً.");
        Assert.Equal(OrphanedUntilThePricingAdr.Length, OrphanedUntilThePricingAdr.Select(o => o.Member).Distinct().Count());
    }

    // ─── حُقولٌ لا تُعرَضُ ولا تُفرَض، ويَقرَؤُها السُلَّمُ والإصلاح (‏2026-11-07، مُراجَعَةُ عَدَساتِ C1 · W13-1·DD-1) ──────────────────────────────────────
    //
    // **شَرطُ رَفعِها ADR التَسعير (W13-2) — ومَعَه قَرارٌ في `Exceeds`/`IsStuck`**: يَومَ يُعادُ تَشكيلُ `TierLimits` يُقرَّرُ مَعَه ما يَقرَؤُه السُلَّمُ
    // (أَيُّ دَرَجَةٍ تُعرَضُ تَرقِيَةً) وما يَقرَؤُه الإصلاحُ (مَن يُعادُ إلى المَجّانِيَّة) — وإلّا غَيَّرَ حَذفُ «يَتيمٍ» السُلَّمَ والإصلاحَ صامِتَين.

    internal static readonly string[] LadderAndRepairInputsUntilThePricingAdr = ["AnalysesPerMonth", "RefinesPerMonth", "AllowExport"];

    public static TheoryData<string> LadderInputs() => new(LadderAndRepairInputsUntilThePricingAdr);

    private static string Code(params string[] path)
        => WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine([ThemeZeroEquivalenceTests.RepoRoot, Template, .. path])));

    private static string ExceedsBody()
    {
        var tier = Code("Services", "Incubator", "StudioTier.cs");
        var exceeds = tier[tier.IndexOf("public static bool Exceeds(", StringComparison.Ordinal)..];
        return exceeds[..exceeds.IndexOf(';')];
    }

    [Theory]
    [MemberData(nameof(LadderInputs))]
    public void A_tier_field_shown_by_no_page_is_read_by_Exceeds_until_the_pricing_adr(string field)
    {
        output.WriteLine($"{field} في Exceeds: {ExceedsBody().Contains("candidate." + field, StringComparison.Ordinal)}");
        Assert.Contains("candidate." + field, ExceedsBody(), StringComparison.Ordinal);
        Assert.DoesNotContain(field, OrphanedUntilThePricingAdr.Select(o => o.Member));
    }

    [Fact]
    public void Exceeds_drives_the_upgrade_ladder_and_the_tier_repair()
    {
        var tier = Code("Services", "Incubator", "StudioTier.cs");
        var nextAbove = tier[tier.IndexOf("public static TierLimits? NextAbove(", StringComparison.Ordinal)..];
        Assert.Equal(3, LadderAndRepairInputsUntilThePricingAdr.Length);
        Assert.Contains("Exceeds(t, current)", nextAbove, StringComparison.Ordinal);
        Assert.Contains("TierCatalog.Exceeds(", Code("Services", "Incubator", "StudioTierRepair.cs"), StringComparison.Ordinal);
    }

    // ─── ونُصوصُ الأَسبابِ المُتَقاعِدَةِ في القامُوس — يَتيمَةٌ مُثَبَّتَةٌ بِشَرطِ الرَفعِ نَفسِه (‏2026-11-07، مُراجَعَةُ C1 · W13-1·F1) ──────────────────────
    //
    // نافِذَةُ التَرقِيَةِ لا تَرسُمُ analyze/refine/export (`StudioUpgradeReason.Drawn`)، وفُروعُها في `TitleFor`/`MessageFor` باقِيَةٌ مُعجَماً مُغلَقاً لا تَبلُغُه
    // صَفحَة — فَنُصوصُها يَتيمَة، ومَعَها جُملَةُ التَجَدُّدِ الَّتي لا تَتبَعُ إلّا حِصَّتَيِ الشَهر (وما زالَت الصَفحَتانِ تَحسِبانِ `RenewsOn` لِلافِتَةٍ لا تَقرَؤُه).
    // **وشَرطُ رَفعِها ADR التَسعير (W13-2)** — تُحذَفُ مَعَ حُقولِها مَرَّةً واحِدَة؛ والقائِمَةُ تَتَقَلَّصُ ولا تَنمو.

    internal static readonly string[] RetiredReasonKeysUntilThePricingAdr =
    [
        "upgrade.title.analyze", "upgrade.msg.analyze", "upgrade.title.refine", "upgrade.msg.refine",
        "upgrade.msg.export", "upgrade.msg.export_not_included", "upgrade.next.renews_on",
    ];

    [Fact]
    public async Task The_retired_reason_texts_stay_in_the_dictionary_and_no_banner_draws_them()
    {
        Assert.True(RetiredReasonKeysUntilThePricingAdr.Length <= 7, "القائِمَةُ نَمَت — نَصٌّ يَتيمٌ جَديدٌ يُقَرَّرُ في مُراجَعَة.");
        var reasons = StudioUpgradeReason.All.Append(UpgradePromptLiveReasonsCharacterizationTests.UnknownReason).ToList();
        var banners = new List<string>();
        foreach (var reason in reasons)
            foreach (var tier in TierCatalog.All.Keys)
                banners.Add(System.Net.WebUtility.HtmlDecode(await UpgradePromptNextStepTests.Render(reason, tier, "2026-11-18")));
        var drawn = new List<string>();
        foreach (var key in RetiredReasonKeysUntilThePricingAdr)
        {
            var text = ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, key);
            Assert.False(string.IsNullOrWhiteSpace(text), $"«{key}» غائِبٌ مِن القامُوس — يُرفَعُ مِن القائِمَةِ بِسَبَبِه.");
            var probe = text!.Split("{0}")[0].Trim();
            if (banners.Any(b => b.Contains(probe, StringComparison.Ordinal))) drawn.Add(key);
        }
        output.WriteLine($"نُصوصُ الأَسبابِ المُتَقاعِدَة: {RetiredReasonKeysUntilThePricingAdr.Length} · لافِتاتٌ مَرسومَة {banners.Count} ({reasons.Count} أَسبابٍ × {TierCatalog.All.Count} دَرَجات) · مَرسومٌ مِنها {drawn.Count}");
        Assert.True(banners.Count >= 20 && banners.Count(b => b.Trim().Length > 0) >= 8, $"أَداةٌ عَمياء: {banners.Count} لافِتَة.");
        Assert.True(drawn.Count == 0, "نَصٌّ يَتيمٌ تَرسُمُه لافِتَة — فَلَيسَ يَتيماً: " + string.Join("، ", drawn));
    }

    /// <summary>صَفحَةُ الباقَةِ وسَطرُ الشَريطِ يَعرِضانِ المَتاجِرَ وَحدَها — بِمِفتاحِ «‏{0}/{1} مَتجَر» ذي العَنصَرَين.</summary>
    [Fact]
    public void The_billing_page_and_the_sidebar_show_the_store_limit_only()
    {
        var shell = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, Template, "Components", "StudioShell.razor")));
        Assert.Contains("L.Format(\"studio.usage.stores\", user.StoresBuilt, Cap(l.StoresMax))", shell, StringComparison.Ordinal);
        var stores = ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "studio.usage.stores");
        Assert.Equal("{0}/{1} مَتجَر", stores);
        var billing = WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, Template, "Components", "Pages", "StudioBilling.razor")));
        Assert.Contains("@t.StoresMax @L.Markup(\"studio.billing.per_apps\")", billing, StringComparison.Ordinal);
        Assert.Contains("@user.StoresBuilt / @l.StoresMax", billing, StringComparison.Ordinal);
    }

    /// <summary>ومَزايا نافِذَةِ التَرقِيَةِ المَتاجِرُ وَحدَها.</summary>
    [Fact]
    public void The_upgrade_benefits_name_the_store_limit_only()
    {
        var prompt = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, Template, "Components", "UpgradePrompt.razor")));
        var benefits = prompt[prompt.IndexOf("IEnumerable<string> BenefitsFor(TierLimits t)", StringComparison.Ordinal)..];
        benefits = benefits[..(benefits.IndexOf('}') + 1)];
        Assert.Single(Regex.Matches(benefits, "yield return"));
        Assert.Contains("upgrade.benefit.stores", benefits, StringComparison.Ordinal);
    }

    /// <summary>وتَصديرُ الدِراسَةِ القَديمَةِ قِراءَةٌ لِصاحِبِها بِلا باقَة — لا يَسأَلُ الحُدودَ ولا يُحَوِّلُ إلى نافِذَةِ تَرقِيَة.</summary>
    [Fact]
    public void The_legacy_study_export_is_the_owners_data_and_asks_no_tier()
    {
        var body = WriteEndpointGuardTests.AllMinimalApiEndpoints().Single(e => e.Route == "/studio/s/{id:guid}/export.xlsx").Body;
        Assert.DoesNotContain("ReadWithLimitsAsync", body, StringComparison.Ordinal);
        Assert.DoesNotContain("?upgrade=", body, StringComparison.Ordinal);
        Assert.Contains("session.OwnerUserId != auth.UserId!.Value", body, StringComparison.Ordinal);
    }
}
