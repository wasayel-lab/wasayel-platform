using ACommerce.Kit.Notifications;
using ACommerce.Platform.I18n;
using Marten;
using Microsoft.AspNetCore.SignalR;

namespace ACommerce.Templates.Customer.Marketplace.Services.Deals;

/// <summary>
/// <para><b>إشعاراتُ الصَفقَةِ عَلى الأُنبوبِ القائِم</b> — وَثيقَةُ <see cref="Notification"/>
/// ثُمَّ جَرَسُ <c>unread_changed</c> ثُمَّ الدَفعُ الفَوريُّ، بِحَرفِ ما تَفعَلُه نُقطَتا
/// <c>chats/{id}/send</c> و<c>offers/{id}/accept</c> (‏<c>offer_accepted</c>) في
/// <c>MarketplaceTemplateExtensions</c> (القاعِدَة ٨: لا أُنبوبَ رابِع).</para>
///
/// <para><b>المَقيسُ قَبلَه</b> (موجَزُ الفَجَوات P1-5؛ مُثَبَّتٌ في
/// <c>DealCashPathCharacterizationTests</c> ‏5554975): <c>/checkout/submit</c> يُنشِئُ
/// الصَفقَةَ ويَحذِفُ السَلَّةَ ويَحفَظُ — بِصِفرِ <c>Notification</c>/<c>NudgeAsync</c>/
/// <c>push.</c>؛ و<c>/accept</c> كَذلك؛ و<c>Notifications.razor</c> يَرسُمُ أَيقونَةً
/// لِنَوعَي <c>deal</c> و<c>deal_advanced</c> <b>بِصِفرِ مُنتِج</b>؛ وتَعليقُ <c>Deal.cs</c>
/// كانَ يَقول «إِشعار الأَطراف يَخرُج تِلقائيّاً» بِلا كود. فَالبائِعُ لا يَعلَمُ أَنّ طَلَباً
/// وَصَلَ إلّا إن فَتَحَ «صَفقات» بِنَفسِه، والمُشتَري لا يَعلَمُ أَنّ طَلَبَه قُبِل
/// (القاعِدَة ١٢).</para>
///
/// <para><b>ولِماذا خِدمَةٌ لا أَسطُرٌ في النُقطَة</b>: جِسمُ النُقطَةِ مُثَبَّتُ السَقف
/// (<c>EndpointBodyBleedTests</c>)، والتَركيبُ هُنا <b>دَوالُّ نَقِيَّةٌ</b>
/// (<see cref="NewDealFor"/>، <see cref="AcceptedFor"/>) تُقاسُ بِلا قاعِدَة؛ والأُنبوبُ
/// نَفسُه (<see cref="NotifyNewDealsAsync"/>، <see cref="NotifyAcceptedAsync"/>) يُقاسُ
/// حَيّاً عَلى Postgres. والنُصوصُ مِن القامُوس (القاعِدَة ١١) وتَحمِلُ عُنوانَ الإعلانِ
/// والمَبلَغ. ومالِكُ الإعلانِ يُقرَأُ مِن <c>Refs[listing_owner]</c> بِالدالَّةِ الواحِدَة
/// <see cref="DealAcceptAuthorization.OwnerIdOf"/> — فَصَفقَةٌ بِلا مالِكٍ لا تُشعِرُ أَحَداً.</para>
///
/// <para><b>والنَوعانِ هُما ما تَعرِفُه شاشَةُ الإشعاراتِ أَصلاً</b>: <c>deal</c> لِلطَلَبِ
/// الجَديد، و<c>deal_advanced</c> لِقَبولِه — فَالأَيقونَةُ اليَتيمَةُ صارَ لَها مُنتِج. <b>وثالِثٌ
/// ‏2026-10-11</b>: <c>deal_cancelled</c> حينَ يُلغي غَيرُ المُشتَري طَلَبَه (نُقطَةُ <c>/cancel</c>)،
/// أُضيفَ إلى مِفتاحِ الأَيقونَةِ نَفسِه في الشاشَة.</para>
/// </summary>
public sealed class DealNotices
{
    /// <summary>طَلَبٌ جَديدٌ وَصَلَ مالِكَ الإعلان.</summary>
    public const string NewDealType = "deal";

    /// <summary>قَبِلَ البائِعُ طَلَبَ المُشتَري.</summary>
    public const string AcceptedType = "deal_advanced";

    /// <summary>أَلغى غَيرُ المُشتَري طَلَبَه — البائِعُ المَربوطُ يَرفُضُه عِندَ <c>Offered</c> أَو
    /// يُلغيه بَعدَها (‏ADR-033 §٦‑ج مُلحَقاً، ‏2026-10-11). نَوعٌ ثالِثٌ تَرسُمُه شاشَةُ الإشعاراتِ
    /// بِأَيقونَةِ النَوعَينِ الأَوَّلَين.</summary>
    public const string CancelledType = "deal_cancelled";

    /// <summary>دَفَعَ المُشتَري ثَمَنَ الطَلَبِ عِندَ بَوّابَةِ المَتجَرِ وثَبَتَ الدَفعُ مِن الخادِم (‏ADR-032 الخُطوَة 10، تَيّارُ S9، ‏2026-10-12)
    /// — نَوعٌ رابِعٌ تَرسُمُه شاشَةُ الإشعاراتِ بِأَيقونَةِ الثَلاثَةِ الأُولى.</summary>
    public const string PaidType = "deal_paid";

    private readonly IDocumentStore _store;
    private readonly IHubContext<ACommerce.Kit.Realtime.Server.RealtimeHub> _hub;
    private readonly WebPushService _push;

    public DealNotices(
        IDocumentStore store,
        IHubContext<ACommerce.Kit.Realtime.Server.RealtimeHub> hub,
        WebPushService push)
    {
        _store = store;
        _hub = hub;
        _push = push;
    }

    // ─── التَركيب — نَقِيٌّ ───────────────────────────────────────────────

    /// <summary>إشعارُ مالِكِ الإعلانِ بِطَلَبٍ جَديد — عُنوانُ الإعلانِ والمَبلَغُ في المَتن.</summary>
    public static Notification NewDealFor(string slug, Deal deal, Guid ownerId, L l, DateTime at) => new()
    {
        Id = Guid.NewGuid(),
        UserId = ownerId,
        Type = NewDealType,
        Title = l["notifications.deal_new.title"],
        Body = l.Format("notifications.deal_new.body", deal.ListingTitle, deal.AmountSar),
        RelatedUrl = $"/{slug}/deals/{deal.Id}",
        At = at,
    };

    /// <summary>مَن يُشعَرُ بِالطَلَبِ الجَديد: مالِكُ الإعلانِ — <b>ما لَم يَكُن هو المُشتَري</b> (‏2026-10-11،
    /// مُراجَعَةُ الموجَةِ الثانِيَةِ الرابِعَة): نَصُّه «افتَح الصَّفقَة لِقَبولِه» والقَبولُ يَرُدُّه بِـ
    /// <see cref="DealAcceptAuthorization.ActorIsInitiator"/> — بابٌ لا يُفتَحُ لَه. و<c>null</c> لِصَفقَةٍ بِلا مالِك.</summary>
    public static Guid? NewDealRecipient(Deal deal)
        => DealAcceptAuthorization.OwnerIdOf(deal) is { } owner && owner != deal.InitiatorId ? owner : null;

    /// <summary>مَتنُ الدَفعِ الفَوريِّ لِلطَلَبِ الجَديد — أَقصَرُ مِن مَتنِ الوَثيقَة.</summary>
    public static string NewDealPushBody(Deal deal, L l)
        => l.Format("notifications.deal_new.push_body", deal.ListingTitle, deal.AmountSar);

    /// <summary>إشعارُ المُشتَري (المُبادِر) بِقَبولِ البائِعِ — عَلى نَسَقِ <c>offer_accepted</c>.</summary>
    public static Notification AcceptedFor(string slug, Deal deal, string acceptorName, L l, DateTime at) => new()
    {
        Id = Guid.NewGuid(),
        UserId = deal.InitiatorId,
        Type = AcceptedType,
        Title = l["notifications.deal_accepted.title"],
        Body = l.Format("notifications.deal_accepted.body", acceptorName, deal.ListingTitle),
        RelatedUrl = $"/{slug}/deals/{deal.Id}",
        At = at,
    };

    public static string AcceptedPushBody(Deal deal, string acceptorName, L l)
        => l.Format("notifications.deal_accepted.push_body", acceptorName, deal.ListingTitle);

    /// <summary>إشعارُ المُشتَري (المُبادِر) بِأَنّ غَيرَه أَلغى طَلَبَه — بِاسمِ المُلغي وعُنوانِ الإعلان.</summary>
    public static Notification CancelledFor(string slug, Deal deal, string cancellerName, L l, DateTime at) => new()
    {
        Id = Guid.NewGuid(),
        UserId = deal.InitiatorId,
        Type = CancelledType,
        Title = l["notifications.deal_cancelled.title"],
        Body = l.Format("notifications.deal_cancelled.body", cancellerName, deal.ListingTitle),
        RelatedUrl = $"/{slug}/deals/{deal.Id}",
        At = at,
    };

    public static string CancelledPushBody(Deal deal, string cancellerName, L l)
        => l.Format("notifications.deal_cancelled.push_body", cancellerName, deal.ListingTitle);

    /// <summary>
    /// <para><b>إشعارُ المُشتَري بِأَنّ إدارَةَ المَتجَرِ أَلغَت طَلَبَه — مِن الاستوديو</b> (‏2026-10-13، مُراجَعَةُ المَوجَة 3a — G4 (iii)؛
    /// ‏ADR-036 §١٢‑٣). نَوعُه نَوعُ الإلغاءِ وأَيقونَتُه، ونَصُّه غَيرُ نَصِّ <see cref="CancelledFor"/>: ذاكَ «أَلغى البائِعُ طَلَبَكَ» بِاسمِ المُلغي،
    /// ومالِكُ المَتجَرِ في الاستوديو لَيسَ بائِعَ الطَلَبِ في مَتجَرٍ مُتَعَدِّدِ البائِعين — ولا يُكتَبُ اسمُه الشَخصِيُّ في إشعارِ مُشتَرٍ لا يَعرِفُه.</para>
    /// </summary>
    public static Notification CancelledByStoreFor(string slug, Deal deal, L l, DateTime at) => new()
    {
        Id = Guid.NewGuid(),
        UserId = deal.InitiatorId,
        Type = CancelledType,
        Title = l["notifications.deal_cancelled_by_store.title"],
        Body = l.Format("notifications.deal_cancelled_by_store.body", deal.ListingTitle),
        RelatedUrl = $"/{slug}/deals/{deal.Id}",
        At = at,
    };

    public static string CancelledByStorePushBody(Deal deal, L l)
        => l.Format("notifications.deal_cancelled_by_store.push_body", deal.ListingTitle);

    /// <summary>
    /// <para><b>إشعارُ المُشتَري بِأَنّ مُهلَةَ دَفعِ طَلَبِه انتَهَت فَأُلغِيَ</b> (‏2026-10-29، قَرارُ المالِك د5 (٤): «ويُخبَر المشتري وكل بائع فيه»): نَوعُه نَوعُ الإلغاء،
    /// ونَصُّه لا يُسَمّي مُلغياً — المَنصَّةُ أَلغَتهُ بِالمُهلَة — ويَقولُ المُدَّةَ مِن ثابِتِها (<see cref="GatewayPayments.InvoiceLifetime"/>، القاعِدَة ١٦) وأَن لا خَصم.</para>
    /// </summary>
    public static Notification PaymentExpiredFor(string slug, Deal deal, L l, DateTime at) => new()
    {
        Id = Guid.NewGuid(),
        UserId = deal.InitiatorId,
        Type = CancelledType,
        Title = l["notifications.deal_payment_expired.title"],
        Body = l.Format("notifications.deal_payment_expired.body", deal.ListingTitle, (int)GatewayPayments.InvoiceLifetime.TotalHours),
        RelatedUrl = $"/{slug}/deals/{deal.Id}",
        At = at,
    };

    public static string PaymentExpiredPushBody(Deal deal, L l)
        => l.Format("notifications.deal_payment_expired.push_body", deal.ListingTitle);

    /// <summary>
    /// <para><b>إشعارُ المُشتَري بِأَنّ إدارَةَ المَتجَرِ قَبِلَت طَلَبَه</b> (‏2026-10-24، تَدقيقُ الإطلاق brief-P1a-1): المَسارُ الإداريُّ في الاستوديو يُخرِجُ
    /// <c>Offered</c> إلى <c>Booked</c> بِهُوِيَّةِ الطَرَف — قَبولٌ بِمِعيارِ <see cref="IsAcceptance"/> — وكانَ صامِتاً (تَوصيفُه <c>727bc9f</c>). نَوعُه نَوعُ القَبولِ
    /// وأَيقونَتُه، ونَصُّه نَسَقُ <see cref="CancelledByStoreFor"/>: «إدارَةُ المَتجَر» لا اسمٌ شَخصِيٌّ لِمالِكٍ لا يَعرِفُه المُشتَري.</para>
    /// </summary>
    public static Notification AcceptedByStoreFor(string slug, Deal deal, L l, DateTime at) => new()
    {
        Id = Guid.NewGuid(),
        UserId = deal.InitiatorId,
        Type = AcceptedType,
        Title = l["notifications.deal_accepted_by_store.title"],
        Body = l.Format("notifications.deal_accepted_by_store.body", deal.ListingTitle),
        RelatedUrl = $"/{slug}/deals/{deal.Id}",
        At = at,
    };

    public static string AcceptedByStorePushBody(Deal deal, L l)
        => l.Format("notifications.deal_accepted_by_store.push_body", deal.ListingTitle);

    /// <summary>
    /// <para><b>أَقَبِلَ هذا التَقَدُّمُ الطَلَب؟</b> — نَجَحَ، وآخِرُ سَطرٍ في سِجِلِّ الصَفقَةِ الَّتي أَعادَها انتِقالٌ مِن <c>Offered</c> (‏2026-10-13،
    /// مُراجَعَةُ المَوجَة 3a — G4 (iii)). بابُ التَقَدُّمِ العامُّ يُخرِجُ <c>Offered</c> إلى <c>Booked</c> بِفاعِلِها — الطَرَفِ المُقابِل — فَهو قَبولٌ
    /// بِغَيرِ <c>/accept</c> (‏ADR-033 §٦‑ط‑٤: «يُعادِلُ القَبولَ بِلا إشعارِ المُشتَري»)؛ ومُستَهلِكُه نُقطَةُ التَقَدُّمِ في الـAPI. دالَّةٌ نَقِيَّة.</para>
    /// </summary>
    public static bool IsAcceptance(DealAdvanceResult res)
        => res.Ok && res.Deal is { Timeline.Count: > 0 } d
           && d.Timeline[^1] is { Action: "advanced", StageBefore: DealStage.Offered };

    /// <summary>إشعارُ مالِكِ الإعلانِ بِأَنّ ثَمَنَ الطَلَبِ دُفِعَ عِندَ بَوّابَةِ المَتجَر — عُنوانُ الإعلانِ والمَبلَغُ في المَتن.
    /// والمُستَلِمُ مَن يُستَلِمُ الطَلَبَ الجَديدَ نَفسُه (<see cref="NewDealRecipient"/>).</summary>
    public static Notification PaidFor(string slug, Deal deal, Guid ownerId, L l, DateTime at) => new()
    {
        Id = Guid.NewGuid(),
        UserId = ownerId,
        Type = PaidType,
        Title = l["notifications.deal_paid.title"],
        Body = l.Format("notifications.deal_paid.body", deal.ListingTitle, deal.AmountSar),
        RelatedUrl = $"/{slug}/deals/{deal.Id}",
        At = at,
    };

    public static string PaidPushBody(Deal deal, L l)
        => l.Format("notifications.deal_paid.push_body", deal.ListingTitle, deal.AmountSar);

    /// <summary>
    /// <para><b>مَن يُشعَرُ بِالدَفع</b>: مُستَلِمُ الطَلَبِ الجَديدِ نَفسُه (<see cref="NewDealRecipient"/>) — <b>ما دامَت الصَفقَةُ قائِمَة</b>
    /// (‏2026-10-12، مُراجَعَةُ تَيّارِ S9 الأولى). نَصُّه «دَفَعَ المُشتَري ثَمَنَ طَلَبِه ✓ … افتَحِ الصَّفقَة لِمُتابَعَتِها»، وصَفقَةٌ مُلغاةٌ أَو في
    /// نِزاعٍ لا يُتابِعُها بائِعُها — وكانَ دَفعٌ وَقَعَ عَلى طَلَبٍ أَلغاه البائِعُ نَفسُه يُبَشَّرُ بِه بِعَلامَةِ ✓. ودَفعُ المُلغاةِ مَكتوبٌ عَلَيها
    /// بِسَطرِ «استِرجاعُه مُستَحَقٌّ عِندَ بَوّابَةِ المَتجَر» يَراهُ صاحِبُ المَتجَرِ في سِجِلِّها. و<c>null</c> لِما سِوى ذلك.</para>
    /// </summary>
    public static Guid? PaidRecipient(Deal deal) => deal.Status == DealStatus.Active ? NewDealRecipient(deal) : null;

    /// <summary>مَن يُشعَرُ بِالإلغاء: المُشتَري، ما لَم يَكُن هُوَ المُلغي — فَمَن يُلغي طَلَبَه لا
    /// يُخبَرُ بِما فَعَلَه.</summary>
    public static bool TellsTheBuyer(Deal deal, Guid cancellerId) => cancellerId != deal.InitiatorId;

    /// <summary>
    /// <para><b>مَن يُخبَرُ مِن جِهَةِ البائِعِ بِالإلغاء</b> (‏2026-10-29، قَرارُ المالِك د8 «أ»: «إشعارٌ واحِدٌ لِلبائِعِ حينَ يُلغى طَلَبٌ عِندَه، أَيّاً كانَ
    /// مَن أَلغاه وأَيّاً كانَ المَسار، ما لَم يَكُن هُوَ المُلغي»): مُستَلِمُ الطَلَبِ الجَديدِ نَفسُه (<see cref="NewDealRecipient"/>) — مالِكُ الإعلانِ ما لَم
    /// يَكُن المُشتَري — <b>ما لَم يَكُن هُوَ المُلغي</b>. و<c>null</c> لِما سِوى ذلك: صَفقَةٌ بِلا مالِك (بَذرَةُ الاستوديو)، أَو بائِعٌ أَلغى بِنَفسِه.
    /// كانَ كُلُّ إشعارِ إلغاءٍ يُخاطِبُ المُشتَري وَحدَه (تَوصيفُه <c>05e2e13</c>)، فَيُجَهِّزُ البائِعُ طَلَباً أَلغاه المُشتَري أَو إدارَةُ المَتجَر.</para>
    /// </summary>
    public static Guid? VendorToTell(Deal deal, Guid cancellerId)
        => NewDealRecipient(deal) is { } vendor && vendor != cancellerId ? vendor : null;

    /// <summary>إشعارُ البائِعِ بِأَنّ طَلَباً عِندَه أُلغِيَ — بِنَوعِ الإلغاءِ وأَيقونَتِه، وعُنوانُ الإعلانِ في المَتن، ولا اسمَ لِلمُلغي: النَصُّ واحِدٌ لِكُلِّ
    /// مَسارٍ (المُشتَري، والاستوديو، ومِفتاحُ الربط) فَلا يُكتَبُ اسمٌ شَخصِيٌّ لِمالِكِ مَتجَرٍ في إشعارِ بائِع.</summary>
    public static Notification CancelledToVendorFor(string slug, Deal deal, Guid vendorId, L l, DateTime at) => new()
    {
        Id = Guid.NewGuid(),
        UserId = vendorId,
        Type = CancelledType,
        Title = l["notifications.deal_cancelled_for_vendor.title"],
        Body = l.Format("notifications.deal_cancelled_for_vendor.body", deal.ListingTitle),
        RelatedUrl = $"/{slug}/deals/{deal.Id}",
        At = at,
    };

    public static string CancelledToVendorPushBody(Deal deal, L l)
        => l.Format("notifications.deal_cancelled_for_vendor.push_body", deal.ListingTitle);

    // ─── صاحِبُ المَتجَر — ما عَلَيه أَن يَفعَلَه في لَوحَةِ ميسر (‏2026-10-29، قَرارُ المالِك د3 «د») ───────────────

    /// <summary>ما يُخبَرُ بِه صاحِبُ المَتجَرِ عَن صَفقَة — مَعجَمٌ مُغلَق: استِرجاعٌ مُستَحَقٌّ، أَو فاتورَةٌ تَعَذَّرَ إلغاؤها.</summary>
    public enum OwnerAction { RefundDue, InvoiceLeftOpen }

    /// <summary>
    /// <para><b>ما عَلى صاحِبِ المَتجَرِ أَن يَفعَلَه في هذِه الصَفقَة الآن</b> — آخِرُ سَطرٍ مِن السَطرَينِ في سِجِلِّها: <see cref="DealEvent.GatewayRefundDue"/> لِصَفقَةٍ مُلغاة
    /// (المالُ عِندَ المَتجَرِ ويُستَرجَعُ مِن لَوحَةِ ميسر)، أَو <see cref="DealEvent.GatewayInvoiceLeftOpen"/> (فاتورَةٌ قَد تُدفَعُ فَتُلغى مِن اللَوحَة). والآخِرُ لا الأَوَّل:
    /// فاتورَةٌ تَعَذَّرَ إلغاؤها ثُمَّ دَفَعَها المُشتَري صارَ عَلَيها استِرجاع. و<c>null</c> لِما سِواهُما. دالَّةٌ نَقِيَّة.</para>
    /// </summary>
    public static OwnerAction? OwnerActionOf(Deal deal)
    {
        for (var i = deal.Timeline.Count - 1; i >= 0; i--)
        {
            if (deal.Timeline[i].Action == DealEvent.GatewayRefundDue && deal.Status == DealStatus.Cancelled) return OwnerAction.RefundDue;
            if (deal.Timeline[i].Action == DealEvent.GatewayInvoiceLeftOpen) return OwnerAction.InvoiceLeftOpen;
        }
        return null;
    }

    /// <summary>مَن يُخبَرُ مِن جِهَةِ المَتجَر: صاحِبُه <c>Tenant.OwnerUserId</c> — مَن رَبَطَ مِفتاحَ ميسر في الاستوديو ولِلوَحَتِه يُحالُ الفِعل — و<c>null</c> لِمَتجَرٍ
    /// بِلا مالِكٍ مُعَيَّن.</summary>
    public static Guid? StoreOwnerOf(ACommerce.Kit.Tenants.Tenant? tenant)
        => tenant is { OwnerUserId: var owner } && owner != Guid.Empty ? owner : null;

    /// <summary>إشعارُ صاحِبِ المَتجَرِ بِاستِرجاعٍ مُستَحَقّ — بِنَصِّ القَرارِ بِحَرفِه: «استرجاعٌ مستحقّ: أُلغي طلب على «{المنتج}» بعد دفع {المبلغ} {العملة} — استرجعه للمشتري
    /// من لوحة ميسر.» والمَبلَغُ مَبلَغُ الصَفقَةِ (سَطرُها في الفاتورَة) والعُملَةُ عُملَةُ الفاتورَةِ كَما ثَبَتَت (<see cref="DealsService.PaymentCurrencyRef"/>)، فَإن غابَت
    /// فَعُملَةُ إنشاءِ الفاتورَةِ نَفسُها (<see cref="ACommerce.Kit.Payments.MoyasarClient.Currency"/>). والرابِطُ صَفحَةُ الصَفقَةِ في الاستوديو.</summary>
    public static Notification RefundDueFor(string slug, Deal deal, Guid ownerId, L l, DateTime at) => new()
    {
        Id = Guid.NewGuid(),
        UserId = ownerId,
        Type = CancelledType,
        Title = l["notifications.deal_refund_due.title"],
        Body = l.Format("notifications.deal_refund_due.body", deal.ListingTitle, deal.AmountSar, CurrencyOf(deal)),
        RelatedUrl = $"/studio/apps/{slug}/deals/{deal.Id}",
        At = at,
    };

    /// <summary>إشعارُ صاحِبِ المَتجَرِ بِفاتورَةٍ تَعَذَّرَ إلغاؤها — بِنَصِّ القَرارِ بِحَرفِه: «تعذّر إلغاء فاتورة الطلب الملغى على «{المنتج}» عند ميسر — ألغِها من لوحة ميسر
    /// حتى لا تُدفع.»</summary>
    public static Notification InvoiceLeftOpenFor(string slug, Deal deal, Guid ownerId, L l, DateTime at) => new()
    {
        Id = Guid.NewGuid(),
        UserId = ownerId,
        Type = CancelledType,
        Title = l["notifications.deal_invoice_left_open.title"],
        Body = l.Format("notifications.deal_invoice_left_open.body", deal.ListingTitle),
        RelatedUrl = $"/studio/apps/{slug}/deals/{deal.Id}",
        At = at,
    };

    private static string CurrencyOf(Deal deal)
        => deal.Refs.TryGetValue(DealsService.PaymentCurrencyRef, out var c) && !string.IsNullOrWhiteSpace(c) ? c : ACommerce.Kit.Payments.MoyasarClient.Currency;

    // ─── الأُنبوب — تَخزينٌ ثُمَّ جَرَسٌ ثُمَّ دَفع ──────────────────────────

    /// <summary>بَعدَ حَفظِ <c>/checkout/submit</c>: إشعارٌ واحِدٌ لِمالِكِ إعلانِ كُلِّ
    /// صَفقَةٍ وُلِدَت. تُقرَأُ الصَفَقاتُ مِن المَخزَنِ (ما حُفِظَ لا ما في الذاكِرَة)، وصَفقَةٌ
    /// بِلا مالِكٍ — أَو مالِكُها مُشتَريها (<see cref="NewDealRecipient"/>) — تُتَجاوَز. تُرجِعُ عَدَدَ ما
    /// أُشعِر — فَلا يُقالُ «أُشعِروا» بِلا عَدّ.</summary>
    public async Task<int> NotifyNewDealsAsync(
        string slug, IReadOnlyList<Guid> dealIds, L l, CancellationToken ct = default)
    {
        var sent = new List<(Guid UserId, Guid DealId, Notification N, string PushBody)>();
        await using (var s = _store.LightweightSession(slug))
        {
            foreach (var id in dealIds)
            {
                var deal = await s.LoadAsync<Deal>(id, ct);
                if (deal is null) continue;
                var ownerId = NewDealRecipient(deal);
                if (ownerId is null) continue;
                var n = NewDealFor(slug, deal, ownerId.Value, l, DateTime.UtcNow);
                s.Store(n);
                sent.Add((ownerId.Value, deal.Id, n, NewDealPushBody(deal, l)));
            }
            if (sent.Count > 0) await s.SaveChangesAsync(ct);
        }

        foreach (var (userId, dealId, n, pushBody) in sent)
        {
            await MarketplaceTemplateExtensions.NudgeAsync(_hub, slug, userId);
            await _push.SendAsync(_store, slug, userId, n.Title, pushBody, url: n.RelatedUrl, tag: $"deal-{dealId}", ct: ct);
        }
        return sent.Count;
    }

    /// <summary>بَعدَ قَبولِ <c>/accept</c>: إشعارٌ لِلمُشتَري بِاسمِ القابِل.</summary>
    public async Task NotifyAcceptedAsync(
        string slug, Deal deal, string acceptorName, L l, CancellationToken ct = default)
    {
        var n = AcceptedFor(slug, deal, acceptorName, l, DateTime.UtcNow);
        await using (var s = _store.LightweightSession(slug))
        {
            s.Store(n);
            await s.SaveChangesAsync(ct);
        }
        await MarketplaceTemplateExtensions.NudgeAsync(_hub, slug, deal.InitiatorId);
        await _push.SendAsync(_store, slug, deal.InitiatorId, n.Title, AcceptedPushBody(deal, acceptorName, l),
            url: n.RelatedUrl, tag: $"deal-{deal.Id}", ct: ct);
    }

    /// <summary>بَعدَ إلغاءٍ ناجِحٍ في <c>/cancel</c>: إشعارٌ لِلمُشتَري بِاسمِ المُلغي إن لَم يَكُن هُوَ
    /// (<see cref="TellsTheBuyer"/>). كانَ الإلغاءُ صامِتاً: البائِعُ المَربوطُ عِندَ الوِلادَةِ يَرفُضُ
    /// الطَلَبَ عِندَ <c>Offered</c> والمُشتَري لا يَعلَمُ إلّا إن فَتَحَ صَفقَتَه. تُرجِعُ هَل أُشعِر.</summary>
    /// <remarks>(‏2026-10-29، قَرارُ المالِك د8 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه: <b>والبائِعُ يُخبَرُ قَبلَه</b> بِـ<see cref="NotifyVendorCancelledAsync"/> ما لَم يَكُن
    /// هُوَ المُلغي — فَإلغاءُ المُشتَري طَلَبَه، وقَد كانَ لا يُخبِرُ أَحَداً، يُخبِرُ بائِعَه. و«تُرجِعُ هَل أُشعِر» باقِيَةٌ لِلمُشتَري.)</remarks>
    public async Task<bool> NotifyCancelledAsync(
        string slug, Deal deal, Guid cancellerId, string cancellerName, L l, CancellationToken ct = default)
    {
        await NotifyVendorCancelledAsync(slug, deal, cancellerId, l, ct);
        await NotifyStoreOwnerAsync(slug, deal, l, ct);
        if (!TellsTheBuyer(deal, cancellerId)) return false;
        var n = CancelledFor(slug, deal, cancellerName, l, DateTime.UtcNow);
        await using (var s = _store.LightweightSession(slug))
        {
            s.Store(n);
            await s.SaveChangesAsync(ct);
        }
        await MarketplaceTemplateExtensions.NudgeAsync(_hub, slug, deal.InitiatorId);
        await _push.SendAsync(_store, slug, deal.InitiatorId, n.Title, CancelledPushBody(deal, cancellerName, l),
            url: n.RelatedUrl, tag: $"deal-{deal.Id}", ct: ct);
        return true;
    }

    /// <summary>بَعدَ إلغاءٍ ناجِحٍ مِن الاستوديو (<c>/studio/apps/{slug}/deals/{id}/cancel</c>): إشعارٌ لِلمُشتَري بِنَصِّ «إدارَةِ المَتجَر»
    /// (<see cref="CancelledByStoreFor"/>) إن لَم يَكُن مالِكُ المَتجَرِ هُوَ المُشتَري (<see cref="TellsTheBuyer"/>) — وكانَ هذا الإلغاءُ صامِتاً
    /// (تَوصيفُه <c>DealNoticeDoorsTests</c>). بِتَرتيبِ الأُنبوبِ نَفسِه. تُرجِعُ هَل أُشعِر. (‏2026-10-13، مُراجَعَةُ المَوجَة 3a — G4 (iii).)</summary>
    /// <remarks>(‏2026-10-29، قَرارُ المالِك د8 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه: والبائِعُ يُخبَرُ قَبلَه بِـ<see cref="NotifyVendorCancelledAsync"/> ما لَم يَكُن مالِكُ
    /// المَتجَرِ هُوَ البائِع.)</remarks>
    public async Task<bool> NotifyCancelledByStoreAsync(
        string slug, Deal deal, Guid ownerId, L l, CancellationToken ct = default)
    {
        await NotifyVendorCancelledAsync(slug, deal, ownerId, l, ct);
        await NotifyStoreOwnerAsync(slug, deal, l, ct);
        if (!TellsTheBuyer(deal, ownerId)) return false;
        var n = CancelledByStoreFor(slug, deal, l, DateTime.UtcNow);
        await using (var s = _store.LightweightSession(slug))
        {
            s.Store(n);
            await s.SaveChangesAsync(ct);
        }
        await MarketplaceTemplateExtensions.NudgeAsync(_hub, slug, deal.InitiatorId);
        await _push.SendAsync(_store, slug, deal.InitiatorId, n.Title, CancelledByStorePushBody(deal, l),
            url: n.RelatedUrl, tag: $"deal-{deal.Id}", ct: ct);
        return true;
    }

    /// <summary>بَعدَ قَبولٍ مِن المَسارِ الإداريِّ في الاستوديو (<c>/studio/apps/{slug}/deals/{id}/advance</c> حينَ <see cref="IsAcceptance"/>): إشعارٌ لِلمُشتَري
    /// بِنَصِّ «إدارَةِ المَتجَر» (<see cref="AcceptedByStoreFor"/>) إن لَم يَكُن مالِكُ المَتجَرِ هُوَ المُشتَري (<see cref="TellsTheBuyer"/>) — بِتَرتيبِ الأُنبوبِ
    /// نَفسِه. تُرجِعُ هَل أُشعِر. (‏2026-10-24، تَدقيقُ الإطلاق brief-P1a-1؛ نَسَقُ <see cref="NotifyCancelledByStoreAsync"/>.)</summary>
    public async Task<bool> NotifyAcceptedByStoreAsync(
        string slug, Deal deal, Guid ownerId, L l, CancellationToken ct = default)
    {
        if (!TellsTheBuyer(deal, ownerId)) return false;
        var n = AcceptedByStoreFor(slug, deal, l, DateTime.UtcNow);
        await using (var s = _store.LightweightSession(slug))
        {
            s.Store(n);
            await s.SaveChangesAsync(ct);
        }
        await MarketplaceTemplateExtensions.NudgeAsync(_hub, slug, deal.InitiatorId);
        await _push.SendAsync(_store, slug, deal.InitiatorId, n.Title, AcceptedByStorePushBody(deal, l),
            url: n.RelatedUrl, tag: $"deal-{deal.Id}", ct: ct);
        return true;
    }

    /// <summary>بَعدَ أَن يَثبُتَ دَفعُ فاتورَةِ البَوّابَةِ ويُكتَبَ (<c>/payments/verify</c>؛ ‏ADR-032 الخُطوَة 10): إشعارٌ واحِدٌ لِمالِكِ إعلانِ
    /// كُلِّ صَفقَةٍ كُتِبَ عَلَيها الدَفعُ <b>الآن</b> — فَالتَحَقُّقُ الثاني لا يُشعِرُ ثانِيَة. بِتَرتيبِ الأُنبوبِ نَفسِه، وصَفقَةٌ بِلا مالِكٍ —
    /// أَو مالِكُها مُشتَريها — تُتَجاوَز. تُرجِعُ عَدَدَ ما أُشعِر.
    /// (‏2026-10-12، مُراجَعَةُ تَيّارِ S9 الأولى — ولا يُعادُ حَرفٌ فَوقَه: والمُستَلِمُ <see cref="PaidRecipient"/> — صَفقَةٌ لَم تَعُد قائِمَةً
    /// تُتَجاوَزُ أَيضاً، ودَفعُها مَكتوبٌ عَلَيها.)</summary>
    public async Task<int> NotifyPaidAsync(
        string slug, IReadOnlyList<Guid> dealIds, L l, CancellationToken ct = default)
    {
        if (dealIds.Count == 0) return 0;
        var owed = new List<Deal>();
        var sent = new List<(Guid UserId, Guid DealId, Notification N, string PushBody)>();
        await using (var s = _store.LightweightSession(slug))
        {
            foreach (var id in dealIds)
            {
                var deal = await s.LoadAsync<Deal>(id, ct);
                // (‏2026-10-29، قَرارُ المالِك د3 — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق: ودَفعٌ ثَبَتَ عَلى صَفقَةٍ مُلغاةٍ يُخبَرُ بِه صاحِبُ المَتجَرِ «استِرجاعٌ مُستَحَقّ».)
                if (deal is not null && OwnerActionOf(deal) == OwnerAction.RefundDue) owed.Add(deal);
                if (deal is null || PaidRecipient(deal) is not { } ownerId) continue;
                var n = PaidFor(slug, deal, ownerId, l, DateTime.UtcNow);
                s.Store(n);
                sent.Add((ownerId, deal.Id, n, PaidPushBody(deal, l)));
            }
            if (sent.Count > 0) await s.SaveChangesAsync(ct);
        }

        foreach (var (userId, dealId, n, pushBody) in sent)
        {
            await MarketplaceTemplateExtensions.NudgeAsync(_hub, slug, userId);
            await _push.SendAsync(_store, slug, userId, n.Title, pushBody, url: n.RelatedUrl, tag: $"deal-{dealId}", ct: ct);
        }
        foreach (var deal in owed) await NotifyStoreOwnerAsync(slug, deal, l, ct);
        return sent.Count;
    }

    /// <summary>
    /// <para><b>بَعدَ أَن تُلغِيَ المَنصَّةُ طَلَباً انتَهَت مُهلَةُ دَفعِه</b> (<c>/payments/verify</c>؛ ‏2026-10-29، قَرارُ المالِك د5 (٤) «ب»: «ويُخبَر المشتري وكل بائع فيه»):
    /// لِكُلِّ صَفقَةٍ أَلغاها التَحَقُّقُ <b>الآن</b> (<see cref="GatewayVerifyResult.Expired"/>) — بائِعُها بِالإشعارِ الواحِد (<see cref="NotifyVendorCancelledAsync"/>، والمُلغي
    /// المَنصَّةُ لا هُوَ)، وصاحِبُ المَتجَرِ إن بَقِيَت الفاتورَةُ مَفتوحَة (<see cref="NotifyStoreOwnerAsync"/>)، ثُمَّ المُشتَري بِـ<see cref="PaymentExpiredFor"/> — بِتَرتيبِ الأُنبوبِ
    /// نَفسِه. صَفقَةٌ لا تَزالُ قائِمَةً أَو لا توجَدُ تُتَجاوَز. تُرجِعُ عَدَدَ ما أُشعِرَ بِه المُشتَري.</para>
    /// </summary>
    public async Task<int> NotifyPaymentExpiredAsync(
        string slug, IReadOnlyList<Guid> dealIds, L l, CancellationToken ct = default)
    {
        var told = 0;
        foreach (var id in dealIds)
        {
            Deal? deal;
            await using (var r = _store.LightweightSession(slug)) deal = await r.LoadAsync<Deal>(id, ct);
            if (deal is not { Status: DealStatus.Cancelled }) continue;
            await NotifyVendorCancelledAsync(slug, deal, GatewayPayments.ExpiryCanceller.ActorId, l, ct);
            await NotifyStoreOwnerAsync(slug, deal, l, ct);
            var n = PaymentExpiredFor(slug, deal, l, DateTime.UtcNow);
            await using (var s = _store.LightweightSession(slug))
            {
                s.Store(n);
                await s.SaveChangesAsync(ct);
            }
            await MarketplaceTemplateExtensions.NudgeAsync(_hub, slug, deal.InitiatorId);
            await _push.SendAsync(_store, slug, deal.InitiatorId, n.Title, PaymentExpiredPushBody(deal, l),
                url: n.RelatedUrl, tag: $"deal-{deal.Id}", ct: ct);
            told++;
        }
        return told;
    }

    /// <summary>
    /// <para><b>إشعارُ إلغاءٍ واحِدٌ لِلبائِع — تَمُرُّ بِه مَساراتُ الإلغاءِ كُلُّها</b> (‏2026-10-29، قَرارُ المالِك د8 «أ»): يُناديه <see cref="NotifyCancelledAsync"/>
    /// (بابُ العُضوِ ومِفتاحُ الربط) و<see cref="NotifyCancelledByStoreAsync"/> (الاستوديو) قَبلَ إخبارِ المُشتَري، والإلغاءُ الآليُّ عِندَ انتِهاءِ الفاتورَةِ يَمُرُّ
    /// بِالأَوَّل. والمُستَلِمُ <see cref="VendorToTell"/>، وبِتَرتيبِ الأُنبوبِ نَفسِه. تُرجِعُ هَل أُشعِر.</para>
    /// <para><b>وما يُستَثنى بِقَصد</b>: طَلَبٌ لَم تُفتَح فاتورَتُه أَو تَعَذَّرَ حَجزُه عِندَ الشِراءِ لا يُخبَرُ بائِعُه بِإلغائِه — لَم يُخبَر بِوِلادَتِه أَصلاً
    /// (‏ADR-032 §٨‑ح‑١٠، و§٩ مُلحَقاً)؛ ذلكَ الإلغاءُ في <c>GatewayPayments</c> ولا يَمُرُّ بِهذِه الخِدمَة.</para>
    /// </summary>
    public async Task<bool> NotifyVendorCancelledAsync(
        string slug, Deal deal, Guid cancellerId, L l, CancellationToken ct = default)
    {
        if (VendorToTell(deal, cancellerId) is not { } vendorId) return false;
        var n = CancelledToVendorFor(slug, deal, vendorId, l, DateTime.UtcNow);
        await using (var s = _store.LightweightSession(slug))
        {
            s.Store(n);
            await s.SaveChangesAsync(ct);
        }
        await MarketplaceTemplateExtensions.NudgeAsync(_hub, slug, vendorId);
        await _push.SendAsync(_store, slug, vendorId, n.Title, CancelledToVendorPushBody(deal, l),
            url: n.RelatedUrl, tag: $"deal-{deal.Id}", ct: ct);
        return true;
    }

    /// <summary>
    /// <para><b>صاحِبُ المَتجَرِ يُخبَرُ بِما عَلَيه في لَوحَةِ ميسر</b> (‏2026-10-29، قَرارُ المالِك د3 «د»: «ومعه إشعار لصاحب المتجر بكل استرجاع مستحقّ وبكل فاتورة
    /// تعذّر إلغاؤها»؛ تَوصيفُ «اليَوم» <c>b55fead</c>): ما تُسَمّيه <see cref="OwnerActionOf"/> لِمَن تُسَمّيه <see cref="StoreOwnerOf"/>، بِتَرتيبِ الأُنبوبِ نَفسِه. يُناديه
    /// إشعارا الإلغاءِ (بَعدَ إلغاءٍ كَتَبَ أَحَدَ السَطرَين) و<see cref="NotifyPaidAsync"/> (دَفعٌ ثَبَتَ عَلى مُلغاة) — فَلا تَتَغَيَّرُ أَجسامُ النِقاط. ولا مالَ يَتَحَرَّك:
    /// الاستِرجاعُ يَدَوِيٌّ مِن اللَوحَة (د3 «أ»). تُرجِعُ هَل أُشعِر.</para>
    /// </summary>
    public async Task<bool> NotifyStoreOwnerAsync(string slug, Deal deal, L l, CancellationToken ct = default)
    {
        if (OwnerActionOf(deal) is not { } action) return false;
        Notification n;
        await using (var s = _store.LightweightSession(slug))
        {
            if (StoreOwnerOf(await s.LoadAsync<ACommerce.Kit.Tenants.Tenant>(slug, ct)) is not { } ownerId) return false;
            n = action == OwnerAction.RefundDue
                ? RefundDueFor(slug, deal, ownerId, l, DateTime.UtcNow)
                : InvoiceLeftOpenFor(slug, deal, ownerId, l, DateTime.UtcNow);
            s.Store(n);
            await s.SaveChangesAsync(ct);
        }
        await MarketplaceTemplateExtensions.NudgeAsync(_hub, slug, n.UserId);
        await _push.SendAsync(_store, slug, n.UserId, n.Title, n.Body, url: n.RelatedUrl, tag: $"deal-{deal.Id}", ct: ct);
        return true;
    }
}
