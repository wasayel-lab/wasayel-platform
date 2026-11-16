namespace ACommerce.Kit.Roles;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// <para><b>أَسماءُ مُضيفاتِ الأَدوار — مُعجَمٌ مُغلَقٌ مِن أَدوارِ الكَتالوج</b> (‏2026-11-07، ADR-050، W13-4·C2a·2). تَطبيقُ الدَورِ مُضيفُه
/// <c>{slug}-{rolehost}.R</c> في المُستَوى الأَوَّلِ (الَّذي تُغَطّيه شَهادَةُ Cloudflare العامَّةُ مَجّاناً)، و<c>{rolehost}</c> قيمَةٌ مِن هذا المُعجَمِ
/// لا اسمُ الدَورِ كَما يَكتُبُه المَتجَر.</para>
///
/// <para><b>ولِماذا مُعجَمٌ لا اسمُ الدَورِ نَفسُه</b>: اسمُ الدَورِ <c>^[a-z][a-z0-9_]*$</c> (‏<see cref="RoleDefinitionValidator"/>) يَقبَلُ الشَرطَةَ
/// السُفلِيَّة، وهي لَيسَت في مِلصَقِ DNS — و<c>tenant_admin</c> وَحدَه مِن عَشَرَةِ أَدوارِ الكَتالوجِ يَخرِقُها، فَيَصيرُ <c>admin</c>. والتِسعَةُ الباقِيَةُ
/// بِأَسمائِها. <b>ولا يَتَغَيَّرُ إلّا بِكودٍ وADR</b>، ودَورٌ يَدخُلُ الكَتالوجَ بِلا اسمِ مُضيفٍ يُحمِّرُ <c>RoleHostNamesTests</c>.</para>
///
/// <para><b>مُستَهلِكُه اليَوم</b> (القاعِدَة ١): <c>StoreHostName</c> في عُدَّةِ المُستَأجِرين — مُعَرِّفُ مَتجَرٍ يَنتَهي بِـ<c>-{قيمَة}</c> يُصادِمُ مُضيفَ
/// دَورِ مَتجَرٍ آخَر، ويُشتَقُّ مِن أَطوَلِ قيمَةٍ سَقفُ طولِ المُعَرِّف. وتَحليلُ المُضيفِ إلى (مَتجَر، دَور) يُضافُ مَعَ مُستَهلِكِه في المَرحَلَةِ الثالِثَة
/// (ADR-050 §٩) — لا قَبلَه.</para>
/// </summary>
public static class RoleHostNames
{
    /// <summary>اسمُ دَورِ الكَتالوج ← مِلصَقُ مُضيفِه.</summary>
    public static readonly IReadOnlyDictionary<string, string> ByCatalogSlug =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["customer"]     = "customer",
            ["rider"]        = "rider",
            ["vendor"]       = "vendor",
            ["driver"]       = "driver",
            ["host"]         = "host",
            ["shipper"]      = "shipper",
            ["tenant_admin"] = "admin",      // الشَرطَةُ السُفلِيَّةُ لَيسَت في مِلصَقِ DNS
            ["broker"]       = "broker",
            ["mover"]        = "mover",
            ["organizer"]    = "organizer",
        };

    /// <summary>القِيَمُ وَحدَها — ما قَد يَلحَقُ مُعَرِّفَ المَتجَرِ بَعدَ شَرطَةٍ في مُضيفِ الدَور.</summary>
    public static readonly IReadOnlyList<string> Values =
        ByCatalogSlug.Values.Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal).ToArray();

    /// <summary>طولُ أَطوَلِ قيمَة — مِنه يُشتَقُّ سَقفُ مُعَرِّفِ المَتجَر.</summary>
    public static int LongestLength => Values.Max(v => v.Length);

    /// <summary><b>دَورُ الكَتالوجِ لِمِلصَقِ مُضيفِ دَور</b> — عَكسُ <see cref="ByCatalogSlug"/> (‏2026-11-07، ADR-050 §٤ · W13-4·C2b·4): <c>admin</c> ←
    /// <c>tenant_admin</c>، والتِسعَةُ بِأَسمائِها؛ و<c>null</c> لِما لَيسَ في المُعجَم. يَسأَلُه تَحليلُ المُضيفِ (<c>RoleFromHost</c>) — مُستَهلِكُه الَّذي وَعَدَ بِه
    /// هذا المِلَفُّ في المَرحَلَةِ الثالِثَة.</summary>
    public static string? CatalogSlugOf(string? hostLabel)
        => hostLabel is null ? null : ByCatalogSlug.FirstOrDefault(kv => string.Equals(kv.Value, hostLabel, StringComparison.Ordinal)).Key;
}
