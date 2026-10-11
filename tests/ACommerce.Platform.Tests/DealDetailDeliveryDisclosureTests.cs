using System.Text.RegularExpressions;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ عُنوانُ التَوصيلِ وهاتِفُ المُشتَري — لِلبائِعِ وَحدَه، بِإفصاح (P1-4) ═══════
//
// **المَقيسُ قَبلَ هذا الكوميت** (موجَزُ الفَجَوات P1-4، ومُثَبَّتٌ في
// `DealCashPathCharacterizationTests` ‏5554975): `/checkout/submit` يَكتُبُ
// `["addr"]` و`["phone"]` في `Attributes` كُلِّ صَفقَة، **وصِفرُ** `Attributes[` في
// صَفحاتِ الصَفقَةِ الأَربَع — فَالبائِعُ لا يَعرِفُ إلى أَينَ يَشحَن، والمُشتَري كَتَبَ
// عُنوانَه لِلا أَحَد.
//
// **والقَرار**: تُرسَمُ لِمَن يُسَلِّمُ وَحدَه (الطَرَفُ المُقابِلُ أَو مالِكُ الإعلان) —
// لا لِلمُشتَري (بَياناتُه هُوَ)، ولا لِثالِث (الصَفحَةُ مُغلَقَةٌ عَلى غَيرِ الطَرَفَين) —
// ومَعَها سَطرُ إفصاحٍ مِن القامُوس: لِلتَسليمِ وَحدَه (‏PDPL، `PRODUCTION-PLAN.md:229`).
// والمِفتاحانِ ثابِتانِ يَقرَؤُهُما الكاتِبُ والقارِئُ مَعاً (نَسَقُ `ListingOwnerRef`).
//
// **ولِماذا قِراءَةُ المَصدَر لا تَصييرُ المُكَوِّن**: الحَدُّ المَحروسُ هُوَ «لِمَن
// يُرسَم» — شَرطٌ في `@code` وفَرعٌ في المارك-أَب — والمَصدَرُ يُقاسُ بِلا خادِم؛ وكُلُّ
// مَقطَعٍ يَفشَلُ إن لَم يُوجَد طَرَفاه (القاعِدَة ١٠).

public class DealDetailDeliveryDisclosureTests
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    private static string Source(params string[] parts)
    {
        var path = Path.Combine(RepoRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياءُ بِلا طَرَفٍ مَقروء.");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 500, $"أَداةٌ عَمياء: {path} طولُه {text.Length} مِحرَفاً — لَم يُقرَأ.");
        return text;
    }

    private static string Page() => Source(Template, "Components", "Pages", "MyDealDetail.razor");

    /// <summary>الصَفحَةُ بِلا تَعليقاتِ Razor ولا C# — فَيُقاسُ الكودُ لا الكَلامُ عَنه.</summary>
    private static string PageCode() => WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(Page()));

    private static string CheckoutSubmit()
    {
        var text = Source(Template, "MarketplaceTemplateExtensions.cs");
        var start = text.IndexOf("\"/{slug}/checkout/submit\"", StringComparison.Ordinal);
        Assert.True(start > 0, "نُقطَةُ إتمامِ الشِراءِ غَيرُ مَوجودَة — الأَداةُ عَمياء.");
        var end = text.IndexOf(".RequireStoreWritable();", start, StringComparison.Ordinal);
        return WriteEndpointGuardTests.StripComments(text.Substring(start, end - start));
    }

    /// <summary>مِن أَوَّلِ <c>{</c> بَعدَ المَوضِعِ إلى قَوسِه المُقابِل.</summary>
    private static string BlockFrom(string text, int at)
    {
        var open = text.IndexOf('{', at);
        Assert.True(open > at, "لا كُتلَةَ بَعدَ الشَرط.");
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0)
                return text.Substring(open, i - open + 1);
        }
        Assert.Fail("كُتلَةٌ بِلا قَوسٍ مُقابِل.");
        return "";
    }

    private static int Count(string text, string needle)
    {
        var n = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            n++;
        return n;
    }

    private static readonly string[] Keys =
    {
        "deals.detail.delivery_title",
        "deals.detail.delivery_addr",
        "deals.detail.delivery_phone",
        "deals.detail.delivery_disclosure",
    };

    [Fact]
    public void The_four_keys_exist_in_the_arabic_dictionary_and_the_disclosure_says_for_delivery_only()
    {
        foreach (var key in Keys)
        {
            var text = LocaleCatalog.Text(LocaleCatalog.Arabic, key);
            Assert.NotEqual(key, text);
            Assert.False(LocaleCatalog.IsPlaceholder(text), $"‏{key} نائِب.");
        }
        var disclosure = LocaleCatalog.Text(LocaleCatalog.Arabic, "deals.detail.delivery_disclosure");
        Assert.Contains("لِتَسليم", disclosure, StringComparison.Ordinal);
        Assert.Contains("وَحدَه", disclosure, StringComparison.Ordinal);
    }

    /// <summary><b>لِمَن يُرسَم</b>: الشَرطُ <c>!isInitiator &amp;&amp; isListingOwner</c>
    /// بِحَرفِه، والمَفاتيحُ الأَربَعَةُ داخِلَ فَرعِه <b>ولا مَرَّةً خارِجَه</b> — فَلا
    /// نُسخَةَ لِلمُشتَري ولا لِثالِث.</summary>
    [Fact]
    public void The_delivery_block_is_rendered_inside_the_vendor_branch_and_nowhere_else()
    {
        var page = Page();
        Assert.Contains("deliveryVisible = !isInitiator && isListingOwner;", page, StringComparison.Ordinal);

        var at = page.IndexOf("@if (deliveryVisible)", StringComparison.Ordinal);
        Assert.True(at > 0, "لا فَرعَ `@if (deliveryVisible)` في الصَفحَة — الأَداةُ عَمياء.");
        var block = BlockFrom(page, at);

        foreach (var key in Keys)
        {
            Assert.Equal(1, Count(block, $"\"{key}\""));
            Assert.Equal(Count(block, $"\"{key}\""), Count(page, $"\"{key}\""));
        }
        Assert.Contains("@buyerAddr", block, StringComparison.Ordinal);
        Assert.Contains("@buyerPhone", block, StringComparison.Ordinal);
        Assert.Equal(1, Count(page, "@buyerAddr"));
        Assert.Equal(1, Count(page, "@buyerPhone"));
    }

    /// <summary>وقِراءَةُ <c>Attributes</c> لا تَقَعُ إلّا خَلفَ الحارِسِ نَفسِه في
    /// <c>@code</c> — بِالثابِتَين، لا بِحَرفِيَّةٍ قَد تَنجَرِفُ عَن الكاتِب.</summary>
    [Fact]
    public void The_attributes_are_read_only_behind_the_vendor_guard_and_by_the_shared_constants()
    {
        var code = PageCode();
        // الأَخيرُ لا الأَوَّل: الأَوَّلُ فَرعُ المارك-أَب (`@if`)، والأَخيرُ حارِسُ `@code`.
        var at = code.LastIndexOf("if (deliveryVisible)", StringComparison.Ordinal);
        Assert.True(at > 0 && at > code.IndexOf("@code", StringComparison.Ordinal), "لا حارِسَ `if (deliveryVisible)` في `@code` — الأَداةُ عَمياء.");
        var guarded = BlockFrom(code, at);

        Assert.Contains("deal.Attributes.TryGetValue(Deal.DeliveryAddressAttribute, out var", guarded, StringComparison.Ordinal);
        Assert.Contains("deal.Attributes.TryGetValue(Deal.DeliveryPhoneAttribute, out var", guarded, StringComparison.Ordinal);
        Assert.Equal(Count(guarded, "deal.Attributes"), Count(code, "deal.Attributes"));
        Assert.DoesNotContain("Attributes[\"addr\"]", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Attributes[\"phone\"]", code, StringComparison.Ordinal);
    }

    /// <summary>الكاتِبُ (<c>/checkout/submit</c>) والقارِئُ يَقرَآنِ نَفسَ الحَرفَين.</summary>
    [Fact]
    public void Writer_and_reader_share_the_attribute_letters()
    {
        Assert.Equal("addr", Deal.DeliveryAddressAttribute);
        Assert.Equal("phone", Deal.DeliveryPhoneAttribute);
        var submit = CheckoutSubmit();
        Assert.Contains("[Services.Deals.Deal.DeliveryAddressAttribute] = addr,", submit, StringComparison.Ordinal);
        Assert.Contains("[Services.Deals.Deal.DeliveryPhoneAttribute] = phone,", submit, StringComparison.Ordinal);
        // (قِراءَةُ حَقلَي الاستِمارَةِ `req.Form["addr"]` تَبقى بِاسمَي الحَقلَين — فَالمَقيسُ
        // كِتابَةُ القامُوسِ لا قِراءَةُ الاستِمارَة.)
        Assert.DoesNotContain("[\"addr\"] = addr", submit, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"phone\"] = phone", submit, StringComparison.Ordinal);
    }

    /// <summary>وحَقنُ <c>IDocumentStore</c> المَيِّتُ زال: كانَ يُحقَنُ ولا يُقرَأ
    /// (‏ADR-033 §٢‑هـ القاعِدَة ٩ طَلَبَت حَذفَه حينَ تُتَّخَذُ نَسَقُ الصَفحَة) — والصَفحَةُ
    /// تَحمِّلُ الصَفقَةَ بِخِدمَةٍ ولا تَفتَحُ جَلسَةَ Marten (الطَبَقَة ٨ تَبقى صِفراً).</summary>
    [Fact]
    public void The_dead_document_store_injection_is_gone_and_the_page_opens_no_session()
    {
        var page = Page();
        Assert.DoesNotContain("@inject IDocumentStore", page, StringComparison.Ordinal);
        var code = PageCode();
        Assert.Equal(0, Regex.Matches(code, @"\bStore\s*\.").Count);
        Assert.DoesNotContain("QuerySession(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("LightweightSession(", code, StringComparison.Ordinal);
        Assert.Contains("@inject Services.Deals.DealsService Deals", page, StringComparison.Ordinal);
    }
}
