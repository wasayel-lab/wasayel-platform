using System.Text.RegularExpressions;
using ACommerce.Kit.Subscriptions;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using PlatformContact = ACommerce.Templates.Customer.Marketplace.Components.Pages.Platform.PlatformContact;
using PlatformPricing = ACommerce.Templates.Customer.Marketplace.Components.Pages.Platform.PlatformPricing;

namespace ACommerce.Platform.Tests;

// ═══ «تَواصَل مَعَنا لِلسِعر» في /pricing بابٌ إلى /contact، و/contact بَريدُها نائِبٌ لَم يُملَأ ═══
//
// تَدقيقُ الإطلاق ‏lens-dead-doors-4 (كُبرى، ‏2026-10-24 عِندَ `dace7cd`؛ أُعيدَ قِياسُه عِندَ `eecbb5e`): كُلُّ بِطاقَةِ باقَةٍ في `/pricing` تَرسُمُ
// رابِطاً إلى `/contact` نَصُّه «تَواصَل مَعَنا لِلسِعر»، و`/contact` تَرسُمُ قِسمَ «البَريدُ الإلِكترونيّ» وفيه `platform.doc.contact_email`
// بِنائِبِه `[[ … ]]` — صِفرُ `mailto`. فَالنَقرَةُ تُفضي إلى صَفحَةٍ لا تَحمِلُ طَريقاً (القاعِدَة ١٢). ولا يُنَبَّهُ إلّا المُشرِفُ في `/admin`.
// والإصلاحُ الأَدنى: يُخفى البابُ وقِسمُ البَريدِ ما دامَ `L.IsPlaceholder("platform.doc.contact_email")` — نَسَقُ `LegalHub` حَرفاً.
//
// **يُثَبَّتُ قَبلَ التَبديلِ** (القاعِدَة ٣): الصَفُّ الأَوَّلُ (البَريدُ أَحَدُ مَفاتيحِ الكِيانِ الخَمسَةِ الَّتي يَعُدُّها `/admin`) يَبقى بِلا
// تَعديلِ حَرف؛ والثاني (`Today_…`) يَنقَلِبُ بِاسمِه في كوميتِ الإخفاء.
public class PlatformContactDoorTests(ITestOutputHelper output)
{
    internal const string ContactEmailKey = "platform.doc.contact_email";

    /// <summary>صَفحَةُ مَنَصَّةٍ مَرسومَةً بِـ<c>HtmlRenderer</c> (نَمَطُ <c>UpgradePromptPriceTests</c>) — لا تَحقِنُ إلّا <c>L</c>.</summary>
    internal static async Task<string> RenderAsync<T>() where T : IComponent
    {
        await using var services = new ServiceCollection()
            .AddSingleton(new L(new HttpContextAccessor()))
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<T>(ParameterView.Empty)).ToHtmlString());
    }

    internal static int ContactCtas(string html) => Regex.Matches(html, @"<a class=""wsl-price-cta"" href=""/contact"">").Count;

    [Fact]
    public void The_contact_email_is_one_of_the_five_entity_keys_the_admin_board_counts()
    {
        Assert.Contains(ContactEmailKey, PlatformDocFields.EntityKeys);
        Assert.Equal(5, PlatformDocFields.EntityKeys.Count);
        Assert.NotNull(LocaleCatalog.Find(LocaleCatalog.Arabic, ContactEmailKey));
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-25، كوميتُ الإخفاء)</b> — كانَ
    /// <c>Today_every_pricing_card_sends_to_a_contact_page_whose_email_is_an_unfilled_placeholder</c> (‏<c>34b8023</c>): بابٌ لِكُلِّ باقَةٍ
    /// إلى قِسمِ بَريدٍ نائِب. <b>والآن البابُ وقِسمُ البَريدِ تَحتَ <c>!L.IsPlaceholder("platform.doc.contact_email")</c> وَحدَه</b> — شَرطُهُما
    /// الوَحيدُ بِحَرفِه — والصَفحَتانِ مَرسومَتانِ بِالقامُوسِ كَما هُوَ: نائِبٌ ⇒ صِفرُ بابٍ ولا قِسم؛ ومَملوءٌ ⇒ بابٌ لِكُلِّ باقَةٍ والقِسمُ
    /// بِالبَريد. فَيَصدُقُ الصَفُّ قَبلَ أَن يَملَأَه المالِكُ وبَعدَه، ولا يُعدَّلُ يَومَ يُملَأ.</summary>
    [Fact]
    public async Task The_pricing_door_and_the_contact_email_section_render_only_once_the_email_is_filled()
    {
        var pricingCode = UpgradePromptPriceTests.Code("libs", "templates", "ACommerce.Templates.Customer.Marketplace",
            "Components", "Pages", "Platform", "PlatformPricing.razor");
        var contactCode = UpgradePromptPriceTests.Code("libs", "templates", "ACommerce.Templates.Customer.Marketplace",
            "Components", "Pages", "Platform", "PlatformContact.razor");
        const string Gate = "!L.IsPlaceholder(\"platform.doc.contact_email\")";
        Assert.Contains(Gate, UpgradePromptPriceTests.EnclosingIfConditions(pricingCode,
            pricingCode.IndexOf("platform.pricing.contact_cta", StringComparison.Ordinal)));
        Assert.Equal(new[] { Gate }, UpgradePromptPriceTests.EnclosingIfConditions(contactCode,
            contactCode.IndexOf("platform.contact.email_label", StringComparison.Ordinal)));
        Assert.Single(Regex.Matches(pricingCode, Regex.Escape("href=\"/contact\"")));

        var pricing = await RenderAsync<PlatformPricing>();
        var contact = await RenderAsync<PlatformContact>();
        var placeholder = LocaleCatalog.IsPlaceholderKey(LocaleCatalog.Arabic, ContactEmailKey);
        var emailLabel = LocaleCatalog.Find(LocaleCatalog.Arabic, "platform.contact.email_label")!;
        var section = contact.Contains($"<h2>{emailLabel}</h2>", StringComparison.Ordinal);
        output.WriteLine($"باقاتُ الكاتالوج: {PlatformPlanCatalog.All.Count} · البَريدُ نائِب: {placeholder} · أَبوابُ «تَواصَل مَعَنا لِلسِعر»: {ContactCtas(pricing)}" +
                         $" · قِسمُ البَريد: {section} · حُروفُ /pricing ‏{pricing.Length} و/contact ‏{contact.Length}");

        Assert.True(PlatformPlanCatalog.All.Count >= 1, "أَداةٌ عَمياء: لا باقَةَ في الكاتالوج.");
        Assert.Contains("wsl-price-card", pricing, StringComparison.Ordinal);                     // ‏الصَفحَةُ رُسِمَت
        Assert.Contains(LocaleCatalog.Find(LocaleCatalog.Arabic, "platform.contact.entity_label")!, contact, StringComparison.Ordinal); // ‏والكِيانُ باقٍ
        Assert.Equal(placeholder ? 0 : PlatformPlanCatalog.All.Count, ContactCtas(pricing));
        Assert.Equal(!placeholder, section);
        if (placeholder)
            Assert.DoesNotContain(LocaleCatalog.Find(LocaleCatalog.Arabic, ContactEmailKey)!, contact, StringComparison.Ordinal);
    }

    /// <summary>جِسمُ قِسمِ البَريدِ في <c>/contact</c>: ما بَينَ عُنوانِه وخاتِمَتِه — يُرسَمُ حينَ يُملَأُ البَريدُ وَحدَه.</summary>
    internal static string EmailSection()
    {
        var code = UpgradePromptPriceTests.Code("libs", "templates", "ACommerce.Templates.Customer.Marketplace",
            "Components", "Pages", "Platform", "PlatformContact.razor");
        var at = code.IndexOf("platform.contact.email_label", StringComparison.Ordinal);
        Assert.True(at > 0, "لا قِسمَ بَريدٍ في /contact — الأَداةُ عَمياء.");
        return code[at..code.IndexOf("</section>", at, StringComparison.Ordinal)];
    }

    /// <summary><b>تَوصيف</b> (‏2026-10-25، مُراجَعَةُ تَيّارِ W8-4 الجَولَة ١، صُغرى — القاعِدَة ٣: يَنقَلِبُ بِاسمِه في كوميتِ الإصلاح): قِسمُ البَريدِ
    /// لا يُرسَمُ إلّا حينَ يُملَأُ البَريد — <b>وحينَها</b> يَرسُمُ البَريدَ المَملوءَ داخِلَ <c>span.wsl-placeholder</c> (صُندوقُ «يَملَؤُه المالِك»
    /// الأَحمَرُ المُتَقَطِّع) بِلا <c>mailto</c>: أَوَّلُ يَومٍ يُملَأُ فيه البَريدُ يُرى نائِباً لا طَريقاً.</summary>
    [Fact]
    public void Today_the_filled_contact_email_is_drawn_inside_the_placeholder_box_with_no_mailto()
    {
        var section = EmailSection();
        var boxed = section.Contains("<span class=\"wsl-placeholder\">@L.Markup(\"platform.doc.contact_email\")</span>", StringComparison.Ordinal);
        var mailto = Regex.Matches(section, "mailto:").Count;
        output.WriteLine($"قِسمُ البَريد ({section.Length} مِحرَفاً): البَريدُ في صُندوقِ النائِب: {boxed} · mailto: {mailto}");
        Assert.True(boxed);
        Assert.Equal(0, mailto);
    }

    /// <summary><b>ودَليلُ النَشرِ يُسَمّي الخَمسَةَ قَبلَ الإطلاق</b> (`docs/DEPLOY.md` §٢ الخُطوَة ٧، ‏2026-10-25): كانَ لا يَذكُرُ مِنها
    /// شَيئاً. والقائِمَةُ مِن <see cref="PlatformDocFields.EntityKeys"/> لا مَكتوبَةً هُنا — فَمِفتاحٌ يُضافُ غَداً يُحمِّرُ هذا الصَفَّ حَتّى
    /// يُكتَبَ صَفُّه في الجَدوَل.</summary>
    [Fact]
    public void The_deploy_guide_names_every_platform_entity_key_before_launch()
    {
        var deploy = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "docs", "DEPLOY.md"));
        var at = deploy.IndexOf("### ٧) مَفاتيحُ هُوِيَّةِ المَنَصَّةِ الخَمسَة", StringComparison.Ordinal);
        Assert.True(at > 0, "لا خُطوَةَ ٧ في DEPLOY §٢ — الأَداةُ عَمياء.");
        var end = deploy.IndexOf("\n## ", at, StringComparison.Ordinal);
        var step = deploy[at..end];
        var rows = PlatformDocFields.EntityKeys.Where(k => step.Contains($"| `{k}` |", StringComparison.Ordinal)).ToList();
        output.WriteLine($"مَفاتيحُ الكِيان: {PlatformDocFields.EntityKeys.Count} · صُفوفُها في الخُطوَة ٧: {rows.Count}");
        Assert.Equal(PlatformDocFields.EntityKeys, rows);
        Assert.Contains("PlatformContactDoorTests", step, StringComparison.Ordinal);
    }
}
