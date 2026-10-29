using System.Text.RegularExpressions;
using ACommerce.Kit.Auth;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Identity;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>حَذفُ الحِسابِ وصَفقَةٌ جارِيَة</b> (‏2026-10-29، قَرارُ المالِك د52 (١) «أ» — «لا يُقبَلُ طَلَبُ الحَذفِ ما دامَت لِلمُستَخدِمِ صَفقَةٌ جارِيَة،
/// فَيُكمِلُها أَو يُلغيها (أَو يَطلُبُ ذلكَ مِن المَتجَر) أَوَّلاً»). <b>تَوصيفُ «اليَوم» قَبلَ التَبديل</b> (القاعِدَة ٣): المَعجَمُ أَربَعَةُ رُموز،
/// و<c>Validate</c> لا يَسأَلُ عَن صَفقَة، وجِسمُ النُقطَةِ لا يَقرَأُ صَفقَةً قَبلَ <c>Erase</c> — فَحِسابٌ لَه صَفقَةٌ في مُنتَصَفِها يُحذَفُ كَأَيِّ حِساب،
/// ويَبقى الطَرَفُ الآخَرُ بِصَفقَةٍ طَرَفُها مَمحُوّ. يُقلَبُ الصَفُّ بِاسمِه في كوميتِ التَبديل.</para>
/// </summary>
public class AccountDeletionOpenDealTests(ITestOutputHelper output)
{
    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-29، كوميتُ التَبديل)</b> — كانَ <c>Today_an_account_with_an_open_deal_is_deleted_like_any_other</c>
    /// (‏<c>c2637e4</c>): «رُموزُ المَعجَم: 4 · وَسائِطُ Validate: 3 · قِراءَةُ صَفقَةٍ في جِسمِ الحَذف: 0». والآن خَمسَةُ رُموزٍ والخامِسُ <c>open_deal</c>،
    /// و<c>Validate</c> يَأخُذُ عَدَدَ الصَفَقاتِ الجارِيَةِ وَسيطاً إلزامِيّاً، والنُقطَةُ تَعُدُّها بِجَلسَتِها قَبلَ <c>Erase</c> — وطَرَفٌ في صَفقَةٍ جارِيَةٍ يُرَدّ.</summary>
    [Fact]
    public void An_account_with_an_open_deal_is_refused_before_anything_is_erased()
    {
        var validate = typeof(AccountDeletion).GetMethod(nameof(AccountDeletion.Validate))!;
        var body = AccountDeletionRetentionTests.DeleteEndpointBody();
        var count = body.IndexOf("AccountErasure.OpenDealsAsync(session, userId)", StringComparison.Ordinal);
        var gate = body.IndexOf("AccountDeletion.Validate(", StringComparison.Ordinal);
        var erase = body.IndexOf("AccountDeletion.Erase(", StringComparison.Ordinal);
        var user = new User { Id = Guid.NewGuid(), TenantSlug = "zz-del", FullName = "طَرَفٌ في صَفقَة" };
        output.WriteLine($"رُموزُ المَعجَم: {AccountDeletion.All.Count} ({string.Join(", ", AccountDeletion.All)}) · وَسائِطُ Validate: {validate.GetParameters().Length} · العَدُّ قَبلَ الحَذف: {count > gate && count < erase}");

        Assert.Equal(5, AccountDeletion.All.Count);
        Assert.Equal(AccountDeletion.OpenDeal, AccountDeletion.All[^1]);
        Assert.Equal(new[] { typeof(User), typeof(int), typeof(string), typeof(string) }, validate.GetParameters().Select(p => p.ParameterType));
        Assert.True(gate > 0 && count > gate && erase > count, "العَدُّ لا يَقَعُ داخِلَ نِداءِ البَوّابَةِ قَبلَ Erase.");
        Assert.Equal(AccountDeletion.OpenDeal, AccountDeletion.Validate(user, 1, "حذف", "حذف")!.Code);
    }

    // ─── «الجارِيَة»: دالَّةٌ واحِدَة — مُوجَبٌ وسالِب ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(DealStatus.Active, true)]
    [InlineData(DealStatus.Disputed, true)]
    [InlineData(DealStatus.Completed, false)]
    [InlineData(DealStatus.Cancelled, false)]
    public void A_deal_is_open_while_it_is_active_or_disputed(DealStatus status, bool open)
        => Assert.Equal(open, AccountErasure.IsOpen(new Deal { Status = status }));

    [Fact]
    public void Open_deals_count_the_ones_the_user_is_a_party_to_and_not_an_offer_waiting_on_their_listing()
    {
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();
        var deals = new[]
        {
            new Deal { InitiatorId = me, CounterpartyId = other, Status = DealStatus.Active },               // اشتَرى — جارِيَة
            new Deal { InitiatorId = other, CounterpartyId = me, Status = DealStatus.Disputed },             // باعَ — في نِزاع
            new Deal { InitiatorId = me, CounterpartyId = other, Status = DealStatus.Completed },            // انتَهَت
            new Deal { InitiatorId = other, CounterpartyId = Guid.NewGuid(), Status = DealStatus.Active },   // لِغَيرِه
            new Deal { InitiatorId = other, Status = DealStatus.Active,
                Refs = new() { [DealsService.ListingOwnerRef] = me.ToString() } },                           // عَرضٌ عَلى إعلانِه لَم يَقبَلْه
        };
        Assert.Equal(2, AccountErasure.OpenDeals(deals, me));
        Assert.Equal(0, AccountErasure.OpenDeals(deals.Where(d => d.Status == DealStatus.Completed), me));
    }

    /// <summary>صَفحَةُ الحَذفِ تَسأَلُ السُؤالَ نَفسَه وتَرسُمُ خُطوَتَه التالِيَةَ مَكانَ استِمارَةٍ تَرُدُّها النُقطَة (القاعِدَة ١٢).</summary>
    [Fact]
    public void The_deletion_page_asks_the_same_question_and_draws_the_next_step_instead_of_a_refused_form()
    {
        var page = WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "DeleteAccount.razor")));
        var queries = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Services", "Queries", "AccountQueries.cs"));
        var branch = page.IndexOf("@if (openDeals > 0)", StringComparison.Ordinal);
        var form = page.IndexOf("/me/delete/confirm", StringComparison.Ordinal);
        Assert.Contains("openDeals = await Accounts.OpenDealsAsync(Slug, Auth.UserId.Value);", page, StringComparison.Ordinal);
        Assert.Contains("return await Identity.AccountErasure.OpenDealsAsync(s, userId, ct);", queries, StringComparison.Ordinal);
        Assert.True(branch > 0 && form > branch && page.IndexOf("else", branch, StringComparison.Ordinal) < form, "الاستِمارَةُ لَيسَت في فَرعِ «لا صَفقَةَ جارِيَة».");
        Assert.Contains("account.delete.open_deal", page, StringComparison.Ordinal);
        Assert.Contains("$\"deals\"", page, StringComparison.Ordinal);
        Assert.NotNull(ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "account.delete.open_deal"));
        Assert.NotNull(ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "account.delete.open_deal_action"));
        Assert.Matches(new Regex(@"Err != ACommerce\.Kit\.Auth\.AccountDeletion\.OpenDeal"), page);
    }
}
