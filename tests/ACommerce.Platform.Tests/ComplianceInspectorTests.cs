using System.Text.RegularExpressions;
using ACommerce.Kit.Compliance;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Compliance;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>الفاحِصُ مَقيسٌ مِن أَربَعَةِ أَطراف</b> — ولِكُلِّ طَرَفٍ
/// سَبَبٌ لا يُغني عَنه غَيرُه:</para>
/// <list type="number">
///   <item><b>يَسقُطُ لَو غابَ عُنصُرُ مادَّةٍ مِن السِتِّ</b> عَن
///   مَتجَرٍ مَبنيّ — وإلّا فَهُوَ يُزَيِّن.</item>
///   <item><b>يَسقُطُ لَو أَحالَ حَذفُ الحِسابِ إلى خارِجِ
///   التَطبيق</b> — المُخالَفَةُ القائِمَةُ بِعَينِها، مَقيسَةً عَلى
///   قامُوسِ المُستودَعِ وجَدوَلِ مَساراتِه الحَقيقِيَّين.</item>
///   <item><b>يَسقُطُ لَو أُضيفَ مِلَفُّ التِزامٍ ولَم يَفحَصه</b> —
///   وذلكَ هُوَ بُرهانُ أَنَّه بَياناتٌ لا كود.</item>
///   <item><b>مِجَسٌّ بِحَقنِ العَيب</b>: ناقِصٌ يُمسَك، ونَظيرُه
///   المُكتَمِلُ يَمُرّ — فَأَداةٌ تَتَّهِمُ كُلَّ شَيءٍ عَمياء،
///   وأَداةٌ لا تَرى شَيئاً عَمياء.</item>
/// </list>
/// </summary>
public class ComplianceInspectorTests
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    // ═══ لَقطَةٌ حَقيقِيَّةٌ مِن المُستودَع ═══════════════════════════
    //
    // القامُوسُ مِن `LocaleCatalog` (نَفسُ المَورِدِ المَضمونِ الَّذي
    // يَقرَؤُه التَطبيق)، وجَدوَلُ المَساراتِ **مَقيسٌ مِن المَصدَر**:
    // كُلُّ `@page` في razor وكُلُّ `Map*("...")` في C#.
    //
    // **ولِماذا مِن المَصدَرِ لا مِن مُضيفٍ مُقلَع**: إقلاعُ مُضيفٍ
    // كامِلٍ يَحتاجُ Postgres، فَيَصيرُ فَحصُ امتِثالٍ رَهناً
    // بِقاعِدَةِ بَيانات. والمَقيسُ هُنا هُوَ نَفسُه ما يُسَجِّلُه
    // المُوَجِّه، ويُطَبَّعُ بِنَفسِ دالَّةِ `ComplianceProbe.Normalize`
    // الَّتي تُطَبِّعُ جَدوَلَ النِهاياتِ الحَيَّ — فَلا مُطَبِّعانِ
    // يَنجَرِفان.

    private static readonly Lazy<IReadOnlySet<string>> RepoRoutesLazy = new(ScanRoutes);

    private static IReadOnlySet<string> RepoRoutes => RepoRoutesLazy.Value;

    private static readonly Regex PageDirective =
        new(@"^@page\s+""(?<p>[^""]+)""", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex MapCall =
        new(@"\.Map(?:Get|Post|Put|Delete|Patch)\(\s*""(?<p>[^""]+)""", RegexOptions.Compiled);

    private static IReadOnlySet<string> ScanRoutes()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in SourceFiles(".razor", ".cs"))
        {
            var text = File.ReadAllText(path);
            var rx = path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
                ? PageDirective : MapCall;
            foreach (Match m in rx.Matches(text))
                set.Add(ComplianceProbe.Normalize(m.Groups["p"].Value));
        }

        return set;
    }

    private static IEnumerable<string> SourceFiles(params string[] extensions)
    {
        foreach (var dir in new[] { "libs", "apps" })
        foreach (var path in Directory.EnumerateFiles(
                     Path.Combine(RepoRoot, dir), "*.*", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal) ||
                path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
                continue;
            if (extensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                yield return path;
        }
    }

    private static ComplianceSubject RepoSubject(string level) =>
        new(level,
            level == ComplianceLevels.Platform ? "platform" : "theme-demo",
            "قِياس",
            ObligationCatalog.ForLevel(level)
                .SelectMany(o => o.Evidence)
                .Where(e => EvidenceKinds.ReadsText(e.Kind))
                .Select(e => e.Target)
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(k => k, k => LocaleCatalog.Find(LocaleCatalog.Arabic, k),
                    StringComparer.Ordinal),
            RepoRoutes);

    /// <summary>حارِسُ العَمى (القاعِدَة ١٠): أَداةُ مَسحٍ تُعطي صِفراً
    /// لا تُميَّزُ عَن أَداةٍ تَقرَأُ الشَجَرَةَ الخَطَأ. فَيُعَدُّ ما
    /// مُسِحَ قَبلَ أَن يُوثَقَ بِنَتيجَتِه.</summary>
    [Fact]
    public void The_route_scanner_is_measured_before_it_is_trusted()
    {
        Assert.True(RepoRoutes.Count > 100,
            $"جَدوَلُ المَساراتِ المَمسوحُ فيه {RepoRoutes.Count} مَساراً فَقَط — " +
            "الأَداةُ نَفسُها مَشكوكٌ فيها، ولا يُبنى عَلى نَتيجَتِها.");

        // مَساراتٌ قائِمَةٌ مُنذُ ما قَبلَ هذِه المَوجَة — بُرهانُ أَنَّ
        // المَسحَ يَرى الشَجَرَةَ الصَحيحَة.
        Assert.Contains("/contact", RepoRoutes);
        Assert.Contains("/{slug}/me", RepoRoutes);
        Assert.Contains("/{slug}/listings/{id}", RepoRoutes);
    }

    // ═══ ١ — يَسقُطُ لَو غابَ عُنصُرُ مادَّةٍ مِن السِتّ ════════════
    //
    // المَوادُّ السِتُّ عَلى مُستَوى المُستَأجِر: ٥، ٦، ٩، ١٠، ١٣، ١٤.
    // ولِكُلٍّ مِلَفُّ التِزامٍ واحِد. والفَحصُ يُبنى عَلى **مَتجَرٍ
    // مَبنيٍّ مُكتَمِل** — لَقطَةٌ تَستَوفي كُلَّ شاهِد — ثُمَّ يُنزَعُ
    // شاهِدٌ واحِدٌ في كُلِّ مَرَّة.

    /// <summary>لَقطَةٌ تَستَوفي كُلَّ شاهِدٍ في مُستَوىً — مَتجَرٌ
    /// مَبنيٌّ مُكتَمِلٌ نِظامِيّاً.</summary>
    private static ComplianceSubject FullySatisfied(string level) =>
        SubjectFor(level, ObligationCatalog.ForLevel(level).SelectMany(o => o.Evidence), null);

    /// <summary>
    /// <para>نَفسُ اللَقطَةِ ولكِن <b>بِشاهِدٍ واحِدٍ مَنزوع</b>.
    /// والنَزعُ <b>بِالهَدَفِ لا بِالرَمز</b>: مِفتاحٌ أَو مَسارٌ
    /// يُطلَبُ في أَكثَرَ مِن شاهِدٍ يَبقى قائِماً لَو نُزِعَ أَحَدُهُما
    /// وَحدَه — فَيَمُرُّ النَقصُ ولا يُرى، وذلكَ عَمىً في أَداةِ
    /// القِياسِ نَفسِها لا في الفاحِص.</para>
    /// </summary>
    private static ComplianceSubject SubjectFor(
        string level, IEnumerable<EvidenceRequirement> evidence, EvidenceRequirement? drop)
    {
        var texts = new Dictionary<string, string?>(StringComparer.Ordinal);
        var routes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var e in evidence)
        {
            if (EvidenceKinds.ReadsText(e.Kind)) texts[e.Target] = "نَصٌّ مَنشورٌ حَقيقيّ.";
            else routes.Add(e.Target);
        }

        if (drop is not null)
        {
            texts.Remove(drop.Target);
            routes.Remove(drop.Target);
        }

        return new ComplianceSubject(level, "built-store", "مَتجَرٌ مَبنيّ", texts, routes);
    }

    [Fact]
    public void A_fully_built_store_satisfies_every_tenant_obligation()
    {
        var report = ComplianceInspector.Inspect(FullySatisfied(ComplianceLevels.Tenant));

        Assert.False(report.IsBlind);
        Assert.Equal(6, report.ObligationsInspected);
        Assert.Equal(report.ObligationsInspected, report.ObligationsSatisfied);
        Assert.Empty(report.Failing);
    }

    /// <summary>المَوادُّ السِتُّ حاضِرَةٌ بِمِلَفّاتِها — وإلّا فَكُلُّ
    /// ما تَحتَها يَقيسُ مَجموعَةً ناقِصَة.</summary>
    [Theory]
    [InlineData("tenant_privacy_art5")]
    [InlineData("tenant_disclosure_art6")]
    [InlineData("tenant_licence_art9")]
    [InlineData("tenant_ad_disclosure_art10")]
    [InlineData("tenant_returns_art13")]
    [InlineData("tenant_delay_cancellation_art14")]
    public void Each_of_the_six_articles_has_a_tenant_obligation(string id)
    {
        var o = ObligationCatalog.Find(id);
        Assert.NotNull(o);
        Assert.Equal(ComplianceLevels.Tenant, o!.Level);
        Assert.NotEmpty(o.Evidence);
        Assert.NotEmpty(o.Source.QuotedAr);
    }

    /// <summary><b>الفَحصُ الأَوَّلُ مِن الأَربَعَة</b>: كُلُّ شاهِدٍ في
    /// المَوادِّ السِتِّ — لَو غابَ وَحدَه عَن مَتجَرٍ مُكتَمِلٍ
    /// فيما عَداه — يُمسَك بِرَمزِ رَفضِه. ‏<c>MemberData</c> يُوَلِّدُ
    /// حالَةً لِكُلِّ شاهِدٍ فِعليّ، فَشاهِدٌ يُضافُ إلى مِلَفٍّ
    /// يُقاسُ تِلقائِيّاً.</summary>
    [Theory]
    [MemberData(nameof(EveryTenantEvidence))]
    public void Dropping_any_single_element_of_the_six_articles_is_caught(
        string obligationId, string rejectionCode)
    {
        var o = ObligationCatalog.Find(obligationId)!;
        var dropped = o.Evidence.Single(e => e.RejectionCode == rejectionCode);

        var report = ComplianceInspector.Inspect([o],
            SubjectFor(ComplianceLevels.Tenant, o.Evidence, dropped));

        var result = report.For(obligationId);
        Assert.NotNull(result);
        Assert.False(result!.IsSatisfied,
            $"نُزِعَ الشاهِدُ «{rejectionCode}» ومَع ذلكَ عَدَّ الفاحِصُ «{obligationId}» مُستَوفىً.");

        // **الطَرَفانِ مَعاً**: يُمسَكُ المَنزوع، ولا يُتَّهَمُ ما بَقِيَ
        // قائِماً. أَداةٌ تَتَّهِمُ كُلَّ شَيءٍ عَمياءُ كَأَداةٍ لا تَرى.
        Assert.Equal(new[] { rejectionCode }, report.RejectionCodes);
    }

    public static TheoryData<string, string> EveryTenantEvidence()
    {
        var data = new TheoryData<string, string>();
        foreach (var o in ObligationCatalog.ForLevel(ComplianceLevels.Tenant))
        foreach (var e in o.Evidence)
            data.Add(o.Id, e.RejectionCode);
        return data;
    }

    // ═══ ٢ — حَذفُ الحِسابِ لا يُحالُ إلى خارِجِ التَطبيق ════════════
    //
    // **هذا هُوَ الفَحصُ الأَحمَرُ الحَقيقيّ**: يُقاسُ عَلى قامُوسِ
    // المُستودَعِ وجَدوَلِ مَساراتِه كَما هُما، لا عَلى لَقطَةٍ
    // مُصطَنَعَة. وكانَ أَحمَرَ قَبلَ هذِه المَوجَة، لِأَنَّ
    // `legal.privacy.deletion` كانَ يَقول: «لِحَذف حِسابك بِالكامِل:
    // تَواصَل عَبر صَفحَة الدَّعم»، و`platform.privacy.s6_body` يَقول
    // «بِمُراسَلَتِنا … خِلالَ ثَلاثينَ يَوماً» — ولا مَسارَ حَذفٍ في
    // المُستودَعِ كُلِّه.

    [Fact]
    public void Account_deletion_is_reachable_inside_the_app_and_no_text_refers_outside()
    {
        var report = ComplianceInspector.Inspect(RepoSubject(ComplianceLevels.Platform));
        var result = report.For("platform_account_deletion");

        Assert.NotNull(result);
        Assert.True(result!.IsSatisfied,
            "حَذفُ الحِسابِ لا يَستَوفي شُهودَه: " +
            string.Join(" | ", result.Missing.Select(m => $"{m.RejectionCode}: {m.DetailAr}")));
    }

    /// <summary>ولِكُلِّ شاهِدٍ مِن شُهودِ الحَذفِ فَحصٌ عَلى
    /// حِدَة — فَلا يُخفي مَجموعُهُم أَيُّهُم سَقَط.</summary>
    [Theory]
    [InlineData("store_privacy_refers_deletion_outside_app")]
    [InlineData("platform_privacy_refers_deletion_outside_app")]
    [InlineData("account_deletion_screen_absent")]
    [InlineData("account_deletion_endpoint_absent")]
    [InlineData("account_deletion_scope_unstated")]
    public void No_account_deletion_rejection_code_fires_on_the_live_repository(string code)
        => Assert.DoesNotContain(code,
            ComplianceInspector.Inspect(RepoSubject(ComplianceLevels.Platform)).RejectionCodes);

    /// <summary><b>والعَكسُ مَقيسٌ أَيضاً</b>: لَو عادَ النَصُّ إلى ما
    /// كانَ عَلَيه حَرفاً، لَاحمَرَّ الفاحِصُ. وبِدونِ هذا الطَرَفِ
    /// يَبقى الفَحصُ أَعلاهُ دَعوى «لَم أَجِد» لا «فَحَصتُ فَلَم
    /// أَجِد».</summary>
    [Fact]
    public void The_exact_old_wording_would_still_be_caught_today()
    {
        var texts = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["legal.privacy.deletion"] = "لِحَذف حِسابك بِالكامِل: تَواصَل عَبر صَفحَة الدَّعم.",
            ["platform.privacy.s6_body"] =
                "ويُمكِنُكَ طَلَبُ حَذفِ حِسابِكَ وبَياناتِكَ بِمُراسَلَتِنا، " +
                "ويُنَفَّذُ الطَلَبُ خِلالَ ثَلاثينَ يَوماً.",
            ["account.delete.retained_note"] = null,
        };

        var report = ComplianceInspector.Inspect(
            [ObligationCatalog.Find("platform_account_deletion")!],
            new ComplianceSubject(ComplianceLevels.Platform, "platform", "قَبلَ الإصلاح",
                texts, new HashSet<string>(StringComparer.Ordinal)));

        Assert.Contains("store_privacy_refers_deletion_outside_app", report.RejectionCodes);
        Assert.Contains("platform_privacy_refers_deletion_outside_app", report.RejectionCodes);
        Assert.Contains("account_deletion_screen_absent", report.RejectionCodes);
        Assert.Contains("account_deletion_endpoint_absent", report.RejectionCodes);
        Assert.Contains("account_deletion_scope_unstated", report.RejectionCodes);
    }

    // ═══ ٣ — التِزامٌ يُضافُ يُفحَص: بُرهانُ «بَياناتٌ لا كود» ══════

    /// <summary>كُلُّ التِزامٍ في الكاتالوجِ يَظهَرُ في تَقريرِ
    /// مُستَواه — لا واحِدَ يَسقُطُ صامِتاً.</summary>
    [Fact]
    public void Every_catalog_obligation_appears_in_the_report_for_its_level()
    {
        foreach (var level in ComplianceLevels.All)
        {
            var expected = ObligationCatalog.ForLevel(level).Select(o => o.Id).ToList();
            var actual = ComplianceInspector
                .Inspect(FullySatisfied(level)).Results.Select(r => r.Obligation.Id).ToList();

            Assert.NotEmpty(expected);
            Assert.Equal(expected, actual);
        }
    }

    /// <summary>
    /// <para><b>الفَحصُ الثالِثُ</b>: مِلَفُّ التِزامٍ <b>لَم يُذكَر في
    /// سَطرِ كودٍ واحِد</b> يُقرَأُ ويُفحَصُ ويُعطي حُكماً — لا في
    /// الشَكلِ فَحسب بَل بِرَمزِ رَفضِه.</para>
    ///
    /// <para>ولَو كانَ الفاحِصُ يَعرِفُ المَوادَّ بِأَسمائِها لَما
    /// أَعطى هذا المِلَفُّ شَيئاً — <b>وهذا بِعَينِه ما يُثبِتُ
    /// أَنَّه بَيانات</b>.</para>
    /// </summary>
    [Fact]
    public void An_obligation_the_inspector_has_never_heard_of_is_still_inspected()
    {
        const string json = """
        {
          "id": "future_regulation_art99",
          "level": "tenant",
          "label": { "ar": "مادَّةٌ لَم تُكتَب بَعد", "en": null },
          "source": {
            "authority": "جِهَةٌ مُستَقبَلِيَّة",
            "reference": "المادَّةُ التاسِعَةُ والتِسعون",
            "url": null,
            "quotedAr": "نَصٌّ يُنقَلُ يَومَ يَصدُر.",
            "penaltyAr": null
          },
          "evidence": [
            {
              "code": "future_notice",
              "kind": "text_filled",
              "target": "legal.future.notice",
              "rejectionCode": "future_notice_absent",
              "label": { "ar": "إشعارٌ مُستَقبَليّ", "en": null },
              "remedyRoute": null,
              "remedy": { "ar": "يُملَأُ يَومَ يُطلَب.", "en": null }
            }
          ],
          "notCheckable": []
        }
        """;

        var added = ObligationDefinitionLoader.ParseDefinition(json);
        Assert.Empty(ObligationDefinitionValidator.Validate(added));

        var catalogPlusOne = ObligationCatalog.All.Append(added).ToList();
        var report = ComplianceInspector.Inspect(
            catalogPlusOne, FullySatisfied(ComplianceLevels.Tenant));

        Assert.Equal(ObligationCatalog.ForLevel(ComplianceLevels.Tenant).Count + 1,
            report.ObligationsInspected);
        Assert.NotNull(report.For("future_regulation_art99"));
        Assert.Contains("future_notice_absent", report.RejectionCodes);
    }

    /// <summary>
    /// <para>ونِصفُ البُرهانِ الآخَر: <b>مِلَفٌّ يُضافُ إلى المُجَلَّدِ
    /// ولا يُدرَجُ في الفِهرِسِ يُفشِلُ الإقلاعَ ولا يَختَفي.</b>
    /// يُقاسُ بِمُقابَلَةِ مَوارِدِ التَجميعَةِ بِما حَمَّلَه
    /// الكاتالوج — وهي نَفسُ المُقابَلَةِ الَّتي يُجريها
    /// <c>LoadEmbedded</c>.</para>
    /// </summary>
    [Fact]
    public void No_obligation_file_can_hide_outside_the_index()
    {
        var embedded = typeof(ObligationCatalog).Assembly
            .GetManifestResourceNames()
            .Where(n => n.EndsWith(".obligation.json", StringComparison.Ordinal))
            .Select(n => n[(n.LastIndexOf(".Definitions.", StringComparison.Ordinal)
                            + ".Definitions.".Length)..^".obligation.json".Length])
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(embedded);
        Assert.Equal(embedded,
            ObligationCatalog.All.Select(o => o.Id).OrderBy(i => i, StringComparer.Ordinal));
    }

    /// <summary>ولا فَرعَ في الفاحِصِ يَتَجاوَزُ المَعجَم: نَوعٌ خارِجَه
    /// يُنتِجُ نَقصاً مُعلَناً لا استِثناءً ولا مُرورَ صامِت.</summary>
    [Fact]
    public void An_evidence_kind_the_inspector_does_not_know_is_reported_not_swallowed()
    {
        var alien = new ObligationDefinition
        {
            Id = "alien_kind",
            Level = ComplianceLevels.Platform,
            Label = new Dictionary<string, string?> { ["ar"] = "غَريب" },
            Source = new ObligationSource
            {
                Authority = "ج", Reference = "م", QuotedAr = "ن",
            },
            Evidence =
            [
                new EvidenceRequirement
                {
                    Code = "x", Kind = "telepathy", Target = "anything",
                    RejectionCode = "x_absent",
                    Label = new Dictionary<string, string?> { ["ar"] = "غَريب" },
                },
            ],
        };

        var report = ComplianceInspector.Inspect([alien],
            new ComplianceSubject(ComplianceLevels.Platform, "platform", "قِياس",
                new Dictionary<string, string?>(), new HashSet<string>()));

        Assert.False(report.For("alien_kind")!.IsSatisfied);
        Assert.Contains(ComplianceInspector.UnknownKindRejection, report.RejectionCodes);
    }

    // ═══ ٤ — مِجَسٌّ بِحَقنِ العَيب ═══════════════════════════════════

    /// <summary>
    /// <para><b>الفَحصُ الرابِع</b>: التِزامانِ <b>مُتَطابِقانِ حَرفاً
    /// إلّا في شَيءٍ واحِد</b> — أَحَدُهُما شاهِدُه قائِمٌ والآخَرُ
    /// ناقِص. الناقِصُ يُمسَك، والمُكتَمِلُ يَمُرّ.</para>
    ///
    /// <para><b>ولِماذا التَوأَمانِ في فَحصٍ واحِد</b>: أَداةٌ تُحمِرُّ
    /// دائِماً تَجتازُ «يُمسَك» وَحدَه، وأَداةٌ تُخضِرُّ دائِماً
    /// تَجتازُ «يَمُرّ» وَحدَه. والطَرَفانِ مَعاً هُما القِياس.</para>
    /// </summary>
    [Fact]
    public void The_probe_catches_the_injected_defect_and_lets_its_complete_twin_pass()
    {
        const string template = """
        {
          "id": "PROBE_ID",
          "level": "platform",
          "label": { "ar": "تَوأَمُ المِجَسّ", "en": null },
          "source": {
            "authority": "جِهَةُ قِياس",
            "reference": "مادَّةُ قِياس",
            "url": null,
            "quotedAr": "نَصٌّ مَنقولٌ لِلقِياس.",
            "penaltyAr": null
          },
          "evidence": [
            {
              "code": "published_text",
              "kind": "text_filled",
              "target": "probe.text.key",
              "rejectionCode": "PROBE_ID_text_absent",
              "label": { "ar": "نَصٌّ مَملوء", "en": null },
              "remedyRoute": null,
              "remedy": { "ar": "يُملَأ.", "en": null }
            },
            {
              "code": "reachable_screen",
              "kind": "route_reachable",
              "target": "/probe/screen",
              "rejectionCode": "PROBE_ID_route_absent",
              "label": { "ar": "شاشَةٌ تُبلَغ", "en": null },
              "remedyRoute": null,
              "remedy": { "ar": "تُبنى.", "en": null }
            }
          ],
          "notCheckable": []
        }
        """;

        var complete = ObligationDefinitionLoader.ParseDefinition(
            template.Replace("PROBE_ID", "probe_complete"));
        var defective = ObligationDefinitionLoader.ParseDefinition(
            template.Replace("PROBE_ID", "probe_defective"));

        // التَوأَمانِ صالِحانِ بِنيَوِيّاً — العَيبُ في **المَفحوصِ** لا
        // في المِلَفّ. وهذا شَرطُ المِجَسّ: لَو رَفَضَهُ المُصادِقُ
        // لَقاسَ الفَحصُ المُصادِقَ لا الفاحِص.
        Assert.Empty(ObligationDefinitionValidator.Validate(complete));
        Assert.Empty(ObligationDefinitionValidator.Validate(defective));

        // لَقطَةٌ تَستَوفي شُهودَ المُكتَمِلِ وَحدَه: النَصُّ مَملوءٌ
        // لِمِفتاحٍ واحِد، والمَسارُ مُسَجَّلٌ لِواحِد.
        var subject = new ComplianceSubject(
            ComplianceLevels.Platform, "platform", "مِجَسّ",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["probe.text.key"] = "نَصٌّ حَقيقِيٌّ مَنشور.",
            },
            new HashSet<string>(StringComparer.Ordinal) { "/probe/screen" });

        var passing = ComplianceInspector.Inspect([complete], subject);
        Assert.True(passing.For("probe_complete")!.IsSatisfied);
        Assert.Empty(passing.RejectionCodes);
        Assert.Equal(2, passing.EvidenceChecked);

        // ونَظيرُه بِلَقطَةٍ يَنقُصُها الشاهِدان.
        var blank = new ComplianceSubject(
            ComplianceLevels.Platform, "platform", "مِجَسّ",
            new Dictionary<string, string?>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal));

        var failing = ComplianceInspector.Inspect([defective], blank);
        Assert.False(failing.For("probe_defective")!.IsSatisfied);
        Assert.Equal(
            new[] { "probe_defective_text_absent", "probe_defective_route_absent" },
            failing.RejectionCodes);
    }

    /// <summary><b>والنائِبُ يُرَدُّ كَما يُرَدُّ الغِياب</b> — وهذا
    /// هُوَ الفَرقُ الَّذي يَجعَلُ الفاحِصَ ذا قيمَة: صَفحَةُ
    /// <c>/contact</c> مَبنِيَّةٌ ومَعروضَةٌ وحُقولُها الأَربَعَةُ
    /// صِفرٌ ذو قيمَة، وفاحِصٌ يَعُدُّ الوُجودَ وَحدَه يُخضِرُّ
    /// مُخالَفَةً قائِمَة.</summary>
    [Fact]
    public void A_placeholder_value_is_refused_exactly_as_absence_is()
    {
        var o = ObligationCatalog.Find("platform_disclosure_art6")!;

        var withPlaceholder = ComplianceInspector.Inspect([o], new ComplianceSubject(
            ComplianceLevels.Platform, "platform", "قِياس",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["platform.doc.entity_name"] = "[[ اسمُ الكِيانِ النِظاميّ — يَملَؤُه المالِك ]]",
            },
            new HashSet<string>(StringComparer.Ordinal) { "/contact" }));

        Assert.Contains("platform_entity_name_placeholder", withPlaceholder.RejectionCodes);

        var withRealValue = ComplianceInspector.Inspect([o], new ComplianceSubject(
            ComplianceLevels.Platform, "platform", "قِياس",
            o.Evidence.Where(e => EvidenceKinds.ReadsText(e.Kind))
                      .ToDictionary(e => e.Target, _ => (string?)"شَرِكَةٌ مُسَجَّلَة",
                          StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal) { "/contact" }));

        Assert.Empty(withRealValue.RejectionCodes);
    }

    /// <summary>عَلامَةُ النائِبِ في الفاحِصِ هي نَفسُها الَّتي
    /// يَعرِفُها قامُوسُ النُصوص. نُسِخَت لِتَبقى العُدَّةُ بِلا
    /// مَرجِع، والانجِرافُ مَحروسٌ هُنا لا مَتروكٌ لِلنِيَّة.</summary>
    [Fact]
    public void The_placeholder_marker_matches_the_locale_catalog_byte_for_byte()
    {
        Assert.Equal(LocaleCatalog.PlaceholderOpen, ComplianceInspector.PlaceholderOpen);
        Assert.Equal(LocaleCatalog.PlaceholderClose, ComplianceInspector.PlaceholderClose);
    }

    // ═══ العَدّادُ جُزءٌ مِن العَقد (القاعِدَة ١٠) ════════════════════

    [Fact]
    public void An_empty_catalog_reports_blindness_not_compliance()
    {
        var report = ComplianceInspector.Inspect([], new ComplianceSubject(
            ComplianceLevels.Platform, "platform", "قِياس",
            new Dictionary<string, string?>(), new HashSet<string>()));

        Assert.True(report.IsBlind);
        Assert.Equal(0, report.EvidenceChecked);
        // ‏«صِفرُ نَقص» مَعَ «صِفرُ فَحص» لا يُقرَآنِ امتِثالاً.
        Assert.Empty(report.Failing);
    }

    [Fact]
    public void The_live_catalog_is_never_blind_at_either_level()
    {
        foreach (var level in ComplianceLevels.All)
        {
            var report = ComplianceInspector.Inspect(RepoSubject(level));
            Assert.False(report.IsBlind);
            Assert.True(report.EvidenceChecked >= report.ObligationsInspected);
        }
    }

    /// <summary>الفاحِصُ يُصَفّي بِالمُستَوى: التِزامُ مَنَصَّةٍ لا
    /// يَظهَرُ في تَقريرِ مَتجَر، والعَكس.</summary>
    [Fact]
    public void Levels_do_not_bleed_into_one_another()
    {
        var tenantReport = ComplianceInspector.Inspect(FullySatisfied(ComplianceLevels.Tenant));
        Assert.All(tenantReport.Results,
            r => Assert.Equal(ComplianceLevels.Tenant, r.Obligation.Level));

        var platformReport = ComplianceInspector.Inspect(FullySatisfied(ComplianceLevels.Platform));
        Assert.All(platformReport.Results,
            r => Assert.Equal(ComplianceLevels.Platform, r.Obligation.Level));

        Assert.Equal(ObligationCatalog.All.Count,
            tenantReport.ObligationsInspected + platformReport.ObligationsInspected);
    }

    // ═══ الكاتالوجُ نَفسُه ═══════════════════════════════════════════

    [Fact]
    public void Every_obligation_carries_a_quoted_source_and_a_rejection_code_per_evidence()
    {
        Assert.NotEmpty(ObligationCatalog.All);

        foreach (var o in ObligationCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(o.Source.Authority), o.Id);
            Assert.False(string.IsNullOrWhiteSpace(o.Source.Reference), o.Id);
            Assert.False(string.IsNullOrWhiteSpace(o.Source.QuotedAr), o.Id);
            Assert.NotEmpty(o.Evidence);

            foreach (var e in o.Evidence)
                Assert.False(string.IsNullOrWhiteSpace(e.RejectionCode), $"{o.Id}/{e.Code}");
        }
    }

    /// <summary>رَمزُ الرَفضِ فَريدٌ عَبرَ الكاتالوجِ كُلِّه لا داخِلَ
    /// الالتِزامِ وَحدَه — وإلّا لَما دَلَّ سَطرٌ في اللوغ عَلى
    /// مَوضِعِه.</summary>
    [Fact]
    public void Rejection_codes_are_unique_across_the_whole_catalog()
    {
        var all = ObligationCatalog.All.SelectMany(o => o.Evidence)
                                       .Select(e => e.RejectionCode).ToList();
        Assert.Equal(all.Count, all.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(all.Count, ObligationCatalog.RejectionCodes.Count);
    }

    /// <summary>ولا رَمزَ رَفضٍ يُصادِمُ رَمزَ خَرقٍ في المُصادِق —
    /// فَـ«عَطَبٌ في المِلَفّ» و«نَقصٌ عِندَ المَفحوص» طَبَقَتانِ لا
    /// تُخلَطان.</summary>
    [Fact]
    public void No_rejection_code_collides_with_a_validator_violation_code()
    {
        foreach (var code in ObligationCatalog.RejectionCodes)
            Assert.False(ObligationDefinitionValidator.ContainsCode(code),
                $"رَمزُ الرَفض «{code}» يُصادِمُ رَمزَ خَرقٍ في المُصادِق.");

        Assert.False(ObligationDefinitionValidator.ContainsCode(
            ComplianceInspector.UnknownKindRejection));
    }

    // ═══ اللَوحَةُ تُبلَغُ بِالنَقر (القاعِدَة ١٢) ══════════════════

    /// <summary>
    /// <para><b>فاحِصٌ لا يَراهُ صاحِبُ المَتجَرِ لا يَسُدُّ مُخالَفَةً
    /// واحِدَة.</b> فَالمَدخَلُ مَقيسٌ لا مَوعود: صَفحَةُ التَطبيقِ في
    /// الاستوديو تَحمِلُ رابِطاً إلى اللَوحَة، واللَوحَةُ تُعلِنُ
    /// مَسارَها.</para>
    ///
    /// <para>ولا يُقاسُ بِوُجودِ المِلَفّ: مِلَفٌّ مَبنيٌّ بِلا رابِطٍ
    /// كودٌ مَيِّتٌ بِكامِلِ كُلفَتِه وبِلا أَثَر.</para>
    /// </summary>
    [Fact]
    public void The_compliance_board_is_reachable_by_click_from_the_studio()
    {
        var pages = Path.Combine(RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "Components", "Pages");

        var board = File.ReadAllText(Path.Combine(pages, "StudioAppCompliance.razor"));
        Assert.Contains("@page \"/studio/apps/{slug}/compliance\"", board, StringComparison.Ordinal);

        var studioApp = File.ReadAllText(Path.Combine(pages, "StudioApp.razor"));
        Assert.Contains("/compliance", studioApp, StringComparison.Ordinal);
        Assert.Contains("studio.compliance.nav_title", studioApp, StringComparison.Ordinal);

        Assert.Contains("/studio/apps/{slug}/compliance", RepoRoutes);
    }

    /// <summary>واللَوحَةُ تَعرِضُ الثَلاثَةَ الَّتي بِلا واحِدٍ مِنها
    /// تَصيرُ حُكماً مُجَرَّداً: المَصدَرَ المَنقول، ورَمزَ الرَفض،
    /// ورابِطَ السَدّ. وعَدّادَها كَذلك (القاعِدَة ١٠).</summary>
    [Fact]
    public void The_board_shows_the_source_the_rejection_code_and_the_fix_link()
    {
        var board = File.ReadAllText(Path.Combine(RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "Components", "Pages",
            "StudioAppCompliance.razor"));

        Assert.Contains("Source.QuotedAr", board, StringComparison.Ordinal);
        Assert.Contains("RejectionCode", board, StringComparison.Ordinal);
        Assert.Contains("RemedyRoute", board, StringComparison.Ordinal);
        Assert.Contains("studio.compliance.counter", board, StringComparison.Ordinal);
        Assert.Contains("IsBlind", board, StringComparison.Ordinal);
        Assert.Contains("NotCheckable", board, StringComparison.Ordinal);
    }

    /// <summary>كُلُّ مِفتاحِ نَصٍّ يُطلَبُ في الكاتالوجِ <b>مُعَرَّفٌ في
    /// قامُوسِ العَرَبِيَّة</b> — أَو غائِبٌ عَمداً لِأَنَّ غِيابَه هُوَ
    /// النَقصُ نَفسُه. والفَرقُ يُقاس: مِفتاحٌ خارِجَ المَعجَمِ
    /// وخارِجَ ما يُتَوَقَّعُ غِيابُه خَطَأٌ إملائِيٌّ يَتَنَكَّرُ في
    /// هَيئَةِ مُخالَفَة.</summary>
    [Fact]
    public void Every_text_target_is_either_defined_or_deliberately_absent()
    {
        // المَفاتيحُ الَّتي لَم تُنشَأ بَعد لِأَنَّ إنشاءَها قَرارُ
        // مالِك — لا مِفتاحَ خارِجَ هذِه ولا خارِجَ القامُوس.
        var deliberatelyAbsent = new HashSet<string>(StringComparer.Ordinal);

        var undefined = ObligationCatalog.All
            .SelectMany(o => o.Evidence)
            .Where(e => EvidenceKinds.ReadsText(e.Kind))
            .Select(e => e.Target)
            .Distinct(StringComparer.Ordinal)
            .Where(k => LocaleCatalog.Find(LocaleCatalog.Arabic, k) is null)
            .Where(k => !deliberatelyAbsent.Contains(k))
            .ToList();

        Assert.Empty(undefined);
    }
}
