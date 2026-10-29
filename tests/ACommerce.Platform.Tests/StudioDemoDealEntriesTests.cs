using ACommerce.Platform.I18n;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ «العَمَلِيَّةُ التَجريبِيَّة» في صَفحَةِ صَفَقاتِ الاستوديو — مَدخَلانِ إلى بابٍ يَكتُبُ صَفقَةً حَقيقِيَّةً تَقِفُ عِندَ «عَرض» ═══
//
// **المَقيسُ عِندَ الرَأس** (`3954046`؛ قَرارُ المالِك د32 «٢» يَنتَظِرُ هذا التَوصيف — القاعِدَة ٣): `StudioAppDeals.razor` تَرسُمُ مَدخَلَينِ يُرسِلانِ إلى
// `POST /studio/apps/{slug}/deals/seed` — زِرُّ «+ عَمَلِيَّة تَجريبيَّة» في الشَريطِ العُلوِيّ (`studio.app_deals.demo_op`) واستِمارَةُ «+ ابدَأ» في الصَفحَةِ
// الفارِغَة (`studio.app_deals.start`) تَحتَ دَعوَةِ «جَرِّب التَّدَفُّق بِإنشاء عَمَلِيَّة تَجريبيَّة:». والبابُ يَكتُبُ صَفقَةً **حَقيقِيَّةً في المَتجَر** بِـ`StartAsync`
// (مَبلَغُها 250، ومُبادِرُها صاحِبُ الفِكرَة نَفسُه، بِلا إعلان) — وتَقِفُ عِندَ «عَرض»: الحارِسُ الَّذي يَمنَعُ المُبادِرَ أَن يَكونَ طَرَفاً مُقابِلاً لِنَفسِه
// يَرُدُّ أَوَّلَ نَقرَةٍ إدارِيَّة (`DealOwnListingCharacterizationTests.The_studio_path_cannot_make_the_app_owner_his_own_counterparty_and_his_demo_deal_stays_at_Offered`).
// فَالمَدخَلانِ يُفضِيانِ إلى صَفقَةٍ لا تَتَحَرَّك (القاعِدَة ١٢). هذا المِلَفُّ يُودَعُ قَبلَ التَبديل، ويُقلَبُ بِاسمِه في كوميتِ القَرار.

// **وقُلِبَ بِاسمَيه** (‏2026-10-29، قَرارُ المالِك د32 «٢»: «يُخفى مدخلا «العمليّة التجريبيّة»… ويُكتب نصّ الصفحة الفارغة بلا دعوة إلى التجربة، ويُحذف باب
// إنشاء العمليّة التجريبيّة نفسه»؛ التَوصيفُ أَعلاه مُودَعٌ في `57fa704`): لا مَدخَلَ في الصَفحَةِ ولا بابَ في جَدوَلِ النِقاط، والمَفتاحانِ `demo_op` و`start` حُذِفا
// (لا قارِئَ لَهُما غَيرُهُما)، ونَصُّ الفَراغِ يَقولُ مَتى تَظهَرُ الصَفَقات. **وشَرحُ مَراحِلِ التَدَفُّقِ في رَأسِ الصَفحَةِ باقٍ كَما هو** (`studio.app_deals.intro`)،
// **والحارِسُ الَّذي يَمنَعُ المُبادِرَ أَن يَكونَ طَرَفاً مُقابِلاً لِنَفسِه لا يُمَسّ** (صَفُّه المَقلوبُ في `DealOwnListingCharacterizationTests`).

public class StudioDemoDealEntriesTests(ITestOutputHelper output)
{
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";
    private const string SeedRoute = "/studio/apps/{slug}/deals/seed";

    private static string Source(params string[] parts)
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياء.");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 500, $"أَداةٌ عَمياء: {path} طولُه {text.Length} مِحرَفاً — لَم يُقرَأ.");
        return text;
    }

    private static int Count(string text, string needle)
    {
        var n = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    /// <summary><b>قُلِبَ بِاسمِه</b> (كانَ <c>Today_the_studio_deals_page_draws_two_demo_entries_to_the_seed_door_under_a_trial_invitation</c>): صِفرُ
    /// استِمارَةٍ إلى البَذرَة في الصَفحَة — لا في الشَريطِ ولا في الفَراغ — والفَراغُ بِنَصٍّ يَقولُ مَتى تَظهَرُ الصَفَقاتُ بِلا دَعوَةٍ إلى التَجرِبَة، والمَفتاحانِ
    /// غائِبانِ عَن القامُوس، وشَرحُ المَراحِلِ في الرَأسِ باقٍ.</summary>
    [Fact]
    public void The_studio_deals_page_draws_no_demo_entry_and_its_empty_text_invites_no_trial()
    {
        var page = WriteEndpointGuardTests.StripMarkupComments(Source(Template, "Components", "Pages", "StudioAppDeals.razor"));
        Assert.Equal(0, Count(page, "deals/seed"));
        Assert.Equal(0, Count(page, "<form"));
        Assert.Contains("@L.Markup(\"studio.app_deals.intro\")", page, StringComparison.Ordinal);

        var bar = page[page.IndexOf("<TopBarActions>", StringComparison.Ordinal)..page.IndexOf("</TopBarActions>", StringComparison.Ordinal)];
        Assert.Contains("@L.Markup(\"studio.app_nav.settings\")", bar, StringComparison.Ordinal);

        var empty = page[page.IndexOf("@if (deals.Count == 0)", StringComparison.Ordinal)..];
        empty = empty[..empty.IndexOf("else", StringComparison.Ordinal)];
        Assert.Contains("@L.Markup(\"studio.app_deals.empty_hint\")", empty, StringComparison.Ordinal);

        var hint = LocaleCatalog.Find(LocaleCatalog.Arabic, "studio.app_deals.empty_hint");
        Assert.Equal("حينَ يَطلُبُ عَميلٌ مِن مَتجَرِك تَظهَرُ صَفقَتُه هُنا بِمَرحَلَتِها.", hint);
        Assert.DoesNotContain("تَجريب", hint, StringComparison.Ordinal);
        Assert.DoesNotContain("جَرِّب", hint, StringComparison.Ordinal);
        foreach (var key in new[] { "studio.app_deals.demo_op", "studio.app_deals.start" })
            Assert.Null(LocaleCatalog.Find(LocaleCatalog.Arabic, key));
        output.WriteLine("مَدخَلا «العَمَلِيَّة التَجريبِيَّة»: 0 · الاستِماراتُ في الصَفحَة 0 · المَفتاحانِ غائِبان · الشَرحُ في الرَأسِ باقٍ");
    }

    /// <summary><b>قُلِبَ بِاسمِه</b> (كانَ <c>Today_the_seed_door_is_registered_and_writes_a_real_deal_of_250_initiated_by_the_app_owner</c>): البابُ غائِبٌ عَن
    /// جَدوَلِ النِقاطِ المَعدود وعَن المَصدَر، ولا نُقطَةَ تَحتَ <c>/studio/</c> تُنادي <c>StartAsync</c> — فَلا تولَدُ صَفقَةٌ مِن الاستوديو بَعدَ اليَوم.</summary>
    [Fact]
    public void The_seed_door_is_gone_and_no_studio_door_births_a_deal()
    {
        var endpoints = WriteEndpointGuardTests.AllMinimalApiEndpoints().ToList();
        Assert.True(endpoints.Count > 100, $"أَداةٌ عَمياء: عُدَّت {endpoints.Count} نُقطَةً فَقَط.");
        Assert.DoesNotContain(endpoints, e => e.Route == SeedRoute);
        Assert.DoesNotContain(endpoints, e => e.Route.EndsWith("/seed", StringComparison.Ordinal));

        var source = WriteEndpointGuardTests.StripComments(Source(Template, "MarketplaceTemplateExtensions.cs"));
        Assert.DoesNotContain(SeedRoute, source, StringComparison.Ordinal);
        Assert.DoesNotContain("initiatorName: \"صاحِب الفِكرَة\"", source, StringComparison.Ordinal);
        var studioStarts = 0;
        foreach (var at in Positions(source, "app.MapPost(\"/studio/"))
        {
            var end = source.IndexOf("app.Map", at + 10, StringComparison.Ordinal);
            if (source[at..(end < 0 ? source.Length : end)].Contains("deals.StartAsync(", StringComparison.Ordinal)) studioStarts++;
        }
        Assert.Equal(0, studioStarts);
        output.WriteLine($"النِقاطُ المَعدودَة {endpoints.Count}: بابُ البَذرَة غائِب · نِقاطُ الاستوديو الَّتي تُولِدُ صَفقَة {studioStarts}");
    }

    private static IEnumerable<int> Positions(string text, string needle)
    {
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
            yield return at;
    }
}
