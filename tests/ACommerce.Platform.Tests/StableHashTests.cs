using ACommerce.Kit.Maps;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── تَجزِئَةٌ يُبنى عَلَيها مُخرَجٌ يَراه المُستَخدِم ────────────────
//
// ‏`string.GetHashCode()` في dotnet **مُبَذَّرَةٌ لِكُلّ عَمَلِيَّة**:
// نَفسُ السِلسِلَة تُعطي رَقَماً مُختَلِفاً بَعدَ كُلّ إقلاع. فَكُلُّ
// مُخرَجٍ مُشتَقٍّ مِنها ثابِتٌ **داخِلَ** العَمَلِيَّة ومُتَقَلِّبٌ
// بَينَها — وهذا أَسوَأُ من التَقَلُّب الصَريح، لِأَنَّه يَنجو مِن كُلّ
// اختِبارٍ يَجري في عَمَلِيَّةٍ واحِدَة.
//
// **والقاعِدَةُ كُتِبَت مَرَّةً ثُمَّ خُرِقَت مَرَّتَين**: وَثَّقَتها
// `NafathNames` صَراحَةً («بَذرَتُه تَتَبَدَّل مَع كُلّ عَمَلِيَّة»)،
// وبَقِيَ في المُستَودَع مَوضِعان يَفعَلانِ ما نَهَت عَنه:
//   · `AcAvatar` — لَونُ صورَةِ الشَخص. والتَعليقُ فَوقَه كانَ يَعِد
//     «نَفس الاسم ⇒ نَفس اللون دائماً»، والكودُ يَنقُضُه.
//   · `MockMapsProvider` — إحداثِيّاتُ عُنوانٍ غَير مَعروف.
//
// **وكَشَفَه القِياسُ لا القِراءَة**: لَقطَتانِ لِنَفس الصَفحَة، قَبلَ
// إعادَة تَشغيل الخادِم وبَعدَها، اختَلَفَتا في سَطرٍ واحِد — سَطرِ
// التَدَرُّج اللَونيّ. ولَولا أَنّ بَوّابَةَ المَظهَر تُقارِن بايتاً
// بِبايت لَما رُئِيَ.
//
// ولِذلك يُثَبَّت هُنا شَيئان: **بَصمَةٌ حَرفِيَّة** لا تُنتِجُها تَجزِئَةٌ
// مُبَذَّرَة أَبَداً، و**مَنعٌ نَصِّيّ** لِعَودَة النَمَط إلى مَسار
// التَصيير — بِقائِمَة مَأذونين فارِغَة تَنمو بِقَرارٍ مَرئيّ لا صامِت.

public class StableHashTests
{
    private static string Root => ThemeZeroEquivalenceTests.RepoRoot;

    /// <summary>المَواضِعُ الَّتي يُمنَع فيها <c>GetHashCode</c> على
    /// سِلسِلَة: كُلُّ ما يُنتِج مَرئيّاً أَو إحداثِيَّة.</summary>
    private static readonly string[] GuardedRoots =
    {
        Path.Combine("libs", "widgets"),
        Path.Combine("libs", "kits", "Maps"),
        Path.Combine("libs", "kits", "Auth"),
        Path.Combine("libs", "templates"),
    };

    /// <summary><b>المَأذونونَ صَراحَةً</b> — و<c>override GetHashCode</c>
    /// لَيسَ مِنهُم أَصلاً: هُوَ تَعريفُ تَجزِئَةٍ لا استِهلاكُها،
    /// فَيُستَثنى بِالنَمَط لا بِالاسم.</summary>
    private static readonly string[] Allowed = Array.Empty<string>();

    [Fact]
    public void RenderingCode_DoesNotDeriveOutputFromFrameworkStringHash()
    {
        var offenders = new List<string>();
        var scanned = 0;

        foreach (var rel in GuardedRoots)
        {
            var dir = Path.Combine(Root, rel);
            if (!Directory.Exists(dir)) continue;

            foreach (var path in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
            {
                if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                    path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                    continue;
                if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
                    !path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
                    continue;

                scanned++;
                var relPath = Path.GetRelativePath(Root, path).Replace('\\', '/');
                if (Allowed.Contains(relPath)) continue;

                foreach (var raw in File.ReadLines(path))
                {
                    var line = raw.Trim();
                    // التَعليقُ يَذكُر الاسمَ لِيَشرَحَ المَنع — فَعَدُّه
                    // خَرقاً يَجعَل الأَداةَ تَتَّهِم الوَثيقَةَ بِأَنَّها كود.
                    if (line.StartsWith("//", StringComparison.Ordinal) ||
                        line.StartsWith("///", StringComparison.Ordinal) ||
                        line.StartsWith("*", StringComparison.Ordinal)) continue;
                    // تَعريفُ التَجزِئَة لِلمُساواة لا اشتِقاقُ مُخرَجٍ مِنها.
                    if (line.Contains("override int GetHashCode", StringComparison.Ordinal)) continue;
                    if (line.Contains(".GetHashCode(", StringComparison.Ordinal))
                        offenders.Add($"{relPath}: {line}");
                }
            }
        }

        // حارِسُ العَمى (القاعِدَة ١٠): أَداةٌ فَحَصَت صِفراً لا تُعطي «صِفر
        // مُخالَفَة» — تَسقُط.
        Assert.True(scanned > 100, $"فَحصٌ أَعمى: {scanned} مِلَفّاً فَقَط فُحِص.");

        Assert.True(offenders.Count == 0,
            "‏GetHashCode على سِلسِلَة في مَسار تَصيير — بَذرَتُها تَتَبَدَّل " +
            "مَع كُلّ عَمَلِيَّة فَيَتَبَدَّل المَرئيّ بَعدَ كُلّ إقلاع:\n  " +
            string.Join("\n  ", offenders));
    }

    /// <summary><b>بَصمَةٌ مُثَبَّتَة لِلمُزَوِّد الوَهميّ</b> — نَفسُ
    /// العُنوان يُعطي نَفسَ الإحداثِيَّة في كُلّ عَمَلِيَّة. ولَو رَجَعَت
    /// تَجزِئَةُ الإطار لَما طابَقَ هذا الرَقَمُ إلّا بِالصُدفَة.</summary>
    [Fact]
    public async Task MockGeocoder_IsPinnedAcrossProcesses()
    {
        var p = new MockMapsProvider();
        var a = await p.GeocodeAsync("حَيّ لا يَعرِفُه المُزَوِّد");
        var b = await p.GeocodeAsync("حَيّ لا يَعرِفُه المُزَوِّد");

        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Equal(a!.Location.Lat, b!.Location.Lat);
        Assert.Equal(a.Location.Lng, b.Location.Lng);

        // القيمَةُ نَفسُها مُثَبَّتَة — لا الحَتمِيَّةُ داخِلَ العَمَلِيَّة
        // وَحدَها (وتِلكَ كانَت تَمُرّ قَبلَ الإصلاح أَيضاً).
        Assert.Equal(24.7074, Math.Round(a.Location.Lat, 4));
        Assert.Equal(46.7036, Math.Round(a.Location.Lng, 4));
    }

    /// <summary>المُدُنُ المَعروفَة لا تَمُرّ بِالتَجزِئَة أَصلاً — ضابِطٌ
    /// يَمنَع أَن يُقاس الفَرعُ الخاطِئ.</summary>
    [Fact]
    public async Task MockGeocoder_KnownCityBypassesTheHash()
    {
        var r = await new MockMapsProvider().GeocodeAsync("شارِع في الرياض");
        Assert.NotNull(r);
        Assert.Equal(24.7136, r!.Location.Lat);
        Assert.Equal(46.6753, r.Location.Lng);
    }
}
