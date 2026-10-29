using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
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

    [Fact]
    public void Today_the_studio_deals_page_draws_two_demo_entries_to_the_seed_door_under_a_trial_invitation()
    {
        var page = WriteEndpointGuardTests.StripMarkupComments(Source(Template, "Components", "Pages", "StudioAppDeals.razor"));
        const string post = "action=\"@($\"/studio/apps/{Slug}/deals/seed\")\"";
        Assert.Equal(2, Count(page, post));

        var bar = page[page.IndexOf("<TopBarActions>", StringComparison.Ordinal)..page.IndexOf("</TopBarActions>", StringComparison.Ordinal)];
        Assert.Equal(1, Count(bar, post));
        Assert.Contains("@L.Markup(\"studio.app_deals.demo_op\")", bar, StringComparison.Ordinal);

        var empty = page[page.IndexOf("@if (deals.Count == 0)", StringComparison.Ordinal)..];
        empty = empty[..empty.IndexOf("else", StringComparison.Ordinal)];
        Assert.Equal(1, Count(empty, post));
        Assert.Contains("@L.Markup(\"studio.app_deals.empty_hint\")", empty, StringComparison.Ordinal);
        Assert.Contains("@L.Markup(\"studio.app_deals.start\")", empty, StringComparison.Ordinal);

        Assert.Equal("جَرِّب التَّدَفُّق بِإنشاء عَمَلِيَّة تَجريبيَّة:", LocaleCatalog.Find(LocaleCatalog.Arabic, "studio.app_deals.empty_hint"));
        Assert.Equal("+ عَمَلِيَّة تَجريبيَّة", LocaleCatalog.Find(LocaleCatalog.Arabic, "studio.app_deals.demo_op"));
        Assert.Equal("+ ابدَأ", LocaleCatalog.Find(LocaleCatalog.Arabic, "studio.app_deals.start"));
        output.WriteLine("مَدخَلا «العَمَلِيَّة التَجريبِيَّة»: 2 (الشَريطُ والصَفحَةُ الفارِغَة) تَحتَ دَعوَةِ التَجرِبَة");
    }

    [Fact]
    public void Today_the_seed_door_is_registered_and_writes_a_real_deal_of_250_initiated_by_the_app_owner()
    {
        var endpoints = WriteEndpointGuardTests.AllMinimalApiEndpoints().ToList();
        Assert.True(endpoints.Count > 100, $"أَداةٌ عَمياء: عُدَّت {endpoints.Count} نُقطَةً فَقَط.");
        Assert.Single(endpoints, e => e.Route == SeedRoute);

        var source = WriteEndpointGuardTests.StripComments(Source(Template, "MarketplaceTemplateExtensions.cs"));
        var at = source.IndexOf($"app.MapPost(\"{SeedRoute}\"", StringComparison.Ordinal);
        Assert.True(at > 0, "بابُ البَذرَةِ غَيرُ مَوجود.");
        var door = source[at..source.IndexOf(".DisableAntiforgery();", at, StringComparison.Ordinal)];
        Assert.Contains("if (!await StudioOwnsAsync(store, auth, slug)) return Results.Redirect(\"/studio\");", door, StringComparison.Ordinal);
        Assert.Contains("await deals.StartAsync(slug, pattern,", door, StringComparison.Ordinal);
        Assert.Contains("initiatorId: owner, initiatorName: \"صاحِب الفِكرَة\",", door, StringComparison.Ordinal);
        Assert.Contains("listingId: null,", door, StringComparison.Ordinal);
        Assert.Contains("amountSar: 250m);", door, StringComparison.Ordinal);

        var world = new DocWorld();
        var deals = new DealsService(world.Store, new DealMoneyClaimFirstTests.RecordingPaymentProvider());
        var owner = Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee");
        var born = deals.StartAsync("zz-demo", "marketplace", owner, "صاحِب الفِكرَة", null, "عَمَلِيَّة تَجريبيَّة — marketplace", 250m).GetAwaiter().GetResult();
        var stored = world.Read<Deal>(born.Id.ToString())!;
        Assert.Equal((owner, (Guid?)null, 250m, DealStage.Offered, DealStatus.Active),
            (stored.InitiatorId, stored.CounterpartyId, stored.AmountSar, stored.Stage, stored.Status));
        output.WriteLine($"بابُ البَذرَة مُسَجَّل: صَفقَةٌ حَقيقِيَّة {stored.AmountSar} عِندَ {stored.Stage} مُبادِرُها صاحِبُ الفِكرَة");
    }
}
