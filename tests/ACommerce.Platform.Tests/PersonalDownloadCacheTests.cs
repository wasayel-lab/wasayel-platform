using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَنزيلُ بَياناتِ صاحِبِها لا يُخَزَّنُ في وَسيط (‏2026-11-07، مُراجَعَةُ عَدَساتِ C1 الجَولَةُ الثانِيَة · W13-1·SEC2-C1-5) ═══
//
// **المَقيسُ قَبل** (تَقريرُ العَدَسَة، رُؤوسُ الجَوابِ الحَيّ): `GET /studio/s/{id}/export.xlsx` ‏200 بِنَوعِ xlsx و`Content-Disposition: attachment`، **بِلا
// `Cache-Control`**؛ وصَفَحاتُ صاحِبِه (`/studio`، `/print`) `no-cache, no-store`، وسابِقَةُ المُستَودَعِ لِتَنزيلِ البَياناتِ الشَخصِيَّةِ `no-store`
// (`/{slug}/me/data/download`). والمَسارُ يَنتَهي بِـ`.xlsx` — امتِدادٌ تُخَزِّنُه طَبَقَةُ Cloudflare افتِراضاً إن لَم يَقُل الأَصلُ شَيئاً، وW13-3 تَضَعُها
// أَمامَ المَنَصَّة: فَحارِسُ «صاحِبُها وَحدَه» يَصدُقُ في الطَلَبِ الأَوَّلِ وَحدَه. **والآن** `no-store, private` قَبلَ المِلَفّ، والصَفُّ يَقرَأُ التَنزيلَين.

public class PersonalDownloadCacheTests(ITestOutputHelper output)
{
    /// <summary>تَنزيلاتُ البَياناتِ الشَخصِيَّةِ الَّتي تُعيدُ مِلَفّاً — كُلُّها تَقولُ <c>no-store</c> قَبلَ <c>Results.File</c>.</summary>
    internal static readonly string[] PersonalDownloads = ["/{slug}/me/data/download", "/studio/s/{id:guid}/export.xlsx"];

    private static readonly Regex CacheControl = new(@"Headers\.CacheControl\s*=\s*""(?<v>[^""]*)""", RegexOptions.Compiled);

    /// <summary>أَيَضبِطُ الجِسمُ <c>Cache-Control</c> بِقيمَةٍ فيها <c>no-store</c> قَبلَ أَوَّلِ <c>Results.File(</c>؟</summary>
    internal static bool SaysNoStoreBeforeTheFile(string body)
    {
        var file = body.IndexOf("Results.File(", StringComparison.Ordinal);
        var cc = CacheControl.Match(body);
        return file > 0 && cc.Success && cc.Index < file
               && cc.Groups["v"].Value.Split(',').Select(v => v.Trim()).Contains("no-store");
    }

    [Fact]
    public void Every_personal_data_download_says_no_store_before_its_file()
    {
        var found = WriteEndpointGuardTests.AllMinimalApiEndpoints().Where(e => PersonalDownloads.Contains(e.Route)).ToList();
        foreach (var e in found) output.WriteLine($"{e.Route}: {(SaysNoStoreBeforeTheFile(e.Body) ? "no-store" : "بِلا no-store")}");
        Assert.Equal(PersonalDownloads.Length, found.Count);
        Assert.All(found, e => Assert.True(SaysNoStoreBeforeTheFile(e.Body), $"{e.Route} يُعيدُ مِلَفّاً بِلا no-store"));
    }

    /// <summary>الماسِحُ يُقاسُ قَبلَ أَن يُوثَقَ بِه (القاعِدَة ١٠).</summary>
    [Theory]
    [InlineData("http.Response.Headers.CacheControl = \"no-store, private\"; return Results.File(b);", true)]
    [InlineData("http.Response.Headers.CacheControl = \"no-store\"; return Results.File(b);", true)]
    [InlineData("return Results.File(b);", false)]
    [InlineData("var r = Results.File(b); http.Response.Headers.CacheControl = \"no-store\"; return r;", false)]
    [InlineData("http.Response.Headers.CacheControl = \"public, max-age=300\"; return Results.File(b);", false)]
    public void The_no_store_scanner_goes_red_on_a_missing_late_or_public_header(string body, bool ok)
        => Assert.Equal(ok, SaysNoStoreBeforeTheFile(body));
}
