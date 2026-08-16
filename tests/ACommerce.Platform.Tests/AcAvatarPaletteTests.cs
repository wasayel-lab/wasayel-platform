using System.Text.RegularExpressions;
using ACommerce.Kit.Theme;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>لَوحَةُ <c>AcAvatar</c> — تُقرَأ مِن المِلَفّ لا مِن نُسخَةٍ
/// هُنا.</b> اللَوحَةُ حَقلٌ خاصّ في مُكَوِّن Razor، ونَسخُ قيَمِها إلى
/// الاختِبار يَجعَلُه يَحرُسُ نُسخَتَه هو: تُعَدَّل اللَوحَةُ فَيَبقى
/// أَخضَرَ. فَيُقرَأ نَصُّ المِلَفّ ويُستَخرَج مِنه ما يُشحَن فِعلاً —
/// وهو نَفسُ ما تَفعَلُه <c>ThemeZeroEquivalenceTests</c> مَعَ الأَنماط.</para>
///
/// <para><b>الكُلفَة الَّتي كَتَبَت هذا المِلَفّ</b> (‏2026-08-16): أَربَعَةٌ
/// مِن ثَمانِيَةِ تَدَرُّجات كانَت دونَ ‏3:1 لِنَصٍّ أَبيَض
/// (‏1.92…2.49)، ولَم تُمسِكها قاعِدَةُ تَبايُنِ الثيم لِأَنّ القيَم
/// حَرفِيَّةٌ في razor لا رُموزٌ في المَعجَم، ولا الطَبَقَةُ السادِسَة
/// لِأَنَّها كانَت تَعبُرُ التَدَرُّجَ إلى ما خَلفَه. أَي أَنّ المَوضِعَ
/// كانَ في الشَقِّ بَينَ أَداتَين.</para>
/// </summary>
public class AcAvatarPaletteTests
{
    private static readonly string RazorPath = Path.Combine(
        ThemeZeroEquivalenceTests.RepoRoot,
        "libs", "widgets", "ACommerce.Widgets", "AcAvatar.razor");

    /// <summary>‏(مِن، إلى) لِكُلّ تَدَرُّج، بِتَرتيب اللَوحَة.</summary>
    private static IReadOnlyList<(string From, string To)> Palette()
    {
        var text = File.ReadAllText(RazorPath);
        var pairs = Regex.Matches(text,
                @"linear-gradient\(135deg,\s*(#[0-9a-fA-F]{6}),\s*(#[0-9a-fA-F]{6})\)")
            .Select(m => (m.Groups[1].Value, m.Groups[2].Value))
            .ToList();

        // عَدّادٌ قَبلَ الحُكم: لَوحَةٌ لَم تُقرَأ تُعطي «كُلُّها سَليمَة»
        Assert.Equal(8, pairs.Count);
        return pairs;
    }

    private static double WhiteOn(string hex)
    {
        Assert.True(ThemeContrastRule.TryParse(hex, out var c), hex);
        return ThemeContrastRule.Ratio(
            ThemeContrastRule.Luminance(new ThemeContrastRule.Rgba(255, 255, 255, 1)),
            ThemeContrastRule.Luminance(c));
    }

    /// <summary>أَسوَأُ نُقطَةٍ مُعتَبَرَة: الطَرَفان ومُنتَصَفُهُما — والنَصُّ
    /// يَقَع في المُنتَصَف، فَإسقاطُه يُجَمِّل الرَقَم.</summary>
    private static double Worst((string From, string To) g)
    {
        ThemeContrastRule.TryParse(g.From, out var a);
        ThemeContrastRule.TryParse(g.To,   out var b);
        var mid = $"#{(int)((a.R + b.R) / 2):x2}{(int)((a.G + b.G) / 2):x2}{(int)((a.B + b.B) / 2):x2}";
        return Math.Min(Math.Min(WhiteOn(g.From), WhiteOn(g.To)), WhiteOn(mid));
    }

    /// <summary>‏<c>Size × 0.4</c>: أَصغَرُ أَفاتار حَيّ (‏40 في
    /// <c>ChatRoom</c>) يُعطي ‏16px — نَصٌّ عادِيٌّ عَتَبَتُه ‏4.5،
    /// لا كَبيرٌ عَتَبَتُه ‏3. مُثَبَّتٌ لِأَنّ الخَلطَ بَينَهُما هو ما
    /// يَجعَلُ لَوحَةً «سَليمَة» ساقِطَةً على شاشَةٍ واحِدَة.</summary>
    [Theory]
    [InlineData(40, 16.0,  4.5)]
    [InlineData(48, 19.2,  3.0)]
    [InlineData(56, 22.4,  3.0)]
    [InlineData(64, 25.6,  3.0)]
    public void TheThresholdFollowsTheRenderedFontSize_NotTheAvatarSize(
        int size, double expectedPx, double expectedThreshold)
    {
        var px = size * 0.4;
        Assert.Equal(expectedPx, px, 3);
        // ‏WCAG: كَبيرٌ إن ‏≥24px، أَو ‏≥18.66px مَعَ وَزن ‏≥700.
        // و‏.ac-avatar وَزنُها ‏800 في premium.css و700 في widgets.css.
        var large = px >= 24 || px >= 18.66;
        Assert.Equal(expectedThreshold, large ? 3.0 : 4.5);
    }

    [Fact]
    public void EveryReplacedGradient_ClearsAA_AtEverySize()
    {
        // الأَربَعَةُ الَّتي حُرِّكَت — ولِكُلٍّ رَقَمُها قَبلُ.
        var replaced = new (int Index, double Before, string Name)[]
        {
            (1, 2.49, "teal-cyan"),
            (2, 2.15, "عنبر-أحمر"),
            (3, 1.98, "emerald-lime"),
            (6, 1.92, "yellow-orange"),
        };

        var palette = Palette();
        foreach (var (index, before, name) in replaced)
        {
            var now = Worst(palette[index]);
            Assert.True(now >= 4.5,
                $"{name} (‏#{index}): أَسوَأ نُقطَة {now:F2} — والمَطلوب 4.5 " +
                $"لِأَنّ ChatRoom يُصَيِّر 16px. كانَت {before:F2}.");
        }
    }

    /// <summary>
    /// الأَربَعَةُ الباقِيَة: تُبقيها هذه الحُدودُ حَيثُ تَرَكَها القَرار.
    /// <b>ولَيسَ هذا تَبريراً بَل تَثبيتاً</b>: مَن أَرادَ رَفعَها
    /// يَرفَعُها بِقَرار، ومَن أَنزَلَها يُسقِط هذا الاختِبار.
    /// </summary>
    [Fact]
    public void TheUntouchedFour_StillClearLargeText_AndTheirDebtAtSize40IsPinned()
    {
        var palette = Palette();
        var untouched = new (int Index, double Expected, string Name)[]
        {
            (0, 4.23, "إندِغو-بنفسجيّ"),
            (4, 3.53, "pink-violet"),
            (5, 3.68, "cyan-blue"),
            (7, 4.60, "violet-pink"),
        };

        foreach (var (index, expected, name) in untouched)
        {
            var now = Worst(palette[index]);
            Assert.True(Math.Abs(now - expected) < 0.01,
                $"{name} (‏#{index}): {now:F2} ≠ {expected:F2} — تَحَرَّكَت قيمَةٌ مُثَبَّتَة.");
            Assert.True(now >= 3.0, $"{name}: دونَ عَتَبَة النَصّ الكَبير.");
        }

        // والدَينُ مَعدود لا مَوصوف: ثَلاثَةٌ مِن الأَربَعَة دونَ 4.5،
        // فَتَسقُط عِندَ Size=40 (‏ChatRoom) وَحدَه.
        Assert.Equal(3, untouched.Count(u => Worst(palette[u.Index]) < 4.5));
    }

    /// <summary>
    /// <para><b>الحارِسُ الحَقيقيّ ضِدَّ العَودَة إلى
    /// <c>GetHashCode()</c></b>. تَجزئَةُ السَلاسِل في .NET Core
    /// عَشوائِيَّةٌ لِكُلّ عَمَلِيَّة — مَقيس: نَفسُ الاسم أَعطى الفَهارِس
    /// ‏6 ثُمَّ 5 ثُمَّ 0 في ثَلاث عَمَلِيّات — فَاختِبارٌ يَقيسُ
    /// «نَفسُ الاسم في نَفسِ العَمَلِيَّة يُعطي نَفسَ الفَهرَس» يَمُرّ
    /// مَعَ الصيغَتَين ولا يَحرُسُ شَيئاً.</para>
    ///
    /// <para>فَالمَقيسُ هُنا قيمَةٌ <b>مُطلَقَة</b> لِـ FNV-1a: تُحسَب
    /// خارِجَ العَمَلِيَّة، ولا تُنتِجُها تَجزئَةٌ عَشوائِيَّة إلّا
    /// بِاحتِمالِ واحِدٍ مِن ‏2³¹.</para>
    /// </summary>
    [Theory]
    [InlineData("—",          289329155,  3)]
    [InlineData("عُضو جَديد", 1959660851, 3)]
    [InlineData("محمد",       310540923,  3)]
    [InlineData("",           18652613,   5)]
    public void TheGradientIndexIsStableAcrossProcesses(string name, int hash, int index)
    {
        Assert.Equal(hash, Fnv1a(name));
        Assert.Equal(index, Fnv1a(name) % 8);
    }

    /// <summary>يُفحَص <b>الكود</b> لا التَعليق: الشَرحُ أَعلاهُ في
    /// <c>AcAvatar.razor</c> يَذكُر <c>GetHashCode</c> بِاسمِه لِيَقولَ
    /// لِماذا رُفِضَ — فَفَحصُ النَصّ خاماً يَرُدُّ الشَرحَ نَفسَه
    /// ويَدفَعُ لِحَذفِه. تُنزَع أَسطُرُ التَعليق أَوَّلاً.</summary>
    [Fact]
    public void TheComponentDoesNotUseARandomizedHash()
    {
        var code = File.ReadAllLines(RazorPath)
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .ToArray();

        // عَدّادٌ قَبلَ الحُكم: نَزعٌ يَبتَلِعُ المِلَفَّ كُلَّه يُعطي
        // «لا GetHashCode» عَن لا شَيء.
        Assert.True(code.Length > 40, $"بَقِيَ {code.Length} سَطراً فَقَط بَعدَ النَزع.");
        Assert.DoesNotContain("GetHashCode", string.Join("\n", code), StringComparison.Ordinal);
    }

    /// <summary>نُسخَةٌ مِن <c>AcAvatar.StableIndex</c> — والتَثبيتُ أَعلاه
    /// يَربِطُهُما: لَو انحَرَفَت إحداهُما سَقَطَ
    /// <see cref="TheComponentDoesNotUseARandomizedHash"/> أَو تَغَيَّرَت
    /// الفَهارِس المَقيسَة.</summary>
    private static int Fnv1a(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (var c in s) { h ^= c; h *= 16777619; }
            return (int)(h & 0x7FFFFFFF);
        }
    }
}
