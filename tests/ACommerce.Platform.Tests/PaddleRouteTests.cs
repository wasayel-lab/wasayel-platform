using ACommerce.Kit.Payments.Providers.Paddle;
using ACommerce.Templates.Customer.Marketplace.Billing;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>عُنوانُ رِسالَةِ Paddle مَكتوبٌ في ثَلاثَةِ مَواضِعَ لا
/// يَملِك أَحَدُها الآخَر</b>: الثابِتُ في الكود، والحَرفيَّةُ في
/// <c>MapPost</c> (لِيَراها الفاحِصُ النَصِّيّ)، ووَثيقَةُ النَشرِ
/// الَّتي يَنسَخُ مِنها المالِكُ إلى لَوحَةِ Paddle.</para>
///
/// <para><b>وافتِراقُها عَطَبٌ صامِتٌ بِامتِياز</b>: رِسالَةُ الدَفعِ
/// تَذهَب إلى ‏404، فَلا تُمَدَّدُ باقَةٌ ولا يَشتَكي شَيء — والمالُ
/// وَصَل. نَفسُ حُجَّةِ <see cref="PayPalRouteTests"/> حَرفاً.</para>
/// </summary>
public class PaddleRouteTests
{
    private static string Read(string relative)
        => File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, relative));

    private const string EndpointsFile =
        "libs/templates/ACommerce.Templates.Customer.Marketplace/Billing/PaddleEndpoints.cs";

    [Fact]
    public void TheRegisteredLiteral_MatchesTheConstant()
    {
        var source = Read(EndpointsFile);
        Assert.Contains($"MapPost(\"{PaddleEndpoints.WebhookPath}\"", source);
        Assert.Contains($"MapGet(\"{PaddleEndpoints.ConfigPath}\"", source);
    }

    /// <summary>والمَسارُ تَحتَ <c>/api/</c> — أَي أَنّ وَسيطَ
    /// المُستَأجِرِ يَتَخَطّاه، فَلا يُستَعلَم عَن مُستَأجِرٍ اسمُه
    /// «‏api» عِندَ كُلّ رِسالَة. <b>ومَقيسٌ مِن القائِمَةِ نَفسِها لا
    /// مَظنون</b>.</summary>
    [Fact]
    public void TheWebhookPath_FallsUnderAReservedFirstSegment()
    {
        Assert.StartsWith("/api/", PaddleEndpoints.WebhookPath);
        Assert.Null(ACommerce.Platform.MultiTenancy.TenantResolverMiddleware
            .SlugFromPath(PaddleEndpoints.WebhookPath));
    }

    /// <summary>وصَفحَتا الدَفعِ والعَودَةِ ونُقطَةُ الإعدادِ تَحتَ
    /// <c>/billing/</c> — مَقطَعٌ يَتَخَطّاه الوَسيطُ كَذلك، فَلا
    /// يُستَعلَم عَن مُستَأجِرٍ اسمُه «‏billing» عِندَ كُلِّ عَودَةِ
    /// دافِع.</summary>
    [Fact]
    public void TheCheckoutAndReturnPaths_FallUnderAReservedFirstSegment()
    {
        foreach (var path in new[] { PaddleTransactionPolicy.ReturnPath, PaddleEndpoints.ConfigPath })
        {
            Assert.StartsWith("/billing/", path);
            Assert.Null(ACommerce.Platform.MultiTenancy.TenantResolverMiddleware.SlugFromPath(path));
        }
    }

    /// <summary>وصَفحَةُ العَودَةِ مُسَجَّلَةٌ فِعلاً بِنَفسِ المَسارِ
    /// الَّذي يُرسَل إلى Paddle عُنوانَ نَجاح — <b>فَما يُبنى هُوَ ما
    /// يُفتَح</b>. ورابِطُ عَودَةٍ إلى ‏404 يَترُك الدافِعَ على شاشَةِ
    /// عَطَبٍ بَعدَ أَن يَدفَع.</summary>
    [Fact]
    public void TheReturnPage_IsRegisteredAtExactlyThatPath()
        => Assert.Contains(
            $"@page \"{PaddleTransactionPolicy.ReturnPath}\"",
            Read("libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages/PaddleReturn.razor"));

    /// <summary>
    /// <para><b>وصَفحَةُ الدَفعِ الساكِنَةُ مَوجودَةٌ فِعلاً</b> —
    /// وهي «رابِطُ الدَفعِ الافتِراضيّ» عِندَ Paddle. <b>ومِلَفٌّ
    /// غائِبٌ يَعني رابِطاً يُرسَل إلى ‏404 بَعدَ أَن يُنشَأَ
    /// الطَلَب.</b></para>
    ///
    /// <para><b>وتَقرَأُ إعدادَها مِن النُقطَةِ لا مِن نَفسِها</b>:
    /// عُنوانٌ مَكتوبٌ بِاليَدِ في المِلَفِّ يَنجَرِف، ورَمزٌ مَكتوبٌ
    /// فيه يَجعَل نُسخَةَ الاختِبارِ تُنادي مُضيفَ الإنتاج.</para>
    /// </summary>
    [Fact]
    public void TheStaticCheckoutPage_ExistsAndReadsItsConfigFromTheEndpoint()
    {
        var page = Read("apps/V1.App/wwwroot/billing/paddle/checkout.html");

        Assert.Contains(PaddleEndpoints.ConfigPath, page);
        Assert.Contains(PaddleTransactionPolicy.TransactionQueryKey, page);
        Assert.Contains("cdn.paddle.com", page);

        // **ولا رَمزَ ولا سِرَّ مَكتوبٌ فيها** — تُقرَأُ كُلُّها مِن
        // النُقطَة. واختِبارٌ سالِبٌ لِأَنّ السَطرَ الَّذي يُضاف
        // يَوماً «لِلتَجرِبَة» يَبقى.
        Assert.DoesNotContain("pdl_", page);
        Assert.DoesNotContain("live_", page);
        Assert.DoesNotContain("test_", page);
    }

    /// <summary>ووَثيقَةُ النَشرِ تَحمِلُ العُنوانَ حَرفاً — فَما
    /// يَنسَخُه المالِكُ إلى لَوحَةِ Paddle هُوَ ما يُسَجَّل.</summary>
    [Fact]
    public void TheDeployDocument_CarriesTheSameWebhookPath()
        => Assert.Contains(PaddleEndpoints.WebhookPath, Read("docs/DEPLOY.md"));

    /// <summary>وأَسماءُ الأَحداثِ السِتَّةِ الَّتي يُسَجِّلُها
    /// المالِكُ مَكتوبَةٌ في الوَثيقَةِ كَما يَقرَؤُها الكود — واسمٌ
    /// مَنسوخٌ بِخَطَإ حَرفٍ يَعني حَدَثاً لا يَصِل.</summary>
    [Fact]
    public void TheDeployDocument_NamesEveryEventTypeTheCodeActsOn()
    {
        var doc = Read("docs/DEPLOY.md");

        Assert.Equal(6, PaddleEventTypes.All.Count);
        Assert.All(PaddleEventTypes.All, t => Assert.Contains(t, doc));

        // **ومَعجَمٌ مُغلَقٌ يُقاسُ سالِباً كَذلك**: أَسماءٌ قَريبَةٌ
        // شَكلاً لا نَتَصَرَّفُ بِها، فَلا يُشترَك فيها ولا تُحسَبُ
        // مَعروفَة.
        Assert.False(PaddleEventTypes.Handles("transaction.created"));
        Assert.False(PaddleEventTypes.Handles("transaction.paid"));
        Assert.False(PaddleEventTypes.Handles("transaction.updated"));
    }

    /// <summary>ووَثيقَةُ النَشرِ تَحمِل <b>المَدى المَسموحَ
    /// لِلمُدَّة</b> كَما يَقرَؤُه المُصادِق — فَما يَكتُبُه المُشرِفُ
    /// اعتِماداً على الوَثيقَةِ هُوَ ما تَقبَلُه الشاشَة.</summary>
    [Fact]
    public void TheDeployDocument_CarriesTheDeclaredDurationCeiling()
        => Assert.Contains($"1..{PaddleTransactionPolicy.MaxDays}", Read("docs/DEPLOY.md"));

    /// <summary>ومُتَغَيِّراتُ الـSpace الخَمسَةُ مَكتوبَةٌ بِحَرفِها —
    /// <b>مَقروءَةً مِن مِلَفِّ الخِيارات لا مَنسوخَة</b>.</summary>
    [Fact]
    public void TheDeployDocument_NamesEveryEnvironmentVariable()
    {
        var doc = Read("docs/DEPLOY.md");
        foreach (var key in new[]
                 {
                     PaddleEnvironment.EnvironmentKey, PaddleEnvironment.ApiKeyKey,
                     PaddleEnvironment.WebhookSecretKey, PaddleEnvironment.ClientTokenKey,
                     PaddleEnvironment.DefaultPaymentLinkKey
                 })
            Assert.Contains(PaddleEnvironment.EnvVarName(key), doc);
    }

    /// <summary>
    /// <para><b>ومِفتاحُ المَرجِعِ في <c>custom_data</c> واحِدٌ
    /// يَكتُبُه المُنشِئُ ويَقرَؤُه القارِئ</b> — واسمانِ
    /// يَنجَرِفانِ يَجعَلانِ كُلَّ رِسالَةٍ «مَرجِعاً مَجهولاً»
    /// و<b>كُلَّ دَفعَةٍ مالاً وَصَلَ ولا يُعرَف لِمَن</b>.</para>
    ///
    /// <para>ويُقاسُ بِرِحلَةٍ كامِلَة: يُبنى الجِسمُ، ويُسَلسَل،
    /// ويُقرَأ — فَلا يَكفي أَنَّهُما نَفسُ الثابِت.</para>
    /// </summary>
    [Fact]
    public void TheReferenceSurvivesTheRoundTrip_FromCreateBodyToParsedEvent()
    {
        var draft = new PaddleTransactionDraft(
            "ejar", "manual", 49m, "USD", 30, "اشتِراكُ شَهر", "2026-09-08");
        var reference = PaddleTransactionPolicy.Reference(draft);

        var sent = System.Text.Json.JsonSerializer.Serialize(
            PaddleTransactionPolicy.CreateBody(draft, reference));

        // الجِسمُ الصادِرُ يَحمِلُ المَرجِعَ تَحتَ نَفسِ المِفتاح…
        Assert.Contains($"\"{PaddleTransactionPolicy.ReferenceKey}\":\"{reference}\"", sent);

        // …والحَدَثُ العائِدُ يُقرَأُ مِنه بِنَفسِه.
        var back = PaddleBillingPolicy.Parse(
            $"{{\"event_id\":\"evt_1\",\"event_type\":\"transaction.completed\"," +
            $"\"data\":{{\"custom_data\":{{\"{PaddleTransactionPolicy.ReferenceKey}\":\"{reference}\"}}}}}}");

        Assert.Equal(reference, back!.Reference);
    }
}
