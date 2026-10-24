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

    /// <summary><b>تَوصيفُ «اليَوم» — يُقلَبُ بِاسمِه في كوميتِ الإصلاح</b>: لا نُقطَةَ تَقرَأُ الاستِمارَةَ تُعلِنُ بَوّابَةَ نَوعِها.</summary>
    [Fact]
    public void Today_no_form_reading_write_endpoint_declares_a_content_gate()
    {
        var form = FormWriteEndpoints();
        var gated = form.Count(e => e.Body.Contains(".RequireFormBody(", StringComparison.Ordinal));
        output.WriteLine($"تُعلِنُ بَوّابَةَ النَوع: {gated}");
        Assert.Equal(0, gated);
    }

    /// <summary><b>ويَبقى بِحَرفِه</b>: النُقطَتانِ اللَّتانِ تَقبَلانِ JSON بِقَصدٍ (تَأكيدُ رَمزِ الهاتِفِ والبَريدِ في المَتجَر) تَسأَلانِ
    /// <c>HasFormContentType</c> قَبلَ القِراءَة — فَلا تُغلِقُهُما بَوّابَةُ نَوع.</summary>
    [Fact]
    public void The_two_dual_endpoints_ask_the_content_type_before_reading()
    {
        var dual = FormWriteEndpoints().Where(e => AcceptsJsonToo(e.Body)).Select(e => e.Route).OrderBy(r => r, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "/{slug}/auth/email/verify", "/{slug}/auth/phone/verify" }, dual);
    }
}
