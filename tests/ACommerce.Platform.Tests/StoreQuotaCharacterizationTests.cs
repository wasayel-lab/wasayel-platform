using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.LiveStoreBirthOnceCharacterizationTests;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ حَدِّ المَتاجِرِ المُتَتابِعِ قَبلَ أَن تَصيرَ الحِصَّةُ والعَدُّ والإدراجُ وَحدَةَ عَمَلٍ واحِدَة (‏2026-11-07، مُراجَعَةُ عَدَساتِ C1 · W13-1·SEC-C1-1) ═══
//
// **المَقيسُ قَبل** (عِندَ `cd829ab`، إقلاعٌ بارِدٌ بِقاعِدَةٍ فارِغَة — تَقريرُ عَدَسَةِ الأَمن): بابُ الإنشاءِ يَسأَلُ `CheckBuildAsync` ثُمَّ يُدرِجُ المَتجَرَ في
// حِفظٍ، ثُمَّ يَزيدُ `StoresBuilt` في حِفظٍ ثانٍ بِتَزامُنٍ تَفاؤُلِيٍّ يُعادُ مَرَّة — ولا قُفلَ بَينَ الثَلاثَة. فَسِتّونَ طَلَباً مُتَزامِناً بِمُعَرِّفاتٍ مُختَلِفَة
// مِن صاحِبٍ عَدّادُه 0 كَتَبَت 60 مَتجَراً والعَدّادُ 14، و46 جَواباً 500 بَعدَ أَن كُتِبَ المَتجَر؛ وصاحِبٌ عَدّادُه 39 (السَقفُ 40) أَرسَلَ سِتّاً مَعاً فَصارَت مَتاجِرُه
// 45 والعَدّادُ 43. أَي أَنّ العَدّادَ يَتَخَلَّفُ عَن المَتاجِرِ فَلا سَقفَ لِلدَفَعات.
//
// **وما يُثَبِّتُه هذا المِلَفُّ — وهُوَ ما يَجِبُ أَن يَنجوَ مِن الإصلاح، ولا يُعادُ حَرفٌ مِنه بَعدَه** (القاعِدَة ٣): **جَوابُ الحَدِّ المُتَتابِع** في البابَين —
// صاحِبٌ تَحتَ السَقفِ بِواحِدٍ يَبني مَتجَراً ويُعَدُّ لَه فَيَبلُغُ السَقف، ثُمَّ يُرَدُّ التالي بِـ`?upgrade=build` (أَو بِنَصِّ الحَدِّ في أَداةِ الوَكيل) ولا يُكتَبُ
// مَتجَرٌ ولا يَتَحَرَّكُ العَدّاد؛ ومَن بَلَغَ السَقفَ يُرَدُّ قَبلَ أَن يُقرَأَ حَقلٌ واحِد. والإصلاحُ يَجعَلُ الدَفعَةَ المُتَزامِنَةَ تُجابُ بِهذا الجَوابِ بِعَينِه — فَهُوَ مِرآتُه.
// يَتَخَطّى نَفسَه ما لَم تُضبَط WASAYEL_LIVE_PROOF=1 وConnectionStrings__Postgres — عَقدُ إخوَتِه، وفي مَجموعَتِهِم التَسلسُلِيَّة.

[Collection(LiveProofCollection.Name)]
public class LiveStoreQuotaCharacterizationTests(ITestOutputHelper output)
{
    private static bool Enabled => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>سَقفُ الدَرَجَةِ الَّتي يولَدُ بِها الحِسابُ — مِن الكَتالوجِ لا رَقَماً مَكتوباً (القاعِدَة ١٦).</summary>
    internal static int Cap => TierCatalog.For("spark").StoresMax;

    /// <summary>جَوابُ حَدِّ المَتاجِرِ في بابِ الاستوديو بِحَرفِه.</summary>
    internal static string StudioUpgradeBuild => $"{StudioStoreCreate.Page}?upgrade={StudioUpgradeReason.Stores}";

    /// <summary>يَضَعُ عَدّادَ صاحِبِ الطَلَبِ عَلى <paramref name="built"/> — بِقِراءَةٍ ثُمَّ حِفظٍ كَأَيِّ كاتِبٍ لِوَثيقَةٍ بِتَزامُنٍ تَفاؤُلِيّ.</summary>
    internal static async Task SetStoresBuiltAsync(LiveTemplateDoorHost host, Probe p, int built)
    {
        await using var s = host.Store.LightweightSession(StudioAuth.Tenant);
        var u = (await s.LoadAsync<StudioUser>(p.User.Id))!;
        u.StoresBuilt = built;
        s.Store(u);
        await s.SaveChangesAsync();
    }

    /// <summary>عَدَدُ المَتاجِرِ الَّتي يَملِكُها صاحِبُ الطَلَبِ في Postgres.</summary>
    internal static async Task<int> OwnedStoresAsync(LiveTemplateDoorHost host, Probe p)
    {
        await using var q = host.Store.QuerySession();
        return await Marten.QueryableExtensions.CountAsync(q.Query<Tenant>().Where(t => t.OwnerUserId == p.User.Id));
    }

    [Fact]
    public async Task One_below_the_cap_a_create_builds_and_reaches_the_cap_and_the_next_is_upgrade_build_and_writes_nothing_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var a = await UserAsync(host);
        var (first, second) = (NewSlug("w13c-"), NewSlug("w13c-"));
        try
        {
            await SetStoresBuiltAsync(host, a, Cap - 1);
            var built = await StudioCreateAsync(host, a, first, "الأَخير");
            var refused = await StudioCreateAsync(host, a, second, "فَوقَ السَقف");
            var counter = await StoresBuiltAsync(host, a);
            output.WriteLine($"[live] السَقف {Cap} · الأُولى ⇒ {built.Status} {built.Location} · الثانِيَة ⇒ {refused.Status} {refused.Location} · العَدّاد {counter}");
            Assert.Equal((302, $"/studio/apps/{first}?built=1"), built);
            Assert.Equal((302, StudioUpgradeBuild), refused);
            Assert.Equal(Cap, counter);
            Assert.Equal(a.User.Id, (await TenantAsync(host, first))!.OwnerUserId);
            Assert.Null(await TenantAsync(host, second));
        }
        finally { await CleanAsync(host, [a], first, second); }
    }

    [Fact]
    public async Task At_the_cap_the_create_door_answers_upgrade_build_before_it_reads_a_field_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var a = await UserAsync(host);
        try
        {
            await SetStoresBuiltAsync(host, a, Cap);
            var refused = await host.PostStudioFormAsync(StudioStoreCreate.Endpoint, new Dictionary<string, string>(), a.Token, []);
            output.WriteLine($"[live] عِندَ السَقفِ بِاستِمارَةٍ فارِغَة ⇒ {refused.Status} {refused.Location} · العَدّاد {await StoresBuiltAsync(host, a)}");
            Assert.Equal((302, StudioUpgradeBuild), refused);
            Assert.Equal(Cap, await StoresBuiltAsync(host, a));
            Assert.Equal(0, await OwnedStoresAsync(host, a));
        }
        finally { await CleanAsync(host, [a]); }
    }

    [Fact]
    public async Task One_below_the_cap_create_tenant_builds_and_reaches_the_cap_and_the_next_is_the_limit_text_and_writes_nothing_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var a = await UserAsync(host);
        var (first, second) = (NewSlug("w13g-"), NewSlug("w13g-"));
        try
        {
            await SetStoresBuiltAsync(host, a, Cap - 1);
            var exec = new AgentToolExecutor(host.Store, new TenantRoleService(host.Store));
            var built = await exec.ExecuteAsync("create_tenant", AgentPayload(first, "الأَخير"), a.User.Id);
            var refused = await exec.ExecuteAsync("create_tenant", AgentPayload(second, "فَوقَ السَقف"), a.User.Id);
            var counter = await StoresBuiltAsync(host, a);
            output.WriteLine($"[live] السَقف {Cap} · الأُولى ⇒ {built} · الثانِيَة ⇒ {refused} · العَدّاد {counter}");
            Assert.Equal((true, AgentCreated(first)), built);
            Assert.Equal((false, AgentStoreLimitRefusalTests.ExpectedFor(Cap, Cap)), refused);
            Assert.Equal(Cap, counter);
            Assert.Null(await TenantAsync(host, second));
        }
        finally { await CleanAsync(host, [a], first, second); }
    }
}
