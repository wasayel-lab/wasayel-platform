using ACommerce.Platform.Providers;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ «أَفي المَتجَرِ مَدخَلُ بائِعٍ فَعّال؟» — الشَرطُ الَّذي يُقَرِّرُ أَيَقرَأُ وارِدُ مَتجَرٍ جِسماً، مَقيساً في CI (‏2026-10-13، G5 (v)) ═════════
//
// مُراجَعَةُ المَوجَة 3b: شَرطُ `HasVendorEntriesAsync` (`Id.StartsWith(prefix) && Status == active`) يُقَرِّرُ لِكُلِّ مَتجَرٍ غَيرِ مَتجَرِ العَرضِ أَيَقرَأُ
// وارِدُه جِسماً، وكانَ مَكتوباً داخِلَ استِعلامِ Marten فَلا يُنَفِّذُه إلّا صَفّانِ حَيّانِ عَلى Postgres (`LiveVendorShippingProofTests`). اُستُخرِجَ بِحَرفِه
// (`TenantProviderBinding.ActiveVendorEntryOf`) فَيُجَمَّعُ هُنا ويُقاسُ عَلى وَثائِقَ في الذاكِرَة — والصَفّانِ الحَيّانِ يَقيسانِ تَرجَمَتَه كَما كانا.

public class ActiveVendorEntryPredicateTests(ITestOutputHelper output)
{
    private static readonly Guid Owner = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    private static TenantProviderBinding Doc(string id, string status = TenantProviderBinding.StatusActive)
        => new() { Id = id, Status = status };

    [Theory]
    // المُوجَب: مَدخَلُ بائِعٍ فَعّالٌ بِالقُدرَةِ المَسؤولِ عَنها — بِمُعَرِّفِ كاتِبِه نَفسِه.
    [InlineData("delivery:bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", TenantProviderBinding.StatusActive, "delivery", true)]
    [InlineData("payments:bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", TenantProviderBinding.StatusActive, "payments", true)]
    // السالِب: مَسحوب، ورَبطُ مَتجَرٍ بِلا فاصِل، وقُدرَةٌ أُخرى، وبادِئَةٌ لَيسَت في أَوَّلِه، وحالَةُ الحَرفِ مُختَلِفَة.
    [InlineData("delivery:bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", TenantProviderBinding.StatusRevoked, "delivery", false)]
    [InlineData("delivery", TenantProviderBinding.StatusActive, "delivery", false)]
    [InlineData("payments:bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", TenantProviderBinding.StatusActive, "delivery", false)]
    [InlineData("payments", TenantProviderBinding.StatusActive, "delivery", false)]
    [InlineData("xdelivery:bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", TenantProviderBinding.StatusActive, "delivery", false)]
    [InlineData("Delivery:bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", TenantProviderBinding.StatusActive, "delivery", false)]
    [InlineData("delivery:bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", "Active", "delivery", false)]
    // وأَوسَعُ مِن IsVendorEntryId — مَقيسٌ ومُعلَن: لاحِقَةٌ لَيسَت مُعَرِّفاً تُعَدّ (لا كاتِبَ يَكتُبُها).
    [InlineData("delivery:not-a-guid", TenantProviderBinding.StatusActive, "delivery", true)]
    [InlineData("delivery:", TenantProviderBinding.StatusActive, "delivery", true)]
    public void The_predicate_counts_an_active_entry_of_its_capability_and_nothing_else(string id, string status, string capability, bool counted)
    {
        var predicate = TenantProviderBinding.ActiveVendorEntryOf(capability).Compile();
        Assert.Equal(counted, predicate(Doc(id, status)));
    }

    /// <summary>عَلى ما يَكتُبُه الكاتِبُ (<see cref="TenantProviderBinding.VendorEntryId"/>) — لِكُلِّ قُدرَةٍ في المَعجَمِ ولِكُلِّ حال — الشَرطُ يُساوي
    /// <see cref="TenantProviderBinding.IsVendorEntryId"/> ∧ «القُدرَةُ المَسؤولُ عَنها» ∧ «فَعّال»؛ والفَرقُ الوَحيدُ خارِجَ ما يُكتَب. والعَدّادُ يُطبَعُ ويَفشَلُ
    /// إن لَم يَبلُغ مُوجَباً وسالِباً (القاعِدَة ١٠).</summary>
    [Fact]
    public void On_every_written_shape_the_predicate_equals_the_named_rule_and_the_status()
    {
        int checkedCount = 0, positives = 0, negatives = 0;
        var docs = new List<TenantProviderBinding>();
        foreach (var capability in ProviderCapabilities.All)
        foreach (var status in new[] { TenantProviderBinding.StatusActive, TenantProviderBinding.StatusRevoked })
        {
            docs.Add(Doc(TenantProviderBinding.VendorEntryId(capability, Owner), status));
            docs.Add(Doc(TenantProviderBinding.VendorEntryId(capability, Guid.NewGuid()), status));
            docs.Add(Doc(capability, status));   // رَبطُ المَتجَرِ نَفسُه
        }

        foreach (var asked in ProviderCapabilities.All)
        {
            var predicate = TenantProviderBinding.ActiveVendorEntryOf(asked).Compile();
            foreach (var d in docs)
            {
                var named = TenantProviderBinding.IsVendorEntryId(d.Id, out var capability, out _)
                            && capability == asked && d.Status == TenantProviderBinding.StatusActive;
                Assert.True(named == predicate(d), $"{asked} ← {d.Id}/{d.Status}: القاعِدَةُ {named} والشَرطُ {predicate(d)}");
                checkedCount++;
                if (named) positives++; else negatives++;
            }
        }

        output.WriteLine($"قُدُرات: {ProviderCapabilities.All.Count} · وَثائِق: {docs.Count} · مُقارَنات: {checkedCount} · مُوجَب: {positives} · سالِب: {negatives}");
        Assert.True(positives > 0 && negatives > 0, "أَداةٌ عَمياء: لا مُوجَبَ أَو لا سالِب.");
        Assert.Equal(ProviderCapabilities.All.Count * docs.Count, checkedCount);
    }

    [Fact]
    public void A_capability_outside_the_lexicon_is_refused_before_any_query()
        => Assert.ThrowsAny<ArgumentException>(() => TenantProviderBinding.ActiveVendorEntryOf("teleport"));
}
