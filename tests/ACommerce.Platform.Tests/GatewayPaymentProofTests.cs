using ACommerce.Kit.Payments;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ بُرهانُ الدَفعِ عِندَ بَوّابَةِ المُستَأجِر — رُموزُ خَرقٍ خَمسَة، لِكُلٍّ مُوجَبٌ وسالِب (‏ADR-032 §٢‑هـ الخُطوَة 8) ═════════
//
// دالَّةٌ نَقِيَّة: فاتورَةٌ كَما جَلَبَها العَميلُ (`MoyasarInvoice`) وصَفَقاتٌ كَما في المَخزَن. **السالِبُ** لِكُلِّ رَمز: البُرهانُ
// السَليمُ لا يُطلِقُه؛ **والمُوجِبُ**: كَسرُ شَرطٍ واحِدٍ يُطلِقُه هُوَ وَحدَه — فَيُقاسُ أَنّ كُلَّ شَرطٍ يَحرُسُ نَفسَه لا جارَه.

public class GatewayPaymentProofTests(ITestOutputHelper output)
{
    private const string InvoiceId = MoyasarClientTests.InvoiceId;

    private static Deal DealOf(decimal amount, string? invoiceRef = InvoiceId)
    {
        var d = new Deal
        {
            Id = Guid.NewGuid(), TenantSlug = "s", Pattern = "marketplace", AmountSar = amount,
            InitiatorId = Guid.NewGuid(), InitiatorName = "مُشتَرٍ",
        };
        d.Attributes[CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.Card;
        if (invoiceRef is not null) d.Refs[GatewayPaymentProof.InvoiceRef] = invoiceRef;
        return d;
    }

    /// <summary>صَفقَتانِ بِـ100 و50 رِيالاً، وفاتورَةٌ مَدفوعَةٌ لَهُما بِـ15000 هَلَلَة — البُرهانُ السَليم.</summary>
    private static (MoyasarInvoice Invoice, IReadOnlyList<Deal> Deals) Sound()
    {
        var deals = new[] { DealOf(100m), DealOf(50m) };
        var invoice = new MoyasarInvoice(InvoiceId, "paid", 15000, "SAR", null,
            GatewayPaymentProof.MetadataFor(deals.Select(d => d.Id).ToArray()));
        return (invoice, deals);
    }

    private static string[] CodesOf(MoyasarInvoice invoice, IReadOnlyList<Deal> deals)
        => GatewayPaymentProof.Validate(invoice, deals).Select(v => v.Code).ToArray();

    // ═══ ١) السَليمُ يَعبُر — وهُوَ سالِبُ كُلِّ رَمز ═════════════════════════════════

    [Fact]
    public void A_sound_proof_passes_with_no_violation_and_the_gate_issues_the_payment()
    {
        var (invoice, deals) = Sound();

        Assert.Empty(GatewayPaymentProof.Validate(invoice, deals));
        Assert.True(GatewayPaymentProof.IsValid(invoice, deals));
        var admitted = GatewayPaymentProof.Gate(invoice, deals);
        Assert.Null(admitted.Violation);
        Assert.Equal(InvoiceId, admitted.Payment!.InvoiceId);
        Assert.Equal(deals.Select(d => d.Id), admitted.Payment.DealIds);
        Assert.Equal((15000L, "SAR"), (admitted.Payment.Amount, admitted.Payment.Currency));
    }

    // ═══ ٢) كُلُّ رَمزٍ بِشَرطِه وَحدَه ═══════════════════════════════════════════════

    /// <summary><c>gateway_invoice_unpaid</c> — كُلُّ حالٍ في مَعجَمِ الفاتورَةِ سِوى «paid» يُطلِقُه وَحدَه.</summary>
    [Theory]
    [InlineData("initiated")]
    [InlineData("failed")]
    [InlineData("refunded")]
    [InlineData("canceled")]
    [InlineData("on_hold")]
    [InlineData("expired")]
    [InlineData("voided")]
    [InlineData("PAID")]          // بِحَرفِ المَعجَم: حالَةُ الأَحرُفِ لا تُطَبَّع
    public void InvoiceUnpaid_fires_alone_for_every_status_but_paid(string status)
    {
        var (invoice, deals) = Sound();
        Assert.Equal(new[] { GatewayPaymentProof.InvoiceUnpaid }, CodesOf(invoice with { Status = status }, deals));
        Assert.Equal(GatewayPaymentProof.InvoiceUnpaid, GatewayPaymentProof.Gate(invoice with { Status = status }, deals).Violation!.Code);
        Assert.Null(GatewayPaymentProof.Gate(invoice with { Status = status }, deals).Payment);
    }

    /// <summary><c>gateway_currency_mismatch</c> — عُملَةٌ غَيرُ الرِيالِ تُطلِقُه وَحدَه.</summary>
    [Theory]
    [InlineData("USD")]
    [InlineData("KWD")]
    [InlineData("sar")]
    public void CurrencyMismatch_fires_alone_for_any_currency_but_SAR(string currency)
    {
        var (invoice, deals) = Sound();
        Assert.Equal(new[] { GatewayPaymentProof.CurrencyMismatch }, CodesOf(invoice with { Currency = currency }, deals));
    }

    /// <summary><c>gateway_amount_mismatch</c> — هَلَلَةٌ زائِدَةٌ أَو ناقِصَة، ومَجموعٌ لا يَصيرُ هَلَلاتٍ صَحيحَة.</summary>
    [Fact]
    public void AmountMismatch_fires_alone_for_one_halala_off_or_an_unrepresentable_total()
    {
        var (invoice, deals) = Sound();
        Assert.Equal(new[] { GatewayPaymentProof.AmountMismatch }, CodesOf(invoice with { Amount = 14999 }, deals));
        Assert.Equal(new[] { GatewayPaymentProof.AmountMismatch }, CodesOf(invoice with { Amount = 15001 }, deals));

        var fractional = new[] { DealOf(100.005m) };
        var listed = invoice with { Amount = 10001, Metadata = GatewayPaymentProof.MetadataFor(fractional.Select(d => d.Id).ToArray()) };
        Assert.Equal(new[] { GatewayPaymentProof.AmountMismatch }, CodesOf(listed, fractional));
    }

    /// <summary><c>gateway_deals_mismatch</c> — صَفقَةٌ ناقِصَةٌ أَو زائِدَةٌ في <c>metadata</c>، أَو قيمَةٌ لَيسَت مُعَرِّفاً، أَو صَفقَةٌ
    /// مُكَرَّرَةٌ في المَعروض، أَو لا صَفَقات.</summary>
    [Fact]
    public void DealsMismatch_fires_when_the_metadata_names_other_deals_than_those_offered()
    {
        var (invoice, deals) = Sound();

        // واحِدَةٌ مِن الاثنَتَينِ فَقَط في الفاتورَة — والمَبلَغُ يُعَدَّلُ لِيَبقى الرَمزُ وَحدَه.
        var one = invoice with { Metadata = GatewayPaymentProof.MetadataFor(new[] { deals[0].Id }) };
        Assert.Equal(new[] { GatewayPaymentProof.DealsMismatch }, CodesOf(one, deals));

        var extra = invoice with { Metadata = GatewayPaymentProof.MetadataFor(new[] { deals[0].Id, deals[1].Id, Guid.NewGuid() }) };
        Assert.Equal(new[] { GatewayPaymentProof.DealsMismatch }, CodesOf(extra, deals));

        var garbled = invoice with { Metadata = new Dictionary<string, string> { ["deal_1"] = deals[0].Id.ToString("N"), ["deal_2"] = "x" } };
        Assert.Equal(new[] { GatewayPaymentProof.DealsMismatch }, CodesOf(garbled, deals));

        var twice = new[] { deals[0], deals[0] };
        var doubled = invoice with { Amount = 20000, Metadata = GatewayPaymentProof.MetadataFor(new[] { deals[0].Id }) };
        Assert.Equal(new[] { GatewayPaymentProof.DealsMismatch }, CodesOf(doubled, twice));

        Assert.Contains(GatewayPaymentProof.DealsMismatch, CodesOf(invoice, Array.Empty<Deal>()));
    }

    /// <summary><c>gateway_invoice_mismatch</c> — صَفقَةٌ لا تَحمِلُ مُعَرِّفَ هذِه الفاتورَة، أَو تَحمِلُ غَيرَه. ومُعَرِّفُ الفاتورَةِ يُقارَنُ
    /// بِلا حالَةِ أَحرُف (‏uuid كَما تَكتُبُه البَوّابَةُ أَو نَكتُبُه).</summary>
    [Fact]
    public void InvoiceMismatch_fires_alone_when_a_deal_does_not_carry_this_invoice()
    {
        var (invoice, deals) = Sound();

        deals[1].Refs.Remove(GatewayPaymentProof.InvoiceRef);
        Assert.Equal(new[] { GatewayPaymentProof.InvoiceMismatch }, CodesOf(invoice, deals));

        deals[1].Refs[GatewayPaymentProof.InvoiceRef] = Guid.NewGuid().ToString();
        Assert.Equal(new[] { GatewayPaymentProof.InvoiceMismatch }, CodesOf(invoice, deals));

        deals[1].Refs[GatewayPaymentProof.InvoiceRef] = InvoiceId.ToUpperInvariant();
        Assert.Empty(CodesOf(invoice, deals));
    }

    /// <summary>والرُموزُ تَتَجَمَّعُ بِتَرتيبٍ ثابِت، والبَوّابَةُ تُرجِعُ أَوَّلَها — ولا «ثَبَتَ» مَعَ خَرق.</summary>
    [Fact]
    public void Violations_accumulate_in_a_fixed_order_and_the_gate_returns_the_first()
    {
        var (invoice, deals) = Sound();
        deals[0].Refs.Remove(GatewayPaymentProof.InvoiceRef);
        var broken = invoice with { Status = "initiated", Currency = "USD", Amount = 1, Metadata = new Dictionary<string, string>() };

        Assert.Equal(GatewayPaymentProof.Codes, CodesOf(broken, deals));
        var admitted = GatewayPaymentProof.Gate(broken, deals);
        Assert.Equal(GatewayPaymentProof.InvoiceUnpaid, admitted.Violation!.Code);
        Assert.Null(admitted.Payment);
        Assert.False(GatewayPaymentProof.IsValid(broken, deals));
    }

    /// <summary>المَعجَمُ مُغلَقٌ عِندَ خَمسَة، وكُلُّ رَمزٍ يُنتِجُه خَرقٌ حَقيقِيّ (لا رَمزَ بِلا مُنتِج)، ولِكُلٍّ رِسالَة — والعَدُّ يُطبَع.</summary>
    [Fact]
    public void Every_declared_code_is_produced_by_some_breach_and_carries_a_message()
    {
        var (invoice, deals) = Sound();
        var produced = new HashSet<string>(StringComparer.Ordinal);
        var breaches = new Func<(MoyasarInvoice, IReadOnlyList<Deal>)>[]
        {
            () => (invoice with { Status = "failed" }, deals),
            () => (invoice with { Currency = "USD" }, deals),
            () => (invoice with { Amount = 1 }, deals),
            () => (invoice with { Metadata = new Dictionary<string, string>() }, deals),
            () => (invoice with { Id = Guid.NewGuid().ToString() }, deals),
        };
        foreach (var b in breaches)
        {
            var (i, d) = b();
            foreach (var v in GatewayPaymentProof.Validate(i, d))
            {
                produced.Add(v.Code);
                Assert.True(v.MessageAr.Length > 15, $"«{v.Code}» بِلا رِسالَة.");
            }
        }

        output.WriteLine($"رُموزٌ مُعلَنَة: {GatewayPaymentProof.Codes.Count} · مُنتَجَة: {produced.Count} · خُروق: {breaches.Length}");
        Assert.Equal(5, GatewayPaymentProof.Codes.Count);
        Assert.Equal(GatewayPaymentProof.Codes.OrderBy(c => c, StringComparer.Ordinal), produced.OrderBy(c => c, StringComparer.Ordinal));
        Assert.All(GatewayPaymentProof.Codes, c => Assert.True(GatewayPaymentProof.Contains(c)));
        Assert.False(GatewayPaymentProof.Contains("gateway_other"));
    }

    // ═══ ٣) ما يَكتُبُه إتمامُ الشِراءِ ويَقرَؤُه البُرهان ═══════════════════════════════

    /// <summary><c>metadata</c> ذَهاباً وإياباً: مِفتاحٌ لِكُلِّ صَفقَةٍ بِصيغَةِ <c>N</c>، داخِلَ حُدودِ الوَثيقَة؛ وأَكثَرُ مِن ثَلاثينَ
    /// صَفقَةً لا تَحمِلُها فاتورَةٌ واحِدَة.</summary>
    [Fact]
    public void The_metadata_round_trips_within_the_documented_bounds()
    {
        var ids = Enumerable.Range(0, GatewayPaymentProof.MaxDealsPerInvoice).Select(_ => Guid.NewGuid()).ToArray();
        var metadata = GatewayPaymentProof.MetadataFor(ids);

        Assert.Equal(30, GatewayPaymentProof.MaxDealsPerInvoice);
        Assert.Equal(ids.Length, metadata.Count);
        Assert.All(metadata, kv => Assert.True(kv.Key.Length <= MoyasarClient.MetadataMaxKeyLength && kv.Value.Length == 32));
        Assert.True(GatewayPaymentProof.DealIdsIn(metadata)!.SetEquals(ids));
        Assert.True(MoyasarClient.IsSendable(new MoyasarInvoiceDraft(100, "د", "https://s.example/r", "https://s.example/r", metadata)));

        Assert.True(GatewayPaymentProof.FitsOneInvoice(1));
        Assert.False(GatewayPaymentProof.FitsOneInvoice(0));
        Assert.False(GatewayPaymentProof.FitsOneInvoice(31));
        Assert.Throws<ArgumentException>(() => GatewayPaymentProof.MetadataFor(Array.Empty<Guid>()));
        Assert.Throws<ArgumentException>(() => GatewayPaymentProof.MetadataFor(ids.Append(Guid.NewGuid()).ToArray()));

        // ومَفاتيحُ لَيسَت لِصَفَقاتٍ تُتَجاوَز؛ وقيمَةٌ مُكَرَّرَةٌ تُبطِلُ القِراءَة.
        var mixed = new Dictionary<string, string>(metadata) { ["note"] = "x" };
        Assert.True(GatewayPaymentProof.DealIdsIn(mixed)!.SetEquals(ids));
        Assert.Null(GatewayPaymentProof.DealIdsIn(new Dictionary<string, string> { ["deal_1"] = ids[0].ToString("N"), ["deal_2"] = ids[0].ToString("N") }));
    }
}
