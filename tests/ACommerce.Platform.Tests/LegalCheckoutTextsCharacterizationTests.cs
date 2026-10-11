using System.Text.Json;
using System.Text.RegularExpressions;
using ACommerce.Kit.Payments;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ ثَلاثَةِ نُصوصٍ تَعِدُ بِما لا يوجَد — قَبلَ تَبديلِها (القاعِدَتان ١٢ و١٦) ═══
//
// **المَقيس** (بَقِيَّةُ الموجَةِ ١ وصِغارُ مُراجَعَتِها؛ سِجِلُّ المالِك S1·١ وS1·٢):
// ‏(أ) `legal.privacy.no_selling` يَقولُ عَلى صَفحَةِ خُصوصِيَّةِ **كُلِّ** مَتجَرٍ «تَخزين الدَّفع
// آمِن عَبر مُزَوِّدين مُعتَمَدين (Moyasar / Noon — PCI-DSS)» — ولا مُزَوِّدَ بِطاقاتٍ مُدمَجٌ بِاسمِ
// أَيٍّ مِنهُما، والنَموذَجُ الوَحيدُ الَّذي يَطلُبُ رَقمَ بِطاقَةٍ (`AcPaymentForm`) بِلا مُستَهلِكٍ
// واحِد؛ ‏(ب) `legal.terms.item_subscription_only` يُرسَمُ بَنداً «يوافِقُ عَلَيه» المُشتَري
// تَحتَ «باستِخدامِك المَنصَّة، فَأَنتَ مُوافِق على:» وهو نَموذَجُ إيرادِ المَنَصَّةِ مَعَ المُستَأجِر
// لا شَرطٌ عَلى المُشتَري، ولا مِفتاحَ في `platform.terms.*` يَتَكَلَّمُ في الإيرادِ أَو العَمولَة
// لِيُنقَلَ إلَيه؛ ‏(ج) `checkout.payment.card_hint` يَقولُ «عَبر مُزَوِّد الدَفع المُفَعَّل في هذا
// المَتجَر» والبِطاقَةُ تُنادي `IPaymentProvider` **العامَّ لِلنُسخَة** — رَبطُ المَتجَرِ عَلَمٌ لا مُزَوِّد.
//
// **ما يُقلَبُ في كوميتِ النَصِّ بِاسمِه**: فُحوصُ «اليَوم» الثَلاثَة (القيَمُ القَديمَةُ ومَواضِعُها
// في لَقَطاتِ الأَساس). **وما يَبقى بِحَرفِه** — وهو سَبَبُ أَنّ النُصوصَ لا تَصدُق: الجُملَةُ
// شاهِدُ الامتِثالِ لِلإفصاحِ لِأَطرافٍ ثالِثَة، ولا شاشَةَ تَجمَعُ بَياناتِ بِطاقَة، والبِطاقَةُ
// تَقبِضُ بِمُزَوِّدِ النُسخَة، ولا شَرطَ مَنَصَّةٍ يَتَكَلَّمُ في الإيراد.

public class LegalCheckoutTextsCharacterizationTests
{
    internal const string NoSellingKey        = "legal.privacy.no_selling";
    internal const string SubscriptionOnlyKey = "legal.terms.item_subscription_only";
    internal const string CardHintKey         = "checkout.payment.card_hint";

    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static string Source(params string[] parts)
    {
        var path = Path.Combine(RepoRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياءُ بِلا طَرَفٍ مَقروء.");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 500, $"أَداةٌ عَمياء: {path} طولُه {text.Length} مِحرَفاً — لَم يُقرَأ.");
        return text;
    }

    private static string Template(params string[] parts)
        => Source(new[] { "libs", "templates", "ACommerce.Templates.Customer.Marketplace" }.Concat(parts).ToArray());

    internal static string Baseline(string page)
        => Source("tests", "characterization", "appearance", "i18n-baseline", page);

    private static int Count(string haystack, string needle)
        => Regex.Matches(haystack, Regex.Escape(needle)).Count;

    // ─── (١) «اليَوم» — يُقلَبُ في كوميتِ النَصِّ بِاسمِه ──────────────────────

    /// <summary>صَفحَةُ خُصوصِيَّةِ كُلِّ مَتجَرٍ تَعِدُ بِتَخزينِ دَفعٍ «آمِن» عِندَ بَوّابَتَينِ
    /// لا تَكامُلَ لِأَيٍّ مِنهُما — بِحَرفِ القامُوسِ وبِحَرفِ لَقطَةِ الأَساس (والسَطرُ الجَديدُ
    /// والمَسافاتُ داخِلَ القيمَةِ بَقِيَّةُ تَرحيلٍ مِن سَطرَينِ في الصَفحَة).</summary>
    [Fact]
    public void Today_the_privacy_page_says_payment_storage_is_secure_through_Moyasar_and_Noon_PCI_DSS()
    {
        var value = LocaleCatalog.Find(LocaleCatalog.Arabic, NoSellingKey);
        Assert.Equal(
            "لا نَبيع بَياناتك لِأَيّ طَرَف ثالِث. تَخزين الدَّفع آمِن\n                   عَبر مُزَوِّدين مُعتَمَدين (Moyasar / Noon — PCI-DSS).",
            value);
        Assert.Equal(1, Count(Baseline("ashare-legal-privacy.html"), value!));
    }

    /// <summary>شُروطُ كُلِّ مَتجَرٍ تَجعَلُ المُشتَري «مُوافِقاً عَلى» نَموذَجِ إيرادِ المَنَصَّة —
    /// بَندٌ ثالِثٌ مِن أَربَعَةٍ تَحتَ «فَأَنتَ مُوافِق على:».</summary>
    [Fact]
    public void Today_the_store_terms_make_the_buyer_agree_to_the_platforms_revenue_model()
    {
        var hub = Template("Components", "Pages", "LegalHub.razor");
        var list = Regex.Match(hub, @"<p>@L\.Markup\(""legal\.terms\.intro""\)</p>\s*<ol>(?<items>.*?)</ol>", RegexOptions.Singleline);
        Assert.True(list.Success, "قائِمَةُ الشُروطِ غائِبَةٌ عَن LegalHub.razor.");
        Assert.Equal(4, Count(list.Groups["items"].Value, "<li>"));
        Assert.Contains($"<li>@L.Markup(\"{SubscriptionOnlyKey}\")</li>", list.Groups["items"].Value, StringComparison.Ordinal);

        var value = LocaleCatalog.Find(LocaleCatalog.Arabic, SubscriptionOnlyKey);
        Assert.Equal(
            "الإقرار بِأَنّ المَنصَّة لا تَجمَع عَمولَة على الصَّفقات: إيرادُها اشتِراكُ صاحِب المَتجَر وَحدَه، ولا تَمُرّ بِها أَموالُ الصَّفقات.",
            value);
        Assert.Equal(1, Count(Baseline("ashare-legal-terms.html"), $"<li>{value}</li>"));
    }

    /// <summary>تَلميحُ البِطاقَةِ يُسَمّي «مُزَوِّد الدَفع المُفَعَّل في هذا المَتجَر».</summary>
    [Fact]
    public void Today_the_card_hint_names_a_provider_enabled_in_this_store()
    {
        var value = LocaleCatalog.Find(LocaleCatalog.Arabic, CardHintKey);
        Assert.Equal("دَفعٌ بِالبِطاقَة عَبر مُزَوِّد الدَفع المُفَعَّل في هذا المَتجَر", value);
        Assert.Equal(1, Count(Baseline("member-checkout-pay.html"), $"<span>{value}</span>"));
    }

    // ─── (٢) ما يَبقى بِحَرفِه — لِماذا النُصوصُ الثَلاثَةُ لا تَصدُق ──────────

    /// <summary>
    /// <para><b>جُملَةُ الخُصوصِيَّةِ شاهِدُ امتِثال</b>: تَعريفُ <c>tenant_privacy_art5</c> يَفحَصُ
    /// حُضورَها (<c>text_present</c>) بِرَمزِ <c>third_party_sharing_stated</c> — فَإعادَةُ كِتابَتِها
    /// تُبقي مِفتاحَها حَيّاً وتُبقي قَولَها في الإفصاحِ لِأَطرافٍ ثالِثَة. وتُرسَمُ فِقرَةً واحِدَةً في
    /// صَفحَةِ الخُصوصِيَّة.</para>
    /// </summary>
    [Fact]
    public void The_privacy_sentence_is_the_stores_compliance_evidence_for_third_party_sharing()
    {
        using var doc = JsonDocument.Parse(Source(
            "libs", "kits", "Compliance", "ACommerce.Kit.Compliance.Core", "Definitions", "tenant_privacy_art5.obligation.json"));
        var evidence = doc.RootElement.GetProperty("evidence").EnumerateArray()
            .Single(e => e.GetProperty("target").GetString() == NoSellingKey);
        Assert.Equal("third_party_sharing_stated", evidence.GetProperty("code").GetString());
        Assert.Equal("text_present", evidence.GetProperty("kind").GetString());

        var hub = Template("Components", "Pages", "LegalHub.razor");
        Assert.Equal(1, Count(hub, $"<p>@L.Markup(\"{NoSellingKey}\")</p>"));
    }

    /// <summary>
    /// <para><b>لا شاشَةَ تَجمَعُ بَياناتِ بِطاقَة</b>: النَموذَجُ الوَحيدُ الَّذي يَطلُبُ رَقمَ بِطاقَةٍ
    /// (<c>AcPaymentForm</c>، «نموذج دفع محاكي») لا يَستَهلِكُه <c>.razor</c> واحِدٌ في
    /// <c>libs/</c> ولا <c>apps/</c>. فَالمَنَصَّةُ لا تَرى رَقمَ بِطاقَةٍ لِتُخَزِّنَه.</para>
    /// </summary>
    [Fact]
    public void No_screen_collects_card_data_the_only_card_form_has_no_consumer()
    {
        var form = Source("libs", "widgets", "ACommerce.Widgets", "AcPaymentForm.razor");
        Assert.Contains("CardNumberLabel", form, StringComparison.Ordinal);

        var razors = new[] { "libs", "apps" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(RepoRoot, d), "*.razor", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();
        Assert.True(razors.Length > 100, $"أَداةٌ عَمياء: {razors.Length} مِلَفّاً فَقَط.");
        Assert.Equal(0, razors.Count(f => File.ReadAllText(f).Contains("<AcPaymentForm", StringComparison.Ordinal)));
    }

    /// <summary>
    /// <para><b>البِطاقَةُ تَقبِضُ بِمُزَوِّدِ النُسخَةِ لا بِرَبطِ المَتجَر</b>: الشاشَةُ والنُقطَةُ
    /// تَحقِنانِ <c>IPaymentProvider</c> العامَّ وتُنادي النُقطَةُ <c>AuthorizeAsync</c> عَلَيه؛ ورَبطُ
    /// المَتجَرِ (<c>PaymentProviderConfigured</c>) عَلَمٌ لا يَكفي وَحدَه لِعَرضِ البِطاقَة. والتَلميحُ
    /// يُرسَمُ مَرَّةً واحِدَةً بِجِوارِ بِطاقَةٍ مَعروضَة، ويُخلي مَكانَه لِتَلميحِ التَجرِبَة.</para>
    /// </summary>
    [Fact]
    public void The_card_path_charges_through_the_instances_IPaymentProvider_not_a_store_binding()
    {
        var page = Template("Components", "Pages", "CheckoutPage.razor");
        Assert.Contains("@inject ACommerce.Kit.Payments.IPaymentProvider Payments", page, StringComparison.Ordinal);
        Assert.Contains("bool ProviderCanCharge => PaymentChargeSurface.CanCharge(Payments);", page, StringComparison.Ordinal);
        Assert.Contains("bool CardOffered => CheckoutPaymentPolicy.CardOffered(TenantCollects, ProviderCanCharge);", page, StringComparison.Ordinal);
        Assert.Equal(1, Count(page,
            "@(Simulated ? L[\"checkout.payment.simulation_hint\"] : L[\"checkout.payment.card_hint\"])"));
        Assert.True(page.IndexOf("@if (CardOffered)", StringComparison.Ordinal)
                    < page.IndexOf("L[\"checkout.payment.card_hint\"]", StringComparison.Ordinal));

        var endpoints = Template("MarketplaceTemplateExtensions.cs");
        Assert.Contains("ACommerce.Kit.Payments.IPaymentProvider payments", endpoints, StringComparison.Ordinal);
        Assert.Equal(1, Count(endpoints, "payments.AuthorizeAsync("));

        Assert.False(CheckoutPaymentPolicy.CardOffered(paymentProviderConfigured: true, providerCanCharge: false));
        Assert.True(CheckoutPaymentPolicy.CardOffered(paymentProviderConfigured: true, providerCanCharge: true));
        Assert.False(PaymentChargeSurface.CanCharge(new UnavailablePaymentProvider()));
    }

    /// <summary>
    /// <para><b>ولا شَرطَ مَنَصَّةٍ يَتَكَلَّمُ في الإيرادِ أَو العَمولَة</b> — فَبَندُ «الاشتِراكِ وَحدَه»
    /// لا بَيتَ لَه في <c>platform.terms.*</c> يُنقَلُ إلَيه، والافتِراضُ الأَدنى حَذفُه مِن شُروطِ
    /// المُشتَري لا نَقلُه.</para>
    /// </summary>
    [Fact]
    public void No_platform_terms_key_speaks_about_revenue_or_commission()
    {
        using var doc = JsonDocument.Parse(Source("libs", "core", "ACommerce.Platform.I18n", "Locales", "ar.json"));
        var terms = doc.RootElement.EnumerateObject()
            .Where(p => p.Name.StartsWith("platform.terms.", StringComparison.Ordinal))
            .ToArray();
        Assert.True(terms.Length > 20, $"أَداةٌ عَمياء: {terms.Length} مِفتاحاً فَقَط تَحتَ platform.terms.");

        foreach (var p in terms)
        {
            var v = p.Value.GetString() ?? "";
            foreach (var word in new[] { "عَمولَة", "عمولة", "إيراد" })
                Assert.DoesNotContain(word, v, StringComparison.Ordinal);
        }
    }
}
