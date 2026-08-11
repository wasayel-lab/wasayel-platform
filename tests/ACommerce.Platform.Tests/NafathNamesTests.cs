using ACommerce.Kit.Auth;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// اِسم العَرض المُشتَقّ مِن رَقم الهُوِيَّة. الشَرط الحاكِم
/// <b>الحَتمِيَّة</b>، والتَنَوُّع بَعدَها.
/// </summary>
public class NafathNamesTests
{
    /// <summary>نَفس الهُوِيَّة ← نَفس الاسم. لَو كُتِبَت التَجزِئَة بِـ
    /// <c>string.GetHashCode</c> لَسَقَطَ هذا الشَرط عَبر العَمَلِيّات لا
    /// داخِلَها، فَلا يَكشِفُه اختِبار داخِل عَمَلِيَّة واحِدَة — ولِذلِك
    /// يُثَبَّت الجَواب حَرفِيّاً في اختِبار البَصمَة أَدناه.</summary>
    [Fact]
    public void For_IsStableForTheSameNationalId()
    {
        Assert.Equal(NafathNames.For("1012345678"), NafathNames.For("1012345678"));
        Assert.Equal(NafathNames.For("2099887766"), NafathNames.For("2099887766"));
    }

    /// <summary><b>بَصمَة مُثَبَّتَة</b> — تَكسِر البِناء لَو تَبَدَّلَت
    /// دالَّة التَجزِئَة أَو تَرتيب القائِمَتَين، وهُما ما يَضمَنان أَنّ
    /// مُستَخدِماً سَجَّلَ أَمس يَبقى بِنَفس الاسم اليَوم.</summary>
    [Theory]
    [InlineData("1012345678", "مُحَمَّد الشَهري")]
    [InlineData("1087654321", "نورَة الحَربي")]
    [InlineData("2099887766", "تُركي السُبَيعي")]
    public void For_IsPinnedAcrossProcesses(string nid, string expected)
        => Assert.Equal(expected, NafathNames.For(nid));

    /// <summary>ثَلاث هُوِيّات مُختَلِفَة ← ثَلاثَة أَسماء مُختَلِفَة.
    /// هذا هو العَرَض الَّذي دَفَعَ التَّغيير.</summary>
    [Fact]
    public void For_GivesDifferentNamesToDifferentIds()
    {
        var names = new[] { "1012345678", "1087654321", "2099887766" }
            .Select(NafathNames.For).ToList();

        Assert.Equal(3, names.Distinct().Count());
        Assert.DoesNotContain(NafathNames.Fallback, names);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void For_FallsBackWhenNoIdGiven(string? nid)
        => Assert.Equal(NafathNames.Fallback, NafathNames.For(nid));

    [Fact]
    public void For_IgnoresSurroundingWhitespace()
        => Assert.Equal(NafathNames.For("1012345678"), NafathNames.For("  1012345678  "));

    /// <summary>التَوزيع لا يَنهار عَلى اسمٍ واحِد: أَلف هُوِيَّة
    /// مُتَتالِيَة تُغَطّي أَكثَر مِن نِصف الفَضاء المُمكِن، وأَشيَع اسم
    /// لا يَبتَلِع عُشر العَيِّنَة.</summary>
    [Fact]
    public void For_SpreadsAcrossTheNameSpace()
    {
        var names = Enumerable.Range(0, 1000)
            .Select(i => NafathNames.For($"10{i:D8}"))
            .ToList();

        var distinct = names.Distinct().Count();
        Assert.True(distinct > NafathNames.Combinations / 2,
            $"تَنَوُّع ضَعيف: {distinct} مِن {NafathNames.Combinations}");

        var commonest = names.GroupBy(n => n).Max(g => g.Count());
        Assert.True(commonest < 100, $"اِسم واحِد اِبتَلَعَ {commonest} مِن ١٠٠٠");
    }

    /// <summary>كُلّ اسم جُزآن — أَوَّل وعائِلَة — فَلا يَظهَر فَراغ
    /// مُعَلَّق في الواجِهَة.</summary>
    [Fact]
    public void For_AlwaysReturnsTwoNonEmptyParts()
    {
        foreach (var i in Enumerable.Range(0, 200))
        {
            var parts = NafathNames.For($"1{i:D9}").Split(' ');
            Assert.Equal(2, parts.Length);
            Assert.All(parts, p => Assert.False(string.IsNullOrWhiteSpace(p)));
        }
    }
}
