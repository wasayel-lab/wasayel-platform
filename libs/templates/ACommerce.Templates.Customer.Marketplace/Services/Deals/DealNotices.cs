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
/// الجَديد، و<c>deal_advanced</c> لِقَبولِه — فَالأَيقونَةُ اليَتيمَةُ صارَ لَها مُنتِج.</para>
/// </summary>
public sealed class DealNotices
{
    /// <summary>طَلَبٌ جَديدٌ وَصَلَ مالِكَ الإعلان.</summary>
    public const string NewDealType = "deal";

    /// <summary>قَبِلَ البائِعُ طَلَبَ المُشتَري.</summary>
    public const string AcceptedType = "deal_advanced";

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

    // ─── الأُنبوب — تَخزينٌ ثُمَّ جَرَسٌ ثُمَّ دَفع ──────────────────────────

    /// <summary>بَعدَ حَفظِ <c>/checkout/submit</c>: إشعارٌ واحِدٌ لِمالِكِ إعلانِ كُلِّ
    /// صَفقَةٍ وُلِدَت. تُقرَأُ الصَفَقاتُ مِن المَخزَنِ (ما حُفِظَ لا ما في الذاكِرَة)، وصَفقَةٌ
    /// بِلا مالِكٍ تُتَجاوَز. تُرجِعُ عَدَدَ ما أُشعِر — فَلا يُقالُ «أُشعِروا» بِلا عَدّ.</summary>
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
                var ownerId = DealAcceptAuthorization.OwnerIdOf(deal);
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
}
