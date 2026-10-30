using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>أَنواعُ مَوانِعِ دَمجِ الجَوّالِ المُكَرَّر</b> (‏2026-10-30، الموجَة ١٢ F1 البَند ١١ — A-SESS-3). سكربتُ العَدِّ يَطبَعُ «مانِعٌ في {جَدوَل}» بِلا نَوعِ
/// المانِع، وتَعليماتُ المالِكِ تَقرَأُ كُلَّ سَطرٍ «يَفصِلُ البائِعُ رَبطَه ثُمَّ يُدمَج» (‏ADR-044 §٤ G-6) — ومَدخَلُ البائِعِ لا بابَ يَفصِلُه، وسائِرُ الأَنواعِ
/// (مُعَرِّفٌ بِلا شَرطات، وصَفٌّ يَجمَعُ الحِسابَين، ومَجرى أَحداث، وحَدَثٌ خارِجَ مَجاري الإعلانات) لا يَرفَعُها البائِعُ أَصلاً. <b>تَوصيفُ «اليَوم» (القاعِدَة ٣)</b>.</para>
/// </summary>
public class DuplicatePhoneBlockKindsTests(ITestOutputHelper output)
{
    private static string Script(string name)
        => File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "scripts", name));

    [Fact]
    public void Today_the_count_keys_block_reasons_by_table_alone()
    {
        var count = Script("count-duplicate-phones.cs");
        var byTable = count.Contains("reasons[b[..b.IndexOf(':')]] = reasons.GetValueOrDefault(b[..b.IndexOf(':')]) + 1;", StringComparison.Ordinal);
        var kind = count.Contains("KindOf(", StringComparison.Ordinal);
        output.WriteLine($"اليَوم: العَدُّ يُفَهرِسُ المَوانِعَ بِالجَدوَلِ وَحدَه {byTable} · يَطبَعُ نَوعَ المانِع {kind}");
        Assert.True(byTable);
        Assert.False(kind);
        Assert.Contains("Console.WriteLine($\"  مانِعٌ في {why}: {n} مَجموعَة\");", count, StringComparison.Ordinal);
    }
}
