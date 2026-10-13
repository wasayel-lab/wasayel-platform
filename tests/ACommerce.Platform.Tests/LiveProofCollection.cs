using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ البَراهينُ الحَيَّةُ تَمشي واحِداً واحِداً — مَجموعَةٌ واحِدَةٌ بِلا تَوازٍ (‏2026-10-13) ═══════════════════════
//
// **الكَلفَةُ الَّتي كَتَبَتها** (مَقيسَةٌ ‏2026-10-11 في تَيّارٍ سابِق): بُرهانانِ حَيّانِ شُغِّلا مَعاً تَسابَقا عَلى إنشاءِ مُخَطَّطِ
// Marten — ‏`23505: duplicate key value violates unique constraint "pg_type_typname_nsp_index"` أَثناءَ «All Configured Changes»
// — فَفَشِلَ أَحَدُهُما، ونَجَحَ كُلٌّ مِنهُما وَحدَه. **والعِلَّة**: xUnit يُشَغِّلُ كُلَّ صِنفِ اختِبارٍ في مَجموعَةٍ تَخُصُّه وبِالتَوازي،
// وكُلُّ صِنفٍ حَيٍّ يَبني `DocumentStore` بِـ`AutoCreate.All` عَلى المُخَطَّطِ `platform` نَفسِه؛ فَإن وَقَعَت أَوَّلُ كِتابَتَينِ مَعاً
// عَلى مُخَطَّطٍ لَم يُنشَأ بَعدُ أَنشَأَ الاثنانِ النَوعَ نَفسَه في Postgres، فَيُرَدُّ أَحَدُهُما.
//
// **والآلِيَّةُ مَقيسَةٌ اليَومَ لا مَنقولَة** (‏2026-10-13، Postgres 16 مَحَلِّيّ، مِجَسٌّ خارِجَ المُستَودَع): مَخزَنانِ بِتَركيبِ
// `LiveDealConcurrencyProofTests.BuildStore` نَفسِه يَكتُبانِ أَوَّلَ `Deal` مَعاً في مُخَطَّطٍ فارِغٍ جَديدٍ لِكُلِّ دَورَة —
// ‏**3 دَوراتٍ مِن 10** يُرَدُّ فيها أَحَدُهُما بِـ`MartenSchemaException ← PostgresException: 23505 … pg_type_typname_nsp_index`؛
// ومَخزَنٌ واحِدٌ ‏**0 مِن 3**. وأَربَعُ جَولاتٍ لِلحَقيبَةِ الحَيَّةِ كُلِّها (‏46 اختِباراً) عَلى قَواعِدَ فارِغَةٍ لَم يَقَع فيها — فَالسِباقُ
// نافِذَةٌ ضَيِّقَةٌ تَقَعُ أَحياناً، وهذا بِعَينِه ما يَجعَلُه يُشبِهُ «اختِباراً مُتَقَلِّباً» فَيُعادُ ولا يُصلَح.
//
// **والعِلاجُ أَصغَرُ الخِيارَين** (التَكليف: مَجموعَةٌ واحِدَةٌ تَسلسُلِيَّة **أَو** مُخَطَّطٌ لِكُلِّ صِنف): مُخَطَّطٌ لِكُلِّ صِنفٍ
// يَمَسُّ سَبعَةً وعِشرينَ تَركيباً ويُبعِدُ البُرهانَ عَن المُخَطَّطِ الَّذي يُشَغِّلُه المُضيف (`platform`) — وهو ما يَقيسُه. والمَجموعَةُ
// بِـ`DisableParallelization` لا تَجري مَعَ أَيِّ مَجموعَةٍ أُخرى، وأَصنافُها واحِداً بَعدَ واحِد؛ والحارِسُ داخِلَ كُلِّ اختِبارٍ
// (`if (!Enabled …) return;`) يَجعَلُ كُلفَتَها في التَشغيلِ العادِيِّ ما لا يُذكَر.
//
// **والحَدُّ مَقيسٌ آلِيّاً لا مَوعود** (القاعِدَة ٢): `LiveProofCollectionTests` أَدناه يَحمَرُّ عَلى صِنفٍ حَيٍّ جَديدٍ خارِجَ
// المَجموعَة. **وحَدُّه يُقال**: العَقدُ اسمُ الصِنف (`Live…ProofTests` — سَبعَةٌ وعِشرونَ مِن سَبعَةٍ وعِشرين اليَوم)، فَصِنفٌ حَيٌّ بِاسمٍ
// آخَرَ لا يَراه.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LiveProofCollection
{
    public const string Name = "live-proof";
}

public class LiveProofCollectionTests
{
    private static readonly Regex LiveProofName = new(@"^Live\w*ProofTests$");

    private static List<Type> LiveProofClasses() =>
        typeof(LiveProofCollectionTests).Assembly.GetTypes()
            .Where(t => t.IsClass && LiveProofName.IsMatch(t.Name))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

    private static string? CollectionOf(Type t) =>
        t.GetCustomAttributesData()
            .Where(a => a.AttributeType == typeof(CollectionAttribute))
            .Select(a => a.ConstructorArguments[0].Value as string)
            .FirstOrDefault();

    /// <summary><b>كُلُّ صِنفِ بُرهانٍ حَيٍّ في المَجموعَةِ التَسلسُلِيَّة</b> — والعَدَدُ يُطبَعُ في الرِسالَةِ ويَفشَلُ عَلى الصِفر
    /// (القاعِدَة ١٠)، ويُقابَلُ بِعَدِّ الأَصنافِ المُعلَنَةِ في المَصدَر فَلا يَعمى المُجَمَّعُ عَن مِلَفٍّ.</summary>
    [Fact]
    public void Every_live_proof_class_runs_in_the_one_serial_collection()
    {
        var live = LiveProofClasses();
        Assert.True(live.Count > 20, $"أَداةٌ عَمياء: {live.Count} صِنفَ بُرهانٍ حَيٍّ في المُجَمَّع.");

        var declared = Directory.EnumerateFiles(
                Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "tests", "ACommerce.Platform.Tests"), "*.cs")
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"public\s+(?:sealed\s+)?class\s+(Live\w*ProofTests)\b")
                .Select(m => m.Groups[1].Value))
            .Distinct()
            .Count();
        Assert.True(declared == live.Count,
            $"المَصدَرُ يُعلِنُ {declared} صِنفاً حَيّاً والمُجَمَّعُ يَحمِلُ {live.Count} — أَحَدُ العَدَّينِ أَعمى.");

        var outside = live.Where(t => CollectionOf(t) != LiveProofCollection.Name).Select(t => t.Name).ToList();
        Assert.True(outside.Count == 0,
            $"صِنفُ بُرهانٍ حَيٍّ خارِجَ مَجموعَةِ «{LiveProofCollection.Name}» ({live.Count} مَعدوداً) — يَجري بِالتَوازي مَعَ غَيرِه "
          + "فَيَتَسابَقانِ عَلى إنشاءِ مُخَطَّطِ Marten (23505 pg_type_typname_nsp_index): " + string.Join(", ", outside));
    }

    /// <summary><b>والمَجموعَةُ تَسلسُلِيَّةٌ فِعلاً</b>: بِلا <c>DisableParallelization</c> تَجري مَجموعَةٌ واحِدَةٌ مَعَ غَيرِها مِن
    /// المَجموعات، وأَصنافُها مُتَتالِيَةٌ فيما بَينَها — فَقَلبُ الخاصِّيَّةِ يُعيدُ نِصفَ السِباقِ ولا يَحمَرُّ غَيرُ هذا.</summary>
    [Fact]
    public void The_live_proof_collection_disables_parallelization()
    {
        var definition = typeof(LiveProofCollection).GetCustomAttributesData()
            .Single(a => a.AttributeType == typeof(CollectionDefinitionAttribute));
        Assert.Equal(LiveProofCollection.Name, definition.ConstructorArguments[0].Value);
        var disable = definition.NamedArguments
            .SingleOrDefault(n => n.MemberName == nameof(CollectionDefinitionAttribute.DisableParallelization));
        Assert.True(disable.TypedValue.Value is true,
            "مَجموعَةُ البَراهينِ الحَيَّةِ بِلا DisableParallelization = true — تَجري مَعَ غَيرِها.");
    }
}
