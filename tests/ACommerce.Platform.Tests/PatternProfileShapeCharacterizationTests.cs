using ACommerce.Templates.Customer.Marketplace.Services.Patterns;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ «اليَوم» — شَخصِيّاتُ الأَنماطِ الخَمسِ بِكُلِّ أَعضائِها، و`CardChipKeys` عُضوٌ بِصِفرِ مُستَهلِك (القاعِدَتانِ ١ و٣) ═══
//
// **المَقيس** (‏2026-10-24، عِندَ `2939229`): `PatternProfile.CardChipKeys` («سِمات تَظهَر في البِطاقَة (≤ ٣)») يُكتَبُ لِكُلِّ نَمَطٍ
// في `PatternProfileResolver.Profile` ولا يَقرَؤُه شَيءٌ في وَقتِ التَشغيل — صِفرُ وُرودٍ لِاسمِه في `libs/` و`apps/` خارِجَ مِلَفِّ تَعريفِه
// (‏`.cs`/`.razor`/`.cshtml`/`.js`)، وصِفرٌ في الاختِبارات؛ وقالَه «أ» S11·١٢(ب) و`ADR-034` §٣‑٤ («حقلٌ ميّت») ولَم يُحذَف.
//
// **ما يُثَبَّتُ قَبلَ الحَذف**: كُلُّ عُضوٍ آخَرَ في الشَخصِيّاتِ الخَمسِ بِحَرفِه (الجَدوَلُ أَدناه — يَبقى أَخضَرَ بِلا تَعديلِ حَرف)،
// وصَفُّ «اليَوم» الَّذي يُثبِتُ العُضوَ حاضِراً بِقِيَمِه وبِلا قارِئ — يَنقَلِبُ وَحدَه بِاسمِه في كوميتِ الحَذف. والعُضوُ يُقرَأُ هُنا
// بِالانعِكاسِ لا بِاسمِه، فَالحَذفُ لا يَكسِرُ بِناءَ هذا المِلَفّ.

public class PatternProfileShapeCharacterizationTests
{
    private readonly ITestOutputHelper _out;
    public PatternProfileShapeCharacterizationTests(ITestOutputHelper output) => _out = output;

    /// <summary>كُلُّ عُضوٍ في الشَخصِيَّةِ إلّا <c>CardChipKeys</c> — في سَطرٍ واحِدٍ مُرَتَّب.</summary>
    internal static string Shape(PatternProfile p) => string.Join(" | ",
        p.Pattern, p.UsesCart, p.PrimaryCtaLabel, p.PrimaryCtaIcon, p.HomeHeroTitlePattern,
        p.ShowFeaturedCarousel, p.ShowLatestCarousel, p.ShowCategoryTree,
        string.Join(",", p.DetailGridKeys), p.FraudWarning ?? "∅", p.ShowDetailMap);

    [Theory]
    [InlineData(AppPattern.Roommate,    "Roommate | False | تَواصَل مَع المُعلِن | message | اِبحَث عَن شَريك السَكَن المُناسِب | True | True | True | gender_pref,occupation_pref,smoking,rent_split,deposit,available_from,min_stay | لا تَدفَع أَيّ مَبلَغ قَبل المُعايَنَة الشَخصِيَّة وَالتَأَكُّد مِن السَكَن. | True")]
    [InlineData(AppPattern.Rental,      "Rental | False | اطلُب مُعايَنَة | calendar | كُلّ ما يُؤَجَّر في مَدينَتِكَ | True | True | True | bedrooms,bathrooms,area_m2,furnished,lease_term,deposit,payment_schedule,utilities | لا تَدفَع التَأمين قَبل تَوقيع العَقد ومُعايَنَة العَقار. | True")]
    [InlineData(AppPattern.Marketplace, "Marketplace | True | أَضِف لِلسَلَّة | package | تَصَفَّح المُنتَجات واطلُب ما تُحِبّ | True | True | True | size,ingredients,calories,prep_time | ∅ | False")]
    [InlineData(AppPattern.Trip,        "Trip | False | قَدِّم عَرضاً | send | تَوصيلَتُك مِن أَيّ مَكان | False | True | False | from,to,when,weight,notes | ∅ | True")]
    [InlineData(AppPattern.Service,     "Service | False | احجِز الخِدمَة | calendar | اِحجِز خِدمَتَكَ بِنَقرَة | True | True | True | duration,location_type,team_size,warranty | ∅ | False")]
    public void Every_profile_member_but_the_card_chip_keys_is_pinned_byte_for_byte(AppPattern pattern, string expected)
        => Assert.Equal(expected, Shape(PatternProfileResolver.Profile(pattern)));

    /// <summary>كُلُّ مِلَفِّ مَصدَرٍ في <c>libs/</c> و<c>apps/</c> يَذكُرُ الاسمَ — مَعدوداً، لِيَطبَعَ الصَفُّ ما فَحَص (القاعِدَة ١٠).</summary>
    internal static (int Scanned, IReadOnlyList<string> Mentions) SourcesNaming(string name)
    {
        var root = ThemeZeroEquivalenceTests.RepoRoot;
        var exts = new[] { ".cs", ".razor", ".cshtml", ".js" };
        var files = new[] { "libs", "apps" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(root, d), "*", SearchOption.AllDirectories))
            .Where(f => exts.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();
        var mentions = files.Where(f => File.ReadAllText(f).Contains(name, StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).ToList();
        return (files.Count, mentions);
    }

    /// <summary>(‏2026-10-24، كوميتُ الحَذف — انقَلَبَ بِقَرارٍ مَرئِيّ: كانَ <c>Today_the_profile_record_carries_card_chip_keys_that_no_runtime_code_reads</c>
    /// يُثبِتُ العُضوَ حاضِراً بِقِيَمِه الخَمسِ (‏Roommate:gender_pref,occupation_pref,rent_split · Rental:bedrooms,area_m2,furnished ·
    /// Marketplace:size,ingredients_count · Trip:from,to,when · Service:duration,location_type) وبِلا قارِئ، والمُنشِئَ 12 وَسيطاً؛ وصارَ
    /// يُثبِتُه غائِباً، والاسمُ لا يَرِدُ في كودٍ — سَطرُ التَعليقِ المُؤَرَّخِ في مِلَفِّ التَعريفِ وَحدَه. والجَدوَلُ أَعلاه لَم يُمَسّ.)</summary>
    [Fact]
    public void The_profile_record_no_longer_carries_card_chip_keys_and_no_code_names_them()
    {
        Assert.Null(typeof(PatternProfile).GetProperty("CardChipKeys"));
        Assert.Equal(11, typeof(PatternProfile).GetConstructors().Single().GetParameters().Length);

        var (scanned, mentions) = SourcesNaming("CardChipKeys");
        _out.WriteLine($"مِلَفّاتٌ مَفحوصَة: {scanned} · تَذكُرُ CardChipKeys: {mentions.Count} ← {string.Join(", ", mentions)}");
        Assert.True(scanned > 300, $"أَداةٌ عَمياء: {scanned} مِلَفّاً فَقَط.");
        const string definition = "libs/templates/ACommerce.Templates.Customer.Marketplace/Services/Patterns/PatternProfile.cs";
        Assert.Equal(new[] { definition }, mentions);
        var codeLines = File.ReadAllLines(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, definition))
            .Where(l => l.Contains("CardChipKeys", StringComparison.Ordinal))
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .ToList();
        Assert.Empty(codeLines);
    }
}
