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

    [Fact]
    public void Today_the_four_payment_lines_fall_back_to_their_english_action_name()
    {
        var body = FormatEventBody();
        var arms = PaymentActions.Count(a => body.Contains($"\"{a}\"", StringComparison.Ordinal));
        output.WriteLine($"أَذرُعُ أَسطُرِ الدَفعِ في FormatEvent: {arms}/{PaymentActions.Length} · السُقوطُ إلى e.Action: {body.Contains("_ => e.Action")}");
        Assert.Equal(0, arms);
        Assert.Contains("_ => e.Action", body, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"event_payment_"), body);
    }
}
