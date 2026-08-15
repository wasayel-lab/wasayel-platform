using ACommerce.Kit.Tenants;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.TenantConfig;

/// <summary>قائِمَةُ الفِئات كَما كَتَبَها المُستَخدِم — سَطرٌ لِكُلّ
/// فِئَة، بِأَعمِدَةٍ يَفصِلُها <c>|</c>. الخِدمَة هي الَّتي تَعرِف
/// الصيغَة، لا النُقطَة.</summary>
public sealed record CategoriesSaveRequest(string Raw);

/// <summary>
/// <para><b>إعادَةُ كِتابَةِ فِئات المُستَأجِر — تَعريفٌ واحِد
/// لِلسَطحَين.</b> كانَ ‏44 سَطراً في <c>/admin</c> و‏36 في
/// <c>/studio</c>، والفَرقُ ثَلاثَةُ مَحاوِر: التَدقيق (يُكتَب هُناكَ
/// ويُهمَل هُنا)، والأَيقونَةُ الافتِراضِيَّة (🏠 مُقابِل 🏷️)،
/// ورَمزا الخَطَأ (<c>bad_categories</c>/<c>no_categories</c> مُقابِل
/// <c>format</c>/<c>empty</c>) — لِنَفس المَعنى تَماماً.</para>
///
/// <para><b>وحَسمُ الأَيقونَة</b>: غَلَبَت 🏷️ الَّتي كانَت في
/// <c>/studio</c>. و<c>🏠</c> تَفتَرِض عَموداً واحِداً — العَقار — في
/// مَنَصَّةٍ بِأَعمِدَةٍ عِدَّة (سَيّارات، خَدَمات، تَوصيل)، فَتَظهَر
/// أَيقونَةُ بَيتٍ فَوقَ فِئَةِ «شاحِنات». والأَثَرُ مَحصور
/// بِبُرهان: الصَفحَتانِ كِلتاهُما تَكتُبانِ عَمود الأَيقونَة في
/// النَصّ المُعاد (<c>{Slug} | {Label} | {Icon} | {Kind}</c>)،
/// فَالفِئات القائِمَة تَعود بِأَيقونَتِها المَحفوظَة، ولا يَنال
/// الافتِراضُ الجَديدُ إلّا صَفّاً كُتِبَ بِعَمودَين.</para>
///
/// <para><b>وحَسمُ التَشذيب</b>: غَلَبَ <c>/admin</c> — يُشَذِّب
/// السَطر ويَتَخَطّى الفارِغ. وكانَ <c>/studio</c> يُمَرِّر السَطرَ
/// كَما هُوَ، فَسَطرٌ بِمَسافَةٍ بادِئَة يُنتِج فِئَةً
/// بِـ<c>slug</c> يَبدَأ بِفَراغ — لا تُطابِق أَيّ رابِط.</para>
/// </summary>
public static class CategoriesSaveService
{
    public const string AuditAction = "tenant.categories_save";

    /// <summary><b>الأَيقونَةُ الافتِراضِيَّة</b> لِصَفٍّ بِلا عَمود
    /// أَيقونَة — راجِع حَسمَ الأَيقونَة أَعلاه.</summary>
    public const string DefaultIcon = "🏷️";

    /// <summary><b>دالَّةُ القَرار، نَقِيَّة</b> (ق٣): نَصٌّ ← فِئات،
    /// أَو رَمزُ رَفض. بِلا Marten وبِلا HTTP.</summary>
    public static (List<Category>? Categories, string? Code) Parse(string raw)
    {
        var categories = new List<Category>();
        var idx = 0;

        foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var l = line.Trim();
            if (l.Length == 0) continue;

            var parts = l.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length < 2) return (null, TenantConfigCodes.BadFormat);

            var slug  = parts[0].Trim().ToLowerInvariant();
            var label = parts[1].Trim();
            if (string.IsNullOrEmpty(slug) || string.IsNullOrEmpty(label))
                return (null, TenantConfigCodes.BadFormat);

            categories.Add(new Category
            {
                Slug  = slug,
                Label = label,
                Icon  = parts.Length > 2 && !string.IsNullOrEmpty(parts[2]) ? parts[2].Trim() : DefaultIcon,
                Kind  = parts.Length > 3 ? parts[3].Trim().ToLowerInvariant() : "",
                SortOrder = idx++,
            });
        }

        return categories.Count == 0
            ? (null, TenantConfigCodes.Empty)
            : (categories, null);
    }

    public static async Task<TenantConfigResult> SaveAsync(
        IDocumentSession session, string slug, CategoriesSaveRequest r,
        CancellationToken ct = default)
    {
        var (categories, code) = Parse(r.Raw);
        if (code is not null) return TenantConfigResult.Reject(code);

        var t = await session.LoadAsync<Tenant>(slug, ct);
        if (t is null) return TenantConfigResult.TenantMissing;

        t.Categories = categories!;
        session.Store(t);
        return TenantConfigResult.Saved;
    }
}
