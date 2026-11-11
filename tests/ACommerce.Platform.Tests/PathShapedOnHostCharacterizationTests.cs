using ACommerce.Platform.MultiTenancy;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ ما يَنجو قَبلَ أَن تُحَوَّلَ صَفحَةٌ بِشَكلِ المَسارِ عَلى مُضيفِها إلى عُنوانِها النَظيف (‏2026-11-11، الإغلاقُ الأَخيرُ لِـW13-4 · W13-4·C2f·6 —
//     C2z-PATHS-R1-04 وC2z-PATHS-R2-07 وC2z-PATHS-R1-07) ═══
//
// `{slug}.R/{slug}/…` و`{slug}-{role}.R/{slug}/r/{role}/…` تُخدَمُ اليَومَ بِشَكلِها (عَودَةُ استِمارَة، `RelatedUrl` مُخَزَّن، رابِطٌ مَحفوظ) فَيُسَجِّلُ عامِلَ الخِدمَةِ
// ومِفتاحَ التَثبيتِ بِبادِئَةٍ لا يُعلِنُها بَيانُ المُضيف. وما لا يَتَغَيَّرُ حينَ تُحَوَّلُ — بِالقَرارِ الحَقيقيِّ وقَوالِبِ القالِبِ (`RoleHostPages.Templates`) — ويَبقى
// بِبَصمَتِه: وَضعُ المَسارِ والجَذرُ بِشَكلِ المَسار؛ والعُنوانُ النَظيفُ يُحقَن؛ وما لَيسَ صَفحَةً بِشَكلِ المَسارِ يَمُرُّ (بابُ GET، والكِتابَة، والشُروطُ الَّتي
// يَكتُبُها `StoreUrl` بِشَكلِ المَسارِ عَمداً)؛ والـAPI و`/_blazor` و`/realtime` والجُذورُ الساكِنَةُ كَما هي.

public class PathShapedOnHostCharacterizationTests
{
    private const string Apex = "example.test";
    private const string Id = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";

    private static StoreHostAnswer Answer(string? baseDomain, string host, string method, string path, string query = "")
        => TenantResolverMiddleware.AnswerStoreHost(host, baseDomain, method, path, query, "https", RoleHostPages.Templates);

    [Theory]
    [InlineData(null, "localhost", "/ashare/explore")]
    [InlineData(null, "localhost", "/ashare/r/vendor/me")]
    [InlineData(null, "localhost", "/ashare")]
    [InlineData(Apex, Apex, "/ashare/explore")]
    [InlineData(Apex, Apex, "/ashare/deals/" + Id)]
    [InlineData(Apex, Apex, "/ashare/r/vendor/me")]
    public void Path_mode_and_the_apex_path_form_pass_untouched(string? baseDomain, string host, string path)
        => Assert.Equal((StoreHostVerdict.Pass, (string?)null, (string?)null), (Answer(baseDomain, host, "GET", path).Verdict, Answer(baseDomain, host, "GET", path).Slug, Answer(baseDomain, host, "GET", path).Location));

    [Theory]
    [InlineData("ashare.example.test", "/", "/ashare")]
    [InlineData("ashare.example.test", "/explore", "/ashare/explore")]
    [InlineData("ashare.example.test", "/deals/" + Id, "/ashare/deals/" + Id)]
    [InlineData("ashare.example.test", "/r/customer/login", "/ashare/r/customer/login")]
    [InlineData("ashare-vendor.example.test", "/me", "/ashare/r/vendor/me")]
    [InlineData("ashare-vendor.example.test", "/", "/ashare/r/vendor")]
    [InlineData("ashare-admin.example.test", "/manage", "/ashare/r/tenant_admin/manage")]
    public void A_clean_page_on_its_host_is_injected_as_today(string host, string path, string served)
    {
        var a = Answer(Apex, host, "GET", path);
        Assert.Equal((StoreHostVerdict.Rewrite, served), (a.Verdict, a.Path));
    }

    [Theory]
    [InlineData("ashare.example.test", "GET", "/ashare/auth/logout/continue")]
    [InlineData("ashare.example.test", "GET", "/ashare/me/data/download")]
    [InlineData("ashare.example.test", "GET", "/ashare/terms")]
    [InlineData("ashare.example.test", "POST", "/ashare/listings/create")]
    [InlineData("ashare.example.test", "POST", "/ashare/auth/logout")]
    [InlineData("ashare-vendor.example.test", "GET", "/ashare/auth/logout/continue")]
    [InlineData("ashare-vendor.example.test", "GET", "/ashare/terms")]
    [InlineData("ashare-vendor.example.test", "POST", "/ashare/listings/create")]
    public void What_is_not_a_page_or_is_written_in_the_path_form_on_purpose_passes(string host, string method, string path)
        => Assert.Equal(StoreHostVerdict.Pass, Answer(Apex, host, method, path).Verdict);

    [Theory]
    [InlineData("ashare.example.test", "/api/ashare/manifest.json", StoreHostVerdict.Pass)]
    [InlineData("ashare-vendor.example.test", "/api/ashare/r/vendor/manifest.json", StoreHostVerdict.Pass)]
    [InlineData("ashare.example.test", "/_blazor", StoreHostVerdict.Pass)]
    [InlineData("ashare.example.test", "/realtime", StoreHostVerdict.Pass)]
    [InlineData("ashare.example.test", "/sw.js", StoreHostVerdict.Pass)]
    [InlineData("ashare-vendor.example.test", "/sw.js", StoreHostVerdict.Pass)]
    [InlineData("ashare.example.test", "/studio", StoreHostVerdict.Redirect)]
    public void The_api_the_hubs_and_the_static_roots_answer_as_today(string host, string path, StoreHostVerdict verdict)
        => Assert.Equal(verdict, Answer(Apex, host, "GET", path).Verdict);
}
