using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Metering;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ المُصَنِّفُ النَقِيّ — `deal.create` مِن صَفقَةٍ وُلِدَت، لا مِن تَحديث، وكُلُّ عَمودٍ قيمَةٌ أَو قيمَةٌ حارِسَة ═══
// (‏2026-11-12، الموجَة ١٣ W13-6·٣ — ADR-051 §١١؛ التَصميم §5.1–§5.5). بِلا قاعِدَةٍ ولا جَلسَة: المُصَنِّفُ دالَّةٌ عَلى قِيَم.

public class MeterRulesTests(ITestOutputHelper output)
{
    private static Deal NewDeal(string slug = "store-a") => new()
    {
        Id = Guid.NewGuid(), TenantSlug = slug, InitiatorId = Guid.NewGuid(), CounterpartyId = Guid.NewGuid(),
    };

    private static IReadOnlyList<UsageEntry> Classify(MeterScopeView? scope, params PendingDocument[] docs)
        => MeterRules.Classify(new PendingSnapshot("store-a", docs), scope, MeterUnitCatalog.Default);

    [Fact]
    public void A_deal_born_in_this_save_gives_one_deal_create_entry_with_a_deterministic_id()
    {
        var deal = NewDeal();
        var e = Assert.Single(Classify(null, new PendingDocument(deal, Created: true)));
        output.WriteLine($"[rules] {e}");
        Assert.Equal($"deal.create:store-a:{deal.Id:D}", e.Id);
        Assert.Equal(UsageEntryId.For("deal.create", "store-a", deal.Id.ToString("D")), e.Id);
        Assert.Equal(("deal.create", 1L, "store-a", "deal_status", MeterSentinels.None, "deal", deal.Id.ToString("D")),
                     (e.Unit, e.Quantity, e.App, e.Feature, e.Provider, e.SourceType, e.SourceId));
        Assert.Equal(e, Assert.Single(Classify(null, new PendingDocument(deal, Created: true))));
    }

    /// <summary><b>التَحديثُ لا يُعَدّ</b> (§5.6): صَفقَةٌ لَها نُسخَةٌ في الجَلسَة (<c>VersionFor != null</c>) تَحديثٌ لا وِلادَة.</summary>
    [Fact]
    public void An_update_of_a_deal_is_not_counted()
        => Assert.Empty(Classify(null, new PendingDocument(NewDeal(), Created: false)));

    [Fact]
    public void A_document_of_another_type_is_not_counted_and_one_deal_twice_is_one_entry()
    {
        var deal = NewDeal();
        Assert.Empty(Classify(null, new PendingDocument(new object(), Created: true), new PendingDocument("text", Created: true)));
        Assert.Single(Classify(null, new PendingDocument(deal, Created: true), new PendingDocument(deal, Created: true)));
        Assert.Equal(2, Classify(null, new PendingDocument(NewDeal(), true), new PendingDocument(NewDeal(), true)).Count);
    }

    /// <summary><b>كُلُّ عَمودٍ مَملوءٌ مِن قيمَةٍ أَو قيمَةٍ حارِسَة</b> (§5.5: <c>NOT NULL</c> لا يُفشِلُ كِتابَةَ عَمَلٍ أَبَداً): بِلا حامِلٍ وبِلا مَتجَرٍ
    /// في الصَفقَة، كُلُّ بُعدٍ مَجهولٍ <c>_unresolved</c> والمُزَوِّدُ <c>_none</c> — ولا يَرمي.</summary>
    [Fact]
    public void Every_column_is_a_value_or_a_sentinel_even_with_no_holder_and_no_store()
    {
        var entries = Classify(null, new PendingDocument(NewDeal(slug: ""), true), new PendingDocument(NewDeal(slug: "  "), true));
        Assert.Equal(2, entries.Count);
        foreach (var e in entries)
        {
            var columns = new[] { e.Id, e.Unit, e.App, e.Entrepreneur, e.Role, e.Feature, e.Provider, e.SourceType, e.SourceId };
            output.WriteLine($"[rules] {string.Join(" | ", columns)}");
            Assert.All(columns, c => Assert.False(string.IsNullOrWhiteSpace(c)));
            Assert.Equal((MeterSentinels.Unresolved, MeterSentinels.Unresolved, MeterSentinels.Unresolved, MeterSentinels.None),
                         (e.App, e.Entrepreneur, e.Role, e.Provider));
            Assert.True(e.Quantity > 0);
        }
        Assert.All(new[] { MeterSentinels.Unresolved, MeterSentinels.None }, s => Assert.True(MeterSentinels.IsSentinel(s)));
    }

    [Fact]
    public void The_holder_of_the_same_store_fills_entrepreneur_and_role_and_any_other_store_leaves_them_unresolved()
    {
        var owner = Guid.NewGuid().ToString("D");
        var same = Assert.Single(Classify(new MeterScopeView("store-a", owner, MeterSentinels.None, false), new PendingDocument(NewDeal(), true)));
        Assert.Equal((owner, MeterSentinels.None), (same.Entrepreneur, same.Role));

        var caseOnly = Assert.Single(Classify(new MeterScopeView("STORE-A", owner, "seller", false), new PendingDocument(NewDeal(), true)));
        Assert.Equal((owner, "seller"), (caseOnly.Entrepreneur, caseOnly.Role));

        var other = Assert.Single(Classify(new MeterScopeView("store-b", owner, MeterSentinels.None, false), new PendingDocument(NewDeal(), true)));
        Assert.Equal((MeterSentinels.Unresolved, MeterSentinels.Unresolved), (other.Entrepreneur, other.Role));

        var partial = Assert.Single(Classify(new MeterScopeView("store-a", owner, null, false), new PendingDocument(NewDeal(), true)));
        Assert.Equal((owner, MeterSentinels.Unresolved), (partial.Entrepreneur, partial.Role));
    }

    [Fact]
    public void The_seed_holder_attributes_every_store_to_seed()
    {
        var seed = new MeterScopeView(null, MeterSentinels.Seed, MeterSentinels.Seed, IsSeed: true);
        var entries = Classify(seed, new PendingDocument(NewDeal("ejar"), true), new PendingDocument(NewDeal("theme-demo"), true));
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal((MeterSentinels.Seed, MeterSentinels.Seed), (e.Entrepreneur, e.Role)));
    }

    /// <summary>المُصَنِّفُ نَقِيٌّ بِالتَوقيع (القاعِدَة ٧): لا مُعامِلَ جَلسَةٍ ولا مَخزَنٍ في أَيِّ دالَّةٍ عامَّةٍ مِنه.</summary>
    [Fact]
    public void The_classifier_takes_no_session_and_no_store()
    {
        var methods = typeof(MeterRules).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var parameters = methods.SelectMany(m => m.GetParameters()).Select(p => p.ParameterType).ToList();
        output.WriteLine($"[rules] دَوالُّ عامَّة {methods.Length} · مُعامِلات {parameters.Count}: {string.Join(", ", parameters.Select(t => t.Name).Distinct())}");
        Assert.True(parameters.Count > 0, "أَداةٌ عَمياء: صِفرُ مُعامِل.");
        Assert.DoesNotContain(parameters, t => typeof(Marten.IQuerySession).IsAssignableFrom(t) || typeof(Marten.IDocumentStore).IsAssignableFrom(t));
    }
}
