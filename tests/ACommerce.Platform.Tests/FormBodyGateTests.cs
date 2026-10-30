using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ طَلَبُ POST بِلا نَوعِ استِمارَةٍ يَرمي عِندَ أَوَّلِ `req.Form[…]` فَيُرَدُّ ‏500 — الصِنفُ كُلُّه لا نُقطَةٌ واحِدَة ═══
//
// **المَقيس** (RELEASE-READINESS §١ البَند ٧ و§٧·١، مُراجَعَةُ S12): `POST …/advance` بِلا جِسمٍ ولا `Content-Type` ⇒ ‏500، لِأَنّ
// `req.Form` يَرمي `InvalidOperationException: Incorrect Content-Type`. والمُتَصَفِّحُ يُرسِلُ نَوعَ الاستِمارَةِ دائِماً فَلا يَبلُغُه —
// دَينُ مَتانَةٍ لا ثَغرَة. **والصِنفُ مَقيسٌ هُنا بِالماسِحِ القائِم** (`WriteEndpointGuardTests.AllMinimalApiEndpoints` — القاعِدَة ٨: ماسِحٌ
// واحِدٌ مَقيس): كُلُّ نُقطَةِ كِتابَةٍ يَقرَأُ جِسمُها الاستِمارَةَ مُباشَرَةً أَو عَبرَ سَطحٍ يَأخُذُ `req` — ولا واحِدَةَ تُعلِنُ بَوّابَةَ نَوعٍ
// قَبلَ القِراءَة. واثنَتانِ تَقبَلانِ الاستِمارَةَ أَو JSON بِقَصدٍ (`HasFormContentType` في الجِسم) فَتُعَدّانِ عَلى حِدَة.
public class FormBodyGateTests(ITestOutputHelper output)
{
    /// <summary>سُطوحٌ تَأخُذُ <c>HttpRequest</c> وتَقرَأُ استِمارَتَه خارِجَ جِسمِ النُقطَة — نِداؤُها قِراءَةُ استِمارَة.</summary>
    internal static readonly string[] FormSurfaceCalls =
    {
        "TenantConfigSurface.Read", "ApiKeySurface.Read(", "ProviderBindSurface.FromForm(",
        "PayPalOrderSurface.DraftFrom(", "PaddleSurface.DraftFrom(", "VendorRegistrationForm.Read(",
    };

    internal static bool ReadsForm(string body) =>
        body.Contains(".Form[", StringComparison.Ordinal) || body.Contains(".Form.", StringComparison.Ordinal)
        || body.Contains("ReadFormAsync", StringComparison.Ordinal)
        || FormSurfaceCalls.Any(c => body.Contains(c, StringComparison.Ordinal));

    /// <summary>تَقبَلُ الاستِمارَةَ أَو JSON بِقَصد — قِراءَتُها لِلاستِمارَةِ تَحتَ <c>HasFormContentType</c>.</summary>
    internal static bool AcceptsJsonToo(string body) => body.Contains("HasFormContentType", StringComparison.Ordinal);

    private List<WriteEndpointGuardTests.Endpoint> FormWriteEndpoints()
    {
        var all = WriteEndpointGuardTests.AllMinimalApiEndpoints().ToList();
        Assert.True(all.Count > 100, $"أَداةٌ عَمياء: {all.Count} نُقطَةً فَقَط.");
        var writes = all.Where(e => System.Text.RegularExpressions.Regex.IsMatch(e.Body, @"^\.Map(?:Post|Put|Delete|Patch)\s*\(")).ToList();
        var form = writes.Where(e => ReadsForm(e.Body)).ToList();
        output.WriteLine($"نِقاط: {all.Count} · كِتابَة: {writes.Count} · تَقرَأُ الاستِمارَة: {form.Count} · مِنها تَقبَلُ JSON أَيضاً: {form.Count(e => AcceptsJsonToo(e.Body))}");
        Assert.True(form.Count > 50, $"أَداةٌ عَمياء: {form.Count} نُقطَةً تَقرَأُ الاستِمارَة.");
        return form;
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-24، S15 C)</b>: كُلُّ نُقطَةٍ تَقرَأُ الاستِمارَةَ — إلّا اللَّتَينِ تَقبَلانِ JSON بِقَصد —
    /// تُعلِنُ <c>.RequireFormBody()</c> <b>أَوَّلَ مُرَشِّحٍ في سِلسِلَتِها</b> (قَبلَ كُلِّ حارِسٍ مُعلَن)، فَلا يَقرَأُ جِسمَها مُرَشِّحٌ
    /// ولا جِسمٌ قَبلَه.</summary>
    [Fact]
    public void Every_form_reading_write_endpoint_declares_the_content_gate_first_in_its_chain()
    {
        var form = FormWriteEndpoints();
        var single = form.Where(e => !AcceptsJsonToo(e.Body)).ToList();
        var gated = single.Count(e => e.Body.Contains(".RequireFormBody(", StringComparison.Ordinal));
        // أَوَّلُ مُرَشِّح: قَبلَ `DisableAntiforgery` (وَسمٌ لا مُرَشِّح) في النِقاطِ العارِيَة، وبَعدَه مُباشَرَةً حَيثُ تَتبَعُه حُرّاس.
        var first = single.Count(e => e.Body.Contains("}).RequireFormBody().DisableAntiforgery();", StringComparison.Ordinal)
                                   || e.Body.Contains("}).DisableAntiforgery().RequireFormBody()", StringComparison.Ordinal));
        output.WriteLine($"تُعلِنُ بَوّابَةَ النَوع: {gated} مِن {single.Count} · أَوَّلَ السِلسِلَة: {first}");
        Assert.Equal(single.Count, gated);
        Assert.Equal(single.Count, first);
        Assert.Equal(80, single.Count);   // ‏2026-10-29 (تَدقيقُ الموجَة ١١ A3): ‏78 ← 80 — `/{slug}/me/contact/remove/start` و`/verify`، كُلٌّ بِبَوّابَةِ النَوعِ أَوَّلَ سِلسِلَتِه. ‏2026-10-29 (د32 «٢»): ‏79 ← 78 — حُذِفَ `/studio/apps/{slug}/deals/seed` («العَمَلِيَّة التَجريبِيَّة») ومَدخَلاه. ‏2026-10-29 (د30): ‏78 ← 79 — `/{slug}/deals/{id}/return` بِبَوّابَةِ النَوعِ أَوَّلَ سِلسِلَتِه. ‏2026-10-29 (د38): ‏77 ← 78 — `/studio/apps/{slug}/deals/{id}/resolve` بِبَوّابَةِ النَوعِ أَوَّلَ سِلسِلَتِه. ‏2026-10-29 (د39 «ب»): ‏78 ← 77 — حُذِفَ `/studio/apps/{slug}/deals/{id}/review`. ‏2026-10-28 (د27): +2 — `/{slug}/me/contact/start` و`/verify`، كُلٌّ بِبَوّابَةِ النَوعِ أَوَّلَ سِلسِلَتِه
        Assert.DoesNotContain(form.Where(e => AcceptsJsonToo(e.Body)), e => e.Body.Contains(".RequireFormBody(", StringComparison.Ordinal));
    }

    // ─── البَوّابَةُ النَقِيَّة — موجِبٌ وسالِب ─────────────────────────────────────────────────────────

    private static Microsoft.AspNetCore.Http.HttpRequest Request(string? contentType)
    {
        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        if (contentType is not null) ctx.Request.ContentType = contentType;
        return ctx.Request;
    }

    [Theory]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData("application/x-www-form-urlencoded; charset=utf-8")]
    [InlineData("multipart/form-data; boundary=----x")]
    public void A_form_body_is_admitted(string contentType)
        => Assert.True(ACommerce.Templates.Customer.Marketplace.Gates.FormBodyGate.Admits(Request(contentType)));

    [Theory]
    [InlineData("application/json")]
    [InlineData("text/plain")]
    [InlineData(null)]
    public void A_body_that_is_not_a_form_is_refused(string? contentType)
        => Assert.False(ACommerce.Templates.Customer.Marketplace.Gates.FormBodyGate.Admits(Request(contentType)));

    /// <summary>والمُرَشِّحُ يَرُدُّ ‏415 بِلا أَن يُنادي النُقطَة — ويُناديها لِلاستِمارَة.</summary>
    [Fact]
    public async Task The_filter_answers_415_without_calling_the_endpoint_and_passes_a_form()
    {
        var filter = new ACommerce.Templates.Customer.Marketplace.Gates.FormBodyFilter();
        var calls = 0;
        Microsoft.AspNetCore.Http.EndpointFilterDelegate next = _ => { calls++; return ValueTask.FromResult<object?>("ok"); };

        var json = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        json.Request.ContentType = "application/json";
        var refused = await filter.InvokeAsync(new Microsoft.AspNetCore.Http.DefaultEndpointFilterInvocationContext(json), next);
        var status = Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IStatusCodeHttpResult>(refused);
        Assert.Equal(415, status.StatusCode);
        Assert.Equal(0, calls);

        var form = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        form.Request.ContentType = "application/x-www-form-urlencoded";
        Assert.Equal("ok", await filter.InvokeAsync(new Microsoft.AspNetCore.Http.DefaultEndpointFilterInvocationContext(form), next));
        Assert.Equal(1, calls);
    }

    /// <summary><b>ويَبقى بِحَرفِه</b>: النُقطَتانِ اللَّتانِ تَقبَلانِ JSON بِقَصدٍ (تَأكيدُ رَمزِ الهاتِفِ والبَريدِ في المَتجَر) تَسأَلانِ
    /// <c>HasFormContentType</c> قَبلَ القِراءَة — فَلا تُغلِقُهُما بَوّابَةُ نَوع.</summary>
    [Fact]
    public void The_two_dual_endpoints_ask_the_content_type_before_reading()
    {
        var dual = FormWriteEndpoints().Where(e => AcceptsJsonToo(e.Body)).Select(e => e.Route).OrderBy(r => r, StringComparer.Ordinal).ToList();
        // ‏(‏2026-10-30، الموجَة ١٢ F1 البَند ٨ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) وثالِثَتُهُما `/{slug}/auth/logout` بِقَرارٍ مَرئيّ: يَقرَأُ `push_endpoint` إن كانَ
        // الجِسمُ استِمارَةً (لِيَمحوَ إشعاراتِ المُتَصَفِّحِ الخارِج)، ويَخرُجُ بِلا قِراءَةٍ لِما سِواها — فَبَوّابَةُ نَوعٍ تُغلِقُه (‏415) كانَت سَتَحبِسُ خُروجاً كانَ يَمُرّ.
        Assert.Equal(new[] { "/{slug}/auth/email/verify", "/{slug}/auth/logout", "/{slug}/auth/phone/verify" }, dual);
    }
}
