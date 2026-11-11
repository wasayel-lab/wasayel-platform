using ACommerce.Platform.MultiTenancy;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ ما يَنجو قَبلَ أَلّا يُحَوَّلَ مَسارٌ بِمَقطَعٍ فارِغٍ عَلى مُضيفِ المَتجَر (‏2026-11-11، الإغلاقُ الأَخيرُ لِـW13-4 · W13-4·C2f-R1·2 —
//     SEC-C2f-R1-2 وC2f-PATHS-R1-02) ═══
//
// `CleanPageOnHost` (C2f·6) يُطابِقُ قالِبَ المَتجَرِ بِلا المَقاطِعِ الفارِغَة، فَـ`/{slug}//me` عَلى مُضيفِه يُحَوَّلُ اليَومَ إلى `Location: //me` — عُنوانٌ بِلا مُخَطَّطٍ
// يَقرَؤُه المُتَصَفِّحُ `https://me/`. وما لا يَتَغَيَّرُ حينَ يُترَكُ المَقطَعُ الفارِغُ كَما كانَ قَبلَ C2f·6 — بِالقَرارِ الحَقيقيِّ وقَوالِبِ القالِب — ويَبقى بِبَصمَتِه:
// (١) تَحويلُ الصَفحَةِ بِشَكلِ المَسارِ بِلا مَقطَعٍ فارِغٍ إلى عُنوانِها النَظيف؛ (٢) جَوابُ HEAD وPOST لِلمَسارِ بِمَقطَعٍ فارِغ (وهُوَ ما يَصيرُه GET)؛ (٣) وَضعُ المَسارِ
// والجَذرُ يَمُرّان؛ (٤) العُنوانُ النَظيفُ بِمَقطَعٍ فارِغٍ يُحقَنُ كَما هُوَ.

public class PathShapedEmptySegmentCharacterizationTests
{
    private const string Apex = "example.test";
    private const string Id = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";

    private static StoreHostAnswer Answer(string? baseDomain, string host, string method, string path, string query = "")
        => TenantResolverMiddleware.AnswerStoreHost(host, baseDomain, method, path, query, "https", RoleHostPages.Templates);

    private static string Shape(StoreHostAnswer a) => $"{a.Verdict} {a.Slug ?? "-"} {a.Path ?? "-"} {a.Location ?? "-"} {a.Role ?? "-"}";

    [Theory]
    [InlineData("ashare.example.test", "/ashare", "", "/")]
    [InlineData("ashare.example.test", "/ashare/", "", "/")]
    [InlineData("ashare.example.test", "/ashare/explore", "?q=x&page=2", "/explore?q=x&page=2")]
    [InlineData("ashare.example.test", "/ashare/me", "", "/me")]
    [InlineData("ashare.example.test", "/ashare/deals/" + Id, "", "/deals/" + Id)]
    [InlineData("ashare.example.test", "/ashare/r/customer/login", "", "/r/customer/login")]
    [InlineData("ashare-vendor.example.test", "/ashare/r/vendor/me", "", "/me")]
    [InlineData("ashare-vendor.example.test", "/ashare/r/vendor", "", "/")]
    [InlineData("ashare-vendor.example.test", "/ashare/r/vendor/", "", "/")]
    [InlineData("ashare-vendor.example.test", "/ashare/me", "", "/me")]
    [InlineData("ashare-vendor.example.test", "/ashare/", "", "/")]
    public void A_path_shaped_page_without_an_empty_segment_is_sent_to_its_clean_url_as_today(string host, string path, string query, string location)
        => Assert.Equal((StoreHostVerdict.Redirect, location), (Answer(Apex, host, "GET", path, query).Verdict, Answer(Apex, host, "GET", path, query).Location));

    [Theory]
    [InlineData("ashare.example.test", "HEAD", "/ashare//me", "Pass ashare - - -")]
    [InlineData("ashare.example.test", "POST", "/ashare//me", "Pass ashare - - -")]
    [InlineData("ashare.example.test", "HEAD", "/ashare//", "Pass ashare - - -")]
    [InlineData("ashare.example.test", "HEAD", "/ashare///me", "Pass ashare - - -")]
    [InlineData("ashare.example.test", "HEAD", "/ashare//login", "Pass ashare - - -")]
    [InlineData("ashare.example.test", "HEAD", "/ashare//deals/" + Id, "Pass ashare - - -")]
    [InlineData("ashare.example.test", "HEAD", "/ashare//r/customer/login", "Pass ashare - - -")]
    [InlineData("ashare.example.test", "HEAD", "/ashare/r/vendor//me", "Pass ashare - - -")]
    [InlineData("ashare.example.test", "HEAD", "/ashare/explore//", "Pass ashare - - -")]
    [InlineData("ashare-vendor.example.test", "HEAD", "/ashare//me", "Rewrite ashare /ashare/r/vendor//me - vendor")]
    [InlineData("ashare-vendor.example.test", "POST", "/ashare//me", "Pass ashare - - vendor")]
    [InlineData("ashare-vendor.example.test", "HEAD", "/ashare/r/vendor//me", "Pass ashare - - vendor")]
    [InlineData("ashare-vendor.example.test", "HEAD", "/ashare/r/vendor//", "Pass ashare - - vendor")]
    [InlineData("ashare-vendor.example.test", "HEAD", "/ashare//r/vendor", "Pass ashare - - vendor")]
    [InlineData("ashare-vendor.example.test", "HEAD", "/ashare/explore//", "Rewrite ashare /ashare/r/vendor/explore// - vendor")]
    public void A_path_with_an_empty_segment_answers_head_and_post_as_today(string host, string method, string path, string shape)
        => Assert.Equal(shape, Shape(Answer(Apex, host, method, path)));

    [Theory]
    [InlineData(null, "localhost", "/ashare//me")]
    [InlineData(null, "localhost", "/ashare/r/vendor//me")]
    [InlineData(Apex, Apex, "/ashare//me")]
    public void Path_mode_and_the_apex_pass_an_empty_segment_untouched(string? baseDomain, string host, string path)
    {
        foreach (var method in new[] { "GET", "HEAD", "POST" })
            Assert.Equal("Pass - - - -", Shape(Answer(baseDomain, host, method, path)));
    }

    [Theory]
    [InlineData("ashare.example.test", "GET", "//me", "Rewrite ashare /ashare//me - -")]
    [InlineData("ashare.example.test", "GET", "//", "Rewrite ashare /ashare// - -")]
    [InlineData("ashare-vendor.example.test", "GET", "//me", "Rewrite ashare /ashare/r/vendor//me - vendor")]
    [InlineData("ashare-vendor.example.test", "POST", "//me", "Rewrite ashare /ashare//me - vendor")]
    public void A_clean_url_with_an_empty_segment_is_injected_as_today(string host, string method, string path, string shape)
        => Assert.Equal(shape, Shape(Answer(Apex, host, method, path)));
}
