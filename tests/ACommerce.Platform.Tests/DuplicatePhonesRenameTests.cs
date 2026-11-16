using ACommerce.Kit.Auth;
using ACommerce.Kit.Listings;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Api;
using ACommerce.Templates.Customer.Marketplace.Services.Identity;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using JasperFx.Events.Projections;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ إعادَةُ كَتبِ الجَوّالِ وكاتِبٌ مُوازٍ بَينَ القِراءَةِ والحِفظ — تَوصيفٌ قَبلَ التَبديل (‏2026-11-05، الموجَة ١٢ G2 · ٣؛ A-SESS-W12-1) ══════════════════════
//
// `DuplicatePhones.RenameAsync` يَقرَأُ كُلَّ حِسابٍ مُفرَدٍ ويَكتُبُ جَوّالَه مُوَحَّداً ويَحفَظُ في جَلسَةٍ واحِدَةٍ لِلمُستَأجِر — **بِلا إعادَة**. ومَخزَنُ سكربتِ الدَمجِ
// (`scripts/merge-duplicate-phones.cs`) يُسَجِّلُ `User` بِالتَزامُنِ التَفاؤُلِيِّ كَالمُضيف، ولا يُسَجِّلُ `StudioUser` (والمُضيفُ يُسَجِّلُه — `MarketplaceTemplateExtensions`).
// فَكاتِبٌ مُوازٍ بَينَ القِراءَةِ والحِفظ: عَلى `User` يَرُدُّه Marten بِـ`ConcurrencyException` فَيَتَوَقَّفُ السكربتُ في مُنتَصَفِه، وعَلى `StudioUser` يَكتُبُ الحِفظُ فَوقَه
// فَيَضيعُ ما كَتَبَه (`SessionVersion`، `IsPlatformAdmin`، `AnalysesUsed`). وADR-041 يَقولُ «`RenameAsync` داخِلَ مُعامَلَةِ الدَمجِ الَّتي يُعيدُها `ApplyAsync`» — ولَيسَ.
// (ودَعوى التَدقيقِ أَنّ لِـMarten 9 دالَّةَ `mt_upsert` لِكُلِّ نَوعٍ تَفشَلُ بِلا التَسجيلِ مَنقوضَة: لا دالَّةَ كَهذِه، فَالحِفظُ بِلا تَسجيلٍ يَمُرُّ ويَكتُبُ فَوق.)
//
// **كَيفَ يُقاسُ هُنا**: مَخزَنُ السكربتِ بِسُطورِه (`DuplicatePhonesTests.StoreLines`) — **وسَطرُ `StudioUser` كَما يَحمِلُه السكربتُ نَفسُه** (يُقرَأُ مِن نَصِّه) — عَلى
// Postgres؛ والكاتِبُ المُوازي يَكتُبُ في جَلسَةٍ أُخرى داخِلَ نِداءِ `setPhone` (بَعدَ قِراءَةِ `RenameAsync` وقَبلَ حِفظِه) مَرَّةً واحِدَة: يَرفَعُ `SessionVersion`.
//
// **ما يُثَبَّتُ هُنا لِيَبقى** (القاعِدَة ٣): بِلا كاتِبٍ مُوازٍ يُعادُ كَتبُ الجَوّالِ في النَوعَينِ ويُعادُ العَدُّ ‏1. **وما يُطبَعُ ولا يُثَبَّت**: بِكاتِبٍ مُوازٍ — اليَومَ
// `User` يَرمي ولا يُكتَبُ الجَوّال، و`StudioUser` يُحفَظُ ويَضيعُ ما كَتَبَه الآخَر. ويَتَغَيَّرُ في كوميتِ التَبديل، والمِلَفُّ يَبقى بِبَصمَتِه.

[Collection(LiveProofCollection.Name)]
public class LiveDuplicatePhonesRenameProofTests(ITestOutputHelper output)
{
    internal static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    internal const string StudioConcurrencyLine = "o.Schema.For<StudioUser>().Identity(x => x.Id).UseOptimisticConcurrency(true);";

    /// <summary>هَل يُسَجِّلُ سكربتُ الدَمجِ <c>StudioUser</c> بِالتَزامُنِ التَفاؤُلِيّ؟ — مِن نَصِّه.</summary>
    internal static bool ScriptMapsStudioUser()
        => File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "scripts", "merge-duplicate-phones.cs")).Contains(StudioConcurrencyLine, StringComparison.Ordinal);

    /// <summary>مَخزَنُ السكربتِ كَما هُوَ: سُطورُ <c>DuplicatePhonesTests.StoreLines</c> الثابِتَة، وسَطرُ <c>StudioUser</c> إن حَمَلَه السكربت.</summary>
    internal static DocumentStore ScriptStore() => DocumentStore.For(o =>
    {
        o.Connection(LiveDealConcurrencyProofTests.ConnectionString!);
        o.DatabaseSchemaName = "platform";
        o.Policies.AllDocumentsAreMultiTenanted();
        o.Events.TenancyStyle = JasperFx.MultiTenancy.TenancyStyle.Conjoined;
        o.Schema.For<Tenant>().SingleTenanted().Identity(x => x.Id);
        o.Schema.For<ApiKeyDocument>().SingleTenanted().Identity(x => x.Id);
        o.Schema.For<User>().Identity(x => x.Id).UseOptimisticConcurrency(true);
        if (ScriptMapsStudioUser()) o.Schema.For<StudioUser>().Identity(x => x.Id).UseOptimisticConcurrency(true);
        o.Projections.Snapshot<Listing>(SnapshotLifecycle.Inline);
        o.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
    });

    /// <summary>ما بَعدَ إعادَةِ الكَتب: هَل رَمى (ونَوعُه)، وما أَعادَه، والجَوّالُ المُخَزَّن، وإصدارُ الجَلسَةِ المُخَزَّن (كَتَبَه المُوازي 1)، وكَم مَرَّةً نودِيَ <c>setPhone</c>.</summary>
    internal sealed record RenameOutcome(string Thrown, int Done, string Phone, int SessionVersion, int Calls)
    {
        public override string ToString()
            => $"{(Thrown == "" ? "لا رَمي" : "يَرمي " + Thrown)} · أُعيدَ {Done} · الجَوّال {(Phone.StartsWith("+966", StringComparison.Ordinal) ? "مُوَحَّد" : "كَما كان")} · "
               + $"إصدارُ الجَلسَة {SessionVersion} · نِداءاتُ setPhone {Calls}";
    }

    /// <summary>
    /// <para>حِسابٌ بِجَوّالٍ غَيرِ مُوَحَّد (<c>05…</c>) — <see cref="User"/> في مَتجَرٍ مُؤَقَّتٍ أَو <see cref="StudioUser"/> في <c>_studio</c> — يُعادُ كَتبُ جَوّالِه بِـ
    /// <c>RenameAsync</c>. و<paramref name="parallelWrites"/>: كَم مَرَّةً يَكتُبُ كاتِبٌ مُوازٍ (يَرفَعُ <c>SessionVersion</c>) داخِلَ <c>setPhone</c> — 0 أَو 1 أَو 2 (في كُلِّ
    /// مُحاوَلَة). والحِسابُ والمَتجَرُ يُمسَحانِ بَعدَه.</para>
    /// </summary>
    internal static async Task<RenameOutcome> RunAsync(bool studio, int parallelWrites)
    {
        await using var store = ScriptStore();
        var tenant = studio ? StudioAuth.Tenant : "zz-g2rn-" + Guid.NewGuid().ToString("N")[..8];
        var id = Guid.NewGuid();
        var typed = "05000007" + Random.Shared.Next(10, 99).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await using (var s = store.LightweightSession(tenant))
        {
            if (studio) s.Store(new StudioUser { Id = id, Phone = typed });
            else s.Store(new User { Id = id, TenantSlug = tenant, Phone = typed });
            await s.SaveChangesAsync();
        }
        var calls = 0;
        async Task ParallelAsync()
        {
            await using var p = store.LightweightSession(tenant);
            if (studio) { var u = (await p.LoadAsync<StudioUser>(id))!; u.SessionVersion++; p.Store(u); }
            else { var u = (await p.LoadAsync<User>(id))!; u.SessionVersion++; p.Store(u); }
            await p.SaveChangesAsync();
        }
        var renames = new[] { new DuplicatePhones.Rename(id, ACommerce.Kit.Auth.PhoneNumber.Canonical(typed)) };
        try
        {
            string thrown = "";
            var done = 0;
            try
            {
                done = studio
                    ? await DuplicatePhones.RenameAsync<StudioUser>(store, tenant, renames, (u, p) => { if (++calls <= parallelWrites) ParallelAsync().GetAwaiter().GetResult(); u.Phone = p; })
                    : await DuplicatePhones.RenameAsync<User>(store, tenant, renames, (u, p) => { if (++calls <= parallelWrites) ParallelAsync().GetAwaiter().GetResult(); u.Phone = p; });
            }
            catch (Exception e) { thrown = e.GetType().Name; }
            await using var q = store.QuerySession(tenant);
            var (phone, version) = studio
                ? (await q.LoadAsync<StudioUser>(id) is { } su ? (su.Phone, su.SessionVersion) : ("", -1))
                : (await q.LoadAsync<User>(id) is { } mu ? (mu.Phone, mu.SessionVersion) : ("", -1));
            return new RenameOutcome(thrown, done, phone, version, calls);
        }
        finally
        {
            await using var s = store.LightweightSession(tenant);
            if (studio) s.Delete<StudioUser>(id); else s.Delete<User>(id);
            await s.SaveChangesAsync();
        }
    }

    /// <summary><b>ما يَبقى</b>: بِلا كاتِبٍ مُوازٍ يُعادُ كَتبُ الجَوّالِ في النَوعَينِ ويُعادُ العَدُّ ‏1 بِلا رَمي.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_rename_with_no_parallel_writer_writes_the_canonical_phone_on_Postgres(bool studio)
    {
        if (!Enabled) return;
        var outcome = await RunAsync(studio, parallelWrites: 0);
        output.WriteLine($"{(studio ? "StudioUser" : "User")} · بِلا كاتِبٍ مُوازٍ ⇒ {outcome}");
        Assert.Equal("", outcome.Thrown);
        Assert.Equal(1, outcome.Done);
        Assert.StartsWith("+966", outcome.Phone, StringComparison.Ordinal);
    }

    /// <summary><b>ما يُطبَعُ ولا يُثَبَّت</b>: كاتِبٌ مُوازٍ مَرَّةً بَينَ القِراءَةِ والحِفظ — اليَومَ <c>User</c> يَرمي ولا يُكتَبُ الجَوّال، و<c>StudioUser</c> يُحفَظُ
    /// ويَضيعُ إصدارُ الجَلسَةِ الَّذي رَفَعَه المُوازي. ويُثَبَّتُ ما لا يَتَغَيَّر: نودِيَ <c>setPhone</c>.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_rename_with_a_parallel_writer_between_its_read_and_its_save_is_measured_on_Postgres(bool studio)
    {
        if (!Enabled) return;
        var outcome = await RunAsync(studio, parallelWrites: 1);
        output.WriteLine($"{(studio ? "StudioUser" : "User")} · كاتِبٌ مُوازٍ مَرَّة · السكربتُ يُسَجِّلُ StudioUser {ScriptMapsStudioUser()} ⇒ {outcome}");
        Assert.True(outcome.Calls >= 1);
    }
}
