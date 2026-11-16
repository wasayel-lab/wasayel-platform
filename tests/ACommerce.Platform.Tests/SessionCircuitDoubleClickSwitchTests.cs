using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ نَقرَتانِ أَثناءَ سُؤالِ الجَلسَةِ فِعلٌ واحِد — بَعدَ التَبديل (‏2026-11-05، الموجَة ١٢ G2 — مُراجَعَةُ `3d0b49a`، G2R1-1) ══════════════════════════════
//
// قُلِبَ ما طَبَعَه التَوصيف (`SessionCircuitDoubleClickTests.cs`، `389cf1f`، والمِلَفُّ بِبَصمَتِه): نَقرَتانِ أَثناءَ سُؤالِ الأُولى كانَتا تَنتَظِرانِ القُفلَ مَعاً
// («أَسئِلَةٌ تَنتَظِرُ القُفل 2») ثُمَّ تَعبُرانِ — «نِداءاتُ النَموذَج 2 · كِتاباتُ الأَداة 2» في «طَبِّق». والآن يَحجِزُ كُلُّ مُعالِجٍ `isWorking` قَبلَ أَوَّلِ
// `await` ثُمَّ يَسأَل، فَالثانِيَةُ تَعودُ قَبلَ أَن يَكتَمِلَ سُؤالُ الأُولى، والنَقرَتانِ تَفعَلانِ ما تَفعَلُه نَقرَةٌ واحِدَة (`OneClick` المُثَبَّتُ في التَوصيف).

[Collection(LiveProofCollection.Name)]
public class LiveSessionCircuitDoubleClickSwitchProofTests(ITestOutputHelper output)
{
    /// <summary><b>قُلِبَ ما طَبَعَه التَوصيف</b>: في الصَفحَتَينِ ولِكُلِّ مُعالِجٍ مِن الأَربَعَة — الثانِيَةُ تَعودُ وسُؤالُ الأُولى مُعَلَّقٌ عَلى القُفل، ولا يَنتَظِرُ القُفلَ
    /// إلّا سُؤالٌ واحِد، ثُمَّ نِداءاتُ النَموذَجِ وكِتاباتُ الأَداةِ والسِجِلِّ وحالَةُ الأَداةِ ما تَفعَلُه نَقرَةٌ واحِدَة (<see cref="LiveSessionCircuitDoubleClickProofTests.OneClick"/>):
    /// نِداءُ نَموذَجٍ واحِد، والأَداةُ تُطَبَّقُ مَرَّة.</summary>
    [Theory]
    [InlineData(LiveSessionCircuitProofTests.StudioPage, "SendAsync")]
    [InlineData(LiveSessionCircuitProofTests.StudioPage, "ApplyAsync")]
    [InlineData(LiveSessionCircuitProofTests.StudioPage, "RejectAsync")]
    [InlineData(LiveSessionCircuitProofTests.StudioPage, "ResetAsync")]
    [InlineData(LiveSessionCircuitProofTests.AdminPage, "SendAsync")]
    [InlineData(LiveSessionCircuitProofTests.AdminPage, "ApplyAsync")]
    [InlineData(LiveSessionCircuitProofTests.AdminPage, "RejectAsync")]
    [InlineData(LiveSessionCircuitProofTests.AdminPage, "ResetAsync")]
    public async Task A_second_click_during_the_first_session_check_returns_at_once_and_the_two_act_once_on_Postgres(string page, string handler)
    {
        if (!LiveSessionCircuitProofTests.Enabled) return;
        var two = await LiveSessionCircuitDoubleClickProofTests.RunAsync(page, handler, clicks: 2);
        output.WriteLine($"{page} · {handler} · نَقرَتانِ أَثناءَ السُؤال ⇒ {two}");
        Assert.Empty(two.Errors);
        Assert.True(two.DoorOpen);
        Assert.True(two.FirstHeld);
        Assert.True(two.SecondReturnedFirst);
        Assert.Equal(1, two.Waiting);
        Assert.Equal(LiveSessionCircuitDoubleClickProofTests.OneClick(handler), two.Acted);
        Assert.Empty(two.Went);
    }
}
