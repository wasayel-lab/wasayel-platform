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

    /// <summary>
    /// <para><b>الرَمزُ السوقِيُّ في الدَليلِ بِاسمِه كَما يَقرَؤُه .NET، وأَثَرُ غِيابِه بِرَمزَي النُقطَتَين</b> (‏ADR-033 الخُطوَة 10، تَيّارُ S8،
    /// ‏2026-10-12): المُتَغَيِّرُ <c>Shipping__Oto__MarketplaceToken</c> سِرّاً، ومَسارا المَدخَلَينِ حَرفاً، و«غِيابُه يُخفي التَسجيلَ ويُبقي
    /// اللَصق» بِـ<c>vendor_register_closed</c> و<c>vendor_paste_closed</c> كَما تُعيدُهُما الخِدمَة، والنَموذَجُ — حِسابُ البائِعِ ومَحفَظَتُه
    /// وعَقدُه — في فِقرَتِه. والقيَمُ مِن الكودِ لا مَنسوخَة: اسمٌ يَفتَرِقُ يَحمَرُّ هُنا.</para>
    /// </summary>
    [Fact]
    public void The_deploy_section_names_the_marketplace_token_and_says_its_absence_hides_register_and_keeps_paste()
    {
        var section = Section();
        Assert.Equal("Shipping__Oto__MarketplaceToken", OtoEnvironment.EnvVarName(OtoEnvironment.MarketplaceTokenKey));
        Assert.Contains($"| `{OtoEnvironment.EnvVarName(OtoEnvironment.MarketplaceTokenKey)}` |", section, StringComparison.Ordinal);
        Assert.Contains("| **Secret** |", section[section.IndexOf("Shipping__Oto__MarketplaceToken", StringComparison.Ordinal)..],
            StringComparison.Ordinal);
        Assert.Contains("`POST /{slug}/me/shipping/oto`", section, StringComparison.Ordinal);
        Assert.Contains("`POST /{slug}/me/shipping/oto/register`", section, StringComparison.Ordinal);
        Assert.Contains("غِيابُه يُخفي مَسارَ التَسجيلِ ويُبقي اللَصق", section, StringComparison.Ordinal);
        Assert.Contains($"`{ACommerce.Templates.Customer.Marketplace.Services.Shipping.VendorShippingService.RegisterClosed}`", section, StringComparison.Ordinal);
        Assert.Contains($"`{ACommerce.Templates.Customer.Marketplace.Services.Shipping.VendorShippingService.PasteClosed}`", section, StringComparison.Ordinal);
        Assert.Contains("الشَحنُ حِسابُ البائِعِ نَفسِه عِندَ OTO", section, StringComparison.Ordinal);
        foreach (var word in new[] { "مَحفَظَتِه", "عَقدُ النَقلِ", "لا تَمُرُّ بِمالِه" })
            Assert.Contains(word, section, StringComparison.Ordinal);
        Assert.Contains($"`{OtoOrderStatusGate.NoticeUnverifiable}`", section, StringComparison.Ordinal);
        Assert.Contains("‏`401`", section, StringComparison.Ordinal);
    }

    /// <summary>
    /// <para><b>والدَليلُ يَقولُ النافِذَةَ كَما يُطَبِّقُها الكود</b> (‏2026-10-12، مُراجَعَةُ تَيّارِ S8، الجَولَة 1؛ ‏ADR-033 §٧‑ع): لا يُرَدُّ إشعارٌ عَلى عُمرِه،
    /// و<c>signature_stale</c> لِخَتمٍ يَسبِقُ الساعَةَ بِأَكثَرَ مِن خَمسِ ثَوانٍ وَحدَه — والرَمزُ مِن ثابِتِه، والخَمسُ مِن <c>ToleranceSeconds</c>، وكَلِمَةُ اللوغِ
    /// كَما تَكتُبُها الخِدمَة. دَليلٌ يَعودُ إلى «خِلالَ خَمسِ ثَوانٍ» بِلا إلحاقِه، أَو كودٌ يَعودُ إلى الطَرَفَين، يَحمَرُّ أَحَدُهُما هُنا أَو في <c>OtoClientTests</c>.</para>
    /// </summary>
    [Fact]
    public void The_deploy_section_says_the_window_refuses_only_a_stamp_ahead_of_our_clock()
    {
        var section = Section();
        Assert.Equal(5, OtoWebhookSignature.ToleranceSeconds);
        Assert.False(OtoWebhookSignature.IsAhead(600_000));
        Assert.True(OtoWebhookSignature.IsAhead(-5001));
        Assert.Contains($"`{OtoOrderStatusGate.SignatureStale}`", section, StringComparison.Ordinal);
        Assert.Contains("لَم يَعُد شَرطاً عَلى عُمرِ الإشعار", section, StringComparison.Ordinal);
        Assert.Contains("إلّا خَتمٌ يَسبِقُ ساعَتَنا بِأَكثَرَ مِن خَمسِ ثَوانٍ", section, StringComparison.Ordinal);
        Assert.Contains("`← verified (age N ms)`", section, StringComparison.Ordinal);
        var service = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Services", "Deals", "DealShipping.cs"));
        Assert.Contains("← verified (age {Age} ms)", service, StringComparison.Ordinal);
    }
}
