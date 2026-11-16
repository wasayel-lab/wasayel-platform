using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Identity;
using ACommerce.Templates.Customer.Marketplace.Services.Queries;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ ثَمَنٌ نَقدِيٌّ عَلى البائِعِ أَن يَرُدَّه وهُوَ يَحذِفُ حِسابَه — تَوصيفٌ قَبلَ التَبديل (‏2026-11-04، الموجَة ١٢ G1 · ٥؛ B-2) ══════════════════════════
//
// **ما يُثَبَّتُ هُنا لِيَبقى** (القاعِدَة ٣): مُشتَري صَفقَةِ نَقدٍ أُلغِيَت بَعدَ القَبضِ وآخِرُ سَطرِ مالِها `cash_refund_due` لَه ثَمَنٌ يُرَدّ (`CashRefundOwed` صادِقَة،
// واستِعلامُ صَفحَةِ الحَذفِ يُعيدُها)، ولا شَيءَ لِمَن لَيسَ طَرَفاً فيها، ولا لِصَفقَةٍ قائِمَة؛ وبِطاقَةُ المُشتَري في صَفحَةِ الحَذفِ بِنَصِّها.
// **وما يُطبَعُ ولا يُثَبَّت**: اليَومَ البائِعُ — الطَرَفُ المُقابِلُ الَّذي يَكتُبُ `DealsService` الرَدَّ عَلَيه — لا تَراه الدالَّةُ ولا الاستِعلامُ (يَقرَأُ `InitiatorId` وَحدَه)،
// فَيَحذِفُ حِسابَه بِلا كَلِمَة، وبَعدَه يَرُدُّ `PartnerGone` المُشتَريَ عَن كُلِّ بابِ مُحادَثَة. وهذا يَتَغَيَّرُ في كوميتِ التَبديل؛ والمِلَفُّ يَبقى بِبَصمَتِه.

public class AccountDeletionCashVendorTests(ITestOutputHelper output)
{
    internal static readonly Guid Buyer = Guid.Parse("0b000001-0000-4000-8000-000000000501");
    internal static readonly Guid Vendor = Guid.Parse("0b000002-0000-4000-8000-000000000501");
    internal static readonly Guid Stranger = Guid.Parse("0b000003-0000-4000-8000-000000000501");

    internal static Deal OwedDeal(string slug = "zz", DealStatus status = DealStatus.Cancelled)
    {
        var d = new Deal { Id = Guid.NewGuid(), TenantSlug = slug, InitiatorId = Buyer, InitiatorName = "مُشتَري النَقد", CounterpartyId = Vendor, CounterpartyName = "بائِعُ النَقد",
            ListingTitle = "سِلعَةٌ نَقدِيَّة", AmountSar = 320m, Status = status };
        foreach (var a in new[] { DealEvent.CashOnDelivery, "cancelled", DealEvent.CashRefundDue })
            d.Timeline.Add(new(DealStage.Delivered, DealStage.Delivered, a, Vendor, "بائِعُ النَقد", null, DateTime.UtcNow));
        return d;
    }

    [Fact]
    public void The_buyer_is_owed_cash_back_and_a_stranger_or_a_live_deal_is_not_and_the_vendor_side_is_printed()
    {
        var owed = OwedDeal();
        output.WriteLine($"المُشتَري: {AccountErasure.CashRefundOwed(owed, Buyer)} · البائِع: {AccountErasure.CashRefundOwed(owed, Vendor)} · الغَريب: {AccountErasure.CashRefundOwed(owed, Stranger)}");
        Assert.True(AccountErasure.CashRefundOwed(owed, Buyer));
        Assert.False(AccountErasure.CashRefundOwed(owed, Stranger));
        Assert.False(AccountErasure.CashRefundOwed(OwedDeal(status: DealStatus.Active), Buyer));
        Assert.Single(AccountErasure.CashRefundsOwed(new[] { owed, OwedDeal(status: DealStatus.Active) }, Buyer));
    }

    [Fact]
    public void The_deletion_page_keeps_the_buyers_cash_card_and_its_text()
    {
        var page = WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "DeleteAccount.razor")));
        output.WriteLine($"مَفاتيحُ بِطاقَةِ البائِع: {System.Text.RegularExpressions.Regex.Matches(page, "account\\.delete\\.cash_refund_vendor_").Count}");
        Assert.Contains("@if (owedCash.Count > 0)", page, StringComparison.Ordinal);
        Assert.Contains("<li>@L.Format(\"account.delete.cash_refund_row\", string.IsNullOrWhiteSpace(d.CounterpartyName) ? L[\"account.delete.cash_refund_vendor\"] : d.CounterpartyName, d.ListingTitle, d.AmountSar)</li>",
            page, StringComparison.Ordinal);
        var l = new ACommerce.Platform.I18n.L(new Microsoft.AspNetCore.Http.HttpContextAccessor());
        Assert.Equal("«سِلعَة» — 250 ريال، يَرُدُّها بائِع", l.Format("account.delete.cash_refund_row", "بائِع", "سِلعَة", 250m));
    }
}

[Collection(LiveProofCollection.Name)]
public class LiveAccountDeletionCashVendorProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>استِعلامُ صَفحَةِ الحَذفِ عَلى Postgres: لِلمُشتَري صَفقَتُه، ولِلغَريبِ لا شَيء — ويُطبَعُ ما يُعيدُه لِلبائِع.</summary>
    [Fact]
    public async Task The_deletion_pages_query_returns_the_buyers_owed_deal_and_nothing_for_a_stranger_on_Postgres()
    {
        if (!Enabled) return;
        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "zz-cashvnd-" + Guid.NewGuid().ToString("N")[..8];
        var owed = AccountDeletionCashVendorTests.OwedDeal(slug);
        try
        {
            await using (var s = store.LightweightSession(slug)) { s.Store(owed); await s.SaveChangesAsync(); }
            var q = new UserDataQueries(store);
            var buyer = await q.CashRefundsOwedAsync(slug, AccountDeletionCashVendorTests.Buyer);
            var vendor = await q.CashRefundsOwedAsync(slug, AccountDeletionCashVendorTests.Vendor);
            var stranger = await q.CashRefundsOwedAsync(slug, AccountDeletionCashVendorTests.Stranger);
            output.WriteLine($"المُشتَري: {buyer.Count} · البائِع: {vendor.Count} · الغَريب: {stranger.Count}");
            Assert.Equal(owed.Id, Assert.Single(buyer).Id);
            Assert.Empty(stranger);
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            s.DeleteWhere<Deal>(d => d.Id != Guid.Empty);
            await s.SaveChangesAsync();
        }
    }
}
