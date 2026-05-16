namespace Ejar.Customer.UI.Components.Pages.Auth;

/// <summary>
/// قَرار التَطبيق: أَيّ مُكَوِّن Razor يُعرَض في <c>/login</c>. الـ template
/// نَفسه يَعرِف Phone-OTP فَقَط؛ Apps أُخرى تُسَجِّل مُكَوِّناً مُختَلِفاً
/// (مَثَلاً NafathLoginContent). الاختِيار صَريح في DI لا تَلقائي مِن
/// "علامَة" أَيّ مُزَوِّد 2FA مُسَجَّل.
///
/// <para>الاستِخدام في Program.cs/Composition:</para>
/// <code>
/// services.AddSingleton&lt;IAuthLoginUi&gt;(_ =&gt;
///     new StaticAuthLoginUi(typeof(NafathLoginContent)));
/// </code>
///
/// <para>الافتِراضي (لَو لَم يُسَجِّل التَطبيق شَيئاً) =
/// <c>PhoneOtpLoginContent</c> — توافُق رَجعي مَع apps موجودَة.</para>
/// </summary>
public interface IAuthLoginUi
{
    /// <summary>الـ Razor component type المَطلوب رَسمه داخِل <c>/login</c>.</summary>
    Type ComponentType { get; }
}

/// <summary>تَنفيذ بَسيط يَلتَقِط Type مُحَدَّد مَرَّة واحِدَة.</summary>
public sealed class StaticAuthLoginUi : IAuthLoginUi
{
    public Type ComponentType { get; }
    public StaticAuthLoginUi(Type componentType) => ComponentType = componentType;
}
