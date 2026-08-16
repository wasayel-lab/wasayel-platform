using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── العَرضُ الوارِدُ يَراهُ مالِكُ الإعلان ────────────────────────────
//
// **الكُلفَةُ الَّتي كَتَبَت هذا المِلَفّ (‏المَوجَة ١٢، مَقيسَة حَيّاً):**
// مَشى `curl` رِحلَةَ التَفاوُض كامِلَةً على خادِمٍ حَيّ — عَرضٌ أُرسِلَ
// بِنَجاح وأُنشِئَت لَه صَفقَة — ثُمَّ **لَم يَجِدهُ مالِكُ الإعلانِ في
// أَيّ شاشَة**: `/{slug}/deals` صِفر، و`/{slug}/me/offers` صِفر،
// و`/{slug}/notifications` صِفر. والميزَةُ الَّتي لا تُبلَغُ بِالنَقر
// غَيرُ مَوجودَة (‏`CLAUDE.md` القاعِدَة ١٢).
//
// **والجَذرُ لَم يَكُن غِيابَ الشَرط بَل مَوضِعَه**: كانَ مَكتوباً في
// `MyDeals.razor` فَوقَ قائِمَةٍ مَصدَرُها `ListForUserAsync`، وشَرطُها
// `InitiatorId == uid || CounterpartyId == uid` — بَينَما المُرَشِّحُ
// يَطلُب `CounterpartyId is null && InitiatorId != uid`، وهو **نَقيضُه
// حَرفاً**. أَي تَركيبٌ **لا يُطابِقُ شَيئاً أَبَداً**، فَلا اختِبارٌ
// أَحمَرَّ ولا عَينٌ رَأَت: الشاشَةُ تُصَيَّر، والقِسمُ يَبقى فارِغاً،
// ويَبدو ذلك «لا عُروضَ بَعد».
//
// ولِذلك تُختَبَر الدالَّةُ النَقِيَّة **بِمُوجَبٍ وسالِبٍ لِكُلّ مِحوَر**:
// اختِبارٌ يَشهَدُ لِلحالَةِ الصَحيحَة وَحدَها كانَ سَيَخضَرّ فَوقَ
// العَطَبِ نَفسِه.

public class IncomingOfferVisibilityTests
{
    private static readonly Guid Owner    = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Offerer  = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Stranger = Guid.Parse("33333333-3333-4333-8333-333333333333");

    /// <summary>عَرضٌ وارِدٌ نَموذَجيّ: بِلا طَرَفٍ مُقابِل، بادَرَ بِه
    /// غَيرُ المالِك، ومَرجِعُ مالِكِ الإعلانِ يُشيرُ إلى المالِك.</summary>
    private static Deal Incoming() => new()
    {
        Id = Guid.NewGuid(),
        InitiatorId = Offerer,
        CounterpartyId = null,
        Refs = new() { [DealsService.ListingOwnerRef] = Owner.ToString() },
    };

    [Fact]
    public void Pending_offer_on_my_listing_is_visible_to_me()
        => Assert.True(DealsService.IsIncomingOfferFor(Incoming(), Owner));

    // ─── سالِبٌ لِكُلّ مِحوَرٍ مِن مَحاوِرِ القَرارِ الأَربَعَة ─────────

    [Fact]
    public void An_accepted_deal_is_not_an_incoming_offer()
    {
        // صارَ لَها طَرَفٌ مُقابِل ⇒ يَلتَقِطُها الشَطرُ الأَوَّل مِن
        // الاستِعلام (‏`CounterpartyId == ownerId`)، فَلا تُعَدُّ هُنا
        // مَرَّةً ثانِيَة.
        var d = Incoming();
        d.CounterpartyId = Owner;
        Assert.False(DealsService.IsIncomingOfferFor(d, Owner));
    }

    [Fact]
    public void My_own_offer_is_not_incoming_to_me()
    {
        // مَن بادَرَ بِالعَرضِ يَراهُ في «عُروضي» لا في «وارِدَة إلَيَّ» —
        // وإلّا ظَهَرَ لَه مَرَّتَين.
        var d = Incoming();
        d.InitiatorId = Owner;
        Assert.False(DealsService.IsIncomingOfferFor(d, Owner));
    }

    [Fact]
    public void An_offer_on_someone_elses_listing_is_not_mine()
        => Assert.False(DealsService.IsIncomingOfferFor(Incoming(), Stranger));

    [Fact]
    public void A_deal_without_the_owner_ref_is_not_claimed_by_anyone()
    {
        // ‏`Refs` ناقِصَة ⇒ لا يُخمَّنُ المالِك. الغِيابُ يُرَدّ ولا
        // يُؤَوَّل، وإلّا رَأى مُستَخدِمٌ صَفقَةَ غَيرِه.
        var d = Incoming();
        d.Refs.Clear();
        Assert.False(DealsService.IsIncomingOfferFor(d, Owner));
    }

    [Fact]
    public void The_owner_ref_key_is_the_one_the_writer_uses()
    {
        // الكاتِبُ (‏`MarketplaceTemplateExtensions`) والقارِئُ يَجِبُ
        // أَن يَقرَآ نَفسَ الحَرف. تَثبيتُ الحَرفِ يَجعَلُ إعادَةَ
        // تَسمِيَتِه في طَرَفٍ واحِدٍ تُحمِر.
        Assert.Equal("listing_owner", DealsService.ListingOwnerRef);
    }
}
