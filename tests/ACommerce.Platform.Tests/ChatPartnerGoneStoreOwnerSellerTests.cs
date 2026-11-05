using ACommerce.Kit.Auth;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ طَلَبُ الاستِرجاعِ عَلى صَفقَةٍ قَبِلَها صاحِبُ المَتجَرِ مِن الاستوديو — تَوصيفُ السِلسِلَةِ قَبلَ التَبديل (‏2026-11-05، الموجَة ١٢ G1b — مُراجَعَةُ الإصلاح ١، G1b-R1-2) ═══
//
// `/studio/apps/{slug}/deals/{id}/advance` يُعَيِّنُ الطَرَفَ المُقابِلَ `auth.UserId` — مُعَرِّفَ صاحِبِ المَتجَرِ في الاستوديو — حينَ لا طَرَف (صَفقَةُ سَلَّةٍ عَلى إعلانٍ بِلا مالِكٍ
// لا يُحَرِّكُها إلّا هُوَ). ومُعَرِّفاتُ أَعضاءِ المَتجَرِ مِن `Guid.NewGuid()`، فَلا وَثيقَةَ `User` لَه في المَتجَر (عَلى wasayel_w12_seed: صِفرٌ مِن ثَلاثَة) — و`PartnerGone(null)`
// صادِقَة. فَالأُنبوبُ يَقولُ «البائِعُ أَغلَقَ حِسابَه» لِصاحِبِ المَتجَرِ عَن نَفسِه، والصَفحَةُ تَقولُه لِلمُشتَري.
//
// **ما يُثَبَّتُ هُنا لِيَبقى** (القاعِدَة ٣): البابُ ⇐ الصَفقَةُ بِلا رَمز، وسَطرٌ واحِد، ولا شَيءَ لِلمُشتَري ولا لِمالِكِ الإعلانِ القائِم؛ وإشعارٌ واحِدٌ لِصاحِبِ المَتجَرِ إلى صَفحَةِ
// الصَفقَةِ في الاستوديو؛ واستِمارَةُ الاستِرجاعِ مَرسومَة؛ وبابُ المُحادَثَةِ إلى مالِكِ الإعلانِ القائِمِ مَرسوم، ولا بابَ مُحادَثَةٍ إلى مُعَرِّفِ صاحِبِ المَتجَر.
// **وما يُطبَعُ ولا يُثَبَّت**: اليَومَ — عُنوانُ إشعارِ صاحِبِ المَتجَر «طَلَبُ استِرجاعٍ بائِعُه أَغلَقَ حِسابَه»، و`return_told=store`، وسَطرُ السِجِلّ «البائِعُ أَغلَقَ حِسابَه، فَأُخبِرَت
// إدارَةُ المَتجَر»، وتَلميحُ «أَغلَقَ البائِعُ حِسابَه»، وبِلا `listing_owner` سَطرُ «لَم يَعُد لِهذا البائِعِ حِسابٌ في المَتجَر». ولا أَحَدَ أَغلَقَ حِساباً. وهذا المِلَفُّ يَبقى بِبَصمَتِه.

[Collection(LiveProofCollection.Name)]
public class LiveChatPartnerGoneStoreOwnerSellerProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>مَتجَرٌ بِصاحِبٍ مُعَيَّنٍ <b>لا وَثيقَةَ لَه في المَتجَر</b> (مُعَرِّفُ الاستوديو)، ومُشتَرٍ، ومالِكُ إعلانٍ قائِم، وصَفقَةُ بَيعِ سِلعَةٍ مِن سَلَّةٍ طَرَفُها المُقابِلُ صاحِبُ المَتجَرِ
    /// — سَلَّمَها أَمسِ ومُكتَمِلَة — بِمَرجِعِ <c>listing_owner</c> إلى مالِكِ الإعلانِ أَو بِدونِه. نَسَقُ <see cref="LiveChatPartnerGoneReturnProofTests.SeedAsync"/>،
    /// و<c>Vendor</c> في السِجِلِّ المُعادِ مالِكُ الإعلانِ القائِم.</summary>
    internal static async Task<LiveChatPartnerGoneReturnProofTests.Seeded> SeedAsync(IDocumentStore store, bool listingOwnerRef)
    {
        var tag = listingOwnerRef ? "lo" : "nolo";
        var slug = $"zz-retowner-{tag}";
        Guid Fixed(int n) => Guid.Parse($"{(listingOwnerRef ? "0f" : "1f")}{n:D6}-0000-4000-8000-000000000412");
        var owner = Fixed(1);
        var day = DateTime.UtcNow.Date;
        var buyer = new User { Id = Fixed(2), TenantSlug = slug, FullName = "مُشتَرٍ مِن صاحِبِ المَتجَر", Phone = "+966500000631", PhoneVerified = true, CreatedAt = day.AddDays(-30) };
        var vendor = new User { Id = Fixed(3), TenantSlug = slug, FullName = "مالِكُ إعلانٍ قائِم", Phone = "+966500000632", PhoneVerified = true, CreatedAt = day.AddDays(-30) };
        var deal = new Deal
        {
            Id = Fixed(4), TenantSlug = slug, ListingId = Fixed(5), ListingTitle = "سِلعَةٌ قَبِلَها صاحِبُ المَتجَر", Pattern = DealReturnRequest.GoodsPattern,
            InitiatorId = buyer.Id, InitiatorName = buyer.FullName, CounterpartyId = owner, CounterpartyName = "مالِك التَّطبيق",
            AmountSar = 150m, Status = DealStatus.Completed, Stage = DealStage.Received,
            Attributes = new() { [CheckoutPayMethods.AttributeKey] = "cash" },
            CreatedAt = day.AddDays(-3), UpdatedAt = day.AddDays(-1).AddHours(10),
        };
        if (listingOwnerRef) deal.Refs[DealsService.ListingOwnerRef] = vendor.Id.ToString();
        deal.Timeline.Add(new(DealStage.Offered, DealStage.Booked, "assigned", owner, deal.CounterpartyName, null, day.AddDays(-3).AddHours(9)));
        deal.Timeline.Add(new(DealStage.Booked, DealStage.Delivered, "advanced", owner, deal.CounterpartyName, null, day.AddDays(-1).AddHours(9)));
        deal.Timeline.Add(new(DealStage.Delivered, DealStage.Received, "advanced", owner, deal.CounterpartyName, null, day.AddDays(-1).AddHours(10)));
        await LiveChatPartnerGoneReturnProofTests.CleanAsync(store, slug);
        await using (var g = store.LightweightSession())
        {
            g.Store(new Tenant { Id = slug, Name = "مَتجَرٌ يَقبَلُ صاحِبُه مِن الاستوديو", OwnerUserId = owner });
            await g.SaveChangesAsync();
        }
        await using (var s = store.LightweightSession(slug))
        {
            s.Store(buyer, vendor);
            s.Store(deal);
            await s.SaveChangesAsync();
        }
        return new LiveChatPartnerGoneReturnProofTests.Seeded(slug, owner, buyer, vendor, deal);
    }

    internal static string OwnerChatAction(LiveChatPartnerGoneReturnProofTests.Seeded seeded) => $"action=\"/{seeded.Slug}/users/{seeded.Owner}/chat\"";

    private static string Text(string key) => LiveChatPartnerGoneOwnerlessStoreProofTests.Text(key);

    private void Print(string tag, LiveChatPartnerGoneReturnProofTests.Seeded seeded, LiveChatPartnerGoneOwnerlessStoreProofTests.Round r)
    {
        var toOwner = r.Notices.Where(n => n.UserId == seeded.Owner).ToList();
        output.WriteLine($"{tag}: قَبلَ الطَلَب: تَلميحُ «أَغلَقَ البائِعُ حِسابَه» {r.Before.Contains("أَغلَقَ البائِعُ حِسابَه")} · تَلميحُ «يُخبَرُ البائِع» {r.Before.Contains("يُخبَرُ البائِعُ بِطَلَبِك")} · "
            + $"«لَم يَعُد لِهذا البائِعِ حِساب» {r.Before.Contains(Text("vendors.profile.contact_gone"))} · بابُ المُحادَثَةِ إلى مالِكِ الإعلان {r.Before.Contains(LiveChatPartnerGoneReturnProofTests.ChatAction(seeded))} · "
            + $"البابُ: {r.Status} → {r.Location} · لِصاحِبِ المَتجَر {toOwner.Count} «{toOwner.FirstOrDefault()?.Title}» ({toOwner.FirstOrDefault()?.RelatedUrl}) · "
            + $"لِمالِكِ الإعلان {r.Notices.Count(n => n.UserId == seeded.Vendor.Id)} · return_told {r.Deal.Refs.GetValueOrDefault(DealNotices.ReturnToldRef) ?? "غائِب"} · "
            + $"السِجِلّ «أَغلَقَ حِسابَه» {r.After.Contains("البائِعُ أَغلَقَ حِسابَه")}");
    }

    /// <summary>طَرَفُها المُقابِلُ صاحِبُ المَتجَرِ ومالِكُ إعلانِها بائِعٌ قائِم. <b>يُثَبَّت</b>: البابُ ⇐ الصَفقَة، سَطرٌ واحِد، إشعارٌ واحِدٌ لِصاحِبِ المَتجَرِ إلى صَفحَةِ الاستوديو، ولا شَيءَ
    /// لِلمُشتَري ولا لِمالِكِ الإعلان، وبابُ المُحادَثَةِ إلى مالِكِ الإعلانِ مَرسومٌ قَبلَه. <b>ويُطبَعُ</b> ما يُقالُ عَن «البائِع».</summary>
    [Fact]
    public async Task A_return_on_a_deal_the_store_owner_accepted_tells_him_once_at_the_studio_and_keeps_the_listing_owners_chat_door_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var seeded = await SeedAsync(host.Store, listingOwnerRef: true);
        try
        {
            var r = await LiveChatPartnerGoneOwnerlessStoreProofTests.RunAsync(host, seeded);
            Print("صاحِبُ المَتجَرِ طَرَفٌ ومالِكُ الإعلانِ قائِم", seeded, r);
            Assert.True(r.Before.Length > 2000, $"أَداةٌ عَمياء: الصَفحَةُ {r.Before.Length} بايت.");
            Assert.Contains(LiveChatPartnerGoneReturnProofTests.ReturnAction(seeded), r.Before, StringComparison.Ordinal);
            Assert.Contains(LiveChatPartnerGoneReturnProofTests.ChatAction(seeded), r.Before, StringComparison.Ordinal);
            Assert.DoesNotContain(OwnerChatAction(seeded), r.Before, StringComparison.Ordinal);
            Assert.Equal((302, $"/{seeded.Slug}/deals/{seeded.Deal.Id}"), (r.Status, r.Location));
            Assert.Single(r.Deal.Timeline, e => e.Action == DealEvent.ReturnRequested);
            var told = Assert.Single(r.Notices);
            Assert.Equal((seeded.Owner, DealNotices.StudioDealUrl(seeded.Slug, seeded.Deal.Id)), (told.UserId, told.RelatedUrl));
        }
        finally { await LiveChatPartnerGoneReturnProofTests.CleanAsync(host.Store, seeded.Slug); }
    }

    /// <summary>طَرَفُها المُقابِلُ صاحِبُ المَتجَرِ ولا <c>listing_owner</c>. <b>يُثَبَّت</b>: البابُ ⇐ الصَفقَة، سَطرٌ واحِد، إشعارٌ واحِدٌ لِصاحِبِ المَتجَرِ إلى صَفحَةِ الاستوديو، ولا شَيءَ
    /// لِلمُشتَري، ولا بابَ مُحادَثَةٍ إلى مُعَرِّفِ صاحِبِ المَتجَر (يَرُدُّه البابُ بِـ<c>partner_missing</c>). <b>ويُطبَعُ</b> سَطرُ «لَم يَعُد لِهذا البائِعِ حِساب» والتَلميحُ والسِجِلّ.</summary>
    [Fact]
    public async Task A_return_on_a_store_owner_deal_without_a_listing_owner_tells_him_once_and_draws_no_chat_door_to_his_studio_id_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var seeded = await SeedAsync(host.Store, listingOwnerRef: false);
        try
        {
            var r = await LiveChatPartnerGoneOwnerlessStoreProofTests.RunAsync(host, seeded);
            Print("صاحِبُ المَتجَرِ طَرَفٌ بِلا مالِكِ إعلان", seeded, r);
            Assert.Contains(LiveChatPartnerGoneReturnProofTests.ReturnAction(seeded), r.Before, StringComparison.Ordinal);
            Assert.DoesNotContain(OwnerChatAction(seeded), r.Before, StringComparison.Ordinal);
            Assert.Equal((302, $"/{seeded.Slug}/deals/{seeded.Deal.Id}"), (r.Status, r.Location));
            Assert.Single(r.Deal.Timeline, e => e.Action == DealEvent.ReturnRequested);
            var told = Assert.Single(r.Notices);
            Assert.Equal((seeded.Owner, DealNotices.StudioDealUrl(seeded.Slug, seeded.Deal.Id)), (told.UserId, told.RelatedUrl));
        }
        finally { await LiveChatPartnerGoneReturnProofTests.CleanAsync(host.Store, seeded.Slug); }
    }
}
