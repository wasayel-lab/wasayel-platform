using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ أَسطُرُ الدَفعِ الأَربَعَةُ في سِجِلِّ الصَفقَةِ تُرسَمُ بِالإنكليزِيَّة — والأَوَّلُ يُكتَبُ «captured» ولَو لَم تَقُل الحالُ إنّه حُصِّل ═══
//
// قَرارُ المالِك د22 (١) «ب» (‏2026-10-29): «تُسمّى بالحال المسجّلة في السطر نفسه، بلغة بسيطة: «تمّ تحصيل الدفع» حين تقول الحال إنه حُصِّل،
// و«تمّ ردّ المبلغ» حين تقول إنه رُدّ، و«تمّ ردّ جزء من المبلغ» للجزئيّ، و«تعذّر تحصيل الدفع» أو «تعذّر ردّ المبلغ» لما سوى ذلك وللسطرين
// الفاشلين». **تَوصيفُ «اليَوم» قَبلَ التَبديل** (القاعِدَة ٣): `DealsService` يَكتُبُ `captured` بِمُلاحَظَةِ «capture {الحال} …» كُلَّما عادَ
// `CaptureAsync` بِلا استِثناء، و`refunded` بِـ«refund {الحال} …»، و`capture_failed`/`refund_failed` بِنَصِّ الاستِثناء؛ و`FormatEvent` في
// `MyDealDetail.razor` لا ذِراعَ لَه لِأَيٍّ مِنها فَيَسقُطُ إلى `e.Action` — فَيَقرَأُ المُشتَري «captured». يُقلَبُ الصَفُّ بِاسمِه في كوميتِ التَبديل.
public class DealPaymentLineLabelTests(ITestOutputHelper output)
{
    internal static readonly string[] PaymentActions = ["captured", "capture_failed", "refunded", "refund_failed"];

    internal static string Page() => UpgradePromptPriceTests.Code("libs", "templates", "ACommerce.Templates.Customer.Marketplace",
        "Components", "Pages", "MyDealDetail.razor");

    internal static string Deals() => UpgradePromptPriceTests.Code("libs", "templates", "ACommerce.Templates.Customer.Marketplace",
        "Services", "Deals", "DealsService.cs");

    /// <summary>جِسمُ <c>FormatEvent</c> — مِن تَوقيعِه إلى إغلاقِ المُفتاح.</summary>
    internal static string FormatEventBody()
    {
        var page = Page();
        var at = page.IndexOf("string FormatEvent(DealEvent e)", StringComparison.Ordinal);
        Assert.True(at > 0, "لا FormatEvent — الأَداةُ عَمياء.");
        var end = page.IndexOf("};", at, StringComparison.Ordinal);
        return page[at..(end + 2)];
    }

    /// <summary>الكاتِبُ: الأَفعالُ الأَربَعَةُ ومُلاحَظَتاها بِحالِ المُزَوِّد — الطَرَفُ الَّذي تَقرَؤُه التَسمِيَة.</summary>
    [Fact]
    public void The_service_writes_the_four_payment_lines_with_the_provider_status_in_the_note()
    {
        var deals = Deals();
        foreach (var action in PaymentActions)
            Assert.Contains($"\"{action}\",", deals, StringComparison.Ordinal);
        Assert.Contains("$\"capture {cr.Status} {cr.AmountSar:0.00} SAR\"", deals, StringComparison.Ordinal);
        Assert.Contains("$\"refund {rr.Status} {rr.AmountSar:0.00} SAR\"", deals, StringComparison.Ordinal);
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-29، كوميتُ التَبديل)</b> — كانَ <c>Today_the_four_payment_lines_fall_back_to_their_english_action_name</c>
    /// (‏<c>369af04</c>): «أَذرُعُ أَسطُرِ الدَفعِ في FormatEvent: 0/4 · السُقوطُ إلى e.Action: True». والآن الأَربَعَةُ ذِراعٌ واحِدَةٌ تَسأَلُ
    /// <see cref="ACommerce.Templates.Customer.Marketplace.Services.Deals.DealPaymentLines.LabelKeyFor"/> بِالفِعلِ والمُلاحَظَة — والسُقوطُ لِما سِواها كَما كان.</summary>
    [Fact]
    public void The_four_payment_lines_are_named_by_the_status_written_in_the_line()
    {
        var body = FormatEventBody();
        var arms = new[] { "DealPaymentLines.Captured", "DealPaymentLines.CaptureFailed", "DealPaymentLines.Refunded", "DealPaymentLines.RefundFailed" }
            .Count(a => body.Contains(a, StringComparison.Ordinal));
        output.WriteLine($"أَذرُعُ أَسطُرِ الدَفعِ في FormatEvent: {arms}/{PaymentActions.Length} · السُقوطُ إلى e.Action: {body.Contains("_ => e.Action")}");
        Assert.Equal(4, arms);
        Assert.Contains("=> L[DealPaymentLines.LabelKeyFor(e)!],", body, StringComparison.Ordinal);
        Assert.Contains("_ => e.Action", body, StringComparison.Ordinal);
        Assert.Equal(PaymentActions, new[]
        {
            ACommerce.Templates.Customer.Marketplace.Services.Deals.DealPaymentLines.Captured,
            ACommerce.Templates.Customer.Marketplace.Services.Deals.DealPaymentLines.CaptureFailed,
            ACommerce.Templates.Customer.Marketplace.Services.Deals.DealPaymentLines.Refunded,
            ACommerce.Templates.Customer.Marketplace.Services.Deals.DealPaymentLines.RefundFailed,
        });
    }

    // ─── المَعجَمُ: مُوجَبٌ وسالِبٌ لِكُلِّ مِفتاح ───────────────────────────────────────────────

    /// <summary>«تَمَّ» لا تُقالُ إلّا إذا قالَتها الحالُ المَكتوبَة — والحالُ بِالحَرفِ الَّذي يَكتُبُه <c>$"capture {cr.Status} …"</c>.</summary>
    [Theory]
    [InlineData("captured", "capture Captured 120.00 SAR", "deals.detail.event_payment_captured")]
    [InlineData("captured", "capture Authorized 120.00 SAR", "deals.detail.event_payment_capture_failed")]
    [InlineData("captured", "capture Failed 120.00 SAR", "deals.detail.event_payment_capture_failed")]
    [InlineData("captured", "capture captured 120.00 SAR", "deals.detail.event_payment_capture_failed")]
    [InlineData("captured", "refund Captured 120.00 SAR", "deals.detail.event_payment_capture_failed")]
    [InlineData("captured", null, "deals.detail.event_payment_capture_failed")]
    [InlineData("capture_failed", "timeout", "deals.detail.event_payment_capture_failed")]
    [InlineData("refunded", "refund Refunded 120.00 SAR", "deals.detail.event_payment_refunded")]
    [InlineData("refunded", "refund PartiallyRefunded 60.00 SAR", "deals.detail.event_payment_partly_refunded")]
    [InlineData("refunded", "refund Failed 120.00 SAR", "deals.detail.event_payment_refund_failed")]
    [InlineData("refunded", "refund Cancelled 120.00 SAR", "deals.detail.event_payment_refund_failed")]
    [InlineData("refunded", "refund 7 120.00 SAR", "deals.detail.event_payment_refund_failed")]
    [InlineData("refunded", "", "deals.detail.event_payment_refund_failed")]
    [InlineData("refund_failed", "gateway 502", "deals.detail.event_payment_refund_failed")]
    public void A_payment_line_says_done_only_when_its_written_status_says_so(string action, string? note, string expected)
        => Assert.Equal(expected, ACommerce.Templates.Customer.Marketplace.Services.Deals.DealPaymentLines.LabelKeyFor(action, note));

    [Theory]
    [InlineData("advanced")]
    [InlineData("cancelled")]
    [InlineData("note")]
    [InlineData("Captured")]
    public void Any_other_action_is_not_a_payment_line(string action)
    {
        Assert.Null(ACommerce.Templates.Customer.Marketplace.Services.Deals.DealPaymentLines.LabelKeyFor(action, "capture Captured 1.00 SAR"));
        Assert.Null(ACommerce.Templates.Customer.Marketplace.Services.Deals.DealPaymentLines.LabelKeyFor(new ACommerce.Templates.Customer.Marketplace.Services.Deals.DealEvent(
            ACommerce.Templates.Customer.Marketplace.Services.Deals.DealStage.Paid, ACommerce.Templates.Customer.Marketplace.Services.Deals.DealStage.Paid,
            action, null, null, "capture Captured 1.00 SAR", DateTime.UtcNow)));
    }

    /// <summary>المَفاتيحُ الخَمسَةُ عَرَبِيَّةٌ حَيَّة، و«تَمَّ» في الثَلاثَةِ المُنجَزَةِ وَحدَها، و«تَعَذَّرَ» في الاثنَين؛ ولا مُصطَلَحَ إنكليزِيّ.</summary>
    [Fact]
    public void The_five_labels_are_plain_arabic_and_only_the_done_ones_say_done()
    {
        var keys = ACommerce.Templates.Customer.Marketplace.Services.Deals.DealPaymentLines.Keys;
        Assert.Equal(5, keys.Count);
        foreach (var key in keys)
        {
            var text = ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, key);
            Assert.False(string.IsNullOrWhiteSpace(text), $"«{key}» بِلا قيمَة.");
            Assert.DoesNotMatch(new Regex("[A-Za-z]"), text!);
            var bare = AccountDeletionRetentionTests.Bare(text!);
            var done = key is "deals.detail.event_payment_captured" or "deals.detail.event_payment_refunded" or "deals.detail.event_payment_partly_refunded";
            Assert.Equal(done, bare.StartsWith("تم ", StringComparison.Ordinal));
            Assert.Equal(!done, bare.StartsWith("تعذر ", StringComparison.Ordinal));
        }
    }
}
