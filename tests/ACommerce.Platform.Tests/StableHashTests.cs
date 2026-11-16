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
//   · `MockMapsProvider` — إحداثِيّاتُ عُنوانٍ غَير مَعروف. (‏حُذِفَ ‏2026-10-29 بِقَرارِ المالِك د35،
//     وانتَقَلَت بَصمَتُه المُثَبَّتَةُ إلى `AcAvatar` — آخِرَ المِلَفّ.)
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

    // ─── ‏2026-10-29 (قَرارُ المالِك د35): البَصمَةُ انتَقَلَت إلى مُستَعمِلٍ حَيّ ─────────────────
    // كانَ هُنا اختِباران يُثَبِّتانِ بَصمَةَ `MockMapsProvider` (‏`MockGeocoder_IsPinnedAcrossProcesses` —
    // ‏24.7074/46.7036 لِعُنوانٍ مَجهول — و`MockGeocoder_KnownCityBypassesTheHash`). حُذِفَ المُحاكي بِصِفرِ
    // مُستَهلِك، فَانتَقَلَ الاختِبارانِ بِشَكلِهِما نَفسِه إلى **المُستَعمِلِ الحَيِّ الوَحيدِ بِلا بَصمَةٍ مُثَبَّتَة**:
    // `AcAvatar` — لَونُ صورَةِ الشَخصِ الَّذي يَعِدُ تَعليقُه «نَفسُ الاسمِ ⇒ نَفسُ اللونِ دائِماً»، والَّذي كَشَفَ
    // القِياسُ تَقَلُّبَه أَوَّلَ مَرَّة. (‏`NafathNames` بَصمَتُها مُثَبَّتَةٌ سَلَفاً في `NafathNamesTests`.)

    private static async Task<string> RenderAvatarAsync(string? name, string? imageUrl = null)
    {
        await using var services = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions
            .BuildServiceProvider(new Microsoft.Extensions.DependencyInjection.ServiceCollection());
        await using var renderer = new Microsoft.AspNetCore.Components.Web.HtmlRenderer(
            services, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<ACommerce.Widgets.AcAvatar>(
                Microsoft.AspNetCore.Components.ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(ACommerce.Widgets.AcAvatar.Name)] = name,
                    [nameof(ACommerce.Widgets.AcAvatar.ImageUrl)] = imageUrl,
                }))).ToHtmlString());
    }

    /// <summary><b>بَصمَةٌ مُثَبَّتَة لِلَونِ الصورَة</b> — نَفسُ الاسمِ يُعطي نَفسَ الزَوجِ في كُلِّ عَمَلِيَّة.
    /// والزَوجُ مَحسوبٌ خارِجَ الإطارِ بِـFNV-1a فَوقَ UTF-8 («سارة العتيبي» ← ‏3453698774 ‏mod 8 = ‏6)؛ ولَو رَجَعَت
    /// تَجزِئَةُ الإطارِ لَما طابَقَ إلّا بِالصُدفَة.</summary>
    [Fact]
    public async Task Avatar_colour_is_pinned_across_processes()
    {
        var a = await RenderAvatarAsync("سارة العتيبي");
        var b = await RenderAvatarAsync("سارة العتيبي");

        Assert.Equal(a, b);
        Assert.Contains("background-color:#886805;", a, StringComparison.Ordinal);
        Assert.Contains("linear-gradient(135deg, #886805, #90430d)", a, StringComparison.Ordinal);
    }

    /// <summary>الصورَةُ المَرفوعَةُ لا تَمُرُّ بِالتَجزِئَةِ أَصلاً — ضابِطٌ يَمنَعُ أَن يُقاسَ الفَرعُ الخاطِئ
    /// (نَظيرُ «المُدُنُ المَعروفَةُ لا تَمُرُّ بِالتَجزِئَة» في الاختِبارِ الَّذي حَلَّ هذا مَحَلَّه).</summary>
    [Fact]
    public async Task Avatar_with_an_image_bypasses_the_hash()
    {
        var html = await RenderAvatarAsync("سارة العتيبي", "/uploads/a.png");

        Assert.Contains("<img class=\"ac-avatar-img\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("background-color:", html, StringComparison.Ordinal);
        Assert.DoesNotContain("linear-gradient", html, StringComparison.Ordinal);
    }
}
