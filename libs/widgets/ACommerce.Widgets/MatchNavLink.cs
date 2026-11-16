using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace ACommerce.Widgets;

/// <summary>
/// <para><b><see cref="NavLink"/> يُطابِقُ العُنوانَ الَّذي في الشَريطِ لا المَسارَ بَعدَ الحَقن</b> (‏2026-11-07، ADR-050 §٩ المَرحَلَةُ الثانِيَة · W13-4·C2b·1).
/// عَلى مُضيفِ المَتجَرِ يُحَوِّلُ الحاقِنُ <c>ashare.R/explore</c> إلى <c>/ashare/explore</c> قَبلَ التَوجيه، فَيَرى <c>NavigationManager</c> المَسارَ المَحقونَ —
/// ورابِطُ التَبويبِ صارَ نَظيفاً (<c>/explore</c>)، فَلا يُطابِقُه <c>NavLink</c> ويَسقُطُ تَمييزُ التَبويبِ النَشِطِ في الشَريطَينِ كِلَيهِما.</para>
/// <para><b>والقاعِدَة</b>: <see cref="MatchUri"/> المُطلَقُ إن أُعطِيَ (العُنوانُ الظاهِرُ عَلى مُضيفِ المَتجَر) هُوَ ما يُطابَق، وإلّا <c>NavLink</c> بِحَرفِه.
/// والرَسمُ رَسمُ <c>NavLink</c> نَفسُه — فَوَضعُ المَسارِ (بِلا <see cref="MatchUri"/>) لا يَتَغَيَّرُ بايتاً.</para>
/// </summary>
public class MatchNavLink : NavLink
{
    /// <summary>العُنوانُ المُطلَقُ الَّذي يُطابَقُ بِه بَدَلَ <c>NavigationManager.Uri</c> — <c>null</c> = كَما يُطابِقُ <c>NavLink</c>.</summary>
    [Parameter] public string? MatchUri { get; set; }

    protected override bool ShouldMatch(string uriAbsolute) => base.ShouldMatch(MatchUri ?? uriAbsolute);
}
