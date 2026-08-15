using System.Text.RegularExpressions;
using ACommerce.Templates.Customer.Marketplace.Services.TenantConfig;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>شَكل الخِدمَة مَفروضٌ آلِيّاً، لا مَوصوفٌ في وَثيقَة</b>
/// (القاعِدَة ٢: الحَدّ الَّذي لا يُقاس آلِيّاً يَنهار). القَرار
/// الهَجين يُعطي الخِدمَةَ ثَلاثَ خَصائِص، ولِكُلٍّ هُنا اختِبارٌ
/// يَفشَل عِندَ خَرقِها:</para>
///
/// <list type="number">
///   <item><b>تَأخُذ الجَلسَة ولا تَملِكُها</b> — لا
///   <c>LightweightSession(</c> ولا <c>QuerySession(</c> ولا
///   <c>SaveChangesAsync</c>. المُعامَلَةُ لِلنُقطَة، وإلّا صارَ
///   حِفظُ عَمَلِيَّتَين مَعاً مُستَحيلاً ذَرِّيّاً.</item>
///
///   <item><b>لا تَعرِف HTTP</b> — لا <c>HttpRequest</c> ولا
///   <c>IFormCollection</c> ولا <c>IResult</c> في أَيّ مِلَفّ خِدمَة.
///   وهذا بِعَينِه ما يَجعَلُها صالِحَةً لِـAPI ولِتَطبيقٍ أَصيل
///   غَداً؛ والمُهايِئ <c>TenantConfigSurface</c> هُوَ
///   <b>الاستِثناء الوَحيد المُعلَن</b>.</item>
///
///   <item><b>مُعجَمُ الرَفض مُغلَق</b> — كُلّ رَمزٍ تَرُدُّه خِدمَةٌ
///   عُضوٌ في <see cref="TenantConfigCodes"/>. والانغِلاق مَفروضٌ
///   مَرَّتَين: نَصِّيّاً هُنا، وفي وَقت التَشغيل بِرَميٍ داخِلَ
///   <see cref="TenantConfigResult.Reject"/> — فَسِلسِلَةٌ عابِرَة لا
///   تَصير رَمزاً.</item>
/// </list>
/// </summary>
public class TenantConfigServiceShapeTests
{
    private const string Dir =
        "libs/templates/ACommerce.Templates.Customer.Marketplace/Services/TenantConfig";

    /// <summary>المُهايِئ وَحدَه يَعرِف HTTP — بِاسمِه وسَبَبِه.</summary>
    private const string SurfaceFile = "TenantConfigSurface.cs";

    private static IReadOnlyList<(string Name, string Code)> ServiceFiles()
    {
        var dir = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, Dir.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(Directory.Exists(dir), $"أَداة عَمياء: لا مُجَلَّد {Dir}.");

        return Directory.EnumerateFiles(dir, "*.cs")
            .Select(f => (Path.GetFileName(f), WriteEndpointGuardTests.StripComments(File.ReadAllText(f))))
            .OrderBy(x => x.Item1, StringComparer.Ordinal)
            .ToArray();
    }

    [Fact]
    public void No_service_opens_or_commits_a_session()
    {
        var files = ServiceFiles();
        Assert.True(files.Count >= 3, $"أَداة عَمياء: {files.Count} مِلَفّ فَقَط.");

        var breaches = new List<string>();
        foreach (var (name, code) in files)
            foreach (var forbidden in new[] { "LightweightSession(", "QuerySession(", "SaveChangesAsync" })
                if (code.Contains(forbidden, StringComparison.Ordinal))
                    breaches.Add($"{name}: «{forbidden}» — الخِدمَة تَأخُذ الجَلسَة ولا تَملِكُها.");

        Assert.True(breaches.Count == 0, string.Join("\n  ", breaches));
    }

    [Fact]
    public void No_service_except_the_declared_surface_adapter_knows_http()
    {
        var files = ServiceFiles();
        var breaches = new List<string>();

        foreach (var (name, code) in files)
        {
            if (name == SurfaceFile) continue;
            foreach (var forbidden in new[] { "HttpRequest", "IFormCollection", "IFormFile", "IResult", "Results." })
                if (code.Contains(forbidden, StringComparison.Ordinal))
                    breaches.Add($"{name}: «{forbidden}» — الخِدمَة لا تَعرِف HTTP؛ المُهايِئ {SurfaceFile} وَحدَه يَعرِفُه.");
        }

        Assert.True(breaches.Count == 0, string.Join("\n  ", breaches));
    }

    /// <summary>والاستِثناءُ يَموت مَع سَبَبِه: مُهايِئٌ لا يَعرِف HTTP
    /// لَم يَعُد مُهايِئاً — يُرفَع مِن الاستِثناء.</summary>
    [Fact]
    public void The_declared_surface_adapter_really_is_the_one_that_knows_http()
    {
        var surface = ServiceFiles().FirstOrDefault(f => f.Name == SurfaceFile);
        Assert.False(surface.Code is null, $"استِثناءٌ مُثَبَّت لِمِلَفٍّ لا وُجودَ لَه: {SurfaceFile}.");
        Assert.Contains("HttpRequest", surface.Code, StringComparison.Ordinal);
    }

    /// <summary>
    /// <para><b>ولا خِدمَةَ بِلا مُستَهلِكَين</b> (القاعِدَة ١: التَجريد
    /// لا يَسبِق مُستَهلِكَه). خِدمَةٌ هُنا لا يُنادِيها
    /// <b>المَسارانِ مَعاً</b> لَيسَت تَوحيداً — هي نُسخَةٌ ثالِثَة.
    /// فَالمَقياس لَيسَ «هَل لَها مُستَهلِك» بَل «هَل لَها
    /// السَطحان».</para>
    /// </summary>
    [Fact]
    public void Every_service_is_called_from_both_surfaces()
    {
        var declared = ServiceFiles()
            .Select(f => Path.GetFileNameWithoutExtension(f.Name))
            .Where(n => n.EndsWith("SaveService", StringComparison.Ordinal))
            .ToArray();

        Assert.True(declared.Length > 0, "أَداة عَمياء: لا خِدمَةَ حِفظٍ واحِدَة.");

        var admin = new Dictionary<string, int>(StringComparer.Ordinal);
        var studio = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var e in WriteEndpointGuardTests.AllMinimalApiEndpoints())
        foreach (var name in declared)
        {
            if (!e.Body.Contains(name + ".", StringComparison.Ordinal)) continue;
            var bucket = e.Route.StartsWith("/admin/", StringComparison.Ordinal) ? admin
                       : e.Route.StartsWith("/studio/", StringComparison.Ordinal) ? studio
                       : null;
            if (bucket is null) continue;
            bucket[name] = bucket.GetValueOrDefault(name) + 1;
        }

        var breaches = declared
            .Where(n => !admin.ContainsKey(n) || !studio.ContainsKey(n))
            .Select(n => $"{n}: admin={admin.GetValueOrDefault(n)}, studio={studio.GetValueOrDefault(n)}")
            .ToArray();

        Assert.True(breaches.Length == 0,
            "خِدمَةٌ لا يُنادِيها السَطحان — نُسخَةٌ ثالِثَة لا تَوحيد:\n  " +
            string.Join("\n  ", breaches));
    }

    private static readonly Regex RejectCall =
        new(@"TenantConfigResult\.Reject\(\s*(?<a>[^)]+?)\s*\)", RegexOptions.Compiled);

    [Fact]
    public void Every_rejection_code_is_a_member_of_the_closed_vocabulary()
    {
        var sites = 0;
        var breaches = new List<string>();

        foreach (var (name, code) in ServiceFiles())
        foreach (Match m in RejectCall.Matches(code))
        {
            sites++;
            var arg = m.Groups["a"].Value.Trim();

            // شَكلانِ مَقبولانِ: ثابِتٌ مِن المُعجَم، أَو مُتَغَيِّرٌ
            // أُسنِدَ مِن دالَّةِ قَرارٍ تُعيد ثَوابِتَ المُعجَم.
            if (arg.StartsWith("TenantConfigCodes.", StringComparison.Ordinal)) continue;
            if (Regex.IsMatch(arg, @"^[a-z][A-Za-z0-9]*$")) continue;

            breaches.Add($"{name}: Reject({arg}) — رَمزٌ لا يَعود إلى TenantConfigCodes.");
        }

        Assert.True(sites > 0, "أَداة عَمياء: لا مَوضِعَ رَفضٍ واحِد.");
        Assert.True(breaches.Count == 0, string.Join("\n  ", breaches));
    }

    /// <summary>والانغِلاق مَفروضٌ في وَقت التَشغيل أَيضاً: رَمزٌ
    /// خارِجَ المُعجَم يَرمي، فَلا يَتَسَلَّل عَبرَ مُتَغَيِّر.</summary>
    [Fact]
    public void A_code_outside_the_vocabulary_throws_at_runtime()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TenantConfigResult.Reject("made_up"));
        foreach (var c in TenantConfigCodes.All)
            Assert.Equal(c, TenantConfigResult.Reject(c).Code);
    }
}
