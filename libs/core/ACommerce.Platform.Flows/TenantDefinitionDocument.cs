namespace ACommerce.Platform.Flows;

/// <summary>
/// <para><b>شَكل وَثيقَة تَعريفٍ يُؤَلِّفُها مُستَأجِر</b> — الأَعضاء
/// الثَمانِيَة الَّتي كانَت مَكتوبَةً <b>مُتَطابِقَةً حَرفاً</b> في
/// <c>TenantRoleDefinition</c> و<c>TenantThemeDefinition</c>: هُوِيَّة
/// هي السلاج، ونَصّ التَعريف كَما كُتِبَ، وحالَة مِن
/// <see cref="ApprovalFlow"/>، وأَثَرُ مَن كَتَبَ ومَن قَرَّرَ.</para>
///
/// <para><b>ولِماذا واجِهَة لا صَنف قاعِدَة</b>: الوَثائِق تَعيش في
/// عُدَدٍ مُستَقِلَّة (‏<c>Roles</c>، <c>Theme</c>، <c>Subscriptions</c>)
/// ولا يَجمَعُها سِوى هذا المِلَفّ عَديم الاعتِماد. الواجِهَة تُعطي
/// الشَكلَ المُشتَرَك بِلا أَن تَجُرَّ عُدَّةً إلى أُخرى — وهو نَفس
/// المُبَرِّر الَّذي وَضَعَ <see cref="ApprovalFlow"/> هُنا.</para>
///
/// <para><b>وما لا تَقولُه، بِقَصد</b>: لا تَذكُر كَيفَ تُقرَأ
/// <c>DefinitionJson</c> ولا ما يَصِحّ فيه. القِراءَة والمُصادَقَة
/// لِكُلّ عُدَّةٍ مُحَمِّلُها ومُصادِقُها — والخَلطُ بَينَهُما كانَ
/// سَيُنتِج «مُصادِقاً عامّاً» لا يَعرِف ما يُصادِق.</para>
/// </summary>
public interface ITenantDefinitionDocument
{
    /// <summary>هُوِيَّة الوَثيقَة = <see cref="Slug"/>. الفَرادَة داخِل
    /// المُستَأجِر مَضمونَة بِالإيجار المُقتَرِن.</summary>
    string Id { get; set; }

    string Slug { get; set; }

    /// <summary>نَصّ التَعريف كَما كُتِبَ — يُقرَأ بِمُحَمِّل عُدَّتِه.</summary>
    string DefinitionJson { get; set; }

    /// <summary>مِن <see cref="ApprovalFlow.All"/> حَصراً.</summary>
    string Status { get; set; }

    /// <summary>مَن كَتَبَ — لِلتَدقيق لا لِلقَرار.</summary>
    string CreatedBy { get; set; }

    DateTime CreatedAt { get; set; }

    /// <summary>مَن قَرَّرَ ومَتى — يُملَآن عِندَ الاعتِماد أَو الرَفض.</summary>
    string? DecidedBy { get; set; }

    DateTime? DecidedAt { get; set; }
}
