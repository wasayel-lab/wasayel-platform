using ACommerce.Kit.Listings;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ إعلانٌ حُذِفَ أَو أَخفاه المُشرِفُ بَعدَ وَضعِه في السَلَّة — أَيُشتَرى؟ وبِأَيِّ سِعر؟ (تَدقيقُ الإطلاق ‏2026-10-24، lens-money-integrity-3) ═══
//
// **المَقيسُ قَبلَ اللَمس** (عِندَ `dace7cd`، ومِن الرَأسِ بَعدَ كوميتِ المُطالَبَة `36b4b5a`): الحَذفُ ناعِم — `Listing.Apply(ListingDeleted)` يَضبِطُ
// `IsDeleted` والوَثيقَةُ باقِيَة، وإخفاءُ المُشرِفِ `IsHiddenByModerator` — فَشَرطُ `listing is null` في حَلقَةِ `/checkout/submit` وفي `cart/add` لا يَصدُقُ
// أَبَداً لِإعلانٍ كانَ. فَيُشتَرى المَحذوفُ والمَخفِيّ، والمَبلَغُ سِعرُ السَلَّةِ (`item.UnitPriceSar`) لا سِعرُ الإعلانِ الآن (مِجَسُّ الشَكّاك: حَذفٌ ثُمَّ
// إرسال ⇒ صَفقَةٌ بِـ200؛ سِعرٌ 100 ← 250 بَعدَ الإضافَة ⇒ صَفقَةٌ بِـ100). و‏ADR-032 §٨‑ح‑١ يَقولُ «النقطةُ تتجاوزُ بندًا حُذِفَ إعلانُه بعدَ وضعِه في السلّة».
//
// **التَوصيفُ قَبلَ التَبديل** (القاعِدَة ٣): صَفّا «اليَوم» أَدناه يُودَعانِ وَحدَهُما، ويُقلَبانِ بِاسمَيهِما في كوميتِ المُراجَعَة.

public class CheckoutCartAvailabilityTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    private static string Source(params string[] parts)
    {
        var path = Path.Combine(RepoRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياء.");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 500, $"أَداةٌ عَمياء: {path} طولُه {text.Length} مِحرَفاً — لَم يُقرَأ.");
        return text;
    }

    private static string Segment(string source, string from, string to)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"‏`{from}` غَيرُ مَوجود — الأَداةُ عَمياء.");
        var end = source.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"‏`{to}` لا يَتلو `{from}` — الأَداةُ عَمياء.");
        return source.Substring(start, end - start);
    }

    private static string Endpoints() => WriteEndpointGuardTests.StripComments(Source(Template, "MarketplaceTemplateExtensions.cs"));

    private static Listing Created(decimal price) => Apply(new Listing(), new ListingCreated(Guid.NewGuid(), "shop", "سِلعَة", null, price, "c", null, null, new(), DateTime.UtcNow));

    private static Listing Apply(Listing l, object e)
    {
        switch (e)
        {
            case ListingCreated c: l.Apply(c); break;
            case ListingDeleted d: l.Apply(d); break;
            case ListingModerated m: l.Apply(m); break;
            case ListingEdited x: l.Apply(x); break;
        }
        return l;
    }

    /// <summary>
    /// <para><b>«اليَوم» في النَموذَج</b>: إعلانٌ حُذِفَ وثانٍ أَخفاه المُشرِفُ وثالِثٌ تَبَدَّلَ سِعرُه — ثَلاثَتُهُم وَثائِقُ قائِمَة (لا <c>null</c>)،
    /// فَلا يُمسِكُها شَرطُ الغِياب.</para>
    /// </summary>
    [Fact]
    public void Today_a_deleted_or_hidden_listing_is_still_a_document_so_a_null_check_never_catches_it()
    {
        var deleted = Apply(Created(200m), new ListingDeleted(Guid.NewGuid(), DateTime.UtcNow));
        var hidden = Apply(Created(200m), new ListingModerated(Guid.NewGuid(), true, "مُخالَفَة", Guid.NewGuid(), DateTime.UtcNow));
        var repriced = Apply(Created(100m), new ListingEdited(Guid.NewGuid(), null, null, 250m, null, null, null, null, DateTime.UtcNow));
        Assert.True(deleted is { IsDeleted: true });
        Assert.True(hidden is { IsHiddenByModerator: true });
        Assert.Equal(250m, repriced.Price);
        output.WriteLine($"مَحذوف: null={deleted is null} IsDeleted={deleted.IsDeleted} · مَخفِيّ: IsHiddenByModerator={hidden.IsHiddenByModerator} · سِعرٌ 100 ← {repriced.Price}");
    }

    /// <summary>
    /// <para><b>«اليَوم» في المَصدَر</b>: حَلقَةُ <c>/checkout/submit</c> تَتَخَطّى الغِيابَ وَحدَه وتَحسُبُ المَبلَغَ مِن سِعرِ السَلَّة، ولا مُراجَعَةَ
    /// لِلسَلَّةِ قَبلَ قَرارِ البِطاقَة؛ و<c>cart/add</c> يَرُدُّ الغِيابَ وَحدَه؛ و‏ADR-032 §٨‑ح‑١ يَقولُ إنّ النُقطَةَ تَتَجاوَزُ المَحذوف.</para>
    /// </summary>
    [Fact]
    public void Today_the_checkout_and_cart_add_refuse_only_an_absent_listing_and_charge_the_cart_price()
    {
        var endpoints = Endpoints();
        var submit = Segment(endpoints, "app.MapPost(\"/{slug}/checkout/submit\"", ".RequireStoreWritable();");
        Assert.Contains("if (listing is null) { line++; continue; }", submit, StringComparison.Ordinal);
        Assert.Contains("var amount = item.UnitPriceSar * item.Quantity;", submit, StringComparison.Ordinal);
        Assert.DoesNotContain("IsDeleted", submit, StringComparison.Ordinal);
        Assert.DoesNotContain("IsHiddenByModerator", submit, StringComparison.Ordinal);
        Assert.DoesNotContain("listing.Price", submit, StringComparison.Ordinal);

        var add = Segment(endpoints, "app.MapPost(\"/{slug}/listings/{id:guid}/cart/add\"", ".RequireStoreWritable();");
        Assert.Contains("if (listing is null) return Results.Redirect(Link(req, slug, \"explore\"));", add, StringComparison.Ordinal);
        Assert.DoesNotContain("IsDeleted", add, StringComparison.Ordinal);

        var adr = Source("docs", "ADR-032-THE-PRODUCT-IS-A-MULTI-VENDOR-STORE-SAAS-PAID-BY-SUBSCRIPTION.md");
        Assert.Contains("النقطةُ تتجاوزُ بندًا حُذِفَ إعلانُه بعدَ وضعِه في السلّة", adr, StringComparison.Ordinal);
        output.WriteLine("الحَلقَةُ وcart/add تَرُدّانِ الغِيابَ وَحدَه، والمَبلَغُ سِعرُ السَلَّة");
    }
}
