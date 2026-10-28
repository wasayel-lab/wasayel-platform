using System.Net;
using System.Text.RegularExpressions;
using ACommerce.Platform.Hosting;
using ACommerce.Templates.Customer.Marketplace.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ د57 «ب»: عُنوانُ الزائِرِ كَما يَراه الخادِم — قِياسٌ لِمُشرِفِ المَنَصَّةِ قَبلَ أَيِّ حَدٍّ لِكُلِّ عُنوان (‏2026-10-28) ═══
//
// قَرارُ المالِك: «يُقاسُ أَوَّلاً هَل يُمَرِّرُ خادِمُ Hugging Face عُنوانَ الزائِرِ الحَقيقيّ… وإن ظَهَرَ أَنَّ العُنوانَ يُمكِنُ تَزويرُه فَلا يُبنى هذا
// الحَدّ». فَهُنا: الدالَّةُ تَقرَأُ ولا تَكتُب، والوَسيطُ نَفسُه (‏ForwardedHeadersMiddleware بِسِياسَةِ المَنَصَّةِ) يُقاسُ بِالحالَتَينِ اللَّتَينِ سَيُفَرِّقُ
// بَينَهُما القِياسُ الحَيّ، والبابُ لِمُشرِفِ المَنَصَّةِ وَحدَه قَبلَ أَيِّ قِراءَة.
public class ClientAddressProbeTests(ITestOutputHelper output)
{
    private static string Mte() => WriteEndpointGuardTests.StripComments(StudioConsentCharacterizationTests.Source(
        "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "MarketplaceTemplateExtensions.cs"));

    /// <summary>مُوجَبٌ وسالِب: يَقرَأُ العُنوانَ والرَأسَينِ كَما هِيَ، والغائِبُ والفارِغُ <c>null</c>.</summary>
    [Fact]
    public void The_probe_reports_the_address_and_both_headers_as_they_are_and_absent_ones_as_null()
    {
        var full = new DefaultHttpContext();
        full.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.7");
        full.Request.Headers["X-Forwarded-For"] = "203.0.113.9";
        full.Request.Headers["X-Original-For"] = "10.0.0.1:443";
        var bare = new DefaultHttpContext();
        bare.Request.Headers["X-Forwarded-For"] = "  ";

        var a = ClientAddressProbe.Of(full.Request);
        var b = ClientAddressProbe.Of(bare.Request);
        output.WriteLine($"مَملوء: {a} · فارِغ: {b}");

        Assert.Equal(new ClientAddressProbe.Report("198.51.100.7", "203.0.113.9", "10.0.0.1:443"), a);
        Assert.Equal(new ClientAddressProbe.Report(null, null, null), b);
    }

    /// <summary><b>ما سَيَقرَؤُه المالِكُ في الحالَتَين</b> — بِوَسيطِ المَنَصَّةِ نَفسِه (‏<c>ForwardedHeadersPolicy.ApplyTo</c>، ‏<c>ForwardLimit</c> = ‏1):
    /// (١) وَسيطٌ <b>يُلحِقُ</b> عُنوانَ النِدِّ بِرَأسٍ زَوَّرَه الزائِر (<c>203.0.113.9, 198.51.100.7</c>) ⇒ العُنوانُ الحَقيقيُّ ويَبقى المُزَوَّرُ في الرَأس؛
    /// (٢) وَسيطٌ <b>يُمَرِّرُ</b> رَأسَ الزائِرِ وَحدَه (<c>203.0.113.9</c>) ⇒ العُنوانُ المُزَوَّرُ نَفسُه ولا رَأسَ باقٍ. والفَرقُ هُوَ ما يَحسِمُ حَدَّ العُنوان.</summary>
    [Fact]
    public async Task Behind_the_platforms_forwarded_headers_an_appending_proxy_and_a_passing_proxy_read_differently()
    {
        var opts = new ForwardedHeadersOptions();
        ForwardedHeadersPolicy.FromConfiguration(null).ApplyTo(opts);

        async Task<ClientAddressProbe.Report> Through(string xff)
        {
            ClientAddressProbe.Report? seen = null;
            var mw = new ForwardedHeadersMiddleware(ctx => { seen = ClientAddressProbe.Of(ctx.Request); return Task.CompletedTask; },
                NullLoggerFactory.Instance, Options.Create(opts));
            var http = new DefaultHttpContext();
            http.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
            http.Request.Headers["X-Forwarded-For"] = xff;
            await mw.Invoke(http);
            return seen!;
        }

        var appending = await Through("203.0.113.9, 198.51.100.7");
        var passing = await Through("203.0.113.9");
        output.WriteLine($"يُلحِق: {appending} · يُمَرِّر: {passing}");

        Assert.Equal("198.51.100.7", appending.RemoteIpAddress);
        Assert.Equal("203.0.113.9", appending.XForwardedFor);
        Assert.Equal("203.0.113.9", passing.RemoteIpAddress);
        Assert.Null(passing.XForwardedFor);
        Assert.All(new[] { appending, passing }, r => Assert.StartsWith("10.0.0.1", r.XOriginalFor, StringComparison.Ordinal));
    }

    /// <summary><b>البابُ لِمُشرِفِ المَنَصَّةِ قَبلَ أَيِّ قِراءَة، ولا يَكتُب</b>: سَطرُه الأَوَّلُ <c>PlatformAdminGuard.EvaluateAsync()</c> (نُسخَةُ الحَقن) ثُمَّ <c>Forbidden()</c>،
    /// ثُمَّ <c>no-store</c> والدالَّة؛ ولا <c>Store</c> ولا <c>SaveChanges</c> ولا <c>Append</c> ولا حِصَّة.</summary>
    [Fact]
    public void The_door_is_platform_admin_only_before_it_reads_and_writes_nothing()
    {
        var mte = Mte();
        var at = mte.IndexOf("app.MapGet(\"/admin/diagnostics/client-address\"", StringComparison.Ordinal);
        Assert.True(at > 0, "لا بابَ لِلقِياس — الأَداةُ عَمياء.");
        var body = mte[at..mte.IndexOf("});", at, StringComparison.Ordinal)];
        var guard = body.IndexOf("if (!(await admin.EvaluateAsync()).Allowed)", StringComparison.Ordinal);
        var refuse = body.IndexOf("return Forbidden();", StringComparison.Ordinal);
        var read = body.IndexOf("Services.ClientAddressProbe.Of(req)", StringComparison.Ordinal);
        output.WriteLine($"الحارِس@{guard} الرَفض@{refuse} القِراءَة@{read} · طولُ الجِسم {body.Length}");

        Assert.True(guard > 0 && refuse > guard && read > refuse);
        Assert.Contains("Services.PlatformAdminGuard admin) =>", body, StringComparison.Ordinal);
        Assert.DoesNotContain("IDocumentStore", body, StringComparison.Ordinal);
        Assert.Contains("res.Headers.CacheControl = \"no-store\";", body, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"\.Store\(|SaveChanges|Events\.Append|TryConsume"), body);
        Assert.Single(Regex.Matches(mte, Regex.Escape("Services.ClientAddressProbe.Of(")));
    }
}
