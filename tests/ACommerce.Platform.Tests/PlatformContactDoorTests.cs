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

    [Fact]
    public async Task Today_every_pricing_card_sends_to_a_contact_page_whose_email_is_an_unfilled_placeholder()
    {
        var pricing = await RenderAsync<PlatformPricing>();
        var contact = await RenderAsync<PlatformContact>();
        var placeholder = LocaleCatalog.IsPlaceholderKey(LocaleCatalog.Arabic, ContactEmailKey);
        var emailLabel = LocaleCatalog.Find(LocaleCatalog.Arabic, "platform.contact.email_label")!;
        output.WriteLine($"باقاتُ الكاتالوج: {PlatformPlanCatalog.All.Count} · أَبوابُ «تَواصَل مَعَنا لِلسِعر»: {ContactCtas(pricing)}" +
                         $" · البَريدُ نائِب: {placeholder} · قِسمُ البَريدِ في /contact: {contact.Contains($"<h2>{emailLabel}</h2>", StringComparison.Ordinal)}" +
                         $" · mailto: {Regex.Matches(contact, "mailto:").Count}");

        Assert.True(PlatformPlanCatalog.All.Count >= 1, "أَداةٌ عَمياء: لا باقَةَ في الكاتالوج.");
        Assert.True(placeholder);
        Assert.Equal(PlatformPlanCatalog.All.Count, ContactCtas(pricing));
        Assert.Contains($"<h2>{emailLabel}</h2>", contact, StringComparison.Ordinal);
        Assert.Contains(LocaleCatalog.Find(LocaleCatalog.Arabic, ContactEmailKey)!, contact, StringComparison.Ordinal);
        Assert.Empty(Regex.Matches(contact, "mailto:"));
    }
}
