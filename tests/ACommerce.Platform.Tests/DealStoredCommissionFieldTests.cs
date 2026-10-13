using System.Text;
using System.Text.RegularExpressions;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Marten;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ وَثائِقُ `Deal` المَخزونَةُ بِحَقلِ `CommissionSar` — أَتُقرَأُ أَم تَحتاجُ تَرحيلاً؟ (‏P3-1، ‏2026-10-12) ═══
//
// ‏ADR-032 §٢‑ب أَسقَطَ الخاصِّيَّةَ مِن `Deal` (‏2026-10-11)، وكُلُّ صَفقَةٍ بَلَغَت `Paid` قَبلَه كُتِبَت بِها. فَالسُؤالُ
// قَبلَ أَيِّ نَشرٍ يَمَسُّ القاعِدَة: هَل يَرمي فَكُّ JSON عَلى خاصِّيَّةٍ لا يَعرِفُها الصِنف؟ و`Deal.cs` يُجيبُ «تُهمَل» في
// تَعليقٍ **بِلا اختِبار** — والقاعِدَة ٢: الدَعوى الَّتي لا يُفشِلُها شَيءٌ لا تَحرُس. فَتُقاسُ هُنا بِطَرَفَين:
//
//   • **المُسَلسِلُ نَفسُه بِلا قاعِدَة** — `StoreOptions.Serializer()` هُوَ ما يَفُكُّ بِه Marten كُلَّ وَثيقَة، ويُشَغَّلُ
//     في CI. ومَعَه فاحِصٌ نَصِّيٌّ يَحمَرُّ إن ضُبِطَ المُسَلسِلُ في مَوضِعِ تَهيئَةٍ فَصارَ القِياسُ قَديماً.
//   • **بُرهانٌ حَيٌّ** عَلى Postgres بِعَقدِ `Live*ProofTests` (‏WASAYEL_LIVE_PROOF=1 وConnectionStrings__Postgres):
//     صَفقَةٌ تُخزَنُ بِالمَخزَنِ الَّذي يُرَكِّبُه المُضيف، ثُمَّ يُحقَنُ الحَقلُ في عَمودِ `data` بِـSQL خامٍ كَما بَقِيَ
//     في الإنتاج، ثُمَّ تُحمَّلُ بِالمُعَرِّفِ وبِـLINQ، ثُمَّ تُكتَبُ — فَيُقاسُ مَتى يَزولُ الحَقلُ لا أَنَّه يُقرَأُ وَحدَه.
//
// **والحُكمُ المَكتوبُ في `docs/DEPLOY.md` §٤‑د مِن هذا المِلَفِّ لا مِن التَعليق**: لا تَرحيلَ ولا فِعلَ رُقعَة.

public class DealStoredCommissionFieldTests
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    /// <summary>وَثيقَةُ صَفقَةٍ كَما كَتَبَها الكودُ قَبلَ ‏ADR-032 §٢‑ب — بِأَسماءِ الخَصائِصِ كَما يُسَلسِلُها Marten (بِلا
    /// سِياسَةِ تَسمِيَة) والحَقلُ الساقِطُ بَينَها.</summary>
    private const string StoredBeforeTheDrop =
        """
        {"Id":"7d1c2a40-5b7e-4c1d-9d0a-2f4e6b8a1c3e","TenantSlug":"zz-commission","Pattern":"marketplace",
         "ListingTitle":"سِلعَة","InitiatorId":"1b2c3d4e-5f60-4718-8293-a4b5c6d7e8f9","InitiatorName":"مُشتَرٍ",
         "AmountSar":150.0,"CommissionSar":3.75,"Stage":3,"Status":0,
         "Refs":{"listing_owner":"0f1e2d3c-4b5a-4968-8776-655443322110"},"Attributes":{},"Timeline":[],
         "CreatedAt":"2026-10-01T10:00:00Z","UpdatedAt":"2026-10-01T10:05:00Z"}
        """;

    /// <summary><b>المُسَلسِلُ الَّذي يَفُكُّ بِه Marten يُهمِلُ الحَقلَ الساقِط</b> ويَقرَأُ ما سِواه حَرفاً — مِن غَيرِ قاعِدَة،
    /// فَيُقاسُ في كُلِّ تَشغيل. ولَو رَمى لَكانَت كُلُّ صَفقَةٍ مَدفوعَةٍ قَبلَ الإسقاطِ صَفحَةَ ‏500.</summary>
    [Fact]
    public void MartensSerializer_ReadsADealStoredWithTheDroppedCommissionField()
    {
        var serializer = new StoreOptions().Serializer();
        using var json = new MemoryStream(Encoding.UTF8.GetBytes(StoredBeforeTheDrop));

        var deal = serializer.FromJson<Deal>(json);

        Assert.NotNull(deal);
        Assert.Equal(Guid.Parse("7d1c2a40-5b7e-4c1d-9d0a-2f4e6b8a1c3e"), deal.Id);
        Assert.Equal(150m, deal.AmountSar);
        Assert.Equal(DealStage.Paid, deal.Stage);
        Assert.Equal("0f1e2d3c-4b5a-4968-8776-655443322110", deal.Refs["listing_owner"]);
        Assert.Null(typeof(Deal).GetProperty("CommissionSar"));
    }

    /// <summary><b>وكِتابَتُها مِن جَديدٍ لا تُعيدُه</b>: الصِنفُ لا يَحمِلُه فَلا يُسَلسَل — أَي أَنّ الحَقلَ يَزولُ مِن كُلِّ
    /// وَثيقَةٍ عِندَ أَوَّلِ كِتابَةٍ تَلي النَشر، بِلا رُقعَة.</summary>
    [Fact]
    public void MartensSerializer_DoesNotWriteTheDroppedFieldBack()
    {
        var serializer = new StoreOptions().Serializer();
        using var json = new MemoryStream(Encoding.UTF8.GetBytes(StoredBeforeTheDrop));
        var deal = serializer.FromJson<Deal>(json);

        var rewritten = serializer.ToJson(deal);

        Assert.Contains("\"AmountSar\"", rewritten, StringComparison.Ordinal);
        Assert.DoesNotContain("CommissionSar", rewritten, StringComparison.Ordinal);
    }

    /// <summary><b>والقِياسُ صالِحٌ ما دامَ المُسَلسِلُ هُوَ الافتِراضِيّ</b>: لا مَوضِعَ في <c>libs/</c> و<c>apps/</c> يَضبِطُ
    /// مُسَلسِلَ Marten، ولا يَحمِلُ مِلَفُّ الصَفقَةِ سِمَةَ رَفضِ الخَصائِصِ المَجهولَة. فَإن ضُبِطَ أَحَدُهُما يَوماً حَمِرَ هذا
    /// الفاحِصُ وأُعيدَ القِياسُ قَبلَ النَشر — بَدَلَ أَن يَبقى «تُهمَل» دَعوى عَن مُسَلسِلٍ آخَر. (و<c>UnmappedMemberHandling
    /// .Disallow</c> القائِمُ في مُحَمِّلاتِ التَعريفاتِ يَخُصُّ خِياراتِها هِيَ لا Marten — مَقيس، فَلا يُعَدّ.) والعَدّادُ
    /// يُطبَعُ في الرِسالَةِ ويَفشَلُ عَلى الصِفر.</summary>
    [Fact]
    public void NoConfigurationSite_ChangesMartensSerializer_AndTheDealRefusesNoUnknownMember()
    {
        var sources = new[] { "libs", "apps" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(RepoRoot, d), "*.cs", SearchOption.AllDirectories))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();
        Assert.True(sources.Count > 100, $"أَداةٌ عَمياء: {sources.Count} مِلَفّاً مَقروءاً.");

        var martenSerializer = new Regex(
            @"UseSystemTextJsonForSerialization|UseNewtonsoftForSerialization|UseJsonNetForSerialization|"
          + @"\b(opts|o|options|storeOptions)\.Serializer\s*[(<]");
        // ‏الأَداةُ تُقاسُ قَبلَ أَن يُوثَقَ بِها (القاعِدَة ١٠): شَكلا الضَبطِ يُمسَكان، ومُحَمِّلُ التَعريفاتِ لا.
        Assert.Matches(martenSerializer, "opts.UseSystemTextJsonForSerialization(EnumStorage.AsString);");
        Assert.Matches(martenSerializer, "o.Serializer(new CustomSerializer());");
        Assert.DoesNotMatch(martenSerializer, "UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,");
        var hits = sources
            .Where(p => martenSerializer.IsMatch(File.ReadAllText(p)))
            .Select(p => Path.GetRelativePath(RepoRoot, p))
            .ToList();

        Assert.True(hits.Count == 0,
            $"مَوضِعٌ يَضبِطُ مُسَلسِلَ Marten ({sources.Count} مِلَفّاً مَفحوصاً): "
          + string.Join(", ", hits) + " — أَعِد قِياسَ وَثائِقِ Deal الحامِلَةِ لِـCommissionSar قَبلَ النَشر.");

        var deal = sources.Single(p => p.EndsWith(
            Path.Combine("Services", "Deals", "Deal.cs"), StringComparison.Ordinal));
        Assert.DoesNotContain("UnmappedMemberHandling", File.ReadAllText(deal), StringComparison.Ordinal);
    }
}

[Collection(LiveProofCollection.Name)]
public class LiveDealStoredCommissionFieldProofTests(ITestOutputHelper output)
{
    /// <summary><b>صَفقَةٌ مَخزونَةٌ بِالحَقلِ الساقِطِ تُحَمَّلُ وتُستَعلَمُ وتُكتَبُ بِلا خَطَأ، ويَزولُ الحَقلُ عِندَ أَوَّلِ
    /// كِتابَة</b> — عَلى Postgres بِالمَخزَنِ الَّذي يُرَكِّبُه المُضيفُ لِلصَفقَة (<see cref="DealsService.RegisterDocument"/>
    /// بِتَزامُنِها التَفاؤُلِيّ)، والحَقلُ مَحقونٌ بِـSQL خامٍ في عَمودِ <c>data</c> كَما بَقِيَ في الإنتاج.</summary>
    [Fact]
    public async Task A_deal_stored_with_CommissionSar_loads_queries_and_saves_and_the_field_goes_on_the_next_write()
    {
        if (!LiveDealConcurrencyProofTests.Enabled || string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString))
            return;

        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "zz-cms-" + Guid.NewGuid().ToString("N")[..8];
        var id = Guid.NewGuid();

        try
        {
            await using (var s = store.LightweightSession(slug))
            {
                s.Store(new Deal
                {
                    Id = id, TenantSlug = slug, ListingTitle = "سِلعَة", InitiatorId = Guid.NewGuid(),
                    InitiatorName = "مُشتَرٍ", AmountSar = 150m, Stage = DealStage.Paid,
                    Refs = { [DealsService.ListingOwnerRef] = Guid.NewGuid().ToString() }
                });
                await s.SaveChangesAsync();
            }

            await using var conn = new NpgsqlConnection(LiveDealConcurrencyProofTests.ConnectionString);
            await conn.OpenAsync();

            // ‏اسمُ الحَقلِ بِحالَةِ أَحرُفِ جارِه المَخزون — يُقرَأُ مِن الصَفِّ لا يُفتَرَض.
            Assert.True(await RawHasKeyAsync(conn, slug, id, "AmountSar"),
                "الوَثيقَةُ المَخزونَةُ لا تَحمِلُ AmountSar بِهذا الاسم — سِياسَةُ التَسمِيَةِ تَغَيَّرَت فَالحَقنُ لا يُشبِهُ الإنتاج.");

            await using (var inject = new NpgsqlCommand(
                "update platform.mt_doc_deal set data = jsonb_set(data, '{CommissionSar}', '3.75'::jsonb) "
              + "where tenant_id = @t and id = @i", conn))
            {
                inject.Parameters.AddWithValue("t", slug);
                inject.Parameters.AddWithValue("i", id);
                Assert.Equal(1, await inject.ExecuteNonQueryAsync());
            }
            Assert.True(await RawHasKeyAsync(conn, slug, id, "CommissionSar"), "الحَقنُ لَم يَقَع.");

            await using (var q = store.QuerySession(slug))
            {
                var loaded = await q.LoadAsync<Deal>(id);
                Assert.NotNull(loaded);
                Assert.Equal(150m, loaded!.AmountSar);
                Assert.Equal(DealStage.Paid, loaded.Stage);

                var queried = await q.Query<Deal>().Where(d => d.Id == id).ToListAsync();
                Assert.Single(queried);
                Assert.Equal(150m, queried[0].AmountSar);
            }

            // ‏القِراءَةُ لا تَكتُب: الحَقلُ باقٍ في الصَفِّ ما لَم تُكتَب الصَفقَة.
            Assert.True(await RawHasKeyAsync(conn, slug, id, "CommissionSar"), "التَحميلُ وَحدَه أَزالَ الحَقل؟");

            await using (var w = store.LightweightSession(slug))
            {
                var deal = await w.LoadAsync<Deal>(id);
                deal!.UpdatedAt = DateTime.UtcNow;
                w.Store(deal);
                await w.SaveChangesAsync();
            }

            Assert.False(await RawHasKeyAsync(conn, slug, id, "CommissionSar"),
                "الكِتابَةُ أَبقَت الحَقلَ الساقِط — فَهُوَ لا يَزولُ بِلا رُقعَة.");
            output.WriteLine("وَثيقَةٌ بِـCommissionSar: حُمِّلَت بِالمُعَرِّفِ وبِـLINQ، وبَقِيَ الحَقلُ بَعدَ القِراءَة، وزالَ بِأَوَّلِ كِتابَة.");
        }
        finally
        {
            await using var clean = store.LightweightSession(slug);
            clean.Delete<Deal>(id);
            await clean.SaveChangesAsync();
        }
    }

    private static async Task<bool> RawHasKeyAsync(NpgsqlConnection conn, string slug, Guid id, string key)
    {
        await using var cmd = new NpgsqlCommand(
            "select data ? @k from platform.mt_doc_deal where tenant_id = @t and id = @i", conn);
        cmd.Parameters.AddWithValue("k", key);
        cmd.Parameters.AddWithValue("t", slug);
        cmd.Parameters.AddWithValue("i", id);
        return await cmd.ExecuteScalarAsync() is true;
    }
}
