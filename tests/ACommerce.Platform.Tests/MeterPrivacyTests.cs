using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Metering;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ G17 — لا مُعَرِّفَ شَخصٍ في الدَفتَرِ إلّا اسماً مُستَعاراً حَتمِيّاً ═══
// (‏2026-11-12، الموجَة ١٣ W13-6·٣ — ADR-051 §١١؛ التَصميم §2 «Privacy (corrected)»). الاسمُ المُستَعارُ أَحادِيُّ الاتِّجاه وحَتمِيّ (مَنعُ
// التَكرارِ والمُطابَقَةُ يُعيدانِ حِسابَه)، ولا مِفتاحَ فيه عَن قَصد. والفَحصُ الحَيُّ عَلى قاعِدَةٍ حَقيقِيَّة في LiveMeterPersistenceProofTests.

public class MeterPrivacyTests(ITestOutputHelper output)
{
    [Fact]
    public void The_pseudonym_is_deterministic_hex_and_never_carries_the_raw_id()
    {
        var raw = Guid.NewGuid().ToString("D");
        var p = UsageEntryId.Pseudonym("member.join", "store-a", raw);
        output.WriteLine($"[G17] {raw} ⇒ {p}");
        Assert.Equal(p, UsageEntryId.Pseudonym("member.join", "store-a", raw));
        Assert.Matches("^[0-9a-f]{32}$", p);
        Assert.DoesNotContain(raw, p, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(raw.Replace("-", ""), p, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(p, UsageEntryId.Pseudonym("member.join", "store-b", raw));
        Assert.NotEqual(p, UsageEntryId.Pseudonym("file.upload", "store-a", raw));
        Assert.NotEqual(p, UsageEntryId.Pseudonym("member.join", "store-a", Guid.NewGuid().ToString("D")));
    }

    /// <summary>القيمَةُ مُثَبَّتَةٌ بِحَرفِها: تَغييرُ الصيغَةِ (بادِئَة، تَرتيب، طول) يَكسِرُ مُطابَقَةَ كُلِّ سَطرٍ قَديم — فَيَحمَرُّ هُنا قَبلَ أَن يَكسِرَها.
    /// المَحسوبُ خارِجَ .NET (‏Python ‏hashlib، ‏2026-11-12): أَوَّلُ ١٦ بايتاً مِن SHA-256 فَوقَ
    /// <c>meter:member.join:store-a:00000000-0000-0000-0000-000000000001</c>.</summary>
    [Fact]
    public void The_pseudonym_format_is_pinned()
        => Assert.Equal("b4297cf15c2380cd069ea713645be47d",
                        UsageEntryId.Pseudonym("member.join", "store-a", "00000000-0000-0000-0000-000000000001"));

    /// <summary><b>مَصدَرُ <c>deal.create</c> الصَفقَةُ لا أَطرافُها</b>: مُعَرِّفُ المُبادِرِ والطَرَفِ الثاني لا يَظهَرانِ في مُعَرِّفِ السَطرِ ولا مَصدَرِه.</summary>
    [Fact]
    public void A_deal_create_entry_carries_no_person_id_of_the_deal()
    {
        var deal = new Deal { Id = Guid.NewGuid(), TenantSlug = "store-a", InitiatorId = Guid.NewGuid(), CounterpartyId = Guid.NewGuid() };
        var e = Assert.Single(MeterRules.Classify(new PendingSnapshot("store-a", [new PendingDocument(deal, true)]), null, MeterUnitCatalog.Default));
        foreach (var person in new[] { deal.InitiatorId, deal.CounterpartyId!.Value })
            foreach (var field in new[] { e.Id, e.SourceId })
            {
                Assert.DoesNotContain(person.ToString("D"), field, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(person.ToString("N"), field, StringComparison.OrdinalIgnoreCase);
            }
    }
}
