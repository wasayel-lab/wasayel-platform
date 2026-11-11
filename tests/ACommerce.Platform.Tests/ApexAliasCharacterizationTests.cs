using ACommerce.Platform.MultiTenancy;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ ما يَنجو قَبلَ أَن يَكُفَّ mail.R عَن خِدمَةِ المَنَصَّة (‏2026-11-11، مُراجَعَةُ إغلاقِ W13-4 الجَولَةُ الثانِيَة · SEC-C2z-R2-2) ═══
//
// **ما سَيَتَغَيَّر**: `mail` يَخرُجُ مِن `TenantResolverMiddleware.ApexAliases` فَيُجابُ 404 كَكُلِّ مِلصَقٍ مَحجوزٍ آخَر — كانَ يَمُرُّ فَيَخدِمُ الاستوديو والإشرافَ ومَتاجِرَ
// المَسارِ عَلى أَصلٍ ثانٍ (مَقيسٌ عِندَ المُراجِع: `mail.localhost:5101/studio` ← 200 بِـSet-Cookie، و`/ashare` ← 200)؛ والقَرارُ سَمّى الجَذرَ و`www` وَحدَهُما.
// **وهذا المِلَفُّ يُثَبِّتُ ما لا يَتَغَيَّرُ** ويَبقى بايتاتِه (القاعِدَة ٣): الجَذرُ و`www` يَمُرّانِ بِكُلِّ مَسارٍ وفِعل؛ وأَخَواتُ `mail` المَحجوزَةُ (smtp، imap، webmail …)
// وسائِرُ المَحجوزِ 404 كَما صارَت في 97ba402؛ ومُضيفُ المَتجَرِ ومُضيفُ دَورِه ومُضيفٌ خارِجَ النِطاقِ كَما هي؛ **وبِلا نِطاقٍ مُهَيَّإٍ لا شَيءَ يَتَغَيَّر** — `mail.R` نَفسُه يَمُرّ.

public class ApexAliasCharacterizationTests(ITestOutputHelper output)
{
    private const string Base = "example.test";

    [Theory]
    [InlineData("example.test", "GET", "/", "Pass")]
    [InlineData("example.test", "GET", "/studio/auth", "Pass")]
    [InlineData("example.test", "GET", "/ashare", "Pass")]
    [InlineData("example.test", "POST", "/admin/tenants/create", "Pass")]
    [InlineData("www.example.test", "GET", "/", "Pass")]
    [InlineData("www.example.test", "GET", "/studio/auth", "Pass")]
    [InlineData("www.example.test", "GET", "/ashare", "Pass")]
    [InlineData("www.example.test", "POST", "/admin/tenants/create", "Pass")]
    [InlineData("WWW.example.test", "GET", "/", "Pass")]
    [InlineData("smtp.example.test", "GET", "/", "NotFound")]
    [InlineData("imap.example.test", "GET", "/studio/auth", "NotFound")]
    [InlineData("webmail.example.test", "GET", "/ashare", "NotFound")]
    [InlineData("autodiscover.example.test", "GET", "/", "NotFound")]
    [InlineData("mx.example.test", "POST", "/admin/tenants/create", "NotFound")]
    [InlineData("admin.example.test", "GET", "/", "NotFound")]
    [InlineData("studio.example.test", "GET", "/ashare", "NotFound")]
    [InlineData("ashare.example.test", "GET", "/", "Rewrite")]
    [InlineData("ashare.example.test", "GET", "/studio", "Redirect")]
    [InlineData("ashare-vendor.example.test", "GET", "/", "Rewrite")]
    [InlineData("mail.ashare.example.test", "GET", "/", "NotFound")]
    [InlineData("mail.other.example", "GET", "/studio", "Pass")]
    public void The_apex_www_and_the_other_reserved_labels_answer_as_they_did(string host, string method, string path, string verdict)
    {
        var a = TenantResolverMiddleware.AnswerStoreHost(host, Base, method, path, "", "https");
        output.WriteLine($"{method} {host}{path} ⇒ {a.Verdict}");
        Assert.Equal(verdict, a.Verdict.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Without_a_domain_every_host_passes_mail_included(string? baseDomain)
    {
        foreach (var host in new[] { "mail.example.test", "www.example.test", "admin.example.test", "localhost:5050" })
        {
            var a = TenantResolverMiddleware.AnswerStoreHost(host, baseDomain, "GET", "/studio", "", "https");
            output.WriteLine($"«{baseDomain}» {host} ⇒ {a.Verdict}");
            Assert.Equal(StoreHostVerdict.Pass, a.Verdict);
        }
    }

    [Fact]
    public void Www_stays_an_apex_alias()
        => Assert.Contains("www", TenantResolverMiddleware.ApexAliases);
}
