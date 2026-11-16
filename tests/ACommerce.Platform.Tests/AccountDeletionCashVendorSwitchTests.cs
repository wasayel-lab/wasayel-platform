using ACommerce.Kit.Auth;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Components.Pages;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Identity;
using Marten;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ والبائِعُ الَّذي عَلَيه الرَدُّ يُحَذَّرُ قَبلَ أَن يَحذِفَ حِسابَه (‏2026-11-04، الموجَة ١٢ G1 · ٥ — B-2؛ تَوصيفُه 495d7b0) ═══════════════════════
//
// قَبلَه («البائِع: False» و«البائِع: 0» و«مَفاتيحُ بِطاقَةِ البائِع: 0» — `AccountDeletionCashVendorTests` يَطبَعُها ويَبقى بِبَصمَتِه). والآن: `CashRefundOwed` لِلطَرَفَين،
// والاستِعلامُ لِلطَرَفَين، وصَفحَةُ الحَذفِ بِطاقَتانِ — ما يُرَدُّ إلَيكَ، وما تَرُدُّه أَنتَ — تَحذيرٌ لا مَنعٌ (G-7 لَم يَجعَلْه رَمزَ مَنع).

public class AccountDeletionCashVendorRowsTests
{
    private static Deal Owed(DealStatus status, params string[] lines)
    {
        var d = AccountDeletionCashVendorTests.OwedDeal(status: status);
        d.Timeline.Clear();
        foreach (var a in lines) d.Timeline.Add(new(DealStage.Delivered, DealStage.Delivered, a, AccountDeletionCashVendorTests.Vendor, "بائِعُ النَقد", null, DateTime.UtcNow));
        return d;
    }

    /// <summary><b>جِهَةُ البائِع</b> — المُوجَب: مُلغاةٌ آخِرُ سَطرِ مالِها الرَدُّ النَقدِيّ. والسالِب: صَفقَةٌ قائِمَة، ومُلغاةٌ بِلا قَبض، ومُلغاةٌ رُدَّ مالُها إلى البِطاقَة.</summary>
    [Fact]
    public void The_vendor_owes_cash_back_on_a_cancelled_deal_whose_latest_money_line_says_so_and_not_otherwise()
    {
        var v = AccountDeletionCashVendorTests.Vendor;
        Assert.True(AccountErasure.CashRefundOwed(Owed(DealStatus.Cancelled, DealEvent.CashOnDelivery, "cancelled", DealEvent.CashRefundDue), v));
        Assert.False(AccountErasure.CashRefundOwed(Owed(DealStatus.Active, DealEvent.CashOnDelivery, DealEvent.CashRefundDue), v));
        Assert.False(AccountErasure.CashRefundOwed(Owed(DealStatus.Cancelled, "cancelled"), v));
        Assert.False(AccountErasure.CashRefundOwed(Owed(DealStatus.Cancelled, DealEvent.CashRefundDue, DealPaymentLines.Refunded), v));
    }

    /// <summary><b>جِهَةُ المُشتَري</b> — المُوجَبُ بِحَرفِه، والسالِب: الصَفقَةُ القائِمَة؛ والغَريبُ لا جِهَةَ لَه.</summary>
    [Fact]
    public void The_buyer_side_and_the_stranger_are_as_before()
    {
        Assert.True(AccountErasure.CashRefundOwed(Owed(DealStatus.Cancelled, DealEvent.CashOnDelivery, DealEvent.CashRefundDue), AccountDeletionCashVendorTests.Buyer));
        Assert.False(AccountErasure.CashRefundOwed(Owed(DealStatus.Active, DealEvent.CashOnDelivery, DealEvent.CashRefundDue), AccountDeletionCashVendorTests.Buyer));
        Assert.False(AccountErasure.CashRefundOwed(Owed(DealStatus.Cancelled, DealEvent.CashOnDelivery, DealEvent.CashRefundDue), AccountDeletionCashVendorTests.Stranger));
    }

    /// <summary>نَصُّ بِطاقَةِ البائِع: المَبلَغُ وعُملَةُ الصَفقَةِ (<c>DealNotices.CurrencyOf</c>) واسمُ مَن يَرُدُّ إلَيه — والمَفاتيحُ الثَلاثَةُ في القامُوس، والصَفحَةُ تَقرَؤُها في فَرعِ الاستِمارَة.</summary>
    [Fact]
    public void The_vendor_card_names_the_amount_the_deals_currency_and_who_he_pays_back_before_the_confirm()
    {
        var l = new L(new HttpContextAccessor());
        var d = AccountDeletionCashVendorTests.OwedDeal();
        Assert.Equal($"«سِلعَةٌ نَقدِيَّة» — 320 {DealNotices.CurrencyOf(d)}، تَرُدُّها أَنتَ لِـمُشتَري النَقد",
            l.Format("account.delete.cash_refund_vendor_row", d.InitiatorName, d.ListingTitle, d.AmountSar, DealNotices.CurrencyOf(d)));
        d.Refs[DealsService.PaymentCurrencyRef] = "USD";
        Assert.Equal("USD", DealNotices.CurrencyOf(d));
        foreach (var k in new[] { "account.delete.cash_refund_vendor_title", "account.delete.cash_refund_vendor_lead", "account.delete.cash_refund_vendor_row" })
            Assert.NotNull(LocaleCatalog.Find(LocaleCatalog.Arabic, k));
        var page = WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "DeleteAccount.razor")));
        var mine = page.IndexOf("@if (owedByMe.Count > 0)", StringComparison.Ordinal);
        var buyer = page.IndexOf("@if (owedCash.Count > 0)", StringComparison.Ordinal);
        var form = page.IndexOf("/me/delete/confirm", StringComparison.Ordinal);
        Assert.True(buyer > 0 && mine > buyer && form > mine, $"البِطاقَةُ قَبلَ الاستِمارَة: المُشتَري {buyer} · البائِع {mine} · الاستِمارَة {form}");
        Assert.Contains("owedByMe = owed.Where(d => d.InitiatorId != Auth.UserId.Value).ToList();", page, StringComparison.Ordinal);
    }
}

[Collection(LiveProofCollection.Name)]
public class LiveAccountDeletionCashVendorSwitchProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private static async Task<string> RenderDeletePageAsync(LiveTemplateDoorHost host, string slug, User who)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        ctx.Request.Scheme = "http";
        ctx.Request.Host = new HostString("localhost");
        ctx.Request.Path = $"/{slug}/me/delete";
        ctx.Request.Headers.Cookie = $"{ACommerce.Templates.Customer.Marketplace.AuthSession.CookieName(slug)}={ACommerce.Kit.Auth.Server.AuthHandlers.MakeToken(who.Id, slug, who.SessionVersion)}";
        if (scope.ServiceProvider.GetService<IHttpContextAccessor>() is { } accessor) accessor.HttpContext = ctx;
        if (scope.ServiceProvider.GetService<NavigationManager>() is Microsoft.AspNetCore.Components.Routing.IHostEnvironmentNavigationManager nav)
            nav.Initialize("http://localhost/", $"http://localhost/{slug}/me/delete");
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, NullLoggerFactory.Instance);
        return System.Net.WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<DeleteAccount>(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(DeleteAccount.Slug)] = slug })))
            .ToHtmlString()));
    }

    /// <summary>قُلِبَ ما طَبَعَه التَوصيف («البائِع: 0»). <b>المُوجَب</b>: صَفحَةُ حَذفِ البائِعِ تَرسُمُ بِطاقَتَه بِالسِلعَةِ والمَبلَغِ وعُملَةِ الصَفقَةِ واسمِ المُشتَري، والاستِمارَةُ
    /// بَعدَها (تَحذيرٌ لا مَنع). <b>والسالِب</b>: صَفحَةُ المُشتَري بِبِطاقَتِه هُوَ لا بِبِطاقَةِ البائِع، وصَفحَةُ الغَريبِ بِلا أَيٍّ مِنهُما.</summary>
    [Fact]
    public async Task The_vendors_deletion_page_draws_his_cash_card_and_the_buyers_and_a_strangers_do_not_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var slug = "zz-cashvsw-" + Guid.NewGuid().ToString("N")[..8];
        User Person(Guid id, string name) => new() { Id = id, TenantSlug = slug, FullName = name, Phone = "+9665000006" + id.ToString("N")[^2..], PhoneVerified = true };
        var (buyer, vendor, stranger) = (Person(AccountDeletionCashVendorTests.Buyer, "مُشتَري النَقد"), Person(AccountDeletionCashVendorTests.Vendor, "بائِعُ النَقد"),
            Person(AccountDeletionCashVendorTests.Stranger, "غَريب"));
        var owed = AccountDeletionCashVendorTests.OwedDeal(slug);
        try
        {
            await using (var s = host.Store.LightweightSession(slug)) { s.Store(buyer, vendor, stranger); s.Store(owed); await s.SaveChangesAsync(); }
            var l = new L(new HttpContextAccessor());
            var vendorRow = l.Format("account.delete.cash_refund_vendor_row", owed.InitiatorName, owed.ListingTitle, owed.AmountSar, DealNotices.CurrencyOf(owed));
            var buyerRow = l.Format("account.delete.cash_refund_row", owed.CounterpartyName, owed.ListingTitle, owed.AmountSar);
            var title = l["account.delete.cash_refund_vendor_title"];
            var vendorPage = await RenderDeletePageAsync(host, slug, vendor);
            var buyerPage = await RenderDeletePageAsync(host, slug, buyer);
            var strangerPage = await RenderDeletePageAsync(host, slug, stranger);
            var confirm = $"/{slug}/me/delete/confirm";
            output.WriteLine($"البائِع: بِطاقَتُه {vendorPage.Contains(vendorRow)} («{vendorRow}») · قَبلَ الاستِمارَة {vendorPage.IndexOf(title, StringComparison.Ordinal) < vendorPage.IndexOf(confirm, StringComparison.Ordinal)} · "
                + $"المُشتَري: بِطاقَتُه {buyerPage.Contains(buyerRow)} · بِطاقَةُ البائِع {buyerPage.Contains(title)} · الغَريب: {strangerPage.Contains(title) || strangerPage.Contains(buyerRow)}");
            Assert.Contains(vendorRow, vendorPage, StringComparison.Ordinal);
            Assert.True(vendorPage.IndexOf(title, StringComparison.Ordinal) is var t && t > 0 && t < vendorPage.IndexOf(confirm, StringComparison.Ordinal));
            Assert.DoesNotContain(buyerRow, vendorPage, StringComparison.Ordinal);
            Assert.Contains(buyerRow, buyerPage, StringComparison.Ordinal);
            Assert.DoesNotContain(title, buyerPage, StringComparison.Ordinal);
            Assert.DoesNotContain(title, strangerPage, StringComparison.Ordinal);
            Assert.DoesNotContain(buyerRow, strangerPage, StringComparison.Ordinal);
            Assert.Contains(confirm, strangerPage, StringComparison.Ordinal);
        }
        finally
        {
            await using (var s = host.Store.LightweightSession(slug)) { s.DeleteWhere<Deal>(d => d.Id != Guid.Empty); await s.SaveChangesAsync(); }
            await LiveZeroRoleAdminProofTests.CleanAsync(host.Store, slug);
        }
    }
}
