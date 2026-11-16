using System.Reflection;
using ACommerce.Kit.Tenants;

namespace ACommerce.Templates.Customer.Marketplace.Services.Ux;

/// <summary>
/// <para><b>قَوالِبُ صَفَحاتِ الدَورِ مِن <c>@page</c> القالِبِ نَفسِه</b> (‏2026-11-07، ADR-050 §٤ · W13-4·C2b·4) — يُسَجِّلُها
/// <c>AddCustomerMarketplaceTemplate</c> في الحاوِيَة فَيَسأَلُها حاقِنُ المُضيفِ (<c>SubdomainTenantPathMiddleware</c>) ولا يَراها مَشروعُه. لا قائِمَةَ بِيَد:
/// كُلُّ مُكَوِّنٍ يَحمِلُ <c>[Route("/{slug}/r/{role}/…")]</c> — وصَفحَةُ دَورٍ جَديدَةٌ تَدخُلُها بِلا لَمسِ شَيء (وتَوأَمُها بِلا دَورٍ مَشروطٌ بِـG5).</para>
/// </summary>
public static class RoleHostPages
{
    /// <summary>كُلُّ قَوالِبِ المَساراتِ في القالِب (<c>@page</c>) — يَقرَؤُها G5 أَيضاً.</summary>
    public static IReadOnlyList<string> AllRoutes { get; } = typeof(RoleHostPages).Assembly.GetTypes()
        .SelectMany(t => t.GetCustomAttributes<Microsoft.AspNetCore.Components.RouteAttribute>(inherit: false))
        .Select(a => a.Template).Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal).ToArray();

    /// <summary>قَوالِبُ صَفَحاتِ الدَور (<c>/{slug}/r/{role}…</c>).</summary>
    public static RolePageTemplates Templates { get; } = new(AllRoutes);
}
