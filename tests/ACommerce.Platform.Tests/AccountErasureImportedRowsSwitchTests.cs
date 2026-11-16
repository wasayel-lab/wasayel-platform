using System.Text.RegularExpressions;
using ACommerce.Platform.Shared;
using ACommerce.Templates.Customer.Marketplace.Services.Audit;
using ACommerce.Templates.Customer.Marketplace.Services.Export;
using ACommerce.Templates.Customer.Marketplace.Services.Identity;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ صُفوفُه الخامُّ المُستَورَدَةُ تُحذَفُ مَعَه، والماسِحُ يَرى الحَقيبَة (‏2026-11-04، الموجَة ١٢ G1 · ٨ — B-6؛ تَوصيفُه 8762190) ═══════════════════════════
//
// قَبلَه («صُفوفُه الباقِيَة 2/2» و«يَقرَؤُها ماسِحُ الأَسماء: False» — `AccountErasureImportedRowsTests` يَطبَعُهُما ويَبقى بِبَصمَتِه). والآن `AccountErasure.ImportedPersonTables`
// مَعجَمٌ مُغلَقٌ مَقيسٌ مِن نَصِّ المُستَورِد، و`ErasesImported` نَقِيَّة، والماسِحُ يَفشَلُ عَلى حَقيبَةٍ مَجهولَةِ الأَعمِدَةِ لا تَصنيفَ لَها.

public class AccountErasureImportedRowsSwitchTests(ITestOutputHelper output)
{
    private static readonly Guid Me = Guid.NewGuid();

    private static ImportedRecord Row(string table, string column, object? value)
        => LiveAccountErasureImportedRowsProofTests.Row(table, "x", new() { ["Id"] = "x", [column] = value });

    [Fact]
    public void A_person_tables_row_whose_user_column_is_his_id_in_any_form_is_his()
    {
        Assert.True(AccountErasure.ErasesImported(Row("Addresses", "UserId", Me.ToString()), Me));
        Assert.True(AccountErasure.ErasesImported(Row("Invoices", "UserId", Me.ToString("N")), Me));
        Assert.True(AccountErasure.ErasesImported(Row("ContactPoint", "UserId", Me.ToString().ToUpperInvariant()), Me));
        Assert.True(AccountErasure.ErasesImported(Row("SupportMessages", "UserId", Me), Me));
    }

    [Fact]
    public void Another_members_row_a_config_row_a_row_without_the_column_or_a_non_id_value_is_not_his()
    {
        Assert.False(AccountErasure.ErasesImported(Row("Addresses", "UserId", Guid.NewGuid().ToString()), Me));
        Assert.False(AccountErasure.ErasesImported(Row("DiscoveryRegions", "UserId", Me.ToString()), Me));
        Assert.False(AccountErasure.ErasesImported(Row("Addresses", "OwnerId", Me.ToString()), Me));
        Assert.False(AccountErasure.ErasesImported(Row("Addresses", "UserId", "aspnet-user-7"), Me));
        Assert.False(AccountErasure.ErasesImported(Row("Addresses", "UserId", null), Me));
    }

    private static IReadOnlyList<string> DumpedTables(string importer)
    {
        var text = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "tools", "ACommerce.Importer", importer));
        var at = text.IndexOf("string[] genericTables", StringComparison.Ordinal);
        Assert.True(at > 0, $"{importer}: لا قائِمَةَ جَداوِل — الأَداةُ عَمياء.");
        return Regex.Matches(text[at..text.IndexOf("};", at, StringComparison.Ordinal)], "\"([A-Za-z]+)\"").Select(m => m.Groups[1].Value).ToList();
    }

    /// <summary>المَعجَمُ مَقيسٌ لا مَظنون (القاعِدَة ٢): كُلُّ جَدوَلٍ فيه يُفرِغُه أَحَدُ المُستَورِدَين بِـ<c>SELECT *</c>، ولا جَدوَلَ إعدادٍ فيه. يَطبَعُ ما فَحَصَه ويَفشَلُ عَلى صِفر.</summary>
    [Fact]
    public void Every_person_table_is_dumped_by_an_importer_and_no_config_table_is_one()
    {
        var ashare = DumpedTables("AshareImporter.cs");
        var ejar = DumpedTables("EjarImporter.cs");
        var dumped = ashare.Concat(ejar).ToHashSet(StringComparer.Ordinal);
        output.WriteLine($"جَداوِلُ مُفرَغَة: عَشير {ashare.Count} · إيجار {ejar.Count} · جَداوِلُ الشَخص {AccountErasure.ImportedPersonTables.Count}");
        Assert.Equal((60, 11), (ashare.Count, ejar.Count));
        Assert.All(AccountErasure.ImportedPersonTables.Keys, t => Assert.Contains(t, dumped));
        foreach (var config in new[] { "DiscoveryRegions", "DiscoveryAmenities", "AttributeDefinitions", "AttributeValues", "CategoryAttributeMappings", "Countries", "Regions",
                     "Cities", "Currencies", "ExchangeRates", "Language", "Translation", "Units", "Products", "ProductCategory", "AppVersions", "LegalPage", "TaxonomyNodes", "SubscriptionPlans" })
        {
            Assert.Contains(config, dumped);
            Assert.False(AccountErasure.ImportedPersonTables.ContainsKey(config), config);
        }
        Assert.Equal(20, AccountErasure.ImportedPersonTables.Count);
        Assert.All(AccountErasure.ImportedPersonTables.Values, cols => Assert.Equal(new[] { "UserId" }, cols));
    }

    /// <summary>الحَقائِبُ مَجهولَةُ الأَعمِدَةِ عَلى الأَنواعِ المُخَزَّنَة — مُصَنَّفَةٌ بِسَبَبِها، وما لا تَصنيفَ لَه يُحمِرُّ.</summary>
    internal static readonly IReadOnlyDictionary<string, string> ClassifiedBags = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ImportedRecord.Data"] = "صَفُّ SELECT * مِن قاعِدَةِ المَتجَرِ السابِقَة — صُفوفُ جَداوِلِ الشَخصِ تُحذَفُ بِحَذفِ الحِساب (`AccountErasure.ImportedPersonTables`).",
    };

    private sealed class InjectedBag
    {
        public Dictionary<string, object?> Values { get; set; } = new();
    }

    internal static IReadOnlyList<string> UnclassifiedBags(IEnumerable<Type> types)
        => AccountErasureImportedRowsTests.OpaqueBags(types).Where(b => !ClassifiedBags.ContainsKey(b)).ToList();

    /// <summary>قُلِبَ ما طَبَعَه التَوصيف («يَقرَؤُها ماسِحُ الأَسماء: False»): كُلُّ حَقيبَةٍ مَجهولَةِ الأَعمِدَةِ عَلى نَوعٍ مُخَزَّنٍ مُصَنَّفَة، والتَصنيفُ مَسنودٌ بِمَعجَمِ المَحوِ
    /// ونِدائِه في <c>EraseCopiesAsync</c>؛ ونَوعٌ مَحقونٌ بِحَقيبَةٍ بِلا تَصنيفٍ يُحمِرُّ — الأَداةُ مَقيسَة.</summary>
    [Fact]
    public void Every_opaque_bag_on_a_stored_type_is_classified_and_an_injected_one_goes_red()
    {
        var (_, closure, _) = AccountDeletionRetentionTests.StoredNameFields();
        var bags = AccountErasureImportedRowsTests.OpaqueBags(closure);
        var unclassified = UnclassifiedBags(closure);
        var injected = UnclassifiedBags(closure.Append(typeof(InjectedBag)));
        output.WriteLine($"أَنواعٌ مَفحوصَة: {closure.Count} · حَقائِب: {bags.Count} · بِلا تَصنيف: {unclassified.Count} · مَعَ المَحقون: {injected.Count} [{string.Join(", ", injected)}]");
        Assert.True(bags.Count >= 1, "أَداةٌ عَمياء: صِفرُ حَقيبَة.");
        Assert.Empty(unclassified);
        Assert.Equal(new[] { "InjectedBag.Values" }, injected);
        Assert.All(ClassifiedBags.Keys, k => Assert.Contains(k, bags));
        var src = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "Services", "Identity", "AccountErasure.cs")));
        var copies = src[src.IndexOf("public static async Task<Counts> EraseCopiesAsync(", StringComparison.Ordinal)..];
        Assert.Contains(".Where(r => ErasesImported(r, userId))", copies, StringComparison.Ordinal);
        Assert.Contains("ImportedRows = imported.Count,", copies, StringComparison.Ordinal);
    }
}

[Collection(LiveProofCollection.Name)]
public class LiveAccountErasureImportedRowsSwitchProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>قُلِبَ ما طَبَعَه التَوصيف («صُفوفُه الباقِيَة 2/2»). <b>المُوجَب</b>: بَعدَ البابِ لا صَفَّ لَه، والحَقيبَةُ لا تَحمِلُ صَفّاً بِمُعَرِّفِه. <b>والسالِب</b>: صَفُّ الآخَرِ
    /// وصَفُّ الإعداد — ولو حَمَلَ مُعَرِّفَه — في المَخزَنِ وفي الحَقيبَة.</summary>
    [Fact]
    public async Task The_door_deletes_his_imported_person_rows_and_the_export_keeps_the_others_and_the_config_row_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var seeded = await LiveAccountErasureImportedRowsProofTests.SeedAsync(host.Store, "zz-eraimpsw-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var token = ACommerce.Kit.Auth.Server.AuthHandlers.MakeToken(seeded.Me.Id, seeded.Slug, seeded.Me.SessionVersion);
            var confirm = ("confirm", ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "account.delete.confirm_word"));
            var (status, at) = await host.PostFormAsync(LiveAccountErasureFilesProofTests.DeleteDoor, new Dictionary<string, string> { ["slug"] = seeded.Slug }, seeded.Slug, token, confirm);
            await using var q = host.Store.QuerySession(seeded.Slug);
            var left = (await Marten.QueryableExtensions.ToListAsync(q.Query<ImportedRecord>())).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
            var tenant = new ACommerce.Kit.Tenants.Tenant { Id = seeded.Slug, Name = "مَتجَرٌ مُستَورَد", OwnerUserId = Guid.NewGuid() };
            var content = await new TenantExportService(host.Store, new ACommerce.Kit.Files.UnavailableFileStorage(), new AuditWriter(host.Store)).CollectAsync(tenant, tenant.OwnerUserId);
            var exported = Assert.Single(content.Tables, t => t.TypeName == nameof(ImportedRecord)).Rows.Select(r => r["id"]?.ToString()).ToHashSet(StringComparer.Ordinal);
            output.WriteLine($"البابُ: {status} → {at.Replace(seeded.Slug, "{slug}")} · صُفوفُه الباقِيَة {seeded.MyRows.Count(left.Contains)}/{seeded.MyRows.Length} · صَفُّ الآخَر {left.Contains(seeded.OtherRow)} · "
                + $"صَفُّ الإعداد {left.Contains(seeded.ConfigRow)} · الحَقيبَة: صُفوفُه {seeded.MyRows.Count(exported.Contains)} · الآخَر {exported.Contains(seeded.OtherRow)} · الإعداد {exported.Contains(seeded.ConfigRow)}");
            Assert.Equal((302, $"/{seeded.Slug}?deleted=1"), (status, at));
            Assert.Equal(0, seeded.MyRows.Count(left.Contains));
            Assert.Equal(0, seeded.MyRows.Count(exported.Contains));
            Assert.Contains(seeded.OtherRow, left);
            Assert.Contains(seeded.ConfigRow, left);
            Assert.Contains(seeded.OtherRow, exported);
            Assert.Contains(seeded.ConfigRow, exported);
        }
        finally { await LiveAccountErasureImportedRowsProofTests.CleanAsync(host.Store, seeded.Slug); }
    }
}
