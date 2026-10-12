using ACommerce.Kit.Delivery;
using ACommerce.Platform.MultiTenancy;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Shipping;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>دَليلُ النَشرِ يَحمِلُ ما يَقرَؤُه الكودُ حَرفاً</b> (‏ADR-033 الخُطوَتانِ 3 و10؛ `docs/DEPLOY.md` §٢·ط): اسما
/// المُتَغَيِّرَينِ كَما يُتَرجِمُهُما .NET، وعُنوانا البيئَتَين، ومَسارُ الوارِدِ الَّذي يُسَجَّلُ عِندَ OTO. نَفسُ حُجَّةِ
/// <see cref="PaddleRouteTests"/>: اسمٌ مَنسوخٌ بِخَطَإِ حَرفٍ يَعني رَمزاً لا يُقرَأ — فَلا استِمارَةَ ولا سَبَبَ يُرى —
/// وعُنواناً يُسَجَّلُ عِندَ OTO فَتَذهَبُ إشعاراتُه إلى ‏404 بِصَمت.</para>
/// </summary>
public class OtoDeployDocTests
{
    private static string Read(string relative)
        => File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, relative));

    private static string Section()
    {
        var doc = Read("docs/DEPLOY.md");
        var start = doc.IndexOf("## ٢·ط. الشَحنُ عَبرَ OTO", StringComparison.Ordinal);
        var end = doc.IndexOf("\n## ", start + 10, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "فِقرَةُ الشَحنِ غائِبَةٌ عَن دَليلِ النَشر — الأَداةُ عَمياء.");
        return doc[start..end];
    }

    [Fact]
    public void The_deploy_section_names_both_variables_as_dotnet_reads_them()
    {
        var section = Section();
        Assert.Equal("Shipping__Oto__Environment", OtoEnvironment.EnvVarName(OtoEnvironment.EnvironmentKey));
        Assert.Equal("Shipping__Oto__RefreshToken", OtoEnvironment.EnvVarName(OtoEnvironment.RefreshTokenKey));
        Assert.Contains($"`{OtoEnvironment.EnvVarName(OtoEnvironment.EnvironmentKey)}`", section, StringComparison.Ordinal);
        Assert.Contains($"`{OtoEnvironment.EnvVarName(OtoEnvironment.RefreshTokenKey)}`", section, StringComparison.Ordinal);
        Assert.Contains($"`{OtoEnvironment.Staging}`", section, StringComparison.Ordinal);
        Assert.Contains(OtoEnvironment.StagingBaseUrl, section, StringComparison.Ordinal);
        Assert.Contains(OtoEnvironment.ProductionBaseUrl, section, StringComparison.Ordinal);
        // والمَفتوحُ اليَومَ هُوَ ما تَقولُه الفِقرَة: staging وَحدَها.
        Assert.True(OtoEnvironment.IsOpen(OtoEnvironment.Staging));
        Assert.False(OtoEnvironment.IsOpen(OtoEnvironment.Production));
    }

    [Fact]
    public void The_deploy_section_carries_the_inbound_path_that_resolves_to_its_store()
    {
        var section = Section();
        var demoUrlPath = OtoShippingEndpoints.OrderStatusPath.Replace("{slug}", DealShipping.DemoStore, StringComparison.Ordinal);
        Assert.Contains($"`{OtoShippingEndpoints.OrderStatusPath}`", section, StringComparison.Ordinal);
        Assert.Contains(demoUrlPath, section, StringComparison.Ordinal);
        // تَحتَ مَتجَرِه لا تَحتَ مَقطَعٍ مَحجوز: الوَسيطُ يَحُلُّ المُستَأجِرَ مِن المَسارِ نَفسِه.
        Assert.Equal(DealShipping.DemoStore, TenantResolverMiddleware.SlugFromPath(demoUrlPath));
        Assert.Contains("\"webhookType\": \"orderStatus\"", section, StringComparison.Ordinal);
    }

    /// <summary>وما يَقَعُ بِلا ضَبطٍ مَكتوبٌ بِرُموزِه كَما يُطَبِّقُها الكود: الاستِمارَةُ لا تُرسَم، والنُقطَةُ تَرُدُّ
    /// <c>carrier_unavailable</c>، والوارِدُ ‏404 — والرُموزُ مِن ثَوابِتِها لا مَنسوخَة.</summary>
    [Fact]
    public void The_deploy_section_says_absence_hides_with_the_codes_the_code_returns()
    {
        var section = Section();
        Assert.Contains(DealShipAuthorization.CarrierUnavailable, section, StringComparison.Ordinal);
        Assert.Equal(404, OtoOrderStatusGate.StatusFor(OtoOrderStatusGate.CarrierUnavailable));
        Assert.Equal(400, OtoOrderStatusGate.StatusFor(OtoOrderStatusGate.NoticeUnreadable));
        Assert.Contains("‏404", section, StringComparison.Ordinal);
        Assert.Contains("‏`400`", section, StringComparison.Ordinal);
        Assert.Contains("لا تُرسَمُ", section, StringComparison.Ordinal);
        Assert.Contains("ولا «قَريباً»", section, StringComparison.Ordinal);
        Assert.Contains("DealShipAuthorization.IsAllowed", section, StringComparison.Ordinal);
    }

    /// <summary>والمُضيفُ يُسَجِّلُ العَميلَ مِن التَهيئَةِ نَفسِها، ويُحيلُ إلى هذِه الفِقرَة.</summary>
    [Fact]
    public void The_host_registers_the_client_from_configuration_and_points_at_the_section()
    {
        var program = Read("apps/V1.App/Program.cs");
        Assert.Contains("builder.Services.AddOtoShipping(builder.Configuration);", program, StringComparison.Ordinal);
        Assert.Contains("`docs/DEPLOY.md` §٢·ط", program, StringComparison.Ordinal);
    }
}
