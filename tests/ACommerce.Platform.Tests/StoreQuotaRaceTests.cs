using ACommerce.Templates.Customer.Marketplace.Services;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.LiveStoreBirthOnceCharacterizationTests;
using static ACommerce.Platform.Tests.LiveStoreQuotaCharacterizationTests;

namespace ACommerce.Platform.Tests;

// ═══ الدَفعَةُ المُتَزامِنَةُ لا تَعبُرُ حَدَّ المَتاجِر: الحِصَّةُ والعَدُّ والإدراجُ في حِفظٍ واحِد (‏2026-11-07، مُراجَعَةُ عَدَساتِ C1 · W13-1·SEC-C1-1) ═══
//
// **المَقيسُ قَبل** (عِندَ `cd829ab`): السُؤالُ (`CheckBuildAsync`) والإدراجُ والعَدُّ ثَلاثُ خُطُواتٍ بِلا قُفلٍ بَينَها، فَالدَفعَةُ تَعبُرُ السَقفَ والعَدّادُ
// يَتَخَلَّفُ والطَلَبُ يُجابُ 500 بَعدَ أَن كُتِبَ المَتجَر. **والآنَ** `StudioTierService.RecordStoreBuiltAsync(ownerId, insertStore)` يَقرَأُ صاحِبَ الطَلَبِ
// ويُعيدُ سُؤالَ الحِصَّةِ ويَزيدُ العَدّادَ ويُدرِجُ المَتجَرَ في حِفظٍ واحِد — وتَعارُضُ الوَثيقَةِ يُعيدُ القِراءَةَ والسُؤال. فَتُجابُ الدَفعَةُ بِجَوابِ الحَدِّ
// المُتَتابِعِ بِعَينِه (‏`LiveStoreQuotaCharacterizationTests` — لا يُمَسّ)، وعَدَدُ المَتاجِرِ هُوَ العَدّاد.

[Collection(LiveProofCollection.Name)]
public class LiveStoreQuotaRaceProofTests(ITestOutputHelper output)
{
    private static bool Enabled => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>طَلَباتٌ مُتَزامِنَةٌ بِمُعَرِّفاتٍ مُختَلِفَةٍ مِن صاحِبٍ واحِد — الرَقمُ عَدَدُ ما يُرسَلُ مَعاً في البُرهان، لا حَدٌّ في المُنتَج.</summary>
    private const int Burst = 12;

    private static string Line(IEnumerable<(int Status, string Location)> answers)
        => string.Join(" · ", answers.GroupBy(a => a.Status == 302 ? a.Location.Split('?').Last() : a.Status.ToString()).Select(g => $"{g.Key}×{g.Count()}"));

    [Fact]
    public async Task A_burst_one_below_the_cap_builds_exactly_one_store_and_answers_the_rest_upgrade_build_with_no_500_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var a = await UserAsync(host);
        var slugs = Enumerable.Range(0, Burst).Select(_ => NewSlug("w13b-")).ToArray();
        try
        {
            await SetStoresBuiltAsync(host, a, Cap - 1);
            var answers = await Task.WhenAll(slugs.Select(slug => StudioCreateAsync(host, a, slug, "دَفعَة")));
            var (counter, owned) = (await StoresBuiltAsync(host, a), await OwnedStoresAsync(host, a));
            output.WriteLine($"[live] السَقف {Cap} · العَدّادُ قَبلُ {Cap - 1} · {Burst} مَعاً ⇒ {Line(answers)} · العَدّاد {counter} · المَتاجِر {owned}");
            Assert.All(answers, r => Assert.Equal(302, r.Status));
            Assert.Equal(1, answers.Count(r => r.Location.EndsWith("?built=1", StringComparison.Ordinal)));
            Assert.Equal(Burst - 1, answers.Count(r => r == (302, StudioUpgradeBuild)));
            Assert.Equal((Cap, 1), (counter, owned));
        }
        finally { await CleanAsync(host, [a], slugs); }
    }

    [Fact]
    public async Task A_burst_under_the_cap_builds_every_store_and_counts_every_one_with_no_500_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var a = await UserAsync(host);
        var slugs = Enumerable.Range(0, Burst).Select(_ => NewSlug("w13u-")).ToArray();
        try
        {
            var answers = await Task.WhenAll(slugs.Select(slug => StudioCreateAsync(host, a, slug, "دَفعَة")));
            var (counter, owned) = (await StoresBuiltAsync(host, a), await OwnedStoresAsync(host, a));
            output.WriteLine($"[live] السَقف {Cap} · العَدّادُ قَبلُ 0 · {Burst} مَعاً ⇒ {Line(answers)} · العَدّاد {counter} · المَتاجِر {owned}");
            Assert.All(answers, r => Assert.Equal(302, r.Status));
            Assert.Equal(Burst, answers.Count(r => r.Location.EndsWith("?built=1", StringComparison.Ordinal)));
            Assert.Equal((Burst, Burst), (counter, owned));
        }
        finally { await CleanAsync(host, [a], slugs); }
    }

    [Fact]
    public async Task A_create_tenant_burst_one_below_the_cap_builds_exactly_one_store_and_answers_the_rest_with_the_limit_text_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var a = await UserAsync(host);
        var slugs = Enumerable.Range(0, Burst).Select(_ => NewSlug("w13k-")).ToArray();
        try
        {
            await SetStoresBuiltAsync(host, a, Cap - 1);
            var exec = new AgentToolExecutor(host.Store, new TenantRoleService(host.Store));
            var answers = await Task.WhenAll(slugs.Select(slug => exec.ExecuteAsync("create_tenant", AgentPayload(slug, "دَفعَة"), a.User.Id)));
            var (counter, owned) = (await StoresBuiltAsync(host, a), await OwnedStoresAsync(host, a));
            output.WriteLine($"[live] السَقف {Cap} · العَدّادُ قَبلُ {Cap - 1} · {Burst} مَعاً ⇒ نَجَحَ {answers.Count(r => r.Item1)} · نَصُّ الحَدّ "
                           + $"{answers.Count(r => r == (false, AgentStoreLimitRefusalTests.ExpectedFor(Cap, Cap)))} · غَيرُهُما {answers.Count(r => !r.Item1 && r.Item2 != AgentStoreLimitRefusalTests.ExpectedFor(Cap, Cap))}"
                           + $" · العَدّاد {counter} · المَتاجِر {owned}");
            Assert.Equal(1, answers.Count(r => r.Item1));
            Assert.Equal(Burst - 1, answers.Count(r => r == (false, AgentStoreLimitRefusalTests.ExpectedFor(Cap, Cap))));
            Assert.Equal((Cap, 1), (counter, owned));
        }
        finally { await CleanAsync(host, [a], slugs); }
    }
}
