using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ إلغاءُ الـAPI يَسأَلُ عَن سَبَبِ الإلغاءِ قَبلَ أَن يَسأَلَ هَل السائِلُ طَرَف (تَدقيقُ الإطلاق ‏2026-10-24، lens-security-10؛ القاعِدَة ٦) ═══
//
// **المَقيسُ عِندَ الرَأس** (`1d2bc48`، وهُوَ ما كانَ عِندَ `dace7cd`): `POST /api/v1/deals/{id}/cancel` يَقرَأُ الجِسمَ ثُمَّ `DealApi.NormalizeCancelReason`
// — فَسَبَبٌ فارِغٌ يُجابُ ‏400 `validation_failed {field: reason}` — ثُمَّ يُحَمِّلُ الصَفقَةَ ويَسأَلُ `DealApi.IsParty` فَيُجيبُ ‏404. فَمِفتاحٌ لا يَملِكُ
// الصَفقَةَ يَسمَعُ «حَقلُ السَبَبِ ناقِص» عَن صَفقَةِ غَيرِه قَبلَ «لا شَيءَ هُنا» — والقاعِدَة ٦ بِحَرفِها: **التَخويلُ يَسبِقُ تَحَقُّقَ الحُقول، وإلّا صارَ خَطَأُ
// التَحَقُّقِ قِناعاً لِلثَغرَة**. و`API-SURFACE-DESIGN.md` §١١٫٦ يَقولُ التَرتيبَ نَفسَه داخِلَ `DealCancelAuthorization.Validate` (الغِياب، ثُمَّ التَخويل،
// ثُمَّ الحالَة) — والنُقطَةُ فَوقَها لَم تَتبَعه.
//
// **التَوصيفُ قَبلَ التَبديل** (القاعِدَة ٣): صَفُّ «اليَوم» يُودَعُ وَحدَه، ويُقلَبُ بِاسمِه في كوميتِ التَرتيب.
//
// **ثُمَّ قُلِبَ بِاسمِه** (‏2026-10-24 — ولا يُعادُ حَرفٌ فَوقَه؛ التَوصيفُ `da36bc4`): التَحميلُ و`IsParty` و‏404 أَوَّلاً، ثُمَّ السَبَبُ و‏400 —
// بِالمَقاييسِ نَفسِها (`Steps`) بِلا تَعديلِ حَرف.

public class ApiDealCancelGuardOrderTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    internal static string ApiCancel()
    {
        var path = Path.Combine(RepoRoot, Template, "Api", "ApiV1Endpoints.cs");
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياء.");
        var text = WriteEndpointGuardTests.StripComments(File.ReadAllText(path));
        Assert.True(text.Length > 500, $"أَداةٌ عَمياء: {path} طولُه {text.Length} مِحرَفاً — لَم يُقرَأ.");
        const string from = "\"/api/v1/deals/{id:guid}/cancel\"";
        const string to = ".RequireApiKey(ApiScopeCatalog.DealsWrite);";
        var start = text.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"‏`{from}` غَيرُ مَوجود — الأَداةُ عَمياء.");
        var end = text.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"‏`{to}` لا يَتلو `{from}` — الأَداةُ عَمياء.");
        return text.Substring(start, end - start);
    }

    /// <summary>مَواضِعُ الخُطُواتِ الأَربَعِ في المَقطَع — وكُلٌّ مَوجودٌ مَرَّةً واحِدَة، وإلّا فَالأَداةُ عَمياء.</summary>
    internal static (int Reason, int ReasonRefusal, int Party, int PartyRefusal) Steps(string cancel)
    {
        int Once(string needle)
        {
            var at = cancel.IndexOf(needle, StringComparison.Ordinal);
            Assert.True(at > 0, $"‏`{needle}` غَيرُ مَوجودٍ في مَقطَعِ الإلغاء — الأَداةُ عَمياء.");
            Assert.Equal(-1, cancel.IndexOf(needle, at + needle.Length, StringComparison.Ordinal));
            return at;
        }
        return (Once("DealApi.NormalizeCancelReason(body?.reason)"),
                Once("ApiOutcome.Error(ApiErrorCatalog.ValidationFailed, new { field = \"reason\" })"),
                Once("!DealApi.IsParty(found, p.ActorUserId)"),
                Once("ApiOutcome.Error(ApiErrorCatalog.NotFound)"));
    }

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (‏2026-10-24، القاعِدَة ٦): كانَ <c>Today_the_api_cancel_validates_the_reason_before_it_asks_whether_the_caller_is_a_party</c>
    /// (<c>da36bc4</c>) — السَبَبُ و‏400 قَبلَ التَحميلِ و<c>IsParty</c> و‏404. <b>وصارَ</b>: غَيرُ الطَرَفِ يُجابُ ‏404 قَبلَ أَن يُقرَأَ حَقلٌ مِن جِسمِه،
    /// والطَرَفُ وَحدَه يَسمَعُ ‏400 عَن سَبَبٍ ناقِص.</para>
    /// </summary>
    [Fact]
    public void The_api_cancel_asks_whether_the_caller_is_a_party_before_it_validates_the_reason()
    {
        var cancel = ApiCancel();
        var (reason, reasonRefusal, party, partyRefusal) = Steps(cancel);
        var load = cancel.IndexOf("await deals.LoadAsync(p.TenantSlug, id, http.RequestAborted)", StringComparison.Ordinal);
        Assert.True(load > 0 && load < party && party < partyRefusal && partyRefusal < reason && reason < reasonRefusal,
            $"load={load} party={party} 404={partyRefusal} reason={reason} 400={reasonRefusal}");
        output.WriteLine($"الإلغاء: التَحميل@{load} → IsParty@{party} → ‏404@{partyRefusal} → السَبَب@{reason} → ‏400@{reasonRefusal}");
    }
}
