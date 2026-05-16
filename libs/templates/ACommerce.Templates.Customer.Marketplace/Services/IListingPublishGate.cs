namespace Ejar.Customer.UI.Services;

/// <summary>
/// بَوّابَة "هَل يُسمَح بِنَشر إعلان؟" تَستَهلِكها صَفحَة <c>CreateListing</c>
/// قَبل الإرسال. الـ template يُسَجِّل default يُمَرِّر دائِماً
/// (<see cref="OpenPublishGate"/>) — تَطبيقات تَفرِض دَفعاً أَو اشتِراكاً
/// تُسَجِّل تَنفيذاً يَفتَح scope (مَثَلاً <see cref="System.IDisposable"/>
/// يَحقُن header الدَفع عَلى الـ HttpClient طَوال مُدَّة CreateAsync).
///
/// <para>الـ kit Listings لا يَتَغَيَّر. الـ controller لا يَتَغَيَّر.
/// الـ HttpClient handler يَستَخدِم سياق scoped لِيَعرِف ما يَضَع.</para>
/// </summary>
public interface IListingPublishGate
{
    Task<PublishAuthorization> AuthorizeAsync(CancellationToken ct = default);
}

/// <summary>
/// نَتيجَة فَتح البَوّابَة:
/// <list type="bullet">
///   <item><c>Allowed=true, Scope=null</c> — مَرَّر بِلا قَيد. السَّيناريو الافتِراضي.</item>
///   <item><c>Allowed=true, Scope=IDisposable</c> — مَرَّر بِشَرط أَن
///         تَجري <c>CreateAsync</c> داخِل <c>using</c>. الـ Scope يَحقُن
///         headers مَؤقَّتاً ثُمّ يُنَظِّف.</item>
///   <item><c>Allowed=false</c> — مَنع. الـ page تَعرِض <c>Error</c>.</item>
/// </list>
/// </summary>
public sealed record PublishAuthorization(
    bool Allowed,
    string? Error = null,
    IDisposable? Scope = null);

/// <summary>الـ default — تَطبيقات بِلا دَفع/اشتِراك.</summary>
public sealed class OpenPublishGate : IListingPublishGate
{
    public Task<PublishAuthorization> AuthorizeAsync(CancellationToken ct = default) =>
        Task.FromResult(new PublishAuthorization(Allowed: true));
}
