using Microsoft.AspNetCore.Http;

namespace ACommerce.Templates.Customer.Marketplace.Gates;

/// <summary>
/// <para><b>بَوّابَةُ نَوعِ الجِسم — طَلَبٌ لَيسَ استِمارَةً يُرَدُّ ‏415 قَبلَ أَيِّ قِراءَة</b> (‏2026-10-24، تَيّارُ S15 البَند C؛ RELEASE-READINESS
/// §١ البَند ٧).</para>
///
/// <para><b>العِلَّةُ المَقيسَة</b> (تَوصيفُها <c>FormBodyGateTests</c> و<c>TenantGatewayEndpointsTests</c>): ‏78 نُقطَةَ كِتابَةٍ تَقرَأُ
/// <c>req.Form[…]</c> مُباشَرَةً أَو عَبرَ سَطحٍ يَأخُذُ <c>req</c>، ولا واحِدَةَ تَسأَلُ نَوعَ الجِسم — فَجِسمٌ JSON أَو بِلا <c>Content-Type</c>
/// يَرمي <c>InvalidOperationException: Incorrect Content-Type</c> ويُرَدُّ ‏500 (‏<c>…/advance</c> مَقيساً في جَولَةِ S12). والمُتَصَفِّحُ يُرسِلُ
/// نَوعَ الاستِمارَةِ دائِماً، فَهذا دَينُ مَتانَةٍ لا ثَغرَة.</para>
///
/// <para><b>ولِماذا مُرَشِّحٌ في السِلسِلَة</b> (القاعِدَة ٨): أُنبوبُ مُرَشِّحاتِ النِقاطِ قائِمٌ (<see cref="GateExtensions"/>)، والحارِسُ
/// يُعلَنُ في التَوقيعِ لا في الجِسم (القاعِدَة ٦) — سَطرُ <c>.RequireFormBody()</c> عَلى كُلِّ نُقطَةٍ تَقرَأُ الاستِمارَة، والفاحِصُ
/// (<c>WriteEndpointGuardTests.Every_form_reading_write_endpoint_declares_the_form_body_gate</c>) يَحمَرُّ عَلى نُقطَةٍ تَقرَؤُها بِدونِه.
/// <b>ولا يَقرَأُ حالَةً مُخَزَّنَة</b> (القاعِدَة ٧): رَأسُ الطَلَبِ وَحدَه، ورَفضُه رَمزٌ بِلا خِدمَةٍ يَعتَمِدُ عَلَيها.</para>
///
/// <para><b>وما لَيسَ حارِسَ تَخويل</b>: لا يُسَجَّلُ في <c>ChainGuards</c> — نُقطَةٌ لا تَحمِلُ غَيرَه مَكشوفَةٌ كَما كانَت، ولا يَجعَلُها
/// «مَحروسَة». <b>وتَرتيبُه</b>: أَوَّلُ مُرَشِّحٍ في السِلسِلَة (‏<c>}).RequireFormBody().DisableAntiforgery();</c>، أَو بَعدَ
/// <c>DisableAntiforgery()</c> مُباشَرَةً حَيثُ تَتبَعُه حُرّاس — وذاكَ وَسمٌ لا مُرَشِّح) فَيَجري قَبلَ كُلِّ مُرَشِّحٍ وقَبلَ
/// الجِسم — نَقلُ طَلَبٍ لا يُقرَأُ لا يَبلُغُ ما يَقرَؤُه. وهذا لا يَجعَلُ خَطَأَ التَحَقُّقِ قِناعاً لِلحارِس (القاعِدَة ٦): استِمارَةٌ بِلا جَلسَةٍ
/// تَبلُغُ الحارِسَ كَما كانَت؛ وما يَتَغَيَّرُ أَنّ <b>مِجَسّاً سُلوكِيّاً لِلحارِسِ يُرسِلُ استِمارَةً لا JSON</b>، وإلّا قاسَ هذِه البَوّابَةَ لا
/// الحارِس (صُحِّحَ مِجَسُّ <c>TenantGatewayEndpointsTests</c> لِذلك).</para>
///
/// <para>(‏2026-10-24 — تَيّارُ S16، يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) <b>وهذا التَرتيبُ انحِرافٌ مُسَمّى عَن نَصِّ القاعِدَة ٦</b> — طَلَبٌ مَجهولٌ بِـJSON
/// يُجابُ ‏415 عَلى المَحروسِ والمَكشوفِ سَواءً — وسَبَبُه وبَديلاه المَرفوضانِ وكَيفَ يُنقَضُ في ‏<c>docs/ADR-037-…</c>.</para>
/// </summary>
public static class FormBodyGate
{
    /// <summary>أَيُقرَأُ هذا الجِسمُ استِمارَة؟ — <c>application/x-www-form-urlencoded</c> أَو <c>multipart/form-data</c>، كَما يَحكُمُ
    /// <see cref="HttpRequest.HasFormContentType"/> نَفسُه (لا قائِمَةٌ ثانِيَةٌ تَنجَرِف).</summary>
    public static bool Admits(HttpRequest req) => req.HasFormContentType;
}

/// <summary>مُرَشِّحُ <see cref="FormBodyGate"/> — ‏415 بِلا جِسمٍ لِما لَيسَ استِمارَة.</summary>
public sealed class FormBodyFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
        => FormBodyGate.Admits(ctx.HttpContext.Request)
            ? next(ctx)
            : ValueTask.FromResult<object?>(Results.StatusCode(StatusCodes.Status415UnsupportedMediaType));
}
