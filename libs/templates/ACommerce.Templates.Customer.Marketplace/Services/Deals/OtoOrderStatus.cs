using System.Text.Json;
using ACommerce.Kit.Delivery;

namespace ACommerce.Templates.Customer.Marketplace.Services.Deals;

/// <summary>خَرقٌ واحِدٌ في وارِدِ حالِ الطَلَب — نَفسُ شَكلِ <see cref="DealShipViolation"/> (القاعِدَة ٤).
/// و<c>MessageAr</c> لِلوغِ ولِلمُراجِعِ البَشَرِيّ: الرَدُّ آلَةٌ لِآلَةٍ ولا نَصَّ يَراه مُستَخدِم.</summary>
public sealed record OtoNoticeViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>إشعارُ <c>orderStatus</c> كَما وَصَل — إشارَةٌ لا بُرهان</b> (‏ADR-033 §٢‑هـ القاعِدَة ٦). يُقرَأُ مِنه
/// مُعَرِّفا الطَلَبِ وكَلِمَةُ الحالِ لِلوغِ وَحدَه؛ والقَرارُ عَلى ما تُجيبُ بِه OTO حينَ نَسأَلُها.</para>
///
/// <para><b>ولا يُبنى إلّا بِبَوّابَتِه</b>: المُنشِئُ داخِلِيٌّ و<see cref="OtoOrderStatusGate.Admit"/> بابُه
/// الوَحيد، و<see cref="DealShipping.ReceiveAsync"/> يَطلُبُه في تَوقيعِه — فَلا يُنادى الوارِدُ بِلا الحارِس
/// (القاعِدَة ٦)، عَلى نَسَقِ <see cref="DealShipper"/>.</para>
/// </summary>
public sealed class OtoOrderStatusNotice
{
    internal OtoOrderStatusNotice(string orderId, string? otoId, string? status)
    {
        OrderId = orderId;
        OtoId = otoId;
        Status = status;
    }

    /// <summary>مُعَرِّفُنا كَما أَرسَلناه في «Create Order» — مُعَرِّفُ الصَفقَةِ بِصيغَةِ <c>N</c>.</summary>
    public string OrderId { get; }

    /// <summary>مُعَرِّفُ OTO كَما في الإشعار، إن حَمَلَه.</summary>
    public string? OtoId { get; }

    /// <summary>ما قالَه الإشعار — لِلوغِ وَحدَه، ولا يُبنى عَلَيه شَيء.</summary>
    public string? Status { get; }

    /// <summary>الصَفقَةُ الَّتي يُشيرُ إلَيها — أَو <c>null</c> لِطَلَبٍ لَم نُنشِئه (حِسابُ البائِعِ عِندَ OTO
    /// يَحمِلُ طَلَباتِ قَنَواتِه الأُخرى، وإشعاراتُها تَصِلُ العُنوانَ نَفسَه).</summary>
    public Guid? DealId => Guid.TryParseExact(OrderId, "N", out var id) ? id : null;
}

/// <summary>ما خَرَجَ مِن البَوّابَة: إشعارٌ مَقبولٌ أَو خَرقٌ — واحِدٌ لا اثنان.</summary>
public sealed record OtoNoticeAdmission(OtoOrderStatusNotice? Notice, OtoNoticeViolation? Violation);

/// <summary>نَتيجَةُ الوارِد: رَمزٌ (مِن مَعجَمِ البَوّابَة، أَو <see cref="OtoOrderStatusGate.Delivered"/>، أَو
/// <see cref="DealsService.ChangedMeanwhile"/>) والصَفقَةُ كَما في المَخزَنِ بَعدَه، ورَمزُ فَشَلِ OTO حينَ لَم تُؤَكِّد.</summary>
public sealed record OtoNoticeOutcome(string Code, Deal? Deal, string? OtoFailure = null);

/// <summary>
/// <para><b>بَوّابَةُ وارِدِ حالِ الطَلَب — دَوالُّ نَقِيَّة.</b> ثَلاثُ مَراحِلَ حَولَ نِداءٍ واحِد: <see cref="Admit"/> عَلى
/// الإشعارِ الخامّ قَبلَ أَيِّ قِراءَة، و<see cref="Match"/> عَلى الصَفقَةِ المَخزونَة، ثُمَّ — <b>بَعدَ أَن تُسأَلَ OTO
/// عَن الطَلَبِ بِرَمزِ البائِع</b> — <see cref="Decide"/> عَلى ما أَجابَت. ولا كِتابَةَ قَبلَ الثالِثَة.</para>
///
/// <para><b>وما يُكتَبُ انتِقالٌ واحِد</b>: <c>Shipping → Delivered</c> عَبرَ <see cref="DealsService.AdvanceAsync"/> بِالطَرَفِ
/// المُقابِلِ (السِياسَةُ تُقَرِّر: فاعِلُ <c>Delivered</c> = <c>counterparty</c>)، ورابِطا البوليصَةِ والتَتَبُّعِ حينَ
/// يَتَغَيَّران. وكُلُّ رَمزٍ غَيرِ ذلك لا يَكتُب.</para>
///
/// <para>(<b>ومَعَه انتِقالٌ ثانٍ — يُلحَقُ ‏2026-10-12، مُراجَعَةُ المَوجَة 3a، تَيّارُ S8؛ ‏ADR-033 §٧‑ط؛ ولا يُعادُ حَرفٌ فَوقَه</b>:
/// <c>Confirmed → Shipping</c> لِصَفقَةٍ طَلَبُها عِندَ OTO حينَ يُثبِتُ الجَوابُ المَجلوبُ شُحنَتَها وبوليصَتَها (<see cref="ShipsFirst"/>) — بِالبائِعِ
/// طَرَفاً مُقابِلاً وبِالجَوابِ مَحمولاً إلى <see cref="DealsService.AdvanceAsync"/>، ثُمَّ يُسأَلُ التَسليمُ عَلى ما كُتِب. ونَتيجَتُه
/// <see cref="Shipped"/> حينَ لا تَسليمَ بَعدُ.)</para>
/// </summary>
public static class OtoOrderStatusGate
{
    // ─── المَعجَمُ المُغلَق — عَشَرَة ─────────────────────────────────────

    /// <summary>لا رَمزَ ناقِلٍ لِهذا المَتجَر — فَلا وارِدَ لَه: ‏404 فارِغ، ولا يُقرَأُ الإشعار. القيمَةُ قيمَةُ بَوّابَةِ الشَحن.</summary>
    public const string CarrierUnavailable = DealShipAuthorization.CarrierUnavailable;

    /// <summary>جِسمٌ لا يُقرَأ، أَو بِلا <c>orderId</c>.</summary>
    public const string NoticeUnreadable = "notice_unreadable";

    /// <summary>لا صَفقَةَ في هذا المَتجَرِ طَلَبُها هذا المُعَرِّف — طَلَبٌ لَم نُنشِئه، أَو صَفقَةٌ بِلا طَلَب.</summary>
    public const string OrderUnknown = "order_unknown";

    /// <summary>مُعَرِّفُ OTO في الإشعارِ أَو في جَوابِ OTO غَيرُ المَكتوبِ في الصَفقَة.</summary>
    public const string OrderMismatch = "order_mismatch";

    /// <summary>لَم تُجِب OTO عَن الطَلَب — فَلا كِتابَة، و‏503 لِتُعيدَ الإرسال.</summary>
    public const string StateUnconfirmed = "state_unconfirmed";

    /// <summary>الحالُ عِندَ OTO لَيسَ <c>delivered</c> — يُكتَبُ الرابِطانِ إن تَغَيَّرا ولا انتِقال.</summary>
    public const string NotDelivered = "not_delivered";

    /// <summary>بَلَغَت الصَفقَةُ <c>Delivered</c> أَو بَعدَها — الإشعارُ المُكَرَّرُ بِلا أَثَر.</summary>
    public const string AlreadyDelivered = "already_delivered";

    /// <summary>الصَفقَةُ غَيرُ فَعّالَة (أُلغِيَت أَو في نِزاع). القيمَةُ قيمَةُ بَوّابَةِ القَبول.</summary>
    public const string DealNotActive = DealAcceptAuthorization.DealNotActive;

    /// <summary>الصَفقَةُ لَيسَت عِندَ <c>Shipping</c> — والانتِقالُ المَكتوبُ هُنا مِنها وَحدَها.</summary>
    public const string DealNotShipping = "deal_not_shipping";

    /// <summary>المُحَرِّكُ لا يُخرِجُها بِطَرَفِها المُقابِل — فَلا يُكتَبُ انتِقالٌ سَيُرَدّ.</summary>
    public const string ActorCannotDeliver = "actor_cannot_deliver";

    public static readonly IReadOnlyList<string> All = new[]
    {
        CarrierUnavailable, NoticeUnreadable, OrderUnknown, OrderMismatch, StateUnconfirmed,
        NotDelivered, AlreadyDelivered, DealNotActive, DealNotShipping, ActorCannotDeliver,
    };

    /// <summary>النَتيجَةُ الَّتي تَكتُب: خَرَجَت الصَفقَةُ إلى <c>Delivered</c>. لَيسَت خَرقاً فَلَيسَت في <see cref="All"/>.</summary>
    public const string Delivered = "delivered";

    /// <summary>
    /// <para><b>النَتيجَةُ الثانِيَةُ الَّتي تَكتُب: دَخَلَت الصَفقَةُ «شَحن» بِجَوابٍ أَثبَتَت فيه OTO شُحنَتَها وبوليصَتَها، ولَم تُسَلَّم بَعد</b>
    /// (مُراجَعَةُ المَوجَة 3a، تَيّارُ S8، ‏2026-10-12؛ ‏ADR-033 §٧‑ط). لَيسَت خَرقاً فَلَيسَت في <see cref="All"/>، وجَوابُها ‏200.</para>
    /// </summary>
    public const string Shipped = "shipped";

    // ─── المَرحَلَةُ الأولى: الإشعارُ الخامّ ───────────────────────────────

    /// <summary>
    /// <para><b>قَبلَ أَيِّ قِراءَة</b>: مَتجَرٌ بِلا ناقِلٍ يُجابُ ولا يُقرَأُ جِسمُه؛ ثُمَّ جِسمٌ بِـ<c>orderId</c> — نَصّاً أَو
    /// رَقَماً — وإلّا فَلا يُقرَأ. وحُقولُ الإشعارِ في وَثيقَةِ OTO («Webhook»): ‏<c>orderId</c> و<c>otoId</c> و<c>status</c>.</para>
    /// </summary>
    public static OtoNoticeAdmission Admit(bool carrierReady, string? rawBody)
    {
        if (!carrierReady)
            return Refused(CarrierUnavailable, "لا رَمزَ ناقِلٍ لِهذا المَتجَر — لا وارِدَ لَه.");

        if (string.IsNullOrWhiteSpace(rawBody))
            return Refused(NoticeUnreadable, "إشعارٌ بِلا جِسم.");

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Text(root, "orderId") is not { } orderId)
                return Refused(NoticeUnreadable, "إشعارٌ بِلا orderId.");
            return new(new OtoOrderStatusNotice(orderId, Text(root, "otoId"), Text(root, "status")), null);
        }
        catch (JsonException)
        {
            return Refused(NoticeUnreadable, "جِسمٌ لَيسَ JSON.");
        }
    }

    // ─── المَرحَلَةُ الثانِيَة: الصَفقَةُ المَخزونَة ─────────────────────────────

    /// <summary>الإشعارُ لِطَلَبٍ أَنشَأناه لِهذِه الصَفقَة: <c>oto_order_id</c> = <c>orderId</c>، ومُعَرِّفُ OTO — إن حَمَلَه
    /// الطَرَفان — واحِد. <c>null</c> يَعني: تُسأَلُ OTO.</summary>
    public static OtoNoticeViolation? Match(OtoOrderStatusNotice notice, Deal? deal)
    {
        if (deal is null || !deal.Refs.TryGetValue(DealShipping.OrderRef, out var ours)
            || !string.Equals(ours, notice.OrderId, StringComparison.Ordinal))
            return new(OrderUnknown, "لا صَفقَةَ في هذا المَتجَرِ طَلَبُها هذا المُعَرِّف.");

        if (notice.OtoId is { } said && OtoIdOf(deal) is { } held && !string.Equals(said, held, StringComparison.Ordinal))
            return new(OrderMismatch, "مُعَرِّفُ OTO في الإشعارِ غَيرُ المَكتوبِ في الصَفقَة.");

        return null;
    }

    // ─── المَرحَلَةُ الثالِثَة: ما أَجابَت بِه OTO ─────────────────────────────

    /// <summary>
    /// <para><b>عَلى الحالِ المَجلوبِ لا عَلى الإشعار</b>، وعَلى الصَفقَةِ كَما في المَخزَنِ الآن — فَتُعادُ نَفسُها عَلى ما
    /// كُتِبَ بَعدَ تَعارُض. والتَرتيب: المُعَرِّفان، ثُمَّ «سُلِّمَ؟»، ثُمَّ «سَبَقَ؟» (فَالمُكَرَّرُ بِلا أَثَر)، ثُمَّ
    /// الحالَة، ثُمَّ المَرحَلَة، ثُمَّ المُحَرِّك (<see cref="DealsService.CanMove"/> بِالطَرَفِ المُقابِل). <c>null</c> يَعني: يُكتَب.</para>
    /// </summary>
    public static OtoNoticeViolation? Decide(Deal deal, OtoOrderState state)
    {
        if (!IdsAgree(deal, state))
            return new(OrderMismatch, "جَوابُ OTO لِطَلَبٍ غَيرِ المَكتوبِ في الصَفقَة.");

        if (!state.IsDelivered)
            return new(NotDelivered, $"الحالُ عِندَ OTO «{state.Status}» — لا انتِقال.");

        if (Reached(deal, DealStage.Delivered))
            return new(AlreadyDelivered, $"الصَفقَةُ عِندَ «{deal.Stage}» — سَبَقَ التَسليم.");

        if (deal.Status != DealStatus.Active)
            return new(DealNotActive, $"الصَفقَةُ في حالَة {deal.Status} — لا انتِقال.");

        if (deal.Stage != DealStage.Shipping || DealsPolicy.Next(deal) != DealStage.Delivered)
            return new(DealNotShipping, $"الصَفقَةُ عِندَ «{deal.Stage}» لا عِندَ الشَحن.");

        if (deal.CounterpartyId is not { } counterparty || !DealsService.CanMove(deal, counterparty))
            return new(ActorCannotDeliver, "المُحَرِّكُ لا يُخرِجُها إلى التَسليمِ بِطَرَفِها المُقابِل.");

        return null;
    }

    /// <summary>
    /// <para><b>أَيُدخِلُ هذا الجَوابُ الصَفقَةَ «شَحن» قَبلَ سُؤالِ التَسليم؟ — دالَّةٌ نَقِيَّة</b> (مُراجَعَةُ المَوجَة 3a، تَيّارُ S8، ‏2026-10-12؛
    /// ‏ADR-033 §٧‑ط). نَعَم حينَ يَصدُقُ كُلُّ ما يَصدُقُ عِندَ «أَكمِل الشَّحن» بِالجَوابِ نَفسِه: المُعَرِّفانِ لِطَلَبِ الصَفقَة، وهي فَعّالَة،
    /// وتاليها «شَحن» وطَلَبُها عِندَ OTO (<see cref="DealShipping.AwaitsCarrier"/>)، وOTO تُثبِتُ شُحنَتَها وبوليصَتَها
    /// (<see cref="DealShipping.ShipmentConfirmed"/>)، والمُحَرِّكُ يُخرِجُها بِطَرَفِها المُقابِل — البائِع (<see cref="DealsService.CanMove"/>).</para>
    ///
    /// <para><b>لِماذا</b>: بَعدَ <c>shipment_unconfirmed</c> تَبقى الصَفقَةُ عِندَ «تأكيد» بِطَلَبِها، فَإن أَنشَأَت OTO الشُحنَةَ وأَرسَلَت إشعاراتِها
    /// قَبلَ نَقرَةِ البائِعِ الثانِيَة كُتِبَت البوليصَةُ عَلى صَفقَةٍ عِندَ «تأكيد»، وأُجيبَ «delivered» بَعدَها ‏200 <see cref="DealNotShipping"/> فَلا
    /// تُعيدُه OTO — فَيَضيعُ التَسليمُ الآلِيُّ (تَوصيفُه <c>dace4bd</c>). والانتِقالُ نَفسُه يَمُرُّ بِـ<see cref="DealsService.AdvanceAsync"/> بِالجَوابِ
    /// مَحمولاً، فَتُعادُ الشُروطُ عَلى الصَفقَةِ الَّتي سَتُكتَب ولا يُصَدَّقُ المُنادي.</para>
    /// </summary>
    public static bool ShipsFirst(Deal deal, OtoOrderState state)
        => IdsAgree(deal, state)
           && deal.Status == DealStatus.Active
           && DealShipping.AwaitsCarrier(deal)
           && DealShipping.ShipmentConfirmed(deal, state)
           && deal.CounterpartyId is { } counterparty
           && DealsService.CanMove(deal, counterparty);

    /// <summary>جَوابُ OTO لِطَلَبِ هذِه الصَفقَة: <c>orderId</c> = <c>oto_order_id</c>، و<c>otoId</c> — إن كُتِبَ — واحِد.</summary>
    private static bool IdsAgree(Deal deal, OtoOrderState state)
        => string.Equals(state.OrderId, deal.Refs.GetValueOrDefault(DealShipping.OrderRef), StringComparison.Ordinal)
           && (OtoIdOf(deal) is not { } held || string.Equals(state.OtoId, held, StringComparison.Ordinal));

    /// <summary>
    /// <para><b>رَدُّ النُقطَة — دالَّةٌ نَقِيَّة.</b> ‏404 لِمَتجَرٍ بِلا ناقِل (كَأَن لا نُقطَة)، و‏400 لِجِسمٍ لا يُقرَأ، و‏503
    /// حَيثُ لَم يُكتَب ما كانَ يَنبَغي أَن يُكتَب (لَم تُجِب OTO، أَو غَلَبَت التَعارُضاتُ المُحاوَلات) فَتُعيدُ OTO الإرسال،
    /// و‏200 لِكُلِّ ما سِواه: كُتِبَ، أَو لا شَيءَ يُكتَبُ لِهذا الإشعارِ أَبَداً — وإعادَتُه لا تُغَيِّرُ شَيئاً.</para>
    /// </summary>
    public static int StatusFor(string code) => code switch
    {
        CarrierUnavailable => 404,
        NoticeUnreadable => 400,
        StateUnconfirmed or DealsService.ChangedMeanwhile => 503,
        Delivered or Shipped or OrderUnknown or OrderMismatch or NotDelivered or AlreadyDelivered
            or DealNotActive or DealNotShipping or ActorCannotDeliver => 200,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "رَمزٌ خارِجَ مَعجَمِ الوارِد."),
    };

    /// <summary>بَلَغَت الصَفقَةُ <paramref name="stage"/> أَو ما بَعدَها في تَرتيبِ مَراحِلِها — بِالحِملِ الواعي بِطَريقَةِ الدَفع
    /// (<c>DealsPolicy.StagesFor(deal)</c>، ‏ADR-033 §٦) كَما تَقرَؤُه الخِدمَةُ والشاشَةُ والـAPI.</summary>
    public static bool Reached(Deal deal, DealStage stage)
    {
        var order = DealsPolicy.StagesFor(deal);
        var target = IndexOf(order, stage);
        return target >= 0 && IndexOf(order, deal.Stage) >= target;
    }

    private static int IndexOf(IReadOnlyList<DealStage> order, DealStage stage)
    {
        for (var i = 0; i < order.Count; i++)
            if (order[i] == stage) return i;
        return -1;
    }

    private static string? OtoIdOf(Deal deal)
        => deal.Refs.TryGetValue(DealShipping.OtoIdRef, out var id) && !string.IsNullOrWhiteSpace(id) ? id : null;

    private static OtoNoticeAdmission Refused(string code, string messageAr) => new(null, new(code, messageAr));

    private static string? Text(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var v)) return null;
        var s = v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
