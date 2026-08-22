using ACommerce.Templates.Customer.Marketplace.Gates;
using ACommerce.Templates.Customer.Marketplace.Services.Api;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ACommerce.Templates.Customer.Marketplace.Api;

/// <summary>
/// <para><b>سَطحُ الـAPI — المَوجَةُ الأولى: الصَفقات.</b> أَربَعُ
/// نِقاطٍ <b>تُغَلِّف <c>DealsService</c> ولا تُعَدِّلُها</b>:
/// استِخراجُ المَنطِق في هذِه المَوجَة <b>صِفرُ سَطر</b>، وهذا
/// هُوَ بُرهانُ اختِيارِ المَورِد — المَورِدُ الَّذي كُلفَةُ
/// استِخراجِه صِفر يُثبِت المِعمارِيَّةَ بِلا أَن يَدفَعَ ثَمَنَها
/// مَرَّتَين.</para>
///
/// <para><b>ومِلَفٌّ مُنفَصِلٌ عَمداً</b>: نِطاقُ الفاحِصَينِ
/// الجَديدَين (٩ و‏١٠ في <c>verify-static.sh</c>) مُعَرَّفٌ
/// <b>نَصِّيّاً بِلا لَبس</b> — «هذا المِلَفّ». والقاعِدَةُ الَّتي
/// يَفرِضانِها:</para>
/// <list type="number">
///   <item><b>لا <c>IDocumentStore</c> ولا <c>IDocumentSession</c>
///   في هذا المِلَفّ</b> — جِسمُ نُقطَةٍ يَقبَل خِدمَةً فَقَط. فَإن
///   لَم توجَد الخِدمَة، <b>لا تُكشَف النُقطَة</b> حَتّى
///   تُستَخرَج. وهذِه هي الضَمانَةُ الَّتي تَجعَل «المَسارَ
///   المُوازي» <b>غَيرَ قابِلٍ لِلكِتابَة</b> لا مَذمومَ
///   الكِتابَة.</item>
///   <item><b>لا تَحويلَ ولا HTML</b> — لا <c>Results.Redirect</c>
///   (‏281 سابِقَة في مِلَفّ النِقاط) ولا <c>Results.Forbid()</c>
///   (يَرمي ‏500 لِغياب <c>IAuthenticationService</c>، عَطَبٌ
///   مُثَبَّتٌ في <c>ForbidResultTests</c>).</item>
/// </list>
///
/// <para><b>والمُستَأجِرُ لا يُقرَأُ مِن المَسار</b> (‏§٣٫٦): لا
/// مَقطَعَ سلاجٍ في <c>/api/v1/…</c>، ووَسيطُ المُستَأجِر يَتَخَطّى
/// <c>api</c> لِأَنَّها مَحجوزَة. المُستَأجِرُ يَخرُج مِن وَثيقَةِ
/// المِفتاح، وكُلُّ جَلسَةٍ بَعدَه تُفتَح بِه — داخِلَ
/// <c>DealsService</c> الَّتي تَقبَلُ <c>tenantSlug</c> صَراحَةً.</para>
///
/// <para><b>ولَيسَت هُنا نُقطَةُ إصدارِ المِفتاح</b>، وهذا
/// انحِرافٌ مَقيسٌ عَن <c>§٩</c> مَكتوبٌ في الوَثيقَة: مِفتاحٌ
/// يُصدِرُ مِفتاحاً يَفتَرِض مِفتاحاً قائِماً، والأَوَّلُ لا
/// يُوجَد. فَالإصدارُ يَقَع في الاستوديو بِحارِسِ المِلكِيَّة —
/// <b>ويُبلَغ بِالنَقر</b> (القاعِدَة ١٢).</para>
/// </summary>
public static class ApiV1Endpoints
{
    /// <summary>البادِئَة — مَوضِعٌ واحِد يَقرَؤُه التَسجيلُ
    /// والفاحِصان و<c>ReservedPaths</c>.</summary>
    public const string Prefix = "/api/v1";

    public static IEndpointRouteBuilder MapApiV1(this IEndpointRouteBuilder app)
    {
        // ─── صَفقاتُ فاعِلِ المِفتاح ────────────────────────────────
        app.MapGet("/api/v1/deals", async (HttpContext http, DealsService deals) =>
        {
            var p = http.ApiPrincipal()!;
            var list = await deals.ListForUserAsync(p.TenantSlug, p.ActorUserId, http.RequestAborted);
            return ApiOutcome.Ok(
                new DealListResponse(list.Select(DealApi.ToDto).ToArray(), list.Count)).ToResult();
        }).RequireApiKey(ApiScopeCatalog.DealsRead);

        // ─── صَفقَةٌ واحِدَة ─────────────────────────────────────────
        // غَيرُ الطَرَف يُرَدّ ‏404 لا ‏403: لا نُفشي وُجودَ مَورِدٍ
        // لا يَملِكُه السائِل (كُتلَة «أ» في حَقيبَة المُطابَقَة).
        app.MapGet("/api/v1/deals/{id:guid}", async (Guid id, HttpContext http, DealsService deals) =>
        {
            var p = http.ApiPrincipal()!;
            var deal = await deals.LoadAsync(p.TenantSlug, id, http.RequestAborted);
            return deal is null || !DealApi.IsParty(deal, p.ActorUserId)
                ? ApiOutcome.Error(ApiErrorCatalog.NotFound).ToResult()
                : ApiOutcome.Ok(DealApi.ToDto(deal)).ToResult();
        }).RequireApiKey(ApiScopeCatalog.DealsRead);

        // ─── تَحريكُ مَرحَلَة — قَلبُ قيمَةِ الناقِل ─────────────────
        // التَخويلُ يَبقى في الخِدمَة: هي تَفحَص أَنّ الفاعِلَ
        // مُخَوَّلٌ بِالمَرحَلَة الحاليَّة، وهذا يُغَلَّف ولا يُكرَّر.
        app.MapPost("/api/v1/deals/{id:guid}/advance", async (
            Guid id, HttpContext http, DealsService deals, ApiIdempotencyService idem) =>
            await ApiWrite.OnceAsync(http, idem, "deals.advance", async p =>
            {
                var body = await ApiBody.ReadAsync<AdvanceDealRequest>(http);
                var res = await deals.AdvanceAsync(
                    p.TenantSlug, id, p.ActorUserId, p.ActorName, body?.note, http.RequestAborted);
                return DealApi.FromAdvance(res);
            })
        ).RequireApiKey(ApiScopeCatalog.DealsWrite);

        // ─── الإلغاء ───────────────────────────────────────────────
        // العُضوِيَّة تُفحَص هُنا لِأَنّ CancelAsync لا تَفحَصُ
        // الفاعِلَ إطلاقاً — مَنطِقٌ غائِبٌ لا مُكَرَّر (DealApi.IsParty).
        app.MapPost("/api/v1/deals/{id:guid}/cancel", async (
            Guid id, HttpContext http, DealsService deals, ApiIdempotencyService idem) =>
            await ApiWrite.OnceAsync(http, idem, "deals.cancel", async p =>
            {
                var body = await ApiBody.ReadAsync<CancelDealRequest>(http);
                var reason = DealApi.NormalizeCancelReason(body?.reason);
                if (reason is null)
                    return ApiOutcome.Error(ApiErrorCatalog.ValidationFailed, new { field = "reason" });

                var found = await deals.LoadAsync(p.TenantSlug, id, http.RequestAborted);
                if (found is null || !DealApi.IsParty(found, p.ActorUserId))
                    return ApiOutcome.Error(ApiErrorCatalog.NotFound);

                return DealApi.FromCancel(
                    await deals.CancelAsync(p.TenantSlug, id, p.ActorUserId, p.ActorName, reason, http.RequestAborted));
            })
        ).RequireApiKey(ApiScopeCatalog.DealsWrite);

        return app;
    }
}

/// <summary><b>قِراءَةُ جِسمِ JSON بِلا رَمي</b> — جِسمٌ فارِغٌ أَو
/// مُشَوَّهٌ يُعطي <c>null</c>، فَتُقَرِّرُ النُقطَةُ ما تَفعَل
/// بِرَمزٍ مِن المَعجَم بَدَلَ أَن يَخرُجَ ‏500 مِن مُفَكِّكِ
/// JSON.</summary>
public static class ApiBody
{
    public static async Task<T?> ReadAsync<T>(HttpContext http) where T : class
    {
        try { return await http.Request.ReadFromJsonAsync<T>(ApiJson.Options, http.RequestAborted); }
        catch { return null; }
    }
}
