using System.Reflection;
using ACommerce.Kit.Auth;
using ACommerce.Platform.Shared;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ الصُفوفُ الخامُّ المَنقولَةُ مِن قاعِدَةِ المَتجَرِ السابِقَةِ بَعدَ حَذفِ صاحِبِها — تَوصيفٌ قَبلَ التَبديل (‏2026-11-04، الموجَة ١٢ G1 · ٨؛ B-6) ═══════════
//
// **ما يُثَبَّتُ هُنا لِيَبقى** (القاعِدَة ٣): `ImportedRecord.Data` حَقيبَةٌ مَجهولَةُ الأَعمِدَة (`Dictionary<string, object?>`، ‏`SELECT *`) عَلى نَوعٍ مُخَزَّن؛ وبابُ الحَذفِ بِجِسمِه
// لا يَمَسُّ صَفَّ عُضوٍ آخَرَ ولا صَفَّ جَدوَلِ إعدادٍ (مَناطِق، خَصائِص).
// **وما يُطبَعُ ولا يُثَبَّت**: اليَومَ تَبقى صُفوفُه هُو (عَناوينُه، وَسائِلُ اتِّصالِه) بَعدَ الحَذفِ وتَخرُجُ في حَقيبَةِ التَخارُج، والماسِحُ (`StoredNameFields`) يَعودُ مِن
// الأَنواعِ العامَّةِ ويَقرَأُ خَصائِصَ النَصِّ المُنتَهِيَةَ بِـName وَحدَها فَلا يَرى الحَقيبَة (القاعِدَة ١٠). وهذا يَتَغَيَّرُ في كوميتِ التَبديل؛ والمِلَفُّ يَبقى بِبَصمَتِه.

public class AccountErasureImportedRowsTests(ITestOutputHelper output)
{
    /// <summary>كُلُّ خاصِّيَّةٍ مِن نَوعِ <c>Dictionary&lt;string, object?&gt;</c> عَلى نَوعٍ يَبلُغُه الماسِحُ مِن المُخَزَّن — بِاسمِ «نَوع.خاصِّيَّة».</summary>
    internal static IReadOnlyList<string> OpaqueBags(IEnumerable<Type> types)
        => types.SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => typeof(IDictionary<string, object?>).IsAssignableFrom(p.PropertyType) || typeof(IDictionary<string, object>).IsAssignableFrom(p.PropertyType))
                .Select(p => $"{t.Name}.{p.Name}"))
            .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

    [Fact]
    public void The_stored_types_carry_one_opaque_bag_the_name_scanner_does_not_read()
    {
        var (roots, closure, names) = AccountDeletionRetentionTests.StoredNameFields();
        var bags = OpaqueBags(closure);
        output.WriteLine($"أَنواعٌ مَفحوصَة: {closure.Count} · حَقائِبُ مَجهولَةُ الأَعمِدَة: {bags.Count} [{string.Join(", ", bags)}] · يَقرَؤُها ماسِحُ الأَسماء: "
            + $"{names.Any(n => n.Key.StartsWith(nameof(ImportedRecord) + ".", StringComparison.Ordinal))}");
        Assert.True(closure.Count >= 30, $"أَداةٌ عَمياء: {closure.Count} نَوعاً.");
        Assert.Contains(typeof(ImportedRecord), closure);
        Assert.Contains($"{nameof(ImportedRecord)}.{nameof(ImportedRecord.Data)}", bags);
    }
}

[Collection(LiveProofCollection.Name)]
public class LiveAccountErasureImportedRowsProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    internal sealed record Seeded(string Slug, User Me, User Other, string[] MyRows, string OtherRow, string ConfigRow);

    internal static ImportedRecord Row(string table, string sourceId, Dictionary<string, object?> data)
        => new() { Id = $"{table}/{sourceId}", Table = table, SourceId = sourceId, ImportedAt = DateTime.UtcNow, Data = data };

    /// <summary>صُفوفٌ كَما يَكتُبُها <c>TargetWriter.DumpGenericAsync</c>: عُنوانُه ووَسيلَةُ اتِّصالِه (عَمودُ <c>UserId</c> مُعَرِّفُ حِسابِه)، وعُنوانُ عُضوٍ آخَر، وصَفُّ جَدوَلِ إعداد.</summary>
    internal static async Task<Seeded> SeedAsync(IDocumentStore store, string slug)
    {
        var me = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = "عُضوٌ مُستَورَد", Phone = "+966500000641", PhoneVerified = true };
        var other = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = "عُضوٌ مُستَورَدٌ آخَر", Phone = "+966500000642", PhoneVerified = true };
        var rows = new[]
        {
            Row("Addresses", "a1", new() { ["Id"] = "a1", ["UserId"] = me.Id.ToString(), ["Street"] = "شارِعُ المَلِك", ["City"] = "الرياض" }),
            Row("ContactPoint", "c1", new() { ["Id"] = "c1", ["UserId"] = me.Id.ToString().ToUpperInvariant(), ["Value"] = "+966500000641" }),
            Row("Addresses", "a2", new() { ["Id"] = "a2", ["UserId"] = other.Id.ToString(), ["Street"] = "شارِعُ العُليا", ["City"] = "الرياض" }),
            Row("DiscoveryRegions", "r1", new() { ["Id"] = "r1", ["Name"] = "الرياض", ["UserId"] = me.Id.ToString() }),
        };
        await using var s = store.LightweightSession(slug);
        s.Store(me, other);
        foreach (var r in rows) s.Store(r);
        await s.SaveChangesAsync();
        return new Seeded(slug, me, other, new[] { rows[0].Id, rows[1].Id }, rows[2].Id, rows[3].Id);
    }

    internal static async Task CleanAsync(IDocumentStore store, string slug)
    {
        await using (var s = store.LightweightSession(slug))
        {
            s.DeleteWhere<ImportedRecord>(x => x.Id != "");
            await s.SaveChangesAsync();
        }
        await LiveZeroRoleAdminProofTests.CleanAsync(store, slug);
    }

    /// <summary>بابُ الحَذفِ بِجِسمِه: 302 إلى <c>?deleted=1</c>، وصَفُّ العُضوِ الآخَرِ وصَفُّ جَدوَلِ الإعدادِ باقِيان. ويُطبَعُ ما بَقِيَ مِن صُفوفِه.</summary>
    [Fact]
    public async Task The_door_leaves_another_members_imported_row_and_a_config_row_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var seeded = await SeedAsync(host.Store, "zz-eraimp-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var token = ACommerce.Kit.Auth.Server.AuthHandlers.MakeToken(seeded.Me.Id, seeded.Slug, seeded.Me.SessionVersion);
            var confirm = ("confirm", ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "account.delete.confirm_word"));
            var (status, at) = await host.PostFormAsync(LiveAccountErasureFilesProofTests.DeleteDoor, new Dictionary<string, string> { ["slug"] = seeded.Slug }, seeded.Slug, token, confirm);
            await using var q = host.Store.QuerySession(seeded.Slug);
            var left = (await Marten.QueryableExtensions.ToListAsync(q.Query<ImportedRecord>())).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
            output.WriteLine($"البابُ: {status} → {at.Replace(seeded.Slug, "{slug}")} · صُفوفُه الباقِيَة {seeded.MyRows.Count(left.Contains)}/{seeded.MyRows.Length} · صَفُّ الآخَر {left.Contains(seeded.OtherRow)} · "
                + $"صَفُّ الإعداد {left.Contains(seeded.ConfigRow)}");
            Assert.Equal((302, $"/{seeded.Slug}?deleted=1"), (status, at));
            Assert.Contains(seeded.OtherRow, left);
            Assert.Contains(seeded.ConfigRow, left);
        }
        finally { await CleanAsync(host.Store, seeded.Slug); }
    }
}
