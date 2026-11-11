using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.MultiTenancy;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ ما يَنجو قَبلَ أَن تَكُفَّ المُضيفاتُ المَحجوزَةُ عَن خِدمَةِ المَنَصَّة (‏2026-11-11، W13-4·C2z·4 — مُراجَعَةُ C2a · SEC-C2a-4 وSEC-C2a-R2-4
//     وSEC-C2a-R2-3 وC2a-P2-02 وC2a-P3-06 وC2a-P2-03 وC2a-P-08) ═══
//
// **ما سَيَتَغَيَّر**: كُلُّ مِلصَقٍ مَحجوزٍ تَحتَ `R` (admin، studio، api، cdn، blog …) سِوى `www` و`mail` يُجابُ 404 — كانَ نُسخَةً كامِلَةً مِن المَنَصَّةِ عَلى أَصلٍ آخَر؛
// ومُعَرِّفٌ هُوَ مَقطَعُ صَفحَةٍ في المَتجَر (explore، deals، cart، login، me …) أَو أَخٌ حَرفِيٌّ لِـ`{slug}` في مَساراتِ الإشراف (new، create) مَحجوزٌ اسمَ مُضيف؛ ودَورٌ
// يُؤَلِّفُه مَتجَرٌ بِاسمِ مُضيفِ دَورٍ (admin) يُرَدُّ عِندَ الاعتِماد. **وهذا المِلَفُّ يُثَبِّتُ ما لا يَتَغَيَّرُ** ويَبقى بايتاتِه (القاعِدَة ٣).

public class HostsThatServeCharacterizationTests(ITestOutputHelper output)
{
    private const string Base = "example.test";

    [Theory]
    [InlineData("example.test", "/", "Pass")]
    [InlineData("example.test", "/studio", "Pass")]
    [InlineData("www.example.test", "/", "Pass")]
    [InlineData("www.example.test", "/studio", "Pass")]
    [InlineData("mail.example.test", "/", "Pass")]
    [InlineData("ashare.example.test", "/", "Rewrite")]
    [InlineData("ashare.example.test", "/studio", "Redirect")]
    [InlineData("zzz-unknown.example.test", "/", "Rewrite")]
    [InlineData("vendor.ashare.example.test", "/", "NotFound")]
    [InlineData("other.example", "/", "Pass")]
    public void The_apex_its_aliases_and_store_hosts_answer_as_they_did(string host, string path, string verdict)
    {
        var a = TenantResolverMiddleware.AnswerStoreHost(host, Base, "GET", path, "", "https");
        output.WriteLine($"{host}{path} ⇒ {a.Verdict}");
        Assert.Equal(verdict, a.Verdict.ToString());
    }

    [Theory]
    [InlineData("ashare")]
    [InlineData("ejar")]
    [InlineData("theme-demo")]
    [InlineData("owner-test")]
    [InlineData("adwar-demo")]
    [InlineData("rewalk-001530")]
    [InlineData("vendors")]
    [InlineData("news")]
    [InlineData("created")]
    [InlineData("explorer")]
    [InlineData("my-cart")]
    public void Store_names_that_are_not_page_segments_stay_valid_hosts(string slug)
        => Assert.True(StoreHostName.IsValid(slug), string.Join(" ", StoreHostName.Validate(slug).Select(v => v.Code)));

    [Theory]
    [InlineData("best-vendor", "host_name_role_suffix")]
    [InlineData("www", "host_name_reserved")]
    [InlineData("r", "host_name_reserved")]
    [InlineData("push", "host_name_reserved")]
    public void The_codes_already_refused_keep_their_codes(string slug, string code)
        => Assert.Contains(StoreHostName.Validate(slug), v => v.Code == code);

    [Fact]
    public void A_tenant_authored_role_with_its_own_name_still_passes_the_tenant_gate()
    {
        var d = new RoleDefinition
        {
            Slug = "photographer", Icon = "📷", HomeRoute = "/explore",
            Label = new LocalizedText("مُصَوِّر"), Description = new LocalizedText("مُصَوِّرُ مُناسَبات."),
            Permissions = ["listing.browse"], Fields = [], Composition = new RoleComposition(),
        };
        Assert.Empty(RoleDefinitionValidator.ValidateTenantDefinition(d));
        Assert.Contains(RoleDefinitionValidator.ValidateTenantDefinition(d with { Slug = "vendor" }), v => v.Code == "slug_shadows_platform_catalog");
    }
}
