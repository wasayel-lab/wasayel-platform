using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ACommerce.Platform.I18n;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace.Services.Audit;
using ACommerce.Templates.Customer.Marketplace.Services.Export;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ التَخارُج — ثَلاثَةُ أَسئِلَةٍ لا سُؤالان ══════════════════════
//
// **كُلُّ اختِبارٍ هُنا كُتِبَ قَبلَ حَرفٍ واحِدٍ مِن المُصَدِّر**
// (القاعِدَة ٣)، واسمُه يَقولُ الأَثَرَ لا اسمَ الدالَّة.
//
// والأَسئِلَةُ الثَلاثَةُ **مُتَساوِيَةُ الوَزن**:
//
//   ١. **أَيَخرُجُ صَفٌّ لِمُستَأجِرٍ آخَر؟** — التَسريب.
//   ٢. **أَيَخرُجُ بَندٌ مِن قائِمَةِ الاستِثناء؟** — الاعتِماد.
//   ٣. **أَيَنقُصُ صِنفٌ مِن أَصنافِ بَياناتِ المُستَأجِر؟** — النَقص.
//
// **والثالِثُ لَيسَ أَخَفَّ مِن الأَوَّلَين**: تَخارُجٌ مَنقوصٌ أَسوَأُ
// مِن لا تَخارُج، لِأَنَّه **يُطَمئِنُ كَذِباً** — يَستَلِمُ العَميلُ
// حَقيبَةً يَظُنُّها بَياناتِه كامِلَةً، ويَكتَشِفُ النَقصَ بَعدَ أَن
// أُغلِقَ حِسابُه.
//
// ─── ولِماذا الحارِسُ في الكاتِبِ لا في الاختِبارِ وَحدَه ───────────
//
// ‏`TenantExportPackageWriter.Write` **يَرمي** عِندَ كُلِّ خَرقٍ مِن
// الثَلاثَة. فَالاختِبارُ يُطعِمُه صَفَّ مُستَأجِرٍ آخَرَ ويَتَأَكَّدُ
// أَنَّه رَمى — أَي أَنّ الحِمايَةَ **تَعمَلُ في الإنتاج** لا في
// حَقيبَةِ الاختِبارِ وَحدَها. حارِسٌ يَعيشُ في الاختِبارِ فَقَط
// يَحرُسُ الاختِبار.
//
// ─── حارِسُ العَمى (القاعِدَة ١٠) ────────────────────────────────────
// كُلُّ فاحِصٍ يَطبَعُ عَدَدَ ما فَحَص، ويَحمَرُّ عِندَ الصِفر.
public class TenantExportTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private const string TemplateRoot = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    private static string Read(string relative)
        => File.ReadAllText(Path.Combine(RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

    // ═════════════════════════════════════════════════════════════════
    //  ١) النَقص — أَيَنقُصُ صِنفٌ مِن أَصنافِ بَياناتِ المُستَأجِر؟
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// <para><b>كُلُّ نَوعِ وَثيقَةٍ في المُستَودَعِ مُصَنَّفٌ في
    /// السِجِلّ</b> — يَخرُج، أَو يُستَثنى بِسَبَبٍ مَكتوب. ولا
    /// ثالِثَ.</para>
    ///
    /// <para><b>ولِماذا هذا هُوَ الفاحِصُ الَّذي يَمنَعُ الانجِراف</b>:
    /// نَوعٌ جَديدٌ يُسَجَّلُ غَداً في مَوجَةٍ أُخرى يَحمَرُّ هُنا
    /// <b>قَبلَ</b> أَن يَصِلَ إلى العَميل — إمّا مَنقوصاً مِن حَقيبَتِه
    /// وإمّا مُسَرَّباً فيها. والقائِمَةُ البَيضاءُ وَحدَها لا تَكفي:
    /// هي تَمنَعُ التَسريبَ ولا تَكشِفُ النَقص.</para>
    /// </summary>
    [Fact]
    public void Every_marten_document_type_in_the_repo_is_classified_in_the_export_ledger()
    {
        // أَنواعُ الوَثائِقِ تُستَخرَجُ مِن **مَواضِعِ الاستِعمالِ
        // الفِعليَّة** لا مِن قائِمَةٍ تُكتَبُ بِاليَد.
        var generic = new Regex(
            @"(?:Query|LoadAsync|LoadManyAsync|Delete|DeleteWhere|AggregateStreamAsync|FetchForWriting|FetchLatest)" +
            @"<(?<t>[A-Za-z_][A-Za-z0-9_.]*)>",
            RegexOptions.Compiled);

        var names = new HashSet<string>(StringComparer.Ordinal);
        var files = 0;
        foreach (var (_, text) in EntitlementContractTests.SourceFiles())
        {
            files++;
            foreach (Match m in generic.Matches(text))
            {
                var raw = m.Groups["t"].Value;
                names.Add(raw.Contains('.', StringComparison.Ordinal)
                    ? raw[(raw.LastIndexOf('.') + 1)..]
                    : raw);
            }
        }

        Assert.True(files > 100, $"أَداةٌ عَمياء: فُحِصَ {files} مِلَفّاً فَقَط.");
        Assert.True(names.Count > 20, $"أَداةٌ عَمياء: استُخرِجَ {names.Count} اسماً فَقَط.");

        // «اسمٌ يَحُلُّ إلى صِنفٍ لَه مُعَرِّف» = وَثيقَةُ Marten.
        // ووَسيطُ نَوعٍ عامٍّ (‏`TDoc`) لا يَحُلّ، فَيَسقُط مِن العَدّ.
        var documentTypes = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var asm in DocumentAssemblies())
            foreach (var t in asm.GetExportedTypes())
            {
                if (!t.IsClass || t.IsAbstract || t.IsGenericTypeDefinition) continue;
                if (t.GetProperty("Id") is null) continue;
                documentTypes.TryAdd(t.Name, t);
            }

        var found = names.Where(documentTypes.ContainsKey).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        output.WriteLine(
            $"فُحِصَ {files} مِلَفَّ مَصدَر، واستُخرِجَ {names.Count} اسماً، مِنها " +
            $"{found.Length} نَوعَ وَثيقَة. والسِجِلُّ يُصَنِّف {TenantExportLedger.All.Count}.");

        Assert.True(found.Length >= 25,
            $"أَداةٌ عَمياء: {found.Length} نَوعَ وَثيقَةٍ فَقَط — والمَقيسُ ‏30 وأَكثَر.");

        var unclassified = found.Where(n => TenantExportLedger.Find(n) is null).ToArray();
        Assert.True(unclassified.Length == 0,
            "نَوعُ وَثيقَةٍ غَيرُ مُصَنَّفٍ في سِجِلِّ التَخارُج — لا يَخرُجُ ولا يُستَثنى، " +
            "أَي أَنَّه يَنقُصُ مِن حَقيبَةِ العَميلِ بِصَمت:\n  " +
            string.Join("\n  ", unclassified) +
            "\nصَنِّفه في `TenantExportLedger.All` بِسَبَبِه في نَفسِ الكوميت.");
    }

    /// <summary>
    /// <para><b>وكُلُّ إدخالَةٍ في السِجِلِّ تُعلِنُ سَبَبَها</b> —
    /// فَالسِجِلُّ دَينٌ مَوصوفٌ لا قائِمَةُ إسكات.</para>
    /// </summary>
    [Fact]
    public void Every_ledger_entry_states_why()
    {
        Assert.True(TenantExportLedger.All.Count >= 30,
            $"أَداةٌ عَمياء: السِجِلُّ فيه {TenantExportLedger.All.Count} إدخالَةً فَقَط.");

        foreach (var e in TenantExportLedger.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(e.WhyAr), $"«{e.TypeName}» بِلا سَبَب.");
            Assert.True(e.WhyAr.Length > 25, $"سَبَبُ «{e.TypeName}» أَقصَرُ مِن أَن يَكونَ سَبَباً.");
        }

        Assert.Equal(
            TenantExportLedger.All.Select(e => e.TypeName).Distinct(StringComparer.Ordinal).Count(),
            TenantExportLedger.All.Count);

        // ومَسارُ كُلِّ صِنفٍ يَخرُج فَريدٌ — وإلّا داسَ مُدخَلٌ مُدخَلاً.
        var paths = TenantExportLedger.Exported.Select(e => e.Entry).ToArray();
        Assert.Equal(paths.Distinct(StringComparer.OrdinalIgnoreCase).Count(), paths.Length);
    }

    /// <summary>
    /// <para><b>الحَدُّ عَمودٌ لا حَقلٌ في JSON</b>: كُلُّ نَوعٍ
    /// مُسَجَّلٍ <c>SingleTenanted()</c> جَدوَلُه <b>بِلا عَمود
    /// <c>tenant_id</c></b>، فَيَرُدُّ صُفوفَ كُلِّ المُستَأجِرينَ
    /// لِأَيِّ جَلسَة. ولا واحِدَ مِنها في مَجموعَةِ ما يَخرُج —
    /// <b>إلّا وَثيقَةَ المُستَأجِرِ نَفسِه</b>، وهي تُحَمَّلُ
    /// بِمُعَرِّفِها الَّذي هُوَ السلاج.</para>
    /// </summary>
    [Fact]
    public void No_globally_registered_document_is_exported_as_a_table()
    {
        var single = SingleTenantedTypeNames();
        output.WriteLine($"أَنواعٌ مُسَجَّلَةٌ SingleTenanted: {single.Count} — " +
                         string.Join("، ", single.OrderBy(s => s, StringComparer.Ordinal)));

        Assert.True(single.Count >= 7,
            $"أَداةٌ عَمياء: وُجِدَ {single.Count} تَسجيلِ SingleTenanted — والمَقيسُ ‏8.");

        var leaking = TenantExportLedger.All
            .Where(e => e.Disposition == ExportDisposition.Export && single.Contains(e.TypeName))
            .Select(e => e.TypeName)
            .ToArray();

        Assert.True(leaking.Length == 0,
            "نَوعٌ عامٌّ (‏SingleTenanted) يَخرُجُ كَجَدوَل — جَدوَلُه بِلا `tenant_id`، " +
            "فَتَصديرُه يُسَلِّمُ عَميلاً واحِداً سِجِلَّ المَنَصَّةِ كُلِّها:\n  " +
            string.Join("\n  ", leaking));

        // وكُلُّ نَوعٍ عامٍّ إمّا مُستَثنىً وإمّا وَثيقَةُ المُستَأجِرِ نَفسِه.
        var misfiled = single
            .Where(n => TenantExportLedger.Find(n) is { } e
                        && e.Disposition is not (ExportDisposition.ExcludeGlobal
                                              or ExportDisposition.ExcludeSecret
                                              or ExportDisposition.ExportSelf))
            .ToArray();
        Assert.True(misfiled.Length == 0,
            "نَوعٌ عامٌّ مُصَنَّفٌ تَصنيفاً لا يُناسِبُه:\n  " + string.Join("\n  ", misfiled));
    }

    /// <summary>
    /// <para><b>حَقيبَةٌ يَنقُصُها صِنفٌ مِن أَصنافِ المُستَأجِرِ
    /// تُرفَض.</b> ‏والرَفضُ في الكاتِبِ نَفسِه لا في هذا المِلَفّ:
    /// عَطَبٌ في الجَمعِ (استِعلامٌ رَمى فابتُلِعَ) كانَ سَيُسَلِّمُ
    /// حَقيبَةً ناقِصَةً تَبدو سَليمَة.</para>
    /// </summary>
    [Fact]
    public void A_package_missing_one_tenant_data_class_is_refused()
    {
        var full = SampleContent();
        TenantExportPackageWriter.Write(new MemoryStream(), full);   // كامِلَةً: تَمُرّ

        var dropped = TenantExportLedger.Exported
            .First(e => e.Disposition == ExportDisposition.Export && e.TypeName == "Listing");

        var short_ = full with
        {
            Tables = full.Tables.Where(t => t.TypeName != dropped.TypeName).ToArray()
        };

        var ex = Assert.Throws<TenantExportViolationException>(
            () => TenantExportPackageWriter.Write(new MemoryStream(), short_));
        Assert.Contains(dropped.TypeName, ex.Message, StringComparison.Ordinal);
        output.WriteLine($"الحَقيبَةُ الكامِلَةُ تَحمِل {full.Tables.Count} جَدوَلاً؛ ونَقصُ واحِدٍ يَرمي: {ex.Message}");
    }

    // ═════════════════════════════════════════════════════════════════
    //  ٢) التَسريب — أَيَخرُجُ صَفٌّ لِمُستَأجِرٍ آخَر؟
    // ═════════════════════════════════════════════════════════════════

    /// <summary><b>صَفٌّ يَحمِلُ سلاجَ مُستَأجِرٍ آخَرَ يُرفَض</b> —
    /// والرَفضُ رَميٌ لا تَخَطٍّ صامِت: حَقيبَةٌ نَقَصَ مِنها صَفٌّ
    /// بِصَمتٍ تَكذِبُ مَرَّتَين.</summary>
    [Fact]
    public void A_row_that_belongs_to_another_tenant_is_refused()
    {
        var content = SampleContent();
        var poisoned = content with
        {
            Tables = content.Tables.Select(t => t.TypeName == "Listing"
                ? t with { Rows = new[] { Row(("id", "1"), ("tenantSlug", "other-store"), ("title", "س")) } }
                : t).ToArray()
        };

        var ex = Assert.Throws<TenantExportViolationException>(
            () => TenantExportPackageWriter.Write(new MemoryStream(), poisoned));
        Assert.Contains("other-store", ex.Message, StringComparison.Ordinal);
    }

    /// <summary><b>ووَثيقَةُ المُستَأجِرِ نَفسِها مُعَرِّفُها هُوَ
    /// السلاج</b> — فَصَفٌّ بِمُعَرِّفٍ آخَرَ صَفُّ مَتجَرٍ آخَر،
    /// وجَدوَلُها بِلا <c>tenant_id</c> فَلا شَبَكَةَ أَمانٍ
    /// تَحتَه.</summary>
    [Fact]
    public void The_tenant_row_whose_id_is_not_the_slug_is_refused()
    {
        var content = SampleContent();
        var poisoned = content with
        {
            Tables = content.Tables.Select(t => t.TypeName == "Tenant"
                ? t with { Rows = new[] { Row(("id", "another-store"), ("name", "س")) } }
                : t).ToArray()
        };

        Assert.Throws<TenantExportViolationException>(
            () => TenantExportPackageWriter.Write(new MemoryStream(), poisoned));
    }

    /// <summary><b>ومِلَفٌّ خارِجَ بادِئَةِ المُستَأجِرِ يُرفَض</b> —
    /// الدَلوُ واحِدٌ لِكُلِّ المُستَأجِرين، والعَزلُ فيه
    /// <b>بِالبادِئَةِ وَحدَها</b>.</summary>
    [Fact]
    public void A_stored_object_outside_the_tenant_prefix_is_refused()
    {
        var content = SampleContent();
        var poisoned = content with
        {
            Files = new[] { new ExportFile("tenants/other-store/listings/1/0.jpg", new byte[] { 1 }) }
        };

        var ex = Assert.Throws<TenantExportViolationException>(
            () => TenantExportPackageWriter.Write(new MemoryStream(), poisoned));
        Assert.Contains("tenants/", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <para><b>والسلاجُ يُحَلُّ إلى وَثيقَةِ <c>Tenant</c> قَبلَ أَيِّ
    /// قِراءَة.</b> ‏<c>_platform</c> و<c>_studio</c> و<c>_admin</c>
    /// أَقسامٌ حَقيقيَّةٌ في القاعِدَةِ ولا وَثيقَةَ مُستَأجِرٍ لَها —
    /// فَمُصَدِّرٌ يَقبَلُ السلاجَ نَصّاً يُسَلِّمُ سِجِلَّ تَدقيقِ
    /// المَنَصَّةِ أَو مُحادَثاتِ كُلِّ رُوَّادِ الأَعمالِ بِنَقرَة،
    /// بِلا خَطَإٍ ولا سَطرِ لوغ.</para>
    /// </summary>
    [Theory]
    [InlineData(null,           TenantExportRefusal.SlugMissing)]
    [InlineData("",             TenantExportRefusal.SlugMissing)]
    [InlineData("   ",          TenantExportRefusal.SlugMissing)]
    [InlineData("_platform",    TenantExportRefusal.SlugReserved)]
    [InlineData("_studio",      TenantExportRefusal.SlugReserved)]
    [InlineData("_incubator",   TenantExportRefusal.SlugReserved)]
    [InlineData("_admin",       TenantExportRefusal.SlugReserved)]
    [InlineData("-",            TenantExportRefusal.SlugReserved)]
    [InlineData("admin",        TenantExportRefusal.SlugReserved)]
    [InlineData("api",          TenantExportRefusal.SlugReserved)]
    public void A_slug_that_is_not_a_real_store_is_refused(string? slug, TenantExportRefusal expected)
        => Assert.Equal(expected, TenantExportAuthorization.Decide(slug, tenant: null, actorUserId: Guid.NewGuid()));

    /// <summary><b>وسلاجٌ لا وَثيقَةَ لَه يُرفَض</b> — وذلك يَحجُب
    /// بَقايا الاختِبارِ الحَيَّةَ في القاعِدَة (‏قيسَ:
    /// <c>hissa-demo</c> بِثَمانِيَةِ صُفوفٍ وبِلا وَثيقَةِ
    /// مُستَأجِر).</summary>
    [Fact]
    public void A_slug_with_no_tenant_document_is_refused()
        => Assert.Equal(TenantExportRefusal.TenantNotFound,
            TenantExportAuthorization.Decide("hissa-demo", tenant: null, actorUserId: Guid.NewGuid()));

    /// <summary><b>ولا يُصَدِّرُ إلّا المالِك</b> — لا موظَّفُ مَتجَرٍ
    /// بِصَلاحِيَّةِ إدارَة: خُروجُ قاعِدَةِ العُملاءِ كُلِّها لَيسَ
    /// عَمَلاً إدارِيّاً يَومِيّاً.</summary>
    [Fact]
    public void A_tenant_the_actor_does_not_own_is_refused()
    {
        var owner = Guid.NewGuid();
        var tenant = new ACommerce.Kit.Tenants.Tenant { Id = "ejar", Name = "إيجار", OwnerUserId = owner };

        Assert.Equal(TenantExportRefusal.None,     TenantExportAuthorization.Decide("ejar", tenant, owner));
        Assert.Equal(TenantExportRefusal.NotOwner, TenantExportAuthorization.Decide("ejar", tenant, Guid.NewGuid()));
        Assert.Equal(TenantExportRefusal.NotOwner, TenantExportAuthorization.Decide("ejar", tenant, null));

        // وسلاجُ الطَلَبِ يَجِبُ أَن يُطابِقَ الوَثيقَةَ المُحَمَّلَة.
        Assert.Equal(TenantExportRefusal.TenantNotFound,
            TenantExportAuthorization.Decide("order", tenant, owner));
    }

    /// <summary>
    /// <para><b>والمُصَدِّرُ لا يَفتَحُ جَلسَةً بِنِطاقٍ يَأتي مِن
    /// المَسار.</b> نِطاقاتُه ثَلاثَة لا رابِعَ لَها: السلاجُ
    /// المُحَلَّلُ بَعدَ التَخويل، وثابِتا <c>_studio</c>
    /// و<c>_incubator</c> و<c>_admin</c> المَكتوبَةُ في الكود —
    /// وهذِه الأَخيرَةُ تُقرَأُ <b>بِمُرَشِّحِ مالِكٍ</b> لا
    /// جُملَةً.</para>
    /// </summary>
    [Fact]
    public void The_export_service_opens_no_session_from_a_route_supplied_scope()
    {
        var src = Read($"{TemplateRoot}/Services/Export/TenantExportService.cs");

        var sessions = Regex.Matches(src, @"(?:QuerySession|LightweightSession)\((?<arg>[^)]*)\)")
            .Select(m => m.Groups["arg"].Value.Trim())
            .ToArray();

        output.WriteLine($"جَلساتُ المُصَدِّر: {sessions.Length} — {string.Join("، ", sessions)}");
        Assert.True(sessions.Length > 0, "أَداةٌ عَمياء: صِفرُ جَلسَةٍ في المُصَدِّر.");

        var allowed = new[]
        {
            "slug",                                 // السلاجُ بَعدَ التَخويل
            "StudioAuth.Tenant",                    // ثابِتٌ في الكود
            "FeasibilityAnalysisService.IncubatorTenant",
        };

        var rogue = sessions.Where(a => !allowed.Contains(a, StringComparer.Ordinal)).ToArray();
        Assert.True(rogue.Length == 0,
            "جَلسَةٌ بِنِطاقٍ خارِجَ الثَلاثَةِ المُعلَنَة:\n  " + string.Join("\n  ", rogue));
    }

    /// <summary>
    /// <para><b>ولا تَرشيحَ بِحَقلٍ في الـJSON.</b> ‏
    /// <c>data-&gt;&gt;'TenantSlug'</c> بَدَلَ <c>tenant_id</c> يَعبُرُ
    /// الحَدَّ البِنيَوِيَّ كُلَّه — وهُوَ أَخطَرُ سَطرٍ يُمكِنُ أَن
    /// يُكتَبَ في مُصَدِّر.</para>
    /// </summary>
    [Fact]
    public void The_export_never_filters_by_a_json_tenant_slug_field()
    {
        var dir = Path.Combine(RepoRoot,
            TemplateRoot.Replace('/', Path.DirectorySeparatorChar), "Services", "Export");
        var files = Directory.GetFiles(dir, "*.cs");
        Assert.True(files.Length >= 4, $"أَداةٌ عَمياء: {files.Length} مِلَفّاً في مُجَلَّدِ التَخارُج.");

        var breaches = new List<string>();
        foreach (var f in files)
        {
            var text = File.ReadAllText(f);
            if (text.Contains("data->>", StringComparison.OrdinalIgnoreCase))
                breaches.Add($"{Path.GetFileName(f)}: تَرشيحٌ بِـ data->>");
            if (Regex.IsMatch(text, @"\.Query<[A-Za-z0-9_.]+>\(\s*""", RegexOptions.None))
                breaches.Add($"{Path.GetFileName(f)}: استِعلامُ SQL خام");
        }

        output.WriteLine($"فُحِصَ {files.Length} مِلَفّاً في `Services/Export`.");
        Assert.True(breaches.Count == 0, string.Join("\n", breaches));
    }

    // ═════════════════════════════════════════════════════════════════
    //  ٣) الاستِثناء — أَيَخرُجُ بَندٌ مِن قائِمَةِ الاستِثناء؟
    // ═════════════════════════════════════════════════════════════════

    /// <summary><b>جَدوَلٌ لِنَوعٍ مُستَثنىً يُرفَض</b> — القائِمَةُ
    /// بَيضاءُ لا سَوداء، فَنَوعٌ لَم يُصَنَّف <b>لا يَخرُج</b>.</summary>
    [Fact]
    public void A_table_of_an_excluded_type_is_refused()
    {
        var excluded = TenantExportLedger.All
            .First(e => e.Disposition == ExportDisposition.ExcludeSecret);

        var content = SampleContent();
        var poisoned = content with
        {
            Tables = content.Tables
                .Append(new ExportTable(excluded.TypeName, "api_keys",
                    new[] { Row(("id", "k1"), ("tenantSlug", "ejar")) }))
                .ToArray()
        };

        var ex = Assert.Throws<TenantExportViolationException>(
            () => TenantExportPackageWriter.Write(new MemoryStream(), poisoned));
        Assert.Contains(excluded.TypeName, ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <para><b>وحَقلُ اعتِمادٍ يُرفَضُ أَينَما وَقَع</b> — ولَو في
    /// عُمقِ الوَثيقَة. ‏<c>SecretHash</c> تَجزئَةُ اعتِماد،
    /// و<c>P256dh</c> مِفتاحُ دَفعٍ حَيّ، و<c>Cipher</c>/<c>Nonce</c>
    /// أَعمِدَةُ ظَرفٍ <b>فارِغَةٌ اليَومَ</b> — والفَحصُ يُكتَبُ الآنَ
    /// لِأَنّ يَومَ تُشحَنُ الخِزانَةُ يَبدَأُ مُصَدِّرٌ قائِمٌ
    /// بِإخراجِ نَصٍّ مُعَمّىً تَحتَ مِفتاحِ المَنَصَّةِ <b>بِلا سَطرٍ
    /// يَتَغَيَّر</b>.</para>
    /// </summary>
    [Theory]
    [InlineData("secretHash")]
    [InlineData("p256dh")]
    [InlineData("cipher")]
    [InlineData("nonce")]
    [InlineData("kekVersion")]
    [InlineData("pushSubscriptions")]
    public void A_row_carrying_a_credential_field_is_refused(string field)
    {
        var content = SampleContent();
        var poisoned = content with
        {
            Tables = content.Tables.Select(t => t.TypeName == "User"
                ? t with
                {
                    Rows = new[]
                    {
                        Row(("id", "u1"), ("tenantSlug", "ejar")) is var r && r is not null
                            ? Nest(r, field)
                            : r!
                    }
                }
                : t).ToArray()
        };

        var ex = Assert.Throws<TenantExportViolationException>(
            () => TenantExportPackageWriter.Write(new MemoryStream(), poisoned));
        Assert.Contains(field, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary><b>والتَعتيمُ يُقاسُ عَلى الوَثائِقِ الحَقيقيَّةِ لا
    /// عَلى دَعوى</b>: هُوِيَّةُ المُستَخدِمِ تَخرُجُ كامِلَةً،
    /// واعتِمادُ الدَفعِ لا يَخرُج.</summary>
    [Fact]
    public void The_exported_user_carries_identity_without_push_credentials()
    {
        var user = new ACommerce.Kit.Auth.User
        {
            Id = Guid.NewGuid(),
            TenantSlug = "ejar",
            Phone = "0500000000",
            Email = "a@b.co",
            NationalId = "1234567890",
            FullName = "اسمٌ كامِل",
            PushSubscriptions =
            {
                new ACommerce.Kit.Auth.PushSubscription
                { Endpoint = "https://push/x", P256dh = "KEY", Auth = "AUTH" }
            }
        };

        var row = TenantExportRedaction.Apply("User", TenantExportRedaction.ToJson(user));
        Assert.NotNull(row);
        // بِنَفسِ خِيارات التَصييرِ الَّتي يَكتُبُ بِها الكاتِب — وإلّا
        // قيسَ نَصٌّ غَيرُ الَّذي يَصِلُ العَميل.
        var text = row!.ToJsonString(TenantExportRedaction.Json);

        Assert.Contains("0500000000", text, StringComparison.Ordinal);   // قائِمَةُ عُملاءٍ بِلا وَسيلَةِ اتِّصالٍ لَيسَت قائِمَة
        Assert.Contains("اسمٌ كامِل", text, StringComparison.Ordinal);
        Assert.DoesNotContain("P256dh", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://push/x", text, StringComparison.Ordinal);
        Assert.DoesNotContain("pushSubscriptions", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary><b>وقَيدُ التَدقيقِ يَخرُجُ بِفِعلِه لا بِعُنوانِ
    /// صاحِبِه</b>: ‏<c>Ip</c> و<c>UserAgent</c> بَياناتٌ شَخصِيَّةٌ
    /// ومِنها عَناوينُ مُشرِفي المَنَصَّةِ حينَ يَتَصَرَّفونَ عَلى
    /// مَتجَر.</summary>
    [Fact]
    public void The_exported_audit_entry_drops_the_ip_and_the_user_agent()
    {
        var entry = new AuditEntry
        {
            Id = Guid.NewGuid(), Scope = "ejar", ActorName = "المالِك",
            Action = "listing.hide", EntityType = "listing", EntityId = "1",
            Ip = "10.0.0.7", UserAgent = "Mozilla/5.0"
        };

        var row = TenantExportRedaction.Apply("AuditEntry", TenantExportRedaction.ToJson(entry));
        Assert.NotNull(row);
        var text = row!.ToJsonString(TenantExportRedaction.Json);

        Assert.Contains("listing.hide", text, StringComparison.Ordinal);
        Assert.DoesNotContain("10.0.0.7", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Mozilla", text, StringComparison.Ordinal);
    }

    /// <summary><b>وأَثَرُ فَوتَرَةِ المَنَصَّةِ لَيسَ أَثَرَ
    /// العَميل</b>: قَيدٌ فاعِلُه <c>paypal ·</c> أَو <c>paddle ·</c>
    /// يَحمِلُ مُعَرِّفَ رِسالَةِ مُزَوِّدِنا، ويُكتَبُ تَحتَ سلاجِ
    /// المُستَأجِر — فَيُحجَبُ صَفّاً كامِلاً.</summary>
    [Theory]
    [InlineData("paypal · BILLING.SUBSCRIPTION.ACTIVATED", true)]
    [InlineData("paddle · transaction.completed", true)]
    [InlineData("المالِك", false)]
    public void A_platform_billing_audit_row_is_withheld(string actor, bool withheld)
    {
        var entry = new AuditEntry
        {
            Id = Guid.NewGuid(), Scope = "ejar", ActorName = actor,
            Action = "plan.extend", EntityType = "tenant_plan", EntityId = "ejar"
        };

        var row = TenantExportRedaction.Apply("AuditEntry", TenantExportRedaction.ToJson(entry));
        Assert.Equal(withheld, row is null);
    }

    /// <summary><b>ورَبطُ المُزَوِّدِ يَخرُجُ مُعَتَّماً بِالبِناء</b> —
    /// عَبرَ <c>ProviderSecrecy</c> لا عَبرَ قائِمَةٍ مَنسوخَةٍ
    /// تَنجَرِف.</summary>
    [Fact]
    public void An_exported_provider_binding_carries_no_envelope_columns()
    {
        var binding = new TenantProviderBinding
        {
            Id = "payments.hosted_link", Slug = "payments.hosted_link",
            TenantSlug = "ejar", ProviderSlug = "moyasar_hosted",
            Values =
            {
                ["invoice_url"] = StoredValue.Explicit(CredentialKinds.HostedLink, "https://pay/x"),
                ["future_secret"] = new StoredValue
                {
                    Kind = CredentialKinds.SecretKey,
                    Cipher = "CIPHERTEXT", Nonce = "NONCE", Tag = "TAG", KekVersion = 3
                }
            }
        };

        var row = TenantExportRedaction.Apply(
            "TenantProviderBinding", TenantExportRedaction.ToJson(binding));
        Assert.NotNull(row);
        var text = row!.ToJsonString(TenantExportRedaction.Json);

        Assert.Contains("https://pay/x", text, StringComparison.Ordinal);   // نَوعٌ يُعرَض — لِلعَميلِ ويَخرُجُ مَعَه
        Assert.DoesNotContain("CIPHERTEXT", text, StringComparison.Ordinal);
        Assert.DoesNotContain("NONCE", text, StringComparison.Ordinal);
        Assert.DoesNotContain("kekVersion", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary><b>وكُلُّ حَقلٍ في قائِمَةِ الحَذفِ يُسَمّي خاصِّيَّةً
    /// قائِمَةً فِعلاً</b> — فَقائِمَةٌ تَحذِفُ اسماً لا وُجودَ لَه
    /// تُطَمئِنُ ولا تَحمي، وهي بِعَينِها الأَداةُ العَمياء.</summary>
    [Fact]
    public void Every_redacted_field_names_a_real_property_on_its_type()
    {
        Assert.True(TenantExportRedaction.Fields.Count >= 4,
            $"أَداةٌ عَمياء: {TenantExportRedaction.Fields.Count} حَقلاً مَحذوفاً فَقَط.");

        var missing = new List<string>();
        foreach (var f in TenantExportRedaction.Fields)
        {
            var entry = TenantExportLedger.Find(f.TypeName);
            Assert.NotNull(entry);
            var prop = entry!.ClrType.GetProperty(f.Property,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop is null) missing.Add($"{f.TypeName}.{f.Property}");
            Assert.False(string.IsNullOrWhiteSpace(f.WhyAr), $"{f.TypeName}.{f.Property} بِلا سَبَب.");
        }

        output.WriteLine($"فُحِصَ {TenantExportRedaction.Fields.Count} حَقلاً مَحذوفاً.");
        Assert.True(missing.Count == 0,
            "حَقلٌ مَحذوفٌ لا وُجودَ لَه على نَوعِه — القائِمَةُ انجَرَفَت:\n  " +
            string.Join("\n  ", missing));
    }

    // ═════════════════════════════════════════════════════════════════
    //  ٤) العَينُ والمَدخَل — القاعِدَتانِ ١٧ و١٢
    // ═════════════════════════════════════════════════════════════════

    /// <summary><b>الحَقيبَةُ عَينٌ يَملِكُها المُستَخدِمُ ويَخرُجُ
    /// بِها</b>: أَرشيفٌ فيه فَهرَسٌ بَشَرِيٌّ وفَهرَسٌ آلِيٌّ وجَداوِلُ
    /// تُفتَحُ بِالنَقر — لا شَرحٌ ولا وَعد.</summary>
    [Fact]
    public void The_package_carries_a_human_index_a_machine_index_and_openable_tables()
    {
        using var ms = new MemoryStream();
        TenantExportPackageWriter.Write(ms, SampleContent());
        ms.Position = 0;

        using var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);
        var names = zip.Entries.Select(e => e.FullName).ToArray();
        output.WriteLine($"مَداخِلُ الأَرشيف: {names.Length}\n  " + string.Join("\n  ", names.Take(12)));

        Assert.Contains("README.md", names);
        Assert.Contains("manifest.json", names);
        Assert.Contains("index.xlsx", names);
        Assert.Contains(names, n => n.StartsWith("data/", StringComparison.Ordinal));
        Assert.Contains(names, n => n.StartsWith("tables/", StringComparison.Ordinal));
        Assert.Contains(names, n => n.StartsWith("owner/", StringComparison.Ordinal));

        // أَسماءُ المَداخِلِ ASCII: الاسمُ العَرَبيُّ في zip يُشَوَّهُ
        // عِندَ أَدَواتٍ لا تَقرَأُ رايَةَ UTF-8.
        Assert.All(names, n => Assert.True(n.All(c => c < 128), $"اسمُ مَدخَلٍ غَيرُ ASCII: {n}"));

        // ومَسؤولِيَّةُ المُستَلِمِ مَنصوصَةٌ في الحَقيبَةِ نَفسِها لا في وَثيقَةٍ بَعيدَة.
        var readme = new StreamReader(zip.GetEntry("README.md")!.Open(), Encoding.UTF8).ReadToEnd();
        Assert.Contains("جِهَةَ تَحَكُّم", readme, StringComparison.Ordinal);
        Assert.Contains("ejar", readme, StringComparison.Ordinal);
    }

    /// <summary><b>و‏CSV يُفتَحُ بِالنَقرِ في Excel</b>: ‏UTF-8
    /// بِـBOM. وبِلا BOM يَقرَأُ Excel العَرَبِيَّةَ رُموزاً —
    /// فَتُسَلَّمُ «عَينٌ» لا تُقرَأ.</summary>
    [Fact]
    public void Every_csv_in_the_package_starts_with_a_utf8_bom()
    {
        using var ms = new MemoryStream();
        TenantExportPackageWriter.Write(ms, SampleContent());
        ms.Position = 0;

        using var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);
        var csvs = zip.Entries.Where(e => e.FullName.EndsWith(".csv", StringComparison.Ordinal)).ToArray();
        Assert.True(csvs.Length > 0, "أَداةٌ عَمياء: صِفرُ مِلَفِّ CSV في الحَقيبَة.");
        output.WriteLine($"فُحِصَ {csvs.Length} مِلَفَّ CSV.");

        foreach (var e in csvs)
        {
            using var s = e.Open();
            var head = new byte[3];
            Assert.Equal(3, s.ReadAtLeast(head, 3, throwOnEndOfStream: false));
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, head);
        }
    }

    /// <summary>
    /// <para><b>والميزَةُ تُبلَغُ بِالنَقرِ مِن إقلاعٍ بارِد</b>
    /// (القاعِدَة ١٢): صَفحَةٌ بِمَسارِها، وصَفٌّ في لَوحَةِ التَطبيقِ
    /// يَفتَحُها، ونُقطَةٌ تُنتِجُ المِلَفّ. <b>وطَرَفٌ واحِدٌ أَخضَرُ
    /// وَحدَه هُوَ كودٌ مَيِّت</b>.</para>
    /// </summary>
    [Fact]
    public void The_export_screen_is_reachable_by_a_click_from_the_app_board()
    {
        var page = Read($"{TemplateRoot}/Components/Pages/StudioAppExport.razor");
        Assert.Contains("@page \"/studio/apps/{slug}/export\"", page, StringComparison.Ordinal);

        var board = Read($"{TemplateRoot}/Components/Pages/StudioApp.razor");
        Assert.Contains("/studio/apps/{Slug}/export", board, StringComparison.Ordinal);

        // النُقطَةُ `POST` لا `GET`: التَصديرُ يَكتُبُ قَيدَ تَدقيق.
        var endpoints = Read($"{TemplateRoot}/MarketplaceTemplateExtensions.cs");
        Assert.Contains("MapPost(\"/studio/apps/{slug}/export.zip\"", endpoints, StringComparison.Ordinal);
        Assert.DoesNotContain("MapGet(\"/studio/apps/{slug}/export.zip\"", endpoints, StringComparison.Ordinal);

        // والزِرُّ في الصَفحَةِ يُرسِلُ إلَيها.
        Assert.Contains("/export.zip", page, StringComparison.Ordinal);
        Assert.Contains("method=\"post\"", page, StringComparison.Ordinal);
    }

    /// <summary><b>ونَصُّ الشاشَةِ مِن القامُوس</b> (القاعِدَة ١١) —
    /// ومِفتاحٌ ناقِصٌ يُطبَعُ خاماً على شاشَةِ المالِك.</summary>
    [Fact]
    public void Every_key_the_export_screen_reads_exists_in_the_arabic_lexicon()
    {
        var text = Read($"{TemplateRoot}/Components/Pages/StudioAppExport.razor") +
                   Read($"{TemplateRoot}/Components/Pages/StudioApp.razor");

        var keys = Regex.Matches(text, @"L(?:\.Markup)?\(?\[?""(?<k>studio\.export\.[a-z0-9_.]+)""")
            .Select(m => m.Groups["k"].Value)
            .ToHashSet(StringComparer.Ordinal);

        output.WriteLine($"مَفاتيحُ شاشَةِ التَخارُج: {keys.Count}");
        Assert.True(keys.Count >= 10, $"أَداةٌ عَمياء: {keys.Count} مِفتاحاً فَقَط.");

        var lexicon = LocaleCatalog.Lexicon.ToHashSet(StringComparer.Ordinal);
        var missing = keys.Where(k => !lexicon.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.True(missing.Length == 0, $"مَفاتيحُ خارِجَ المَعجَم: {string.Join("، ", missing)}");

        // ولا قيمَةَ نائِبَة: «‏[[ … ]]» تُعرَضُ لِلمالِكِ كَما هي.
        var placeholders = keys.Where(k => LocaleCatalog.IsPlaceholderKey("ar", k)).ToArray();
        Assert.True(placeholders.Length == 0, $"قيَمٌ نائِبَة: {string.Join("، ", placeholders)}");
    }

    // ─── أَدَواتُ الاختِبار ──────────────────────────────────────────

    private static IEnumerable<Assembly> DocumentAssemblies()
        => TenantExportLedger.All
            .Select(e => e.ClrType.Assembly)
            .Append(typeof(TenantExportLedger).Assembly)
            .Concat(AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name?.StartsWith("ACommerce", StringComparison.Ordinal) == true))
            .Distinct();

    /// <summary>الأَنواعُ المُسَجَّلَةُ <c>SingleTenanted()</c> —
    /// تُقرَأُ مِن مَصدَرِ التَسجيلِ نَفسِه لا مِن قائِمَةٍ ثانِيَة.</summary>
    private static HashSet<string> SingleTenantedTypeNames()
    {
        var sources = new[]
        {
            "libs/core/ACommerce.Platform.Hosting/HostingExtensions.cs",
            $"{TemplateRoot}/MarketplaceTemplateExtensions.cs",
        };

        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rel in sources)
        {
            var text = Read(rel);
            foreach (Match m in Regex.Matches(text,
                @"Schema\.For<(?<t>[A-Za-z0-9_.]+)>\(\)\s*(?://[^\n]*\n\s*)*\.SingleTenanted\(\)"))
            {
                var raw = m.Groups["t"].Value;
                set.Add(raw.Contains('.', StringComparison.Ordinal)
                    ? raw[(raw.LastIndexOf('.') + 1)..] : raw);
            }
        }
        return set;
    }

    private static JsonObject Row(params (string Key, string Value)[] fields)
    {
        var o = new JsonObject();
        foreach (var (k, v) in fields) o[k] = v;
        return o;
    }

    /// <summary>يَدُسُّ حَقلَ اعتِمادٍ في عُمقِ الصَفّ — لِيُقاسَ أَنّ
    /// الفَحصَ يَنزِلُ ولا يَقِفُ عِندَ السَطح.</summary>
    private static JsonObject Nest(JsonObject row, string field)
    {
        row["values"] = new JsonObject
        {
            ["some_field"] = new JsonObject { [field] = "قيمَةٌ لا يَجوزُ خُروجُها" }
        };
        return row;
    }

    /// <summary>حَقيبَةٌ كامِلَةٌ صَحيحَة — كُلُّ صِنفٍ يَخرُجُ لَه
    /// جَدوَلُه، ولَو فارِغاً.</summary>
    private static ExportContent SampleContent()
    {
        var tables = TenantExportLedger.Exported.Select(e =>
        {
            var rows = e.TypeName switch
            {
                "Tenant"   => new[] { Row(("id", "ejar"), ("name", "إيجار")) },
                "Listing"  => new[] { Row(("id", "1"), ("tenantSlug", "ejar"), ("title", "شَقَّة")) },
                "User"     => new[] { Row(("id", "u1"), ("tenantSlug", "ejar"), ("phone", "0500000000")) },
                _          => Array.Empty<JsonObject>(),
            };
            return new ExportTable(e.TypeName, e.Entry, rows);
        }).ToArray();

        return new ExportContent(
            TenantSlug: "ejar",
            TenantName: "إيجار",
            OwnerUserId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            GeneratedAtUtc: new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc),
            Tables: tables,
            Files: Array.Empty<ExportFile>(),
            MissingFileKeys: Array.Empty<string>(),
            NotesAr: Array.Empty<string>());
    }
}
