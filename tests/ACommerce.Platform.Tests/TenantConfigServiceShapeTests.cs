using System.Text.RegularExpressions;
using ACommerce.Templates.Customer.Marketplace.Services.Listings;
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
///   غَداً؛ ومُهايِئُ كُلّ مُجَلَّد — إن وُجِدَ — هُوَ
///   <b>الاستِثناء الوَحيد المُعلَن</b> فيه.</item>
///
///   <item><b>مُعجَمُ الرَفض مُغلَق</b> — كُلّ رَمزٍ تَرُدُّه خِدمَةٌ
///   عُضوٌ في مُعجَم مُجَلَّدِها. والانغِلاق مَفروضٌ مَرَّتَين:
///   نَصِّيّاً هُنا، وفي وَقت التَشغيل بِرَميٍ داخِلَ
///   <c>Reject</c> — فَسِلسِلَةٌ عابِرَة لا تَصير رَمزاً.</item>
/// </list>
///
/// <para><b>ووُسِّعَ في المَوجَة ٤ لِيَشمَلَ مُجَلَّداً ثانِياً</b>
/// (<c>Services/Listings</c>). والتَوسيعُ هُوَ الجَواب الصَحيح لا
/// فاحِصٌ ثانٍ (القاعِدَة ٨: «لا أُنبوب رابِع»): شَكلٌ واحِد
/// مَفروضٌ مِن مَوضِعٍ واحِد، وقائِمَةُ المُجَلَّدات بَياناتٌ في
/// أَعلى المِلَفّ. وفاحِصٌ ثانٍ بِقائِمَةٍ ثانِيَة يَنجَرِف عَنها
/// — وهذا بِعَينِه عَطَبُ «تَعريفَين لِقَرارٍ واحِد».</para>
/// </summary>
public class TenantConfigServiceShapeTests
{
    /// <summary>
    /// <para><b>صِنفُ المُجَلَّد — ومَن يَملِك الجَلسَة فيه.</b> والمِحوَرُ
    /// لَيسَ «يَقرَأ أَم يَكتُب» بَل <b>«هَل لِمُنادِيه مُعامَلَة؟»</b>:</para>
    /// <list type="bullet">
    ///   <item><see cref="Transactional"/> — يُنادى مِن جِسم نُقطَة، فَالمُعامَلَةُ
    ///   قائِمَةٌ والخِدمَةُ تَنضَمّ إلَيها ولا تَفتَح جَلسَةً ولا تُودِع.</item>
    ///   <item><see cref="Read"/> — يُنادى مِن صَفحَة <c>.razor</c>، ولا
    ///   مُعامَلَةَ لِلصَفحَة تَنضَمُّ إلَيها. فَالخِدمَةُ تَفتَح
    ///   <c>QuerySession</c> بِنَفسِها — ومَنعُها مِن ذلك يُعيد الجَلسَةَ
    ///   إلى الصَفحَة، وهو عَينُ الدَين الَّذي يُسَدَّد.</item>
    /// </list>
    /// </summary>
    private enum ServiceKind { Transactional, Read }

    /// <summary>مُجَلَّدُ خِدماتٍ يَخضَع لِلشَكل. <c>SurfaceFile</c>
    /// اسمُ المُهايِئ الَّذي يُسمَح لَه وَحدَه بِمَعرِفَة HTTP —
    /// و<c>null</c> تَعني «لا مُهايِئ هُنا، ولا مِلَفَّ يَعرِف
    /// HTTP». و<c>GlobalFile</c> (لِمُجَلَّدات <see cref="ServiceKind.Read"/>
    /// وَحدَها) اسمُ المِلَفّ الوَحيد المَأذون لَه بِجَلسَةٍ <b>بِلا
    /// سلاج مُستَأجِر</b>، لِأَنّ وَثيقَتَه مُسَجَّلَة
    /// <c>SingleTenanted</c>.</summary>
    private sealed record ShapedDir(
        string Path, string? SurfaceFile, string WhyAr,
        ServiceKind Kind = ServiceKind.Transactional, string? GlobalFile = null);

    private static readonly ShapedDir[] Dirs =
    {
        new("libs/templates/ACommerce.Templates.Customer.Marketplace/Services/TenantConfig",
            "TenantConfigSurface.cs",
            "سِتّ عَمَلِيّات إعدادِ مُستَأجِر، كُلٌّ يُنادِيها السَطحان " +
            "(‏/admin و/studio). والمُهايِئُ يُحَوِّل النَموذَجَ إلى طَلَبٍ " +
            "مَكتوبٍ بِأَنواعِه، والنَتيجَةَ إلى ردٍّ يَراه الجُمهور."),

        new("libs/templates/ACommerce.Templates.Customer.Marketplace/Services/Listings",
            null,
            "تَحريرُ الإعلان وحَذفُه — سَطحٌ واحِد اليَوم (نُقطَتا القالِب). " +
            "ولا مُهايِئَ هُنا عَمداً: النُقطَةُ تَقرَأ النَموذَجَ وتَعرِض " +
            "بِنَفسِها، فَالمُجَلَّدُ **صِفرُ مَعرِفَةٍ بِـHTTP** لا " +
            "مِلَفٌّ واحِدٌ مُستَثنى. مُهايِئٌ بِمُستَهلِكٍ واحِد تَجريدٌ " +
            "يَسبِق مُستَهلِكَه (القاعِدَة ١)."),

        new("libs/templates/ACommerce.Templates.Customer.Marketplace/Services/Subscriptions",
            null,
            "طَلَبُ الاشتِراك وقَرارُه — سَطحانِ اثنان: نُقطَةُ " +
            "`plans/{planId}/subscribe` ونُقطَةُ " +
            "`admin/tenants/{slug}/subscriptions/{reference}/decide`. ولا " +
            "مُهايِئَ هُنا عَمداً: النُقطَتانِ تَقرَآنِ النَموذَجَ " +
            "وتَعرِضانِ بِأَنفُسِهِما، فَالمُجَلَّدُ **صِفرُ مَعرِفَةٍ " +
            "بِـHTTP**. **والمُجَلَّدُ يُعلَن هُنا يَومَ يُنشَأ لا " +
            "بَعدَه**: مُجَلَّدُ خِدمَةٍ غَيرُ خاضِعٍ لِلفاحِص هُوَ " +
            "بِالضَبط الطَريقُ الَّذي يَنجَرِف بِه حَدٌّ (القاعِدَة ٢)."),

        new("libs/templates/ACommerce.Templates.Customer.Marketplace/Services/Queries",
            null,
            "خِدماتُ الاستِعلام لِلصَفَحات — المَوجَة ٥. تُنادى مِن " +
            "`.razor` لا مِن نُقطَة، ولا مُعامَلَةَ لِلصَفحَة تَنضَمُّ " +
            "إلَيها، فَتَفتَح جَلسَةَ قِراءَةٍ بِنَفسِها كَما تَفعَل " +
            "`ListingLookupService` مُنذُ المَوجَة ٤. وشَرطُها الَّذي " +
            "يَقوم مَقامَ «لا تَفتَح جَلسَة»: **كُلّ جَلسَةٍ بِسلاج " +
            "مُستَأجِر** إلّا في سِجِلّ المُستَأجِرين نَفسِه.",
            ServiceKind.Read,
            GlobalFile: "TenantDirectory.cs"),
    };

    /// <summary>كُلّ مِلَفّات الخِدمَة في كُلّ مُجَلَّد خاضِع، مَع
    /// مُجَلَّدِها — فَرِسالَةُ الخَرق تَقول أَينَ وَقَع.</summary>
    private static IReadOnlyList<(ShapedDir Dir, string Name, string Code)> ServiceFiles()
    {
        var rows = new List<(ShapedDir, string, string)>();

        foreach (var d in Dirs)
        {
            var dir = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
                d.Path.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(Directory.Exists(dir), $"أَداة عَمياء: لا مُجَلَّد {d.Path}.");

            foreach (var f in Directory.EnumerateFiles(dir, "*.cs").OrderBy(x => x, StringComparer.Ordinal))
                rows.Add((d, Path.GetFileName(f),
                          WriteEndpointGuardTests.StripComments(File.ReadAllText(f))));
        }

        return rows;
    }

    [Fact]
    public void No_service_opens_or_commits_a_session()
    {
        var files = ServiceFiles();
        Assert.True(files.Count >= 5, $"أَداة عَمياء: {files.Count} مِلَفّ فَقَط.");

        // ولا مُجَلَّدَ فارِغ يَمُرّ صامِتاً: مُجَلَّدٌ مُثَبَّت بِلا
        // مِلَفّ يَجعَل الفاحِصَ يُصادِق على لا شَيء.
        foreach (var d in Dirs)
            Assert.True(files.Any(f => f.Dir == d), $"أَداة عَمياء: {d.Path} بِلا مِلَفّ.");

        var breaches = new List<string>();
        foreach (var (d, name, code) in files)
        {
            // ومُجَلَّدُ القِراءَة يُعفى مِن `QuerySession(` وَحدَها —
            // لا مِن الكِتابَة. الجَلسَةُ القابِلَةُ لِلكِتابَة والإيداع
            // يَبقَيانِ مَمنوعَين في كُلّ مُجَلَّد خاضِع: صَفحَةٌ تَكتُب
            // تَقَع خارِج المُعامَلَة وخارِج الصُندوق الصادِر، وذاكَ هُوَ
            // الخَطَر الَّذي يُمَيِّزُه سِجِلُّ الطَبَقَة ٨ نَفسُه.
            var forbidden = d.Kind == ServiceKind.Read
                ? new[] { "LightweightSession(", "SaveChangesAsync" }
                : new[] { "LightweightSession(", "QuerySession(", "SaveChangesAsync" };

            foreach (var f in forbidden)
                if (code.Contains(f, StringComparison.Ordinal))
                    breaches.Add(d.Kind == ServiceKind.Read
                        ? $"{d.Path}/{name}: «{f}» — خِدمَةُ استِعلامٍ تَقرَأ ولا تَكتُب."
                        : $"{d.Path}/{name}: «{f}» — الخِدمَة تَأخُذ الجَلسَة ولا تَملِكُها.");
        }

        Assert.True(breaches.Count == 0, string.Join("\n  ", breaches));
    }

    /// <summary>
    /// <para><b>العَزلُ بُنيَويّ لا اتِّفاقيّ — والفاحِصُ هُوَ ما
    /// يَجعَلُه كَذلك.</b> كُلّ <c>QuerySession(</c> في مُجَلَّد قِراءَةٍ
    /// يَجِب أَن يَحمِل <b>وَسيطاً</b> (سلاج المُستَأجِر). والنَداءُ
    /// العاري <c>QuerySession()</c> يُعطي جَلسَةَ <c>*DEFAULT*</c> —
    /// وهو بِعَينِه العَطَبُ الَّذي قاسَته المَوجَة ١ في سِتّ مُعالِجات،
    /// وأَثبَتَه <c>LiveOutboxTenantProofTests</c> بِطَرَفَيه
    /// (‏<c>detect=off → *DEFAULT*</c>).</para>
    ///
    /// <para><b>والاستِثناءُ واحِدٌ ومُعلَنٌ وثُنائيّ الاتِّجاه</b>:
    /// وَثيقَةُ <c>Tenant</c> مُسَجَّلَة <c>SingleTenanted()</c> — سِجِلُّ
    /// المُستَأجِرين لا يَقَع في مُستَأجِر. فَمِلَفٌّ واحِدٌ يُعلَن
    /// بِاسمِه، <b>ويَحمَرّ إن لَم يَعُد يَفتَح جَلسَةً عارِيَة</b> —
    /// فَلا يَبقى إذنٌ حَيّاً بَعدَ زَوال سَبَبِه.</para>
    /// </summary>
    [Fact]
    public void Every_read_service_scopes_its_session_to_a_tenant()
    {
        var readDirs = Dirs.Where(d => d.Kind == ServiceKind.Read).ToArray();
        Assert.True(readDirs.Length > 0, "أَداة عَمياء: لا مُجَلَّدَ قِراءَةٍ واحِداً.");

        var scoped = 0;
        var global = 0;
        var breaches = new List<string>();

        foreach (var (d, name, code) in ServiceFiles())
        {
            if (d.Kind != ServiceKind.Read) continue;

            foreach (Match m in Regex.Matches(code, @"QuerySession\(\s*(?<a>[^)]*)\)"))
            {
                var arg = m.Groups["a"].Value.Trim();
                if (arg.Length > 0) { scoped++; continue; }

                global++;
                if (name != d.GlobalFile)
                    breaches.Add($"{d.Path}/{name}: «QuerySession()» بِلا سلاج — " +
                                 $"جَلسَةُ *DEFAULT*. المَأذون وَحدَه: {d.GlobalFile ?? "لا أَحَد"}.");
            }
        }

        // عَدّادانِ لِطَرَفَي المِعيار (القاعِدَة ١٠): صِفرُ جَلسَةٍ
        // مُسَلَّجَة يَعني أَنّ النَمَطَ لَم يَرَ شَيئاً؛ وصِفرُ جَلسَةٍ
        // عارِيَة يَعني أَنّ الاستِثناءَ المُعلَن ماتَ ولَم يُرفَع.
        Assert.True(scoped > 0, "أَداة عَمياء: صِفر «QuerySession(slug)» في مُجَلَّدات القِراءَة.");

        foreach (var d in readDirs)
        {
            if (d.GlobalFile is null) continue;
            var file = ServiceFiles().FirstOrDefault(f => f.Dir == d && f.Name == d.GlobalFile);
            Assert.False(file.Code is null,
                $"استِثناءٌ مُثَبَّت لِمِلَفٍّ لا وُجودَ لَه: {d.Path}/{d.GlobalFile}.");
            Assert.True(Regex.IsMatch(file.Code, @"QuerySession\(\s*\)"),
                $"{d.Path}/{d.GlobalFile} لَم يَعُد يَفتَح جَلسَةً عارِيَة — " +
                "ارفَع الاستِثناء مِن القائِمَة، فَالإذنُ يَموت مَع سَبَبِه.");
        }

        Assert.True(global > 0, "أَداة عَمياء: صِفر «QuerySession()» — إمّا زالَ سِجِلُّ " +
                                "المُستَأجِرين وإمّا كَذَبَ النَمَط.");
        Assert.True(breaches.Count == 0, string.Join("\n  ", breaches));
    }

    [Fact]
    public void No_service_except_the_declared_surface_adapter_knows_http()
    {
        var breaches = new List<string>();

        foreach (var (d, name, code) in ServiceFiles())
        {
            if (name == d.SurfaceFile) continue;
            foreach (var forbidden in new[] { "HttpRequest", "IFormCollection", "IFormFile", "IResult", "Results." })
                if (code.Contains(forbidden, StringComparison.Ordinal))
                    breaches.Add($"{d.Path}/{name}: «{forbidden}» — الخِدمَة لا تَعرِف HTTP" +
                                 (d.SurfaceFile is null
                                     ? "، ولا مُهايِئَ مُعلَناً في هذا المُجَلَّد."
                                     : $"؛ المُهايِئ {d.SurfaceFile} وَحدَه يَعرِفُه."));
        }

        Assert.True(breaches.Count == 0, string.Join("\n  ", breaches));
    }

    /// <summary>والاستِثناءُ يَموت مَع سَبَبِه: مُهايِئٌ لا يَعرِف HTTP
    /// لَم يَعُد مُهايِئاً — يُرفَع مِن الاستِثناء. ومُجَلَّدٌ أُعلِنَ
    /// بِلا مُهايِئ ثُمَّ صارَ لَه واحِد يُمسِكُه الفَحصُ أَعلاه.</summary>
    [Fact]
    public void The_declared_surface_adapter_really_is_the_one_that_knows_http()
    {
        var files = ServiceFiles();
        var declared = Dirs.Count(d => d.SurfaceFile is not null);
        Assert.True(declared > 0, "أَداة عَمياء: لا مُهايِئَ مُعلَناً واحِداً.");

        foreach (var d in Dirs)
        {
            if (d.SurfaceFile is null) continue;
            var surface = files.FirstOrDefault(f => f.Dir == d && f.Name == d.SurfaceFile);
            Assert.False(surface.Code is null,
                $"استِثناءٌ مُثَبَّت لِمِلَفٍّ لا وُجودَ لَه: {d.Path}/{d.SurfaceFile}.");
            Assert.Contains("HttpRequest", surface.Code, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// <para><b>ولا خِدمَةَ بِلا مُستَهلِكَين</b> (القاعِدَة ١: التَجريد
    /// لا يَسبِق مُستَهلِكَه). خِدمَةٌ هُنا لا يُنادِيها
    /// <b>المَسارانِ مَعاً</b> لَيسَت تَوحيداً — هي نُسخَةٌ ثالِثَة.
    /// فَالمَقياس لَيسَ «هَل لَها مُستَهلِك» بَل «هَل لَها
    /// السَطحان».</para>
    ///
    /// <para><b>ونِطاقُه <c>*SaveService</c> وَحدَها، وهذا مَقصود</b>:
    /// شَرطُ السَطحَين وُجِدَ لِأَنّ تِلكَ السِتّ كانَت
    /// <b>مَكتوبَةً مَرَّتَين</b> فَوُحِّدَت. و<c>ListingEditService</c>
    /// لَم تُكتَب مَرَّتَين قَطّ — بَل لَم يَكُن لَها سَطحٌ واحِد،
    /// وذاكَ هُوَ العَطَبُ الَّذي سَدَّته المَوجَة ٤. فَشَرطُ
    /// السَطحَين عَلَيها يَقلِب القاعِدَةَ إلى طَقس: يُوجِب سَطحاً
    /// إدارِيّاً لا يَطلُبُه أَحَد.</para>
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

    /// <summary>كُلّ نَوعِ نَتيجَةٍ في مُجَلَّدٍ خاضِع — الاسمُ في
    /// النِداء يَجِب أَن يَكون أَحَدَها، فَنَوعٌ ثالِثٌ يُضاف بِلا
    /// تَسجيلٍ هُنا لا يُفحَص انغِلاقُه.</summary>
    private static readonly Regex RejectCall =
        new(@"(?<t>TenantConfigResult|ListingEditResult)\.Reject\(\s*(?<a>[^)]+?)\s*\)",
            RegexOptions.Compiled);

    /// <summary>ورَمزُ المُعجَم يُكتَب بِثابِتٍ مِن صِنفِ مُعجَمِ
    /// نَوعِه — لا مُعجَمَ مُشتَرَك، لِأَنّ الاشتِراكَ يَفتَحُهُما
    /// مَعاً.</summary>
    private static readonly IReadOnlyDictionary<string, string> CodeClassOf =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TenantConfigResult"] = "TenantConfigCodes.",
            ["ListingEditResult"]  = "ListingEditCodes.",
        };

    [Fact]
    public void Every_rejection_code_is_a_member_of_the_closed_vocabulary()
    {
        var sites = 0;
        var breaches = new List<string>();

        foreach (var (d, name, code) in ServiceFiles())
        foreach (Match m in RejectCall.Matches(code))
        {
            sites++;
            var type = m.Groups["t"].Value;
            var arg  = m.Groups["a"].Value.Trim();

            // شَكلانِ مَقبولانِ: ثابِتٌ مِن مُعجَم نَوعِه، أَو
            // مُتَغَيِّرٌ أُسنِدَ مِن دالَّةِ قَرارٍ تُعيد ثَوابِتَه.
            if (arg.StartsWith(CodeClassOf[type], StringComparison.Ordinal)) continue;
            if (Regex.IsMatch(arg, @"^[a-z][A-Za-z0-9]*$")) continue;

            breaches.Add($"{d.Path}/{name}: {type}.Reject({arg}) — رَمزٌ لا يَعود إلى {CodeClassOf[type]}");
        }

        Assert.True(sites > 0, "أَداة عَمياء: لا مَوضِعَ رَفضٍ واحِد.");

        // وعَدّادٌ لِكُلّ نَوع: نَوعٌ بِصِفر مَوضِع رَفض يَعني أَنّ
        // النَمَطَ لَم يَرَه — وذاكَ صِفرٌ كاذِب لا انغِلاقٌ مُثبَت.
        foreach (var type in CodeClassOf.Keys)
            Assert.True(
                ServiceFiles().Any(f => f.Code.Contains(type + ".Reject(", StringComparison.Ordinal)),
                $"أَداة عَمياء: {type}.Reject بِصِفر مَوضِع — إمّا زالَ النَوع، وإمّا كَذَبَ النَمَط.");

        Assert.True(breaches.Count == 0, string.Join("\n  ", breaches));
    }

    /// <summary>والانغِلاق مَفروضٌ في وَقت التَشغيل أَيضاً: رَمزٌ
    /// خارِجَ المُعجَم يَرمي، فَلا يَتَسَلَّل عَبرَ مُتَغَيِّر.
    /// <b>وبِالطَرَفَين لِكُلّ نَوع</b> — رَميٌ لِلغَريب، وقَبولٌ
    /// لِكُلّ عُضو.</summary>
    [Fact]
    public void A_code_outside_the_vocabulary_throws_at_runtime()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TenantConfigResult.Reject("made_up"));
        foreach (var c in TenantConfigCodes.All)
            Assert.Equal(c, TenantConfigResult.Reject(c).Code);

        Assert.Throws<ArgumentOutOfRangeException>(() => ListingEditResult.Reject("made_up"));
        // ورَمزٌ مِن المُعجَم الآخَر غَريبٌ هُنا — والمُعجَمانِ
        // مُنفَصِلانِ فِعلاً لا بِالاسم فَقَط.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ListingEditResult.Reject(TenantConfigCodes.NoScope));
        foreach (var c in ListingEditCodes.All)
            Assert.Equal(c, ListingEditResult.Reject(c).Code);
    }
}
