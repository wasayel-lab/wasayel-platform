using ACommerce.Kit.Payments.Providers.PayPal;
using ACommerce.Kit.Subscriptions;
using ACommerce.Templates.Customer.Marketplace.Billing;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>عُنوانُ الرِسالَةِ مَكتوبٌ في ثَلاثَةِ مَواضِعَ لا يَملِك
/// أَحَدُها الآخَر</b>: الثابِتُ في الكود، والحَرفيَّةُ في
/// <c>MapPost</c> (لِيَراها الفاحِصُ النَصِّيّ)، ووَثيقَةُ النَشرِ
/// الَّتي يَنسَخُ مِنها المالِكُ إلى لَوحَةِ PayPal.</para>
///
/// <para><b>وافتِراقُها عَطَبٌ صامِتٌ بِامتِياز</b>: رِسالَةُ الدَفعِ
/// تَذهَب إلى ‏404، فَلا يُمَدَّدُ اشتِراكٌ ولا يَشتَكي شَيء — والمالُ
/// وَصَلَ. فَالثَلاثَةُ تُقاسُ هُنا، والتَفاوُتُ يُحمِر.</para>
/// </summary>
public class PayPalRouteTests
{
    private static string Read(string relative)
        => File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, relative));

    private const string EndpointsFile =
        "libs/templates/ACommerce.Templates.Customer.Marketplace/Billing/PayPalEndpoints.cs";

    [Fact]
    public void TheRegisteredLiteral_MatchesTheConstant()
    {
        var source = Read(EndpointsFile);
        Assert.Contains($"MapPost(\"{PayPalEndpoints.WebhookPath}\"", source);
    }

    /// <summary>والمَسارُ تَحتَ <c>/api/</c> — أَي أَنّ وَسيطَ
    /// المُستَأجِرِ يَتَخَطّاه، فَلا يُستَعلَم عَن مُستَأجِرٍ اسمُه
    /// «‏api» عِندَ كُلّ رِسالَة. <b>ومَقيسٌ مِن القائِمَةِ نَفسِها لا
    /// مَظنون</b>.</summary>
    [Fact]
    public void TheWebhookPath_FallsUnderAReservedFirstSegment()
    {
        Assert.StartsWith("/api/", PayPalEndpoints.WebhookPath);
        Assert.Null(ACommerce.Platform.MultiTenancy.TenantResolverMiddleware
            .SlugFromPath(PayPalEndpoints.WebhookPath));
    }

    /// <summary>ووَثيقَةُ النَشرِ تَحمِلُ العُنوانَ حَرفاً — فَما
    /// يَنسَخُه المالِكُ إلى لَوحَةِ PayPal هُوَ ما يُسَجَّل.</summary>
    [Fact]
    public void TheDeployDocument_CarriesTheSameWebhookPath()
        => Assert.Contains(PayPalEndpoints.WebhookPath, Read("docs/DEPLOY.md"));

    /// <summary>وأَسماءُ الأَحداثِ الأَربَعَةِ الَّتي يُسَجِّلُها
    /// المالِكُ مَكتوبَةٌ في الوَثيقَةِ كَما يَقرَؤُها الكود — واسمٌ
    /// مَنسوخٌ بِخَطَإ حَرفٍ يَعني حَدَثاً لا يَصِل.</summary>
    [Fact]
    public void TheDeployDocument_NamesEveryEventTypeTheCodeActsOn()
    {
        var doc = Read("docs/DEPLOY.md");
        Assert.All(PayPalEventTypes.All, t => Assert.Contains(t, doc));
    }

    /// <summary>
    /// <para><b>وأَسماءُ أَحداثِ مَسارِ الطَلَباتِ السَبعَةِ كَذلك</b>
    /// (‏ADR-006) — نَفسُ الحَدِّ حَرفاً، ولِنَفسِ السَبَبِ حَرفاً:
    /// اسمٌ يُنسَخ بِخَطَإِ حَرفٍ **يَحمَرُّ هُنا** ولا يُكتَشَف مِن
    /// دَفعَةٍ لَم تُمَدِّد.</para>
    ///
    /// <para><b>و<c>CHECKOUT.ORDER.COMPLETED</c> يُقاسُ سالِباً</b>:
    /// وَصفُه الرَسميّ «‏For use by marketplaces and platforms only» —
    /// فَلا يُشترَك فيه، ووُجودُه في المَعجَمِ يَعني اشتِراكاً في
    /// حَدَثٍ لا يَصِل.</para>
    /// </summary>
    [Fact]
    public void TheDeployDocument_NamesEveryOrderEventTypeTheCodeActsOn()
    {
        var doc = Read("docs/DEPLOY.md");

        Assert.Equal(7, PayPalOrderEventTypes.All.Count);
        Assert.All(PayPalOrderEventTypes.All, t => Assert.Contains(t, doc));
        Assert.False(PayPalOrderEventTypes.Handles("CHECKOUT.ORDER.COMPLETED"));
    }

    /// <summary>ووَثيقَةُ النَشرِ تَحمِل <b>المَدى المَسموحَ
    /// لِلمُدَّة</b> كَما يَقرَؤُه المُصادِق — فَما يَكتُبُه المُشرِفُ
    /// اعتِماداً على الوَثيقَةِ هُوَ ما تَقبَلُه الشاشَة.</summary>
    [Fact]
    public void TheDeployDocument_CarriesTheDeclaredDurationCeiling()
        => Assert.Contains($"1..{PayPalOrderPolicy.MaxDays}", Read("docs/DEPLOY.md"));

    /// <summary>
    /// <para><b>وصَفحَتا العَودَةِ والإلغاءِ تَحتَ مَقطَعٍ مَحجوز</b> —
    /// أَي أَنّ وَسيطَ المُستَأجِرِ يَتَخَطّاهُما، فَلا يُستَعلَم عَن
    /// مُستَأجِرٍ اسمُه «‏billing» عِندَ كُلِّ عَودَةِ دافِع.
    /// <b>ومَقيسٌ مِن القائِمَةِ نَفسِها لا مَظنون.</b></para>
    /// </summary>
    [Fact]
    public void TheReturnAndCancelPages_FallUnderAReservedFirstSegment()
    {
        Assert.StartsWith("/billing/", PayPalOrderPolicy.ReturnPath);
        Assert.StartsWith("/billing/", PayPalOrderPolicy.CancelPath);

        foreach (var path in new[] { PayPalOrderPolicy.ReturnPath, PayPalOrderPolicy.CancelPath })
            Assert.Null(ACommerce.Platform.MultiTenancy.TenantResolverMiddleware.SlugFromPath(path));
    }

    /// <summary>والصَفحَتانِ مُسَجَّلَتانِ فِعلاً بِنَفسِ المَسارِ الَّذي
    /// يُرسَل إلى PayPal — <b>فَما يُبنى في جِسمِ الطَلَبِ هُوَ ما
    /// يُفتَح</b>. ورابِطُ عَودَةٍ إلى ‏404 يَترُك الدافِعَ على شاشَةِ
    /// عَطَبٍ بَعدَ أَن يَدفَع.</summary>
    [Fact]
    public void TheReturnAndCancelPages_AreRegisteredAtExactlyThosePaths()
    {
        const string dir = "libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages/";
        Assert.Contains($"@page \"{PayPalOrderPolicy.ReturnPath}\"", Read(dir + "PayPalReturn.razor"));
        Assert.Contains($"@page \"{PayPalOrderPolicy.CancelPath}\"", Read(dir + "PayPalCancel.razor"));
    }

    /// <summary>ومُتَغَيِّراتُ الـSpace الأَربَعَةُ مَكتوبَةٌ
    /// بِحَرفِها — مَقروءَةً مِن مِلَفِّ الخِيارات لا مَنسوخَةً.</summary>
    [Fact]
    public void TheDeployDocument_NamesEveryEnvironmentVariable()
    {
        var doc = Read("docs/DEPLOY.md");
        foreach (var key in new[]
                 {
                     PayPalEnvironment.ClientIdKey, PayPalEnvironment.ClientSecretKey,
                     PayPalEnvironment.EnvironmentKey, PayPalEnvironment.WebhookIdKey
                 })
            Assert.Contains(PayPalEnvironment.EnvVarName(key), doc);
    }

    /// <summary>
    /// <para><b>الكاتالوجُ يَحمِل الحَقلَ ولا يَحمِل قيمَتَه</b> —
    /// وهذا هُوَ العَقد: <c>paypalPlanId</c> يَملَؤُه المالِكُ يَومَ
    /// يُنشِئ خُطَّتَه بِسِعرِها، ولا سِعرَ يُكتَب في المُستَودَع ولا
    /// يُخمَّن (القاعِدَة ١٦).</para>
    ///
    /// <para><b>ولا زِرَّ يَقول «قَريباً»</b>: بِلا قيمَةٍ لا تُرسَم
    /// بِطاقَةُ PayPal في <c>/admin</c> ولا في الاستوديو
    /// (القاعِدَة ١٢).</para>
    /// </summary>
    [Fact]
    public void EveryPlanDefinition_CarriesTheOptionalPayPalPlanId_AndNoneIsFilledYet()
    {
        Assert.NotEmpty(PlatformPlanCatalog.All);
        Assert.All(PlatformPlanCatalog.All, p => Assert.Null(p.PayPalPlanId));

        // والحَقلُ يُقرَأُ فِعلاً حينَ يُكتَب — فَلا يَكون «مَوجوداً في
        // الصِنفِ ومَجهولاً لِلقارِئ».
        var parsed = PlatformPlanCatalog.ParseDefinition(
            """
            {"slug":"manual","labelAr":"ت","descriptionAr":"و",
             "defaultGraceDays":14,"paypalPlanId":"P-9XYZ"}
            """);
        Assert.Equal("P-9XYZ", parsed.PayPalPlanId);
    }
}
