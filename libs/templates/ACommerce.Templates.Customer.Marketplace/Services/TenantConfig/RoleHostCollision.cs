using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.TenantConfig;

/// <summary>
/// <para><b>مُضيفُ دَورٍ لا يُصادِمُ مَتجَراً قائِماً</b> (‏2026-11-07، ADR-050 §٤ · W13-4·C2b·4). تَطبيقُ الدَورِ <c>{slug}-{rolehost}.R</c>، ومَتجَرٌ قائِمٌ اسمُه
/// <c>best-vendor</c> (وُلِدَ قَبلَ <c>StoreHostName</c> أَو بِمُعَرِّفٍ لا يَجتازُه) لا مُضيفَ لَه — لكِنّ الاسمَ نَفسَه يَصيرُ مُضيفَ دَورِ البائِعِ لِمَتجَرِ
/// <c>best</c>. فَيُرَدُّ: <b>حِفظُ دَورٍ</b> يَدخُلُ المَتجَرَ ومُضيفُه اسمُ مَتجَرٍ قائِم (‏<see cref="TenantConfigCodes.RoleHostTaken"/>)، و<b>إنشاءُ مَتجَرٍ</b> مُضيفُ دَورٍ
/// مِن أَدوارِ الكَتالوجِ لَه اسمُ مَتجَرٍ قائِم (‏<c>slug_taken</c> — المَتجَرُ قَد يَختارُ أَيَّ دَورٍ بَعدَها). <b>والاتِّجاهُ الآخَرُ مَحروسٌ سَلَفاً</b>: مُعَرِّفٌ
/// يَنتَهي بِـ<c>-{rolehost}</c> لا يولَدُ (<c>host_name_role_suffix</c> ← <c>slug_reserved</c> في الأَبوابِ الثَلاثَة).</para>
/// <para>بابُ حالَةٍ مَخزونَةٍ يُجيبُ بِأَوَّلِ خَرق (ADR-038): أَوَّلُ مُضيفِ دَورٍ مَأخوذٍ أَو <c>null</c>.</para>
/// </summary>
public static class RoleHostCollision
{
    /// <summary>مُضيفاتُ الأَدوارِ لِهذا المَتجَرِ (<c>{slug}-{rolehost}</c>) لِأَدوارِ الكَتالوجِ المُعطاةِ وَحدَها — ما لا اسمَ مُضيفٍ لَه لا مُضيفَ لَه.</summary>
    public static IReadOnlyList<string> Hosts(string slug, IEnumerable<string> catalogSlugs)
        => catalogSlugs.Where(c => RoleHostNames.ByCatalogSlug.ContainsKey(c))
            .Select(c => $"{slug}-{RoleHostNames.ByCatalogSlug[c]}").Distinct(StringComparer.Ordinal).ToList();

    /// <summary>أَوَّلُ مُضيفِ دَورٍ هُوَ اسمُ مَتجَرٍ قائِم — أَو <c>null</c>.</summary>
    public static async Task<string?> FirstTakenAsync(IQuerySession session, string slug, IEnumerable<string> catalogSlugs, CancellationToken ct = default)
    {
        foreach (var host in Hosts(slug, catalogSlugs))
            if (await session.LoadAsync<Tenant>(host, ct) is not null) return host;
        return null;
    }

    /// <summary>أَيُّ مُضيفِ دَورٍ مُمكِنٍ لِمَتجَرٍ جَديدٍ (أَدوارُ الكَتالوجِ كُلُّها) اسمُ مَتجَرٍ قائِم؟</summary>
    public static Task<string?> FirstTakenForNewStoreAsync(IQuerySession session, string slug, CancellationToken ct = default)
        => FirstTakenAsync(session, slug, RoleHostNames.ByCatalogSlug.Keys, ct);
}
