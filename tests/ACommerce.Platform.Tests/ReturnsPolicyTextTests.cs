using ACommerce.Kit.Compliance;
using ACommerce.Platform.I18n;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ نَصُّ الاستِرجاعِ المَنشورُ كانَ يَعِدُ بِما لا يوجَد ═════════════════
//
// **المَقيسُ** (موجَزُ الفَجَوات P0-5، ‏2026-10-10): `legal.returns.how` يُصَيَّرُ
// عَلى `/{slug}/legal` لِكُلِّ مَتجَر، وكانَ يَقول: «الاستِرجاع عَبر صَفحَة
// «طَلَباتي» → اِختَر الطَّلَب → «اِستِرجاع». المَنصَّة تَستَرجِع بِكَامِل المَبلَغ
// في ٣-٥ أَيّام عَمَل.» — وثَلاثُ كَذِباتٍ في جُملَتَين: لا صَفحَةَ «طَلَباتي»
// لِلمُشتَرَيات، ولا زِرَّ «اِستِرجاع»، والمَنصَّةُ لا تَمسِكُ ثَمَنَ البَيعِ
// أَصلاً (‏ق‑٦؛ `platform.terms.s2_note`: «لا تَكونُ طَرَفاً في أَيِّ صَفقَة»).
// وكانَ فاحِصُ الالتِزامِ يَرى التَناقُضَ (`refund_liability_contradicts_platform_terms`
// في `tenant_returns_art13`) **ولا اختِبارَ يَقيسُه عَلى القامُوسِ الحَيّ**.
//
// **القَرارُ المُنَفَّذُ تَحتَ التَفويض** (DECISIONS-PENDING م‑٢ الاتِّجاه (أ)،
// مُسَجَّلٌ في سِجِلِّ أَسئِلَةِ المالِك): المَنصَّةُ وَسيطٌ والبائِعُ هُوَ
// المُلتَزِم — فَالنَصُّ يَصِفُ الواقِع: المُشتَري يَطلُبُ الاستِرجاعَ مِن البائِع
// عَبرَ صَفحَةِ الصَفقَةِ أَو مُراسَلَتِه، ولا استِرجاعَ مِن المَنصَّة.
//
// **وما بَقِيَ أَحمَرَ بِقَصد**: `return_action_reachable` (المَسارُ
// `/{slug}/deals/{id}/return`) يَنتَظِرُ قَرارَ المالِكِ م‑٣ — لا يُبنى هُنا
// ولا يُخفى: يُثَبَّتُ أَحمَرَ حَتّى يُقرَّر، فَيَومَ يُبنى يَحمَرُّ هذا الفَحصُ
// ويُرفَعُ بِقَرارٍ مَرئِيّ.

public class ReturnsPolicyTextTests
{
    private const string ObligationId = "tenant_returns_art13";

    /// <summary>الالتِزامُ بِعَينِه كَما يَقرَؤُه الكاتالوج — لا نُسخَة.</summary>
    private static ObligationDefinition Obligation()
    {
        var o = ObligationCatalog.Find(ObligationId);
        Assert.NotNull(o);
        return o!;
    }

    /// <summary>لَقطَةٌ حَقيقِيَّة: القامُوسُ العَرَبيُّ المَضمونُ وجَدوَلُ
    /// المَساراتِ المَمسوحُ مِن المَصدَر — نَفسُ ما تَفعَلُه
    /// <c>ComplianceInspectorTests.RepoSubject</c>.</summary>
    private static ComplianceSubject LiveSubject(IDictionary<string, string?>? overrides = null)
    {
        var texts = Obligation().Evidence
            .Where(e => EvidenceKinds.ReadsText(e.Kind))
            .Select(e => e.Target)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(k => k, k => LocaleCatalog.Find(LocaleCatalog.Arabic, k), StringComparer.Ordinal);
        if (overrides is not null)
            foreach (var (k, v) in overrides) texts[k] = v;

        Assert.True(texts.Count >= 3, $"أَداةٌ عَمياء: {texts.Count} مِفتاحاً فَقَط.");
        Assert.True(ComplianceInspectorTests.RepoRoutes.Count > 100, "أَداةٌ عَمياء: جَدوَلُ المَساراتِ فارِغ.");

        return new ComplianceSubject(ComplianceLevels.Tenant, "theme-demo", "قِياس", texts,
            ComplianceInspectorTests.RepoRoutes);
    }

    private static ComplianceReport Inspect(ComplianceSubject subject)
        => ComplianceInspector.Inspect([Obligation()], subject);

    // ─── الأَخضَرُ الجَديد ──────────────────────────────────────────────

    /// <summary><b>هذا هُوَ الفَحصُ الَّذي كانَ أَحمَرَ قَبلَ هذا الكوميت</b>
    /// عَلى القامُوسِ الحَيّ: النَصُّ المَنشورُ لا يَعِدُ بِأَنّ المَنصَّةَ
    /// تَستَرجِع.</summary>
    [Fact]
    public void The_published_returns_text_no_longer_promises_a_platform_refund()
    {
        var report = Inspect(LiveSubject());
        Assert.DoesNotContain("refund_liability_contradicts_platform_terms", report.RejectionCodes);
    }

    /// <summary>ويُسَمّي البائِعَ مُلتَزِماً، ولا يَعِدُ بِصَفحَةٍ أَو زِرٍّ لا
    /// وُجودَ لَهُما.</summary>
    [Fact]
    public void The_text_names_the_vendor_as_the_liable_party_and_promises_no_button()
    {
        var how = LocaleCatalog.Find(LocaleCatalog.Arabic, "legal.returns.how");
        Assert.False(string.IsNullOrWhiteSpace(how));

        Assert.Contains("البائِع", how!, StringComparison.Ordinal);
        Assert.Contains("صَفقاتي", how, StringComparison.Ordinal);            // الصَفحَةُ المَوجودَةُ فِعلاً
        Assert.DoesNotContain("طَلَباتي", how, StringComparison.Ordinal);     // صَفحَةٌ لا وُجودَ لَها لِلمُشتَرَيات
        Assert.DoesNotContain("«اِستِرجاع»", how, StringComparison.Ordinal);  // زِرٌّ لا وُجودَ لَه
        Assert.DoesNotContain("تَستَرجِع", how, StringComparison.Ordinal);
        Assert.DoesNotContain("أَيّام عَمَل", how, StringComparison.Ordinal); // مُدَّةٌ تَعِدُ بِها المَنصَّةُ عَن غَيرِها

        // والوَثيقَةُ الأُخرى الحَيَّةُ الَّتي كانَ يُناقِضُها ما زالَت تَقولُ
        // إنّ المَنصَّةَ لَيسَت طَرَفاً — فَلا تَناقُضَ بَينَ وَثيقَتَين.
        var note = LocaleCatalog.Find(LocaleCatalog.Arabic, "platform.terms.s2_note");
        Assert.Contains("لا تَكونُ طَرَفاً", note!, StringComparison.Ordinal);
    }

    /// <summary><b>والعَكسُ مَقيس</b>: لَو عادَ النَصُّ إلى حَرفِه القَديم
    /// لَاحمَرَّ الفاحِصُ — وبِدونِ هذا الطَرَفِ يَبقى الفَحصُ أَعلاهُ دَعوى
    /// «لَم أَجِد» لا «فَحَصتُ فَلَم أَجِد».</summary>
    [Fact]
    public void The_exact_old_wording_would_still_be_caught_today()
    {
        var report = Inspect(LiveSubject(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["legal.returns.how"] =
                "الاستِرجاع عَبر صَفحَة «طَلَباتي» → اِختَر الطَّلَب → «اِستِرجاع».\n" +
                "                   المَنصَّة تَستَرجِع بِكَامِل المَبلَغ في ٣-٥ أَيّام عَمَل.",
        }));
        Assert.Contains("refund_liability_contradicts_platform_terms", report.RejectionCodes);
    }

    // ─── الأَحمَرُ المُعلَن ─────────────────────────────────────────────

    /// <summary>
    /// <para><b>مَسارُ الاستِرجاعِ غائِبٌ، ويُقالُ ولا يُبتلَع</b>: الشاهِدُ
    /// <c>return_action_reachable</c> أَحمَرُ عَلى المُستَودَعِ الحَيِّ لِأَنّ
    /// <c>/{slug}/deals/{id}/return</c> غَيرُ مُسَجَّل — وبِناؤُه قَرارُ مالِك
    /// (م‑٣) لا يُتَّخَذُ هُنا. يُثَبَّتُ أَحمَرَ حَتّى يُقرَّر؛ فَيَومَ يُبنى
    /// المَسارُ يَحمَرُّ هذا الفَحصُ ويُرفَعُ بِقَرارٍ مَرئِيّ لا صامِت.</para>
    /// </summary>
    [Fact]
    public void The_return_route_is_still_absent_and_that_is_declared_not_hidden()
    {
        var report = Inspect(LiveSubject());
        Assert.Contains("return_action_absent", report.RejectionCodes);
        Assert.DoesNotContain("/{slug}/deals/{id}/return", ComplianceInspectorTests.RepoRoutes);

        // وهُوَ الأَحمَرُ الوَحيدُ الباقي في هذِه المادَّة.
        Assert.Equal(new[] { "return_action_absent" }, report.RejectionCodes);
    }
}
