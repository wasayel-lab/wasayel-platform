namespace Ejar.Customer.UI.Services;

/// <summary>
/// فَحص "هَل يَسمَح لِلمُستَخدِم بِفَتح صَفحَة إنشاء إعلان؟" — يَجري
/// مَرَّة واحِدَة عَلى الإقلاع. الفَرق عَن <see cref="IListingPublishGate"/>:
/// <list type="bullet">
///   <item><b>Precheck</b> (هُنا) يَحجِب الفورم نَفسه — مَفيد لِأَنظِمَة
///         اشتِراك (إذا لا اشتِراك ⇒ لا تَفتَح الفورم).</item>
///   <item><b>Gate</b> يَفحَص عِند ضَغطَة "نَشر" — مَفيد لِدَفع لِكُلّ
///         إعلان (الفورم مَفتوح، الدَفع عِند الإرسال).</item>
/// </list>
///
/// <para>الـ default <see cref="OpenCreatePrecheck"/> يُمَرِّر دائِماً —
/// تَطبيقات الاشتِراك تُسَجِّل impl يَفحَص <c>subscription.get_active</c>
/// مَثَلاً، وتَعرِض رابِط "/plans" إذا لا اشتِراك.</para>
/// </summary>
public interface IListingCreatePrecheck
{
    Task<CreatePrecheckResult> CanCreateAsync(CancellationToken ct = default);
}

/// <param name="Allowed">السَّماح بِعَرض الفورم.</param>
/// <param name="Title">عُنوان عَرض في شاشَة الحَجب (إذا <c>!Allowed</c>).</param>
/// <param name="Message">نَصّ شَرح في شاشَة الحَجب.</param>
/// <param name="ActionLabel">نَصّ زُرّ الإجراء (مَثَلاً "اعرِض الباقات").</param>
/// <param name="ActionUrl">رابِط الإجراء (مَثَلاً <c>/plans</c>).</param>
public sealed record CreatePrecheckResult(
    bool Allowed,
    string? Title = null,
    string? Message = null,
    string? ActionLabel = null,
    string? ActionUrl = null);

/// <summary>Default — تَطبيقات بِلا اشتِراك (مِثل V3 بِالدَفع لِكُلّ إعلان).</summary>
public sealed class OpenCreatePrecheck : IListingCreatePrecheck
{
    public Task<CreatePrecheckResult> CanCreateAsync(CancellationToken ct = default) =>
        Task.FromResult(new CreatePrecheckResult(Allowed: true));
}
