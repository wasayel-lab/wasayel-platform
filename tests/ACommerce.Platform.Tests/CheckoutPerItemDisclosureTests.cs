using System.Text.RegularExpressions;
using ACommerce.Platform.I18n;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ خُطوَةُ الدَفعِ تَتَكَلَّمُ عَن «طَلَبٍ» واحِد، والإرسالُ يَبدَأُ صَفقَةً لِكُلِّ بَند (موجَزُ الفَجَوات P3-9؛ ‏ADR-033 §٣‑٩) ═══
//
// **المَقيس** (‏2026-10-13): `POST /{slug}/checkout/submit` يَمشي `cart.Items` ويَبدَأُ لِكُلِّ بَندٍ صَفقَةً بِـ`deals.StartAsync`
// — بِمالِكِ إعلانِها طَرَفاً مُقابِلاً، ومَبلَغِها، وطَريقَةِ دَفعِها — فَكُلُّ بَندٍ يُشحَنُ بِبوليصَتِه (`/deals/{id}/ship` لِكُلِّ صَفقَة)
// ويُدفَعُ نَقدُه عِندَ تَسليمِه. **والصَفقَةُ لِكُلِّ بَندٍ لا لِكُلِّ بائِع**: بَندانِ مِن بائِعٍ واحِدٍ صَفقَتان. وخُطوَةُ الدَفعِ — حَيثُ
// يُختارُ النَقد — تَقولُ «اِدفَع عِندَما يَصِل طَلَبك» بِالمُفرَد، ولا نَصَّ عَن تَعَدُّدِ الشُحناتِ ولا الدَفَعات.

public class CheckoutPerItemDisclosureTests
{
    private static string Template(params string[] parts)
    {
        var path = Path.Combine(new[] { ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace" }
            .Concat(parts).ToArray());
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 2000, $"أَداةٌ عَمياء: {path} لَم يُقرَأ.");
        return text;
    }

    /// <summary>جِسمُ نُقطَةِ الإرسال — مِن تَعريفِها إلى النُقطَةِ الَّتي تَليها.</summary>
    internal static string SubmitBody()
    {
        var ext = Template("MarketplaceTemplateExtensions.cs");
        var at = ext.IndexOf("app.MapPost(\"/{slug}/checkout/submit\"", StringComparison.Ordinal);
        Assert.True(at > 0, "لا نُقطَةَ إرسال — الأَداةُ عَمياء.");
        var end = ext.IndexOf("app.Map", at + 10, StringComparison.Ordinal);
        Assert.True(end > at, "لا نُقطَةَ بَعدَها — الأَداةُ عَمياء.");
        return ext[at..end];
    }

    /// <summary>فَرعُ خُطوَةِ الدَفعِ في <c>CheckoutPage.razor</c> — مِن <c>else if (Step == 2)</c> إلى الفَرعِ الَّذي يَليه.</summary>
    internal static string PaymentStep()
    {
        var page = Template("Components", "Pages", "CheckoutPage.razor");
        var at = page.IndexOf("else if (Step == 2)", StringComparison.Ordinal);
        Assert.True(at > 0, "لا خُطوَةَ دَفع — الأَداةُ عَمياء.");
        var end = page.IndexOf("\n        else\n", at, StringComparison.Ordinal);
        Assert.True(end > at, "لا فَرعَ بَعدَ خُطوَةِ الدَفع — الأَداةُ عَمياء.");
        return page[at..end];
    }

    /// <summary>الحَقيقَةُ الَّتي يَصِفُها النَصّ — صَفقَةٌ لِكُلِّ بَندٍ داخِلَ حَلقَةِ بُنودِ السَلَّة.</summary>
    [Fact]
    public void Submit_starts_one_deal_per_cart_item_not_per_vendor()
    {
        var body = SubmitBody();
        var loop = body.IndexOf("foreach (var item in cart.Items)", StringComparison.Ordinal);
        var start = body.IndexOf("await deals.StartAsync(", StringComparison.Ordinal);
        Assert.True(loop > 0 && start > loop, $"الحَلقَةُ {loop}، البَدءُ {start}.");
        Assert.Single(Regex.Matches(body, Regex.Escape("deals.StartAsync(")));
        Assert.Contains("listingId: item.ListingId", body, StringComparison.Ordinal);
        Assert.DoesNotContain("GroupBy", body, StringComparison.Ordinal);   // ‏لا تَجميعَ بِالبائِع
    }

    /// <summary><b>خُطوَةُ الدَفعِ تُفصِحُ: طَلَبٌ لِكُلِّ مُنتَج، يُشحَنُ وَحدَه، ونَقدُه عِندَ تَسليمِه</b> (‏2026-10-13 — انقَلَبَ بِقَرارٍ
    /// مَرئِيٍّ مِن <c>Today_the_payment_step_speaks_of_one_order_and_discloses_nothing_per_item</c>). المِفتاحُ بِالعَرَبِيَّةِ وَحدَها،
    /// يَقولُ «كُلُّ مُنتَجٍ» لا «كُلُّ بائِع» — لِأَنّ الحَلقَةَ أَعلاه بِالبَند — ويُقرَأُ مَرَّةً في الخُطوَةِ ٢ بَعدَ بِطاقاتِ طُرُقِ الدَفعِ
    /// وخارِجَ أَيِّ شَرط (فَيُرى مَعَ البِطاقَةِ والنَقدِ مَعاً)؛ ونَصُّ النَقدِ لَم يَتَبَدَّل.</summary>
    [Fact]
    public void The_payment_step_discloses_one_order_per_item_where_the_pay_method_is_chosen()
    {
        const string Key = "checkout.payment.per_item_note";
        var text = LocaleCatalog.Find(LocaleCatalog.Arabic, Key);
        Assert.Equal("كُلُّ مُنتَجٍ في سَلَّتِكَ طَلَبٌ مُستَقِلٌّ مَعَ بائِعِه — يُشحَنُ وَحدَه، والدَفعُ عِندَ الاستِلامِ عِندَ تَسليمِ كُلٍّ مِنها.", text);
        Assert.DoesNotContain("بائِعٍ واحِد", text, StringComparison.Ordinal);
        Assert.DoesNotMatch("[0-9٠-٩]", text);
        Assert.Null(LocaleCatalog.Find("en", Key));

        var step = PaymentStep();
        Assert.Single(Regex.Matches(step, Regex.Escape($"@L.Markup(\"{Key}\")")));
        var cards = step.IndexOf("acm-radio-cards", StringComparison.Ordinal);
        var cardsEnd = step.IndexOf("</div>\n                        @*", cards, StringComparison.Ordinal);
        var note = step.IndexOf(Key, StringComparison.Ordinal);
        Assert.True(cards > 0 && cardsEnd > cards && note > cardsEnd, $"البِطاقات {cards}، نِهايَتُها {cardsEnd}، الإفصاح {note}.");

        Assert.Contains("@L.Markup(\"checkout.payment.cash_hint\")", step, StringComparison.Ordinal);
        Assert.Equal("اِدفَع عِندَما يَصِل طَلَبك", LocaleCatalog.Find(LocaleCatalog.Arabic, "checkout.payment.cash_hint"));

        var page = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "CheckoutPage.razor"));
        Assert.Single(Regex.Matches(page, Regex.Escape(Key)));   // ‏في الخُطوَةِ ٢ وَحدَها
    }
}
