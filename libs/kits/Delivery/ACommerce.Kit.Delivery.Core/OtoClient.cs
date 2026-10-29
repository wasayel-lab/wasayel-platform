using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ACommerce.Kit.Delivery;

/// <summary>
/// <para><b>عَميلُ OTO — صِنفُ <c>HttpClient</c> واحِدٌ بِلا واجِهَة</b> (‏ADR-033 §٢‑هـ
/// القاعِدَة ١ والخُطوَة 3). يَعرِفُ ثَلاثَ عَمَلِيّاتٍ ورَمزَها: فَحصُ سِعرِ الشَحن،
/// وإنشاءُ طَلَبٍ يُنشِئُ شُحنَتَه (<c>createShipment: true</c>) بِمَبلَغِ التَحصيل،
/// وجَلبُ حالِ الطَلَب. وكُلُّ فَشَلٍ <b>رَمزٌ مُسَمّى</b> مِن <see cref="OtoFailures"/> —
/// لا استِثناءٌ يَصعَدُ إلى النُقطَة، ولا «لَم يَحدُث شَيء».</para>
///
/// <para>(‏2026-10-13، مُراجَعَةُ المَوجَة 3b، G5 (vi) — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق: <b>والعَمَلِيّاتُ خَمسٌ لا ثَلاث</b> — مُنذُ الخُطوَة 7 تَسجيلُ الوارِد
/// (<see cref="RegisterWebhookAsync"/>، الرابِعَة) ومُنذُ الخُطوَة 8 تَسجيلُ البائِعِ بِالاعتِمادِ السوقِيّ (<see cref="RegisterVendorAsync"/>، الخامِسَة)؛
/// ومُبادَلَةُ الرَمزِ (<see cref="AccessTokenAsync"/>، قِسمُ «الرَمز») يُناديها سَطحُ البائِعِ مُباشَرَةً مُنذُ <c>8f5bdd6</c> لِيَرُدَّ رَمزاً مَلصوقاً تَرُدُّه OTO.
/// <b>ومُستَهلِكاه اليَومَ خِدمَتانِ في القالِب</b> لا واحِدَة — شَحنُ الصَفَقاتِ (<c>DealShipping</c>: الشَحنُ والوارِد) وسَطحُ البائِع
/// (<c>VendorShippingService</c>: اللَصقُ والتَسجيل) — وكِلتاهُما تَطلُبُ OTO بِعَينِها لا ناقِلاً يُبَدَّل، فَشَرطُ الاستِخراجِ أَدناه بِحالِه.)</para>
///
/// <para><b>ولا يُنَفِّذُ <c>IDeliveryProvider</c> — ولا <c>Decide</c> لِلناقِل</b>
/// (‏ADR-033 الخُطوَة 9): تِلكَ الواجِهَةُ تُنَمذِجُ مَندوبَ تَوصيلٍ (هاتِفُ سائِقٍ،
/// مَوقِعٌ حَيّ) والمُستَهلِكُ يَحتاجُ ناقِلَ طُرود (بوليصَة، تَحصيلٌ عِندَ الاستِلام،
/// حالُ طَلَب). وما يَحتاجُه مَكتوبٌ هُنا مِن حاجَتِه لا مِن ذاكِرَة.</para>
///
/// <para><b>شَرطُ استِخراجِ واجِهَةٍ مِنه — مَكتوبٌ هُنا كَما تَطلُبُ القاعِدَةُ ١</b>:
/// ثَلاثَةُ مُستَهلِكينَ في وَقتِ التَشغيلِ يَحتاجونَ <b>تَبديلَ</b> الناقِل (ناقِلٌ
/// ثانٍ حَقيقيٌّ بِجِوارِ OTO — طُرود، ‏`SHGAP-07` — أَو مُحاكٍ مَشروعٌ بِنَمَطِ ‏ADR-014).
/// ومُستَهلِكُه اليَومَ واحِد: مَرحَلَةُ <c>DealStage.Shipping</c> عَبرَ خِدمَةِ شَحنِ
/// الصَفَقاتِ في القالِب. فَلا واجِهَةَ، ولا مُحاكيَ يُسَجَّلُ مَكانَه.</para>
///
/// <para><b>والحُقولُ والمَساراتُ مَقروءَةٌ لا مُخمَّنَة</b> — مِن مَجموعَةِ وَثائِقِ
/// OTO العامَّةِ (<c>https://apis.tryoto.com/</c>، ‏SPA صُيِّرَت بِـChromium يَومَ
/// ‏2026-10-12، وأَصلُها وَرَقَةُ البَحثِ `24-SHIPPING.md` §٣‑أ): «Refresh Token»،
/// «Check OTO Delivery Fee»، «Create Order»، «Order Tracking» (‏<c>POST /rest/v2/orderStatus</c>).
/// <b>و«‏cod_amount» في ‏ADR-033 اسمُ مَفهومٍ لا حَقل</b>: الوَثيقَةُ تُسَمّيه
/// <c>payment_method: "cod"</c> و<c>amount_due</c> («‏If order paid amount_due will be 0,
/// otherwise equal to amount»).</para>
/// </summary>
public sealed class OtoClient
{
    // ─── المَسارات — مُثَبَّتَةٌ لِيَقرَأَها الاختِبارُ بَدَلَ أَن يَنسَخَها ─────────
    public const string RefreshTokenPath        = "/rest/v2/refreshToken";
    public const string CheckOtoDeliveryFeePath = "/rest/v2/checkOTODeliveryFee";
    public const string CreateOrderPath         = "/rest/v2/createOrder";
    public const string OrderStatusPath         = "/rest/v2/orderStatus";

    /// <summary>تَسجيلُ عُنوانِ الوارِدِ وتَحديثُه — <c>POST</c> يُنشِئُ و<c>PUT</c> يُحَدِّثُ بِـ<c>id</c> (قِسمُ «Webhook › webhook»
    /// في الوَثائِقِ العامَّة، مُصَيَّراً ‏2026-10-12؛ تَيّارُ S8، ‏ADR-033 الخُطوَة 7).</summary>
    public const string WebhookPath            = "/rest/v2/webhook";

    /// <summary>نَوعُ الوارِدِ الَّذي نُسَجِّلُه: «orderStatus sends order status changes» — بِحَرفِ حَقلِ <c>webhookType</c>.</summary>
    public const string OrderStatusWebhookType = "orderStatus";

    /// <summary>تَسجيلُ بائِعٍ بِالاعتِمادِ السوقِيّ — قِسمُ «Marketplace › Register» («‏POST https://api.tryoto.com/rest/v2/register»)،
    /// مُصَيَّراً ‏2026-10-12 (تَيّارُ S8، ‏ADR-033 الخُطوَة 8).</summary>
    public const string RegisterPath           = "/rest/v2/register";

    /// <summary>قِيَمُ <c>payment_method</c> كَما تَقولُها الوَثيقَة: «‏Possible values:
    /// cod, paid».</summary>
    public const string CashOnDelivery = "cod";
    public const string Prepaid        = "paid";

    /// <summary>حالُ الطَلَبِ حينَ يُسَلَّم — «‏delivered · Delivery · Order is delivered
    /// to end customer successfully» في «‏List Of Statuses».</summary>
    public const string DeliveredStatus = "delivered";

    /// <summary>
    /// <para><b>الحالاتُ الَّتي تَعني أَنّ لِلطَلَبِ عِندَ OTO شُحنَةً ذاهِبَةً أُنشِئَت</b> — بِحَرفِ «‏List Of Statuses»
    /// (‏2026-10-12؛ مُراجَعَةُ تَيّارِ S7): صُفوفُ المَراحِلِ «Creation» و«Pickup» و«Transit» و«Delivery» كُلُّها وبِتَرتيبِها،
    /// أَوَّلُها «‏searchingDriver · Creation · Shipment created and waiting for delivery company to assign driver». وما
    /// قَبلَها — «Order Management» و«Warehouse Management»، ومِنها «‏missingData · The order is missing required information
    /// to proceed with shipment creation» — طَلَبٌ بِلا شُحنَة؛ وما بَعدَها رُجوعٌ أَو إلغاءٌ أَو شُحنَةٌ عَكسِيَّة.</para>
    ///
    /// <para><b>قائِمَةُ سَماحٍ لا قائِمَةُ مَنع</b>: حالٌ لا تَعرِفُه — ومِنه <c>shipmentProcessing</c> الَّذي في مِثالِ قِسمِ
    /// «Webhook» ولَيسَ في القائِمَة — لا يُعَدُّ شُحنَة، فَيُعادُ السُؤالُ ولا تَتَقَدَّمُ صَفقَةٌ عَلى ظَنّ. و<c>pickupAttemted</c>
    /// بِحَرفِ الوَثيقَة.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> ShipmentStatuses = new[]
    {
        // Creation
        "searchingDriver", "shipmentCreated", "goingToPickup", "arrivedPickup", "pickupAttemted",
        // Pickup
        "pickedUp",
        // Transit
        "arrivedDestinationTerminal", "arrivedTerminal", "departedTerminal", "inTransit", "arrivedOriginTerminal",
        "outForDelivery", "arrivedDestination", "shipmentInProgress", "undeliveredAttempt", "shipmentOnHold", "heldForPickup",
        // Delivery
        DeliveredStatus,
    };

    /// <summary>
    /// <para><b>الحالاتُ الَّتي لا يَعودُ مِنها الطَلَبُ إلى شُحنَةٍ ذاهِبَة</b> — بِحَرفِ «‏List Of Statuses» (مُصَيَّرَةً ‏2026-10-12؛ مُراجَعَةُ
    /// المَوجَة 3a، تَيّارُ S8): «‏canceled · Order Management · Order is canceled»، و«‏deleted · Order Management · Order is deleted»،
    /// و«‏returned · Returned · Order is returned back to origin»، و«‏shipmentCanceled · Tracking and Return Management · Shipment is
    /// canceled». وهي الأَربَعُ الَّتي سَمّاها ‏ADR-033 §٧‑ح‑٣‑١: طَلَبٌ فيها يُجابُ «أَكمِل الشَّحن» بِلا نِهايَة — والمُعَرِّفُ مُعَرِّفُ
    /// الصَفقَةِ فَلا يُنشَأُ ثانٍ (OTO1063).</para>
    ///
    /// <para><b>قائِمَةُ سَماحٍ لا تَخمين</b>: <c>lostOrDamaged</c> («Shipment is lost or damaged») و<c>destroyed</c> («Package was damaged
    /// beyond delivery») يَصِفانِ نِهايَةً أَيضاً ولَيسا هُنا — لَم يُسَمِّهِما البَند، ويُقاسانِ عَلى بيئَةِ الاختِبار (سِجِلُّ المالِكِ «ب» S8).</para>
    /// </summary>
    public static readonly IReadOnlyList<string> TerminalStatuses = new[]
    {
        "canceled", "deleted", "returned", "shipmentCanceled",
    };

    /// <summary>
    /// <para><b>الحالانِ اللَّذانِ يَحتاجُ فيهِما الطَلَبُ إنساناً</b> (‏2026-10-29، قَرارُ المالِك د9 (١) «أ»: «لا يُغلق الطلب آليًّا. يُعلَّم "يحتاج تدخّلًا"
    /// ويُبلَّغ البائع وصاحب المتجر، وهما يقرّران الإلغاء أو التعويض أو إعادة الإرسال») — بِحَرفِ «‏List Of Statuses» كَما سَمّاهُما تَعليقُ
    /// <see cref="TerminalStatuses"/>: <c>lostOrDamaged</c> («Shipment is lost or damaged») و<c>destroyed</c> («Package was damaged beyond delivery»).
    /// <b>لَيسا في <see cref="TerminalStatuses"/> بِقَصد</b>: هُناكَ يُغلَقُ «أَكمِل الشَّحن» آليّاً، وهُنا لا يُغلَقُ شَيءٌ ولا تَتَحَرَّكُ صَفقَة — عَلامَةٌ
    /// وإشعار. ولا حالَ في القائِمَتَينِ مَعاً. قائِمَةُ سَماحٍ بِمُقارَنَةٍ حَرفِيَّة.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> InterventionStatuses = new[]
    {
        "lostOrDamaged", "destroyed",
    };

    private readonly HttpClient _http;
    private readonly OtoOptions _opts;
    private readonly OtoTokenCache _tokens;
    private readonly ILogger<OtoClient> _log;

    public OtoClient(HttpClient http, IOptions<OtoOptions> opts, OtoTokenCache tokens, ILogger<OtoClient> log)
    {
        _http = http;
        _opts = opts.Value;
        _tokens = tokens;
        _log = log;
    }

    /// <summary>المُضيفُ الَّذي يُنادى، و<c>null</c> حَيثُ لا بيئَةَ مَفتوحَة
    /// (<see cref="OtoEnvironment.IsOpen"/>) — يَقرَؤُه الاختِبارُ فَلا يَبقى «أَيُّ
    /// مُضيفٍ» دَعوى.</summary>
    public string? BaseUrl => OtoEnvironment.IsOpen(_opts.Environment)
        ? OtoEnvironment.BaseUrlFor(_opts.Environment)
        : null;

    // ═══ العَمَلِيّاتُ الثَلاث ═══════════════════════════════════════════
    // (‏2026-10-13، G5 (vi) — ولا يُعادُ حَرفٌ فَوقَه: وتَحتَ هذا العُنوانِ خَمس — الثَلاثُ ثُمَّ تَسجيلُ الوارِدِ (الخُطوَة 7) وتَسجيلُ البائِع (الخُطوَة 8).)

    /// <summary>
    /// <para><b>أَسعارُ الشَحنِ بِأَسعارِ OTO</b> — <c>POST /rest/v2/checkOTODeliveryFee</c>
    /// بِالحُقولِ الثَلاثَةِ الإلزامِيَّة (<c>originCity</c>، <c>destinationCity</c>،
    /// <c>weight</c>) ويُعيدُ خِياراتِ <c>deliveryCompany</c>.</para>
    ///
    /// <para><b>ومُستَهلِكُه في هذِه المَوجَةِ أَمرُ البُرهانِ الحَيّ لا نُقطَة</b>: نُقطَةُ
    /// الشَحنِ لا تَعرِفُ مَدينَةَ البائِعِ ولا وَزنَ الطَرد، ولا تَحتاجُ خِياراً —
    /// «‏If deliveryOptionId is not sent on the request, the assignment is made to the
    /// delivery company that complies with your feasibility rule» (قِسمُ «Create
    /// Shipment»). فَتَسعيرُ ‏`SHGAP-04` يُقاسُ بِهذِه الدالَّةِ عَلى بيئَةِ الاختِبار.</para>
    /// </summary>
    public async Task<OtoResult<IReadOnlyList<OtoDeliveryOption>>> CheckOtoDeliveryFeeAsync(
        string refreshToken, OtoFeeQuery query, CancellationToken ct = default)
    {
        var res = await PostAsync(refreshToken, CheckOtoDeliveryFeePath, new Dictionary<string, object?>
        {
            ["originCity"]      = query.OriginCity,
            ["destinationCity"] = query.DestinationCity,
            ["weight"]          = query.WeightKg,
        }, "checkOTODeliveryFee", ct);
        if (!res.Ok) return OtoResult<IReadOnlyList<OtoDeliveryOption>>.Fail(res.Failure!);
        var root = res.Value;

        if (!root.TryGetProperty("deliveryCompany", out var list) || list.ValueKind != JsonValueKind.Array)
            return Unreadable<IReadOnlyList<OtoDeliveryOption>>("checkOTODeliveryFee", "deliveryCompany");

        var options = new List<OtoDeliveryOption>();
        foreach (var o in list.EnumerateArray())
        {
            if (Text(o, "deliveryOptionId") is not { } id) continue;
            options.Add(new OtoDeliveryOption(id, Text(o, "deliveryOptionName"), Number(o, "price"), Text(o, "currency")));
        }
        return OtoResult<IReadOnlyList<OtoDeliveryOption>>.Success(options);
    }

    /// <summary>
    /// <para><b>يُنشِئُ الطَلَبَ وشُحنَتَه مَعاً</b> — <c>POST /rest/v2/createOrder</c> بِـ
    /// <c>"createShipment": true</c> («‏This will automatically create the shipment without
    /// needing a separate createShipment call»)، ويُعيدُ مُعَرِّفَنا ومُعَرِّفَ OTO
    /// (<c>otoId</c>) لِيُكتَبا في مَراجِعِ الصَفقَة.</para>
    ///
    /// <para><b>وما لا يُرسَلُ يُقال</b>: لا <c>deliveryOptionId</c> (التَعيينُ لِقاعِدَةِ
    /// حِسابِ البائِعِ عِندَ OTO — أَعلاه)، ولا <c>senderInformation</c> ولا
    /// <c>pickupLocationCode</c> («‏Predefined pickup address of store/warehouse. If not
    /// exist, OTO assigns automatically»): عُنوانُ البائِعِ لا يَعرِفُه الكود، ومَكانُه
    /// مَواقِعُ الاستِلامِ في حِسابِه.</para>
    ///
    /// <para><b>ونِصفُ ما اقتُبِسَ أَعلاه — يُلحَقُ ‏2026-10-12 (مُراجَعَةُ تَيّارِ S7) ولا يُمحى</b>: جُملَةُ «‏This will
    /// automatically create the shipment» مِن صَفحَةِ «Create a Shipment»، وقِسمُ «Create Order» نَفسُه يُكمِلُها: «‏Note: If
    /// you send the createShipment: true parameter and the delivery company returns an error during shipment creation, you
    /// must register the shipmentError webhook in order to receive and view the error details». <b>فَنَجاحُ هذا النِداءِ طَلَبٌ
    /// لا شُحنَة</b> — والشُحنَةُ تُقرَأُ مِن «Order Tracking» (<see cref="OtoOrderState.HasShipment"/>) قَبلَ أَن تَتَحَرَّكَ
    /// صَفقَة. <b>وجَدوَلُ حُقولِه يَسِمُ <c>senderInformation</c> بِـ«yes»</b> («‏Sender data (can not be used together with
    /// pickupLocationCode.)»)، وكُلُّ مِثالٍ مِن أَمثِلَتِه الثَلاثَةَ عَشَرَ يُرسِلُه أَو <c>pickupLocationCode</c> (في رَأسِه أَو في
    /// عَناصِرِه) — فَإغفالُهُما هُنا غَيرُ مَقيس، يَتبَعُه سِجِلُّ المالِكِ «ب» S7·٣ (١) ولا يُظَنُّ بِه.</para>
    /// </summary>
    public async Task<OtoResult<OtoCreatedOrder>> CreateOrderAsync(
        string refreshToken, OtoOrderDraft draft, CancellationToken ct = default)
    {
        var res = await PostAsync(refreshToken, CreateOrderPath, new Dictionary<string, object?>
        {
            ["orderId"]          = draft.OrderId,
            ["createShipment"]   = true,
            ["payment_method"]   = draft.PaymentMethod,
            ["amount"]           = draft.Amount,
            ["amount_due"]       = draft.AmountDue,
            ["currency"]         = draft.Currency,
            ["item_description"] = draft.ItemDescription,
            ["customer"] = new Dictionary<string, object?>
            {
                ["name"]    = draft.Customer.Name,
                ["mobile"]  = draft.Customer.Mobile,
                ["address"] = draft.Customer.Address,
                ["city"]    = draft.Customer.City,
                ["country"] = draft.Customer.Country,
            },
        }, "createOrder", ct);
        if (!res.Ok) return OtoResult<OtoCreatedOrder>.Fail(res.Failure!);
        var root = res.Value;

        // ‏«{"success": true, "otoId": 540789}» — رَقَمٌ في المِثالِ ونَصٌّ في رَدِّ الحال؛
        // يُقرَأُ الاثنان.
        if (Text(root, "otoId") is not { } otoId)
            return Unreadable<OtoCreatedOrder>("createOrder", "otoId");
        return OtoResult<OtoCreatedOrder>.Success(new OtoCreatedOrder(draft.OrderId, otoId));
    }

    /// <summary>
    /// <para><b>حالُ الطَلَبِ مِن OTO نَفسِها</b> — <c>POST /rest/v2/orderStatus</c> بِـ
    /// <c>orderId</c> («‏Current order status (e.g.,pickedUp, outForDelivery, delivered)
    /// … print AWB URL»). <b>وهذا هُوَ بُرهانُ الوارِدِ في هذِه المَوجَة</b>: إشعارُ
    /// <c>orderStatus</c> إشارَةٌ يُستَجلَبُ بَعدَها الحالُ مِن هُنا بِرَمزِ البائِعِ قَبلَ
    /// أَيِّ كِتابَة (‏ADR-033 §٢‑هـ القاعِدَة ٦).</para>
    ///
    /// <para><b>وكانَ اسمُ النُقطَةِ خارِجَ وَرَقَةِ البَحث</b> — قُرِئَ يَومَ ‏2026-10-12
    /// مِن قِسمِ «Tracking › Order Tracking» في الوَثائِقِ العامَّة.</para>
    /// </summary>
    public async Task<OtoResult<OtoOrderState>> GetOrderStatusAsync(
        string refreshToken, string orderId, CancellationToken ct = default)
    {
        var res = await PostAsync(refreshToken, OrderStatusPath, new Dictionary<string, object?>
        {
            ["orderId"] = orderId,
        }, "orderStatus", ct);
        if (!res.Ok) return OtoResult<OtoOrderState>.Fail(res.Failure!);
        var root = res.Value;

        if (Text(root, "status") is not { } status)
            return Unreadable<OtoOrderState>("orderStatus", "status");

        return OtoResult<OtoOrderState>.Success(new OtoOrderState(
            Text(root, "orderId") ?? "",
            Text(root, "otoId"),
            status,
            Text(root, "printAWBURL"),
            Text(root, "trackingUrl")));
    }

    /// <summary>
    /// <para><b>يُسَجِّلُ عُنوانَ وارِدِ <c>orderStatus</c> عِندَ حِسابِ البائِعِ — أَو يُحَدِّثُه</b> (تَيّارُ S8، ‏2026-10-12؛ ‏ADR-033
    /// الخُطوَة 7 و§٧‑م). العَمَلِيَّةُ الرابِعَة — والثَلاثُ أَعلاه بِحالِها. <c>POST /rest/v2/webhook</c> بِـ
    /// <c>{"method": "post", "url", "secretKey", "webhookType": "orderStatus"}</c> («‏secretKey · With this key message is signed and
    /// receiver validate.(Character limit is 255)»)، ورَدُّه «‏{"success": true, "id": "59", "message": "webhook has been created"}» —
    /// فَيُعادُ <c>id</c>. و<b>حينَ يُعطى <paramref name="existingId"/></b>: <c>PUT</c> بِالجِسمِ نَفسِه ومَعَه <c>id</c> («‏allows updating
    /// an already registered webhook by providing its unique ID … such as URL, secretKey»)، ورَدُّه بِلا <c>id</c> فَيُعادُ القائِم.</para>
    ///
    /// <para><b>ولا يُرسَلُ <c>authorizationKey</c> ولا <c>timestampFormat</c> ولا <c>orderPrefix</c></b>: الأَوَّلُ لا تَكتُبُ الوَثيقَةُ
    /// اسمَ الرَأسِ الَّذي يَعودُ فيه (سِجِلُّ المالِكِ «ب»)، والثاني يُبقي <c>timestamp</c> بِصيغَتِه الوَحيدَةِ المَوصوفَة («Unix epoch
    /// timestamp (in milliseconds)») — وهي ما يَدخُلُ النَصَّ المُوَقَّع — والثالِثُ لِطَلَباتٍ بِبادِئَةٍ لا نُرسِلُها.</para>
    /// </summary>
    public async Task<OtoResult<string>> RegisterWebhookAsync(
        string refreshToken, string url, string secretKey, string? existingId = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["method"]      = "post",
            ["url"]         = url,
            ["secretKey"]   = secretKey,
            ["webhookType"] = OrderStatusWebhookType,
        };
        if (existingId is not null) body["id"] = existingId;

        var res = await PostAsync(refreshToken, WebhookPath, body, "webhook", ct,
            existingId is null ? HttpMethod.Post : HttpMethod.Put);
        if (!res.Ok) return OtoResult<string>.Fail(res.Failure!);

        if (existingId is not null) return OtoResult<string>.Success(existingId);
        return Text(res.Value, "id") is { } id
            ? OtoResult<string>.Success(id)
            : Unreadable<string>("webhook", "id");
    }

    /// <summary>
    /// <para><b>يُسَجِّلُ بائِعاً عِندَ OTO بِالاعتِمادِ السوقِيّ — العَمَلِيَّةُ الخامِسَة</b> (تَيّارُ S8، ‏2026-10-12؛ ‏ADR-033 الخُطوَة 8).
    /// «‏It requires a special kind of token provided by OTO. Regular refresh tokens will not work here.» — فَيُمَرَّرُ
    /// <paramref name="marketplaceToken"/> مَكانَ رَمزِ التَحديثِ في المَسارِ نَفسِه (رَمزُ وُصولٍ مِنه، ثُمَّ <c>Bearer</c>؛ والمِثالُ
    /// «‏HEADER Authorization Bearer your_access_token»). <b>وأَنّه رَمزُ تَحديثٍ يُبادَلُ لا رَمزُ وُصولٍ يُرسَلُ كَما هُوَ — قِراءَةٌ لا
    /// قِياس</b> (سِجِلُّ المالِكِ «ب»).</para>
    ///
    /// <para><b>الحُقولُ بِأَسماءِ جَدوَلِ الوَثيقَةِ حَرفاً</b>: <c>country · city · email · mobileNumber · firstName · lastName ·
    /// isVatRegistered · billingAddress</c> دائِماً، و<b>حينَ <c>isVatRegistered</c> وَحدَه</b> <c>companyName · vatNumber · CRNumber ·
    /// district · zipCode · buildingNo · streetName</c> («‏SA (Saudi Arabia): – If isVatRegistered is true: CRNumber (10 digits), vatNumber
    /// (15 digits, starts with ‘3’), district, zipCode (5 digits), buildingNo (4 digits), streetName required»؛ و<c>companyName</c>
    /// «‏Required if isVatRegistered is true»). <b>ومِثالُ الجِسمِ في الوَثيقَةِ يُرسِلُ <c>fullName</c></b> — والجَدوَلُ يَقولُ
    /// <c>firstName</c>/<c>lastName</c>؛ نَتبَعُ الجَدوَل (‏ADR-033 §٣‑٦: «تُحسَمُ في بيئةِ الاختبارِ لا بالقراءة»).</para>
    /// <para>(‏2026-10-13، مُراجَعَةُ المَوجَة 3b، G5 (vii) — ولا يُعادُ حَرفٌ فَوقَه: <b>والوَثيقَةُ تُناقِضُ نَفسَها في <c>zipCode</c>/<c>buildingNo</c></b> —
    /// جَدوَلُ حُقولِها: «‏zipCode … Required for SA (5 digits) and other countries if VAT registered» و«‏buildingNo … Required for SA (4 digits)
    /// and other countries if VAT registered»، وفِقرَةُ السُعودِيَّةِ المُقتَبَسَةُ أَعلاه تَشرِطُهُما بِـ<c>isVatRegistered</c>. نَتبَعُ الفِقرَة — فَإن كانَت OTO
    /// تَعمَلُ بِالجَدوَلِ رُدَّ كُلُّ بائِعٍ غَيرِ مُسَجَّلٍ في الضَريبَة. لَم يُقَس؛ أَمرُ القِياسِ عَلى staging في سِجِلِّ المالِكِ «ب» S11b·٢٧، والسُؤالُ في «ج» S8·١٤ (١٢).)</para>
    ///
    /// <para>ورَدُّه «‏{"success": true, "activationLink": "https://login.tryoto.com/XYZ", "refreshToken": "_refreshToken_"}» — فَيُعادُ
    /// الاثنان؛ وغِيابُ أَيِّهِما، أَو رابِطٌ لَيسَ <c>https</c> مُطلَقاً، <see cref="OtoFailures.Unreadable"/>: لا يُحفَظُ رَمزٌ لا يُعرَفُ
    /// كَيفَ يُفَعِّلُه صاحِبُه. ولا يُكتَبُ جِسمٌ ولا رَدٌّ في لوغ — فيهِما بَياناتُ البائِعِ ورَمزُه.</para>
    /// </summary>
    public async Task<OtoResult<OtoRegisteredVendor>> RegisterVendorAsync(
        string marketplaceToken, OtoVendorRegistration vendor, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["country"]         = vendor.Country,
            ["city"]            = vendor.City,
            ["email"]           = vendor.Email,
            ["mobileNumber"]    = vendor.MobileNumber,
            ["firstName"]       = vendor.FirstName,
            ["lastName"]        = vendor.LastName,
            ["isVatRegistered"] = vendor.IsVatRegistered,
            ["billingAddress"]  = vendor.BillingAddress,
        };
        if (vendor.IsVatRegistered)
        {
            body["companyName"] = vendor.CompanyName;
            body["vatNumber"]   = vendor.VatNumber;
            body["CRNumber"]    = vendor.CrNumber;
            body["district"]    = vendor.District;
            body["zipCode"]     = vendor.ZipCode;
            body["buildingNo"]  = vendor.BuildingNo;
            body["streetName"]  = vendor.StreetName;
        }

        var res = await PostAsync(marketplaceToken, RegisterPath, body, "register", ct);
        if (!res.Ok) return OtoResult<OtoRegisteredVendor>.Fail(res.Failure!);

        if (Text(res.Value, "refreshToken") is not { } token)
            return Unreadable<OtoRegisteredVendor>("register", "refreshToken");
        if (Text(res.Value, "activationLink") is not { } link
            || !Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return Unreadable<OtoRegisteredVendor>("register", "activationLink");
        return OtoResult<OtoRegisteredVendor>.Success(new OtoRegisteredVendor(token, link));
    }

    // ═══ الرَمز ════════════════════════════════════════════════════════

    /// <summary>
    /// <para><b>رَمزُ الوُصولِ مِن رَمزِ التَحديث</b> — <c>POST /rest/v2/refreshToken</c>
    /// بِجِسمِ <c>{"refresh_token": …}</c>، مَرَّةً كُلَّ ساعَةٍ إلّا دَقيقَة لا مَرَّةً
    /// لِكُلِّ نِداء (<see cref="OtoTokenCache"/>). <b>والرَمزانِ في الجِسمِ والرَأسِ
    /// وَحدَهُما</b> — لا في عُنوانٍ ولا في لوغ.</para>
    /// </summary>
    public Task<OtoResult<string>> AccessTokenAsync(string refreshToken, CancellationToken ct = default)
        => BaseUrl is null || string.IsNullOrWhiteSpace(refreshToken)
            ? Task.FromResult(Fail<string>(OtoFailures.NotConfigured, "refreshToken", null, null))
            : _tokens.GetAsync(OtoTokenCache.KeyOf(refreshToken),
                c => FetchTokenAsync(refreshToken, c), DateTimeOffset.UtcNow, ct);

    private async Task<OtoResult<OtoAccessToken>> FetchTokenAsync(string refreshToken, CancellationToken ct)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Post, BaseUrl + RefreshTokenPath)
        {
            Content = Json(new Dictionary<string, object?> { ["refresh_token"] = refreshToken }),
        };

        var sent = await SendAsync(msg, "refreshToken", ct);
        if (sent.Value is not { } resp) return OtoResult<OtoAccessToken>.Fail(sent.Failure!);
        using (resp)
        {
            // ‏5xx «لا جَواب» هُنا كَما في كُلِّ نِداء (مَعجَمُ `OtoFailures.Unreachable` و`PostAsync`) — عُطلُ خادِمٍ لا
            // يَقولُ شَيئاً عَن الرَمز. (وكانَ هُنا — حَتّى مُراجَعَةِ المَوجَة 3a، تَيّارُ S8، ‏2026-10-12؛ تَوصيفُه `1ee37ed` —: «أَيُّ
            // رَدٍّ غَيرُ ناجِحٍ هُنا رَمزٌ مَرفوض»، فَيُقالُ لِلبائِعِ «رَفَضَت OTO رَمزَ حِسابِ الشَّحن» عَن رَمزٍ سَليم. ولا يُعادُ حَرفٌ فَوقَه.)
            var status = (int)resp.StatusCode;
            if (status >= 500)
                return Fail<OtoAccessToken>(OtoFailures.Unreachable, "refreshToken", status, null);

            // وما سِواه مِن رَدٍّ غَيرِ ناجِحٍ رَمزٌ مَرفوض — ولا يُقرَأُ جِسمُه في لوغ.
            if (!resp.IsSuccessStatusCode)
                return Fail<OtoAccessToken>(OtoFailures.TokenRefused, "refreshToken", status, null);

            var root = await ReadJsonAsync(resp, ct);
            if (root is not { } r || IsFalse(r, "success") || Text(r, "access_token") is not { } token)
                return Fail<OtoAccessToken>(OtoFailures.TokenRefused, "refreshToken", (int)resp.StatusCode, null);

            // ‏«"expires_in": "3600"» نَصٌّ في المِثال — ويُقرَأُ رَقَماً أَيضاً.
            var lifetime = int.TryParse(Text(r, "expires_in"), out var s) && s > 0
                ? s : OtoEnvironment.DocumentedTokenLifetimeSeconds;
            return OtoResult<OtoAccessToken>.Success(new OtoAccessToken(token, lifetime));
        }
    }

    // ═══ الأُنبوبُ الواحِد ═════════════════════════════════════════════

    /// <summary>
    /// <para><b>كُلُّ عَمَلِيَّةٍ تَمُرُّ مِن هُنا</b>: بيئَةٌ مَفتوحَةٌ ورَمزُ تَحديثٍ، ثُمَّ
    /// رَمزُ وُصول، ثُمَّ <c>POST</c> بِـ<c>Authorization: Bearer</c> («‏Include the
    /// access_token in the request headers as: Authorization: Bearer access_token»)، ثُمَّ
    /// تَصنيفُ الرَدّ. و‏401 يُسقِطُ الرَمزَ المُخَزَّنَ ويُعيدُ المُحاوَلَةَ مَرَّةً واحِدَة.</para>
    ///
    /// <para><b>والتَصنيفُ</b>: ‏5xx أَو انقِطاعٌ أَو مُهلَة ← <see cref="OtoFailures.Unreachable"/>؛
    /// ‏4xx أَو <c>"success": false</c> ← <see cref="OtoFailures.Refused"/> بِرَمزِ OTO
    /// (<c>otoErrorCode</c>، مِثالُ الوَثيقَة: «‏OTO1006 · Credit is not enough»)؛ جِسمٌ
    /// لا يُقرَأ ← <see cref="OtoFailures.Unreadable"/>.</para>
    /// </summary>
    private async Task<OtoResult<JsonElement>> PostAsync(
        string refreshToken, string path, object body, string operation, CancellationToken ct,
        HttpMethod? method = null)
    {
        if (BaseUrl is null || string.IsNullOrWhiteSpace(refreshToken))
            return Fail<JsonElement>(OtoFailures.NotConfigured, operation, null, null);

        var key = OtoTokenCache.KeyOf(refreshToken);
        // بِلا هذا القَيدِ يَدورُ ‏401 مِن رَمزٍ جَديدٍ إلى رَمزٍ جَديدٍ بِلا نِهايَة؛ والمُحاوَلَةُ الثانِيَةُ
        // تَمُرُّ مِن فَرعِ ‏401 إلى «مَرفوض» كَأَيِّ ‏4xx.
        for (var attempt = 1; ; attempt++)
        {
            var token = await AccessTokenAsync(refreshToken, ct);
            if (token.Value is not { } bearer) return OtoResult<JsonElement>.Fail(token.Failure!);

            // ‏`PUT` لِتَحديثِ تَسجيلِ الوارِدِ وَحدَه (‏2026-10-12)؛ وكُلُّ ما سِواه `POST` كَما كان.
            using var msg = new HttpRequestMessage(method ?? HttpMethod.Post, BaseUrl + path) { Content = Json(body) };
            msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

            var sent = await SendAsync(msg, operation, ct);
            if (sent.Value is not { } resp) return OtoResult<JsonElement>.Fail(sent.Failure!);
            using (resp)
            {
                if (resp.StatusCode == HttpStatusCode.Unauthorized && attempt == 1)
                {
                    _tokens.Invalidate(key);
                    continue;
                }

                var status = (int)resp.StatusCode;
                if (status >= 500)
                    return Fail<JsonElement>(OtoFailures.Unreachable, operation, status, null);

                var root = await ReadJsonAsync(resp, ct);
                if (!resp.IsSuccessStatusCode || (root is { } r && IsFalse(r, "success")))
                    return Fail<JsonElement>(OtoFailures.Refused, operation, status,
                        root is { } e ? Text(e, "otoErrorCode") : null);

                return root is { } ok
                    ? OtoResult<JsonElement>.Success(ok)
                    : Fail<JsonElement>(OtoFailures.Unreadable, operation, status, null);
            }
        }
    }

    private async Task<OtoResult<HttpResponseMessage>> SendAsync(
        HttpRequestMessage msg, string operation, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(OtoEnvironment.Timeout(_opts.TimeoutSeconds));
        try
        {
            return OtoResult<HttpResponseMessage>.Success(await _http.SendAsync(msg, cts.Token));
        }
        catch (HttpRequestException)
        {
            return Fail<HttpResponseMessage>(OtoFailures.Unreachable, operation, null, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // مُهلَتُنا نَحن انقَضَت — لا إلغاءُ المُنادي (ذاكَ يَصعَدُ كَما هو).
            return Fail<HttpResponseMessage>(OtoFailures.Unreachable, operation, null, null);
        }
    }

    // ═══ أَدَوات ═══════════════════════════════════════════════════════

    private OtoResult<T> Fail<T>(string code, string operation, int? httpStatus, string? otoErrorCode)
    {
        // اللوغُ يَحمِلُ الرَمزَ والحالَ ورَمزَ OTO — **ولا يَحمِلُ رَمزاً ولا جِسماً**:
        // جِسمُ الطَلَبِ فيه اسمُ المُشتَري وهاتِفُه وعُنوانُه.
        _log.LogWarning("[OTO] {Operation} ← {Code} (HTTP {Status}, {OtoErrorCode})",
            operation, code, httpStatus?.ToString() ?? "—", otoErrorCode ?? "—");
        return OtoResult<T>.Fail(OtoFailures.Describe(code, httpStatus, otoErrorCode));
    }

    private OtoResult<T> Unreadable<T>(string operation, string field)
    {
        _log.LogWarning("[OTO] {Operation} ← {Code} (لا «{Field}» في الرَدّ)", operation, OtoFailures.Unreadable, field);
        return OtoResult<T>.Fail(OtoFailures.Describe(OtoFailures.Unreadable, null, null));
    }

    private static StringContent Json(object body)
        => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static async Task<JsonElement?> ReadJsonAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            var text = await resp.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(text)) return null;
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.Clone() : null;
        }
        catch (JsonException) { return null; }
    }

    private static bool IsFalse(JsonElement root, string name)
        => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.False;

    /// <summary>حَقلٌ نَصّاً — والرَقَمُ يُقرَأُ نَصّاً كَذلك (<c>otoId</c> رَقَمٌ في رَدِّ
    /// الإنشاءِ ونَصٌّ في رَدِّ الحال، و<c>expires_in</c> نَصٌّ في المِثال).</summary>
    private static string? Text(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var v)) return null;
        var s = v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null
        };
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    private static decimal? Number(JsonElement root, string name)
        => decimal.TryParse(Text(root, name), System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
}

// ═══ الأَشكال — ما يُرسَل وما يَعود ═══════════════════════════════════════

/// <summary>رَمزُ وُصولٍ وعُمرُه بِالثَواني كَما قالَته OTO.</summary>
public sealed record OtoAccessToken(string Token, int ExpiresInSeconds);

/// <summary>سُؤالُ السِعر — الحُقولُ الإلزامِيَّةُ الثَلاثَة («‏originCity · destinationCity
/// · weight»)، والوَزنُ بِالكيلوغرام.</summary>
public sealed record OtoFeeQuery(string OriginCity, string DestinationCity, decimal WeightKg);

/// <summary>خِيارُ شَحنٍ مِن رَدِّ السِعر — مُعَرِّفُه واسمُه وسِعرُه وعُملَتُه.</summary>
public sealed record OtoDeliveryOption(string DeliveryOptionId, string? Name, decimal? Price, string? Currency);

/// <summary>المُستَلِم — الحُقولُ الخَمسَةُ الإلزامِيَّةُ في «‏Request Parameters for Customer
/// Data»: <c>name · mobile · address · city · country</c> (ورَمزُ البَلَدِ ‏ISO2).</summary>
public sealed record OtoCustomer(string Name, string Mobile, string Address, string City, string Country);

/// <summary>مُسَوَّدَةُ الطَلَب — <see cref="PaymentMethod"/> مِن
/// <see cref="OtoClient.CashOnDelivery"/>/<see cref="OtoClient.Prepaid"/>، و<see cref="AmountDue"/>
/// يُساوي <see cref="Amount"/> لِلتَحصيلِ عِندَ الاستِلامِ وصِفراً لِلمَدفوع.</summary>
public sealed record OtoOrderDraft(
    string OrderId, string PaymentMethod, decimal Amount, decimal AmountDue,
    string Currency, OtoCustomer Customer, string ItemDescription);

/// <summary>الطَلَبُ المُنشَأ: مُعَرِّفُنا كَما أُرسِل، ومُعَرِّفُ OTO كَما عاد.</summary>
public sealed record OtoCreatedOrder(string OrderId, string OtoId);

/// <summary>حالُ الطَلَبِ كَما جَلَبناه — والرابِطانِ (البوليصَةُ والتَتَبُّع) حَيثُ
/// تُعطيهِما OTO.</summary>
public sealed record OtoOrderState(string OrderId, string? OtoId, string Status, string? LabelUrl, string? TrackingUrl)
{
    public bool IsDelivered => string.Equals(Status, OtoClient.DeliveredStatus, StringComparison.Ordinal);

    /// <summary>لِلطَلَبِ عِندَ OTO شُحنَةٌ أُنشِئَت — حالُه مِن <see cref="OtoClient.ShipmentStatuses"/>.</summary>
    public bool HasShipment => OtoClient.ShipmentStatuses.Contains(Status, StringComparer.Ordinal);

    /// <summary>الطَلَبُ عِندَ OTO في حالٍ لا يَعودُ مِنه — حالُه مِن <see cref="OtoClient.TerminalStatuses"/> (‏2026-10-12).</summary>
    public bool IsTerminal => OtoClient.TerminalStatuses.Contains(Status, StringComparer.Ordinal);

    /// <summary>فُقِدَت الشُحنَةُ أَو تَلِفَت أَو أُتلِفَت — حالُه مِن <see cref="OtoClient.InterventionStatuses"/> (‏2026-10-29، قَرارُ المالِك د9 (١) «أ»).</summary>
    public bool NeedsIntervention => OtoClient.InterventionStatuses.Contains(Status, StringComparer.Ordinal);
}

/// <summary>
/// <para><b>بائِعٌ يُسَجَّلُ عِندَ OTO</b> (تَيّارُ S8، ‏2026-10-12) — حُقولُ «Marketplace › Register» بِأَسمائِها، وحُقولُ الضَريبَةِ لا تُرسَلُ
/// إلّا مَعَ <see cref="IsVatRegistered"/>. يَملَؤُه البائِعُ عَن نَفسِه. <b>ولا يُطبَعُ</b>: <c>ToString</c> بِلا بَياناتٍ شَخصِيَّة.</para>
/// </summary>
public sealed record OtoVendorRegistration(
    string Country, string City, string Email, string MobileNumber, string FirstName, string LastName,
    bool IsVatRegistered, string BillingAddress,
    string? CompanyName = null, string? VatNumber = null, string? CrNumber = null, string? District = null,
    string? ZipCode = null, string? BuildingNo = null, string? StreetName = null)
{
    public override string ToString() => $"OtoVendorRegistration(country={Country}, vat={IsVatRegistered})";
}

/// <summary>ما يَعودُ مِن التَسجيل: رَمزُ تَحديثِ حِسابِ البائِعِ الدائِمُ ورابِطُ تَفعيلِه — سِرّانِ لا يُطبَعان.</summary>
public sealed record OtoRegisteredVendor(string RefreshToken, string ActivationLink)
{
    public override string ToString() => "OtoRegisteredVendor(token=hidden, link=hidden)";
}

/// <summary>فَشَلٌ مُسَمّى — <c>Code</c> مِن <see cref="OtoFailures"/> لِلاختِبارِ واللوغِ
/// والقامُوس، و<c>MessageAr</c> لِلمُراجِعِ البَشَرِيّ؛ وحالُ HTTP ورَمزُ OTO حَيثُ
/// يوجَدان. نَسَقُ <c>XViolation(Code, MessageAr)</c> في القاعِدَة ٤.</summary>
public sealed record OtoFailure(string Code, string MessageAr, int? HttpStatus = null, string? OtoErrorCode = null);

/// <summary>نَتيجَةُ عَمَلِيَّة: قيمَةٌ أَو فَشَلٌ مُسَمّى — لا استِثناء.</summary>
public sealed record OtoResult<T>(T? Value, OtoFailure? Failure)
{
    public bool Ok => Failure is null;
    public static OtoResult<T> Success(T value) => new(value, null);
    public static OtoResult<T> Fail(OtoFailure failure) => new(default, failure);
}

/// <summary>
/// <para><b>مَعجَمُ فَشَلِ العَميلِ — خَمسَةٌ لا سادِسَ لَها.</b> كُلٌّ بِمُوجَبٍ وسالِبٍ
/// في <c>OtoClientTests</c>، والرَمزُ يَصِلُ صَفحَةَ الصَفقَةِ بِنَصٍّ مِن القامُوس.</para>
/// </summary>
public static class OtoFailures
{
    /// <summary>لا بيئَةَ مَفتوحَة أَو لا رَمزَ تَحديث — فَلا نِداءَ أَصلاً.</summary>
    public const string NotConfigured = "oto_not_configured";

    /// <summary>رَفَضَت OTO رَمزَ التَحديث (أَو رَدَّت بِلا <c>access_token</c>) — ‏4xx مِن نُقطَةِ الرَمز؛ و‏5xx مِنها
    /// <see cref="Unreachable"/> (‏2026-10-12، مُراجَعَةُ المَوجَة 3a).</summary>
    public const string TokenRefused = "oto_token_refused";

    /// <summary>لا جَواب: انقِطاعٌ، أَو مُهلَةٌ، أَو ‏5xx — مِن أَيِّ نُقطَة، ومِنها نُقطَةُ الرَمز.</summary>
    public const string Unreachable = "oto_unreachable";

    /// <summary>أَجابَت OTO بِالرَفض: ‏4xx أَو <c>"success": false</c>.</summary>
    public const string Refused = "oto_refused";

    /// <summary>أَجابَت بِنَجاحٍ لا يُقرَأ — جِسمٌ لَيسَ JSON أَو بِلا الحَقلِ المَطلوب.</summary>
    public const string Unreadable = "oto_unreadable";

    public static readonly IReadOnlyList<string> All =
        new[] { NotConfigured, TokenRefused, Unreachable, Refused, Unreadable };

    public static bool Contains(string code) => All.Contains(code, StringComparer.Ordinal);

    /// <summary>الفَشَلُ بِرِسالَتِه لِلمُراجِع — ولا سِرَّ فيها ولا جِسم.</summary>
    public static OtoFailure Describe(string code, int? httpStatus, string? otoErrorCode) => code switch
    {
        NotConfigured => new(code, "الشَحنُ عَبرَ OTO غَيرُ مُهَيَّأٍ هُنا — لا بيئَةَ مَفتوحَةَ أَو لا رَمزَ تَحديث.", httpStatus, otoErrorCode),
        TokenRefused  => new(code, "رَفَضَت OTO رَمزَ التَحديث — يُراجَعُ الرَمزُ في لَوحَةِ البائِع.", httpStatus, otoErrorCode),
        Unreachable   => new(code, "لا جَوابَ مِن OTO — انقِطاعٌ أَو مُهلَةٌ أَو خَطَأُ خادِم.", httpStatus, otoErrorCode),
        Refused       => new(code, "رَفَضَت OTO الطَلَب — رَمزُها في الحَقلِ المُرفَق.", httpStatus, otoErrorCode),
        Unreadable    => new(code, "رَدُّ OTO ناجِحٌ لا يُقرَأ — حَقلٌ مَطلوبٌ غائِب.", httpStatus, otoErrorCode),
        _ => throw new ArgumentException($"الرَمز «{code}» خارِج مَعجَم OtoFailures.", nameof(code)),
    };
}
