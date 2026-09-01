using System.Text.RegularExpressions;
using ACommerce.Kit.Compliance;
using ACommerce.Platform.I18n;
using Microsoft.AspNetCore.Routing;

namespace ACommerce.Templates.Customer.Marketplace.Services.Compliance;

/// <summary>
/// <para><b>جامِعُ اللَقطَةِ — وهُوَ نِصفُ قيمَةِ الفاحِص.</b> الفاحِصُ
/// يَحكُمُ عَلى لَقطَةٍ ولا يَستَعلِم؛ وهذا المِلَفُّ يَبنيها مِن
/// <b>مَصدَرَينِ مَقيسَين</b>: قامُوسُ النُصوصِ مِن
/// <see cref="LocaleCatalog"/>، وجَدوَلُ المَساراتِ مِن
/// <see cref="EndpointDataSource"/> الحَيِّ لِلتَطبيق.</para>
///
/// <para><b>ولِماذا جَدوَلُ النِهاياتِ لا قائِمَةٌ مَكتوبَة</b>
/// (القاعِدَة ١٠): قائِمَةٌ تُكتَبُ بِاليَدِ تَقولُ ما نَظُنُّه
/// مُسَجَّلاً، وجَدوَلُ النِهاياتِ يَقولُ ما سَجَّلَه المُوَجِّهُ
/// فِعلاً. والفَرقُ بَينَهُما هُوَ الفَرقُ بَينَ أَداةٍ تَقيسُ
/// وأَداةٍ تُصَدِّق. وشاشَةٌ تُحذَفُ يَختَفي مَسارُها هُنا في
/// اللَحظَةِ نَفسِها — بِلا سَطرٍ يُعَدَّل.</para>
///
/// <para><b>ومَفاتيحُ النُصوصِ مُشتَقَّةٌ مِن الكاتالوجِ لا
/// مَكتوبَة</b>: التِزامٌ يُضافُ بِمِفتاحٍ جَديدٍ يُجمَعُ نَصُّه
/// تِلقائِيّاً — ولا سَطرَ هُنا يُلمَس. <b>وهذا هُوَ نِصفُ بُرهانِ
/// «بَياناتٌ لا كود»</b>: النِصفُ الآخَرُ أَنَّ
/// <c>ComplianceInspector</c> لا يَذكُرُ مادَّةً واحِدَةً بِاسمِها.</para>
/// </summary>
public sealed class ComplianceProbe
{
    /// <summary>مُعَرِّفُ لَقطَةِ المَنَصَّة.</summary>
    public const string PlatformSubjectId = "platform";

    private readonly IEnumerable<EndpointDataSource> _sources;

    public ComplianceProbe(IEnumerable<EndpointDataSource> sources) => _sources = sources;

    /// <summary>لَقطَةُ المَنَصَّةِ نَفسِها — تُفحَصُ مَرَّةً واحِدَةً
    /// عَلى أُصولٍ ثابِتَة.</summary>
    public ComplianceSubject PlatformSubject(string displayNameAr) =>
        Build(ComplianceLevels.Platform, PlatformSubjectId, displayNameAr);

    /// <summary>لَقطَةُ مَتجَرٍ واحِد — تُفحَصُ مَرَّةً لِكُلِّ
    /// مُستَأجِر.</summary>
    public ComplianceSubject TenantSubject(string slug, string displayNameAr) =>
        Build(ComplianceLevels.Tenant, slug, displayNameAr);

    private ComplianceSubject Build(string level, string subjectId, string displayNameAr)
    {
        // ─── النُصوص: المَفاتيحُ مُشتَقَّةٌ مِن الالتِزاماتِ نَفسِها ───
        var texts = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var e in ObligationCatalog.ForLevel(level).SelectMany(o => o.Evidence))
        {
            if (!EvidenceKinds.ReadsText(e.Kind)) continue;
            // ‏`Find` لا `Text`: السُقوطُ إلى المِفتاحِ الخام كانَ
            // سَيَجعَل مِفتاحاً غائِباً يَبدو نَصّاً مَنشوراً.
            texts[e.Target] = LocaleCatalog.Find(LocaleCatalog.Arabic, e.Target);
        }

        return new ComplianceSubject(level, subjectId, displayNameAr, texts, Routes());
    }

    /// <summary>أَنماطُ المَساراتِ المُسَجَّلَةُ فِعلاً،
    /// مُطَبَّعَة.</summary>
    public IReadOnlySet<string> Routes()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in _sources)
        foreach (var endpoint in source.Endpoints)
            if (endpoint is RouteEndpoint re && re.RoutePattern.RawText is { } raw)
                set.Add(Normalize(raw));
        return set;
    }

    private static readonly Regex Constraint =
        new(@"\{([^{}:?=]+)[^{}]*\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// <para><b>تَطبيعُ النَمَطِ قَبلَ المُقارَنَة</b>: يُسقِطُ القُيودَ
    /// والقِيَمَ الافتِراضِيَّةَ مِن الوَسائِط
    /// (<c>{id:guid}</c> ← <c>{id}</c>)، ويَضمَنُ شَرطَةً بادِئَةً
    /// ولا شَرطَةَ خاتِمَة.</para>
    ///
    /// <para><b>ولِماذا يُطَبَّعُ أَصلاً</b>: القَيدُ تَفصيلُ تَوجيهٍ
    /// يَتَغَيَّرُ بِلا أَن يَتَغَيَّرَ ما يَبلُغُه المُستَخدِم. ومِلَفُّ
    /// التِزامٍ يَحمِلُ <c>{id:guid}</c> كانَ سَيَنكَسِرُ يَومَ يُبَدَّلُ
    /// القَيدُ — وذاكَ كَسرٌ في الأَداةِ يُقرَأُ مُخالَفَةً في
    /// المَتجَر.</para>
    /// </summary>
    public static string Normalize(string routePattern)
    {
        var p = Constraint.Replace(routePattern, m => "{" + m.Groups[1].Value.Trim() + "}");
        if (!p.StartsWith('/')) p = "/" + p;
        if (p.Length > 1 && p.EndsWith('/')) p = p[..^1];
        return p;
    }
}
