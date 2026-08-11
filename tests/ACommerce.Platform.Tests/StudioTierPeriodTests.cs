using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── تَوصيف دَوَران فَترَة الحِصَّة ─────────────────────────────────────
//
// **لِماذا هذا المِلَفّ مَوجود**: كانَت قاعِدَة الدَوَران مَحبوسَة داخِل
// دالَّة تَفتَح جَلسَة Marten وتَكتُب — فَلا تُختَبَر إلّا بِقاعِدَة
// بَيانات حَيَّة، ولِذلِك لَم تُختَبَر قَطّ. أُخرِجَت القاعِدَة إلى
// دالَّتَين نَقِيَّتَين (بِلا قاعِدَة بَيانات وبِلا ساعَة ضِمنِيَّة)
// فَصارَ تَوصيفُها مُمكِناً — وهذا نِصف قيمَة الفَصل.
//
// **وما يُوَصَّف هُنا هو سُلوك اليَوم حَرفاً**، لا سُلوكاً مُحَسَّناً:
// كُتِبَت هذه الحالات مِن قِراءَة الكود **قَبل** تَغييرِه، فَإن غَيَّرَ
// الفَصلُ حِساباً سَقَطَت. الَّذي تَغَيَّرَ هو **مَوضِع الكِتابَة** لا
// القاعِدَة.
//
// وما لا يُوَصَّف هُنا بِصَراحَة: أَنّ القِراءَة لا تَكتُب. ذاكَ يَلزَمُه
// Postgres حَيّ، ولَيسَ في هذه الحُزمَة. البُرهان البَديل بُنيَويّ:
// `ReadWithLimitsAsync` تَفتَح `QuerySession` — وهي لا تَملِك
// `SaveChanges` أَصلاً، فَالمَنع مِن نَوع الجَلسَة لا مِن انضِباط
// الكاتِب.

public class StudioTierPeriodTests
{
    private static readonly DateTime Now = new(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);

    // ─── ١. قاعِدَة الانقِضاء ───────────────────────────────────────

    [Theory]
    [InlineData(0,    false)]   // بَدَأَت الآن
    [InlineData(29,   false)]   // دونَ الحَدّ
    [InlineData(30,   true)]    // عِندَ الحَدّ بِالضَبط — `>=` لا `>`
    [InlineData(31,   true)]
    [InlineData(400,  true)]    // فَترَة مَنسِيَّة مُنذُ زَمَن
    public void PeriodElapses_AtThirtyDaysInclusive(int daysAgo, bool expected)
        => Assert.Equal(expected,
            StudioTierService.PeriodElapsed(Now.AddDays(-daysAgo), Now));

    [Fact]
    public void ThePeriodIsThirtyDays()
        => Assert.Equal(30, StudioTierService.PeriodDays);

    [Fact]
    public void AFuturePeriodStart_NeverCountsAsElapsed()
        => Assert.False(StudioTierService.PeriodElapsed(Now.AddDays(5), Now));

    // ─── ٢. ما الَّذي يُصَفَّر — وما الَّذي لا يُصَفَّر ─────────────────

    [Fact]
    public void WhenThePeriodElapsed_TheTwoMonthlyCountersResetAndTheStampMoves()
    {
        var u = User(daysAgo: 30, analyses: 7, refines: 40, stores: 3);

        Assert.True(StudioTierService.ApplyPeriodRollover(u, Now));

        Assert.Equal(Now, u.PeriodStart);
        Assert.Equal(0, u.AnalysesUsed);
        Assert.Equal(0, u.RefinesUsed);
    }

    [Fact]
    public void StoresBuilt_IsNotAMonthlyQuota_AndSurvivesTheRollover()
    {
        // عَدّاد التَطبيقات حَدّ **تَراكُمِيّ** (‏StoresMax) لا حِصَّة
        // شَهرِيَّة. لَو صُفِّرَ لَصارَ كُلّ مُشتَرِك يَبني بِلا حَدّ
        // بِانتِظار ثَلاثينَ يَوماً — وهذا سُلوك اليَوم، مُثَبَّت.
        var u = User(daysAgo: 365, analyses: 9, refines: 9, stores: 4);

        StudioTierService.ApplyPeriodRollover(u, Now);

        Assert.Equal(4, u.StoresBuilt);
    }

    [Fact]
    public void TheTier_IsNeverTouchedByARollover()
    {
        var u = User(daysAgo: 90, analyses: 1, refines: 1, stores: 1);
        u.Tier = "growth";

        StudioTierService.ApplyPeriodRollover(u, Now);

        Assert.Equal("growth", u.Tier);
    }

    [Fact]
    public void WhenThePeriodIsStillRunning_NothingMovesAtAll()
    {
        var start = Now.AddDays(-29);
        var u = User(daysAgo: 29, analyses: 7, refines: 40, stores: 3);

        Assert.False(StudioTierService.ApplyPeriodRollover(u, Now));

        Assert.Equal(start, u.PeriodStart);
        Assert.Equal(7,  u.AnalysesUsed);
        Assert.Equal(40, u.RefinesUsed);
        Assert.Equal(3,  u.StoresBuilt);
    }

    // ─── ٣. الدَوَران لا يَعتَمِد على عَدَد مَرّات تَطبيقِه ──────────────

    [Fact]
    public void ApplyingTheRolloverTwice_IsTheSameAsApplyingItOnce()
    {
        // مُهِمّ بَعدَ الفَصل: القِراءَة تُطَبِّقُه على نُسخَتِها،
        // والكِتابَة تُطَبِّقُه على نُسخَتِها. فَلَو لَم يَكُن
        // idempotent لَاختَلَفَ المَعروض عَن المَحفوظ.
        var u = User(daysAgo: 45, analyses: 5, refines: 5, stores: 2);

        Assert.True(StudioTierService.ApplyPeriodRollover(u, Now));
        Assert.False(StudioTierService.ApplyPeriodRollover(u, Now));

        Assert.Equal(Now, u.PeriodStart);
        Assert.Equal(0, u.AnalysesUsed);
        Assert.Equal(0, u.RefinesUsed);
        Assert.Equal(2, u.StoresBuilt);
    }

    private static StudioUser User(int daysAgo, int analyses, int refines, int stores) => new()
    {
        Id           = Guid.NewGuid(),
        PeriodStart  = Now.AddDays(-daysAgo),
        AnalysesUsed = analyses,
        RefinesUsed  = refines,
        StoresBuilt  = stores,
    };
}
