using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Patterns;

namespace ACommerce.Templates.Customer.Marketplace.Services.TenantConfig;

/// <summary>
/// <para><b>تَرميمُ مَتاجِرِ المُنتَجاتِ الَّتي وُلِدَت «إيجاراً» بِلا سَلَّة</b> (‏2026-10-29، قَرارُ المالِك د24 «ب»؛ ‏ADR-034 مُلحَق د24) —
/// القَرارُ كُلُّه هُنا دالَّةً نَقِيَّة، والسكربتُ <c>scripts/repair-commercial-categories.cs</c> يَقرَأُ ويَعرِضُ ويُنَفِّذ.</para>
///
/// <para><b>العِلَّةُ المَقيسَة</b> (‏ADR-034 §١): قَبلَ <c>c4278c0</c> كانَ مَصنَعُ الاستوديو يَكتُبُ فِئَتَي قِطاعِ <c>ecommerce</c> —
/// <c>products</c> و<c>deals</c> — بِتَجميعِ <c>commercial</c>، و<c>PatternOf</c> يَقرَؤُه إيجاراً بِلا سَلَّة. الإصلاحُ مَنَعَ الجَديدَ ولَم يُصلِحِ
/// القائِم (§٣ البَند ١)، وهذا عَطَبٌ مِن المَنَصَّةِ نَفسِها فَإصلاحُه عَلى وَسايِل.</para>
///
/// <para><b>والحَدُّ بِحَرفِ القَرار</b>: مَتجَرٌ بُنِيَ مِن الاستوديو (<see cref="Tenant.SourceAnalysisId"/> مَضبوط)، وفِئَتا <c>products</c>
/// و<c>deals</c> وَحدَهُما حينَ تَجميعُهُما <c>commercial</c> بِالضَبط — فَتَصيرانِ بِلا تَجميعٍ كَما يَكتُبُهُما المَصنَعُ اليَوم. ولا يُمَسُّ سِواهُما:
/// فِئَةُ «مَحَلّات» في مَتجَرِ عَقارٍ تَبقى «تِجاريّ» عَن قَصد. وبَعدَه يُغَيِّرُ صاحِبُ المَتجَرِ التَجميعَ مِن مُحَرِّرِ الفِئاتِ كَما يَشاء.</para>
/// </summary>
public static class CommercialCategoryRepair
{
    /// <summary>الفِئَتانِ اللَّتانِ كَتَبَهُما المَصنَعُ قَبلَ الإصلاح — <c>CategoriesForSector("ecommerce")</c>.</summary>
    public static readonly IReadOnlyList<string> Slugs = new[] { "products", "deals" };

    /// <summary>سَطرُ التَدقيقِ لِكُلِّ مَتجَرٍ يُرَمَّم.</summary>
    public const string AuditAction = "tenant.categories_kind_repair";

    /// <summary>الفِئاتُ الَّتي يُغَيِّرُها التَرميمُ في هذا المَتجَر — فارِغَةٌ لِما لا يَمَسُّه.</summary>
    public static IReadOnlyList<string> Targets(Tenant tenant)
        => tenant.SourceAnalysisId is null
            ? Array.Empty<string>()
            : tenant.Categories
                .Where(c => Slugs.Contains(c.Slug, StringComparer.Ordinal)
                         && string.Equals(c.Kind, CategoryKinds.Commercial, StringComparison.Ordinal))
                .Select(c => c.Slug)
                .ToList();

    /// <summary>يُطَبِّقُ التَرميمَ عَلى الوَثيقَة (بِلا جَلسَة)، ويُعيدُ عَدَدَ الفِئاتِ الَّتي تَغَيَّرَت. تَكرارُه لا يُغَيِّرُ شَيئاً.</summary>
    public static int Apply(Tenant tenant)
    {
        var targets = Targets(tenant);
        var changed = 0;
        foreach (var c in tenant.Categories.Where(c => targets.Contains(c.Slug, StringComparer.Ordinal)))
        {
            c.Kind = CategoryKinds.None;
            changed++;
        }
        return changed;
    }
}
