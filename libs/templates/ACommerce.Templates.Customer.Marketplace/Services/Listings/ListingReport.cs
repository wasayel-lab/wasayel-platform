using ACommerce.Kit.Notifications;
using ACommerce.Kit.Support;

namespace ACommerce.Templates.Customer.Marketplace.Services.Listings;

/// <summary>
/// <para><b>البَلاغُ عَن إعلانٍ بِنَصٍّ واحِدٍ يَكتُبُه بابُه ويَقرَؤُه المَحو</b> (‏2026-10-29، مُراجَعَةُ تَيّارِ S2 الجَولَة ٢؛ قَرارُ المالِك د52 (١)). بابُ البَلاغِ
/// (<c>POST /{slug}/listings/{id}/report</c>) يَفتَحُ تَذكِرَةً بِاسمِ المُبَلِّغِ وهُوِيَّتِه (<c>TicketCreated.AuthorId</c>)، ثُمَّ يُخطِرُ مُديري المَتجَرِ بِإشعارٍ لا هُوِيَّةَ
/// فيه: عُنوانُه السَبَبُ ومَتنُه الاسم، ورابِطُه صَفحَةُ التَذاكِرِ بِلا مُعَرِّف. فَكانَ الحَذفُ يَمحو كُلَّ إشعارِ بَلاغٍ يَحوي الاسمَ نَصّاً — فَيَكتُبُ «حِسابٌ مَحذوف»
/// في بَلاغِ «عَلي حَسَن» إذا حُذِفَ «عَلي»، وفي بَلاغِ سَمِيِّه.</para>
///
/// <para><b>والرَبطُ بِالتَذكِرَةِ لا بِالنَصّ</b> (<see cref="Announces"/>): الإشعارُ يُعلِنُ تَذكِرَةَ البَلاغِ الَّتي كُتِبَ بَعدَها مُباشَرَةً — السَبَبُ نَفسُه في عُنوانِه،
/// واسمُ كاتِبِها يَومَئِذٍ مَتنُه كُلُّه، ووَقتُه عِندَ التَذكِرَةِ أَو بَعدَها وقَبلَ التَذكِرَةِ التالِيَةِ بِالمَوضوعِ نَفسِه في المَتجَر. والبابُ يَكتُبُ التَذكِرَةَ ويَحفَظُها
/// ثُمَّ الإشعارَ في الطَلَبِ نَفسِه، فَالتَرتيبُ مَضمون. وهذا يَصدُقُ عَلى كُلِّ بَلاغٍ مُخَزَّنٍ اليَوم — لا صيغَةَ جَديدَةً تُشتَرَط.</para>
///
/// <para><b>والنُصوصُ هُنا بِحَرفِها كَما كَتَبَها البابُ مُنذُ وِلادَتِه</b>: المَحوُ يُطابِقُ ما خُزِّنَ بِها، فَنَقلُها إلى القامُوسِ (القاعِدَة ١١ — دَينٌ قائِمٌ قَبلَ هذا)
/// يُبقي هذِه لِلمُخَزَّنِ ويُضيفُ مِفتاحَ الجَديد.</para>
/// </summary>
public static class ListingReport
{
    /// <summary>نَوعُ إشعارِ البَلاغِ لِمُديري المَتجَر.</summary>
    public const string NoticeType = "report";

    private const string SubjectPrefix = "تَبليغ: ";
    private const string TitlePrefix = "بَلاغ: ";
    private const string BodySuffix = " بَلَّغَ عَن إعلان";

    public static string TicketSubject(string reason) => SubjectPrefix + reason;

    public static string NoticeTitle(string reason) => TitlePrefix + reason;

    public static string NoticeBody(string reporterName) => reporterName + BodySuffix;

    /// <summary>
    /// <para><b>رابِطُ إشعارِ البَلاغ: الإعلانُ المُبَلَّغُ عَنه</b> (‏2026-10-30، تَدقيقُ الموجَة ١١ CO7؛ تَوصيفُه <c>390eb0d</c>). كانَ البابُ يَكتُبُ
    /// <c>/admin/tenants/{slug}/tickets</c> — ولا مَسارَ يَخدُمُه فَالنَقرُ ‏404 (القاعِدَة ١٢). والإعلانُ صَفحَةٌ يَفتَحُها مُديرُ المَتجَرِ كَأَيِّ عُضو، وهِيَ ما يُحكَمُ
    /// فيه البَلاغ؛ نَسَقُ رابِطِ إشعارِ «إعلان جَديد». والمَحوُ لا يَربِطُ إشعارَ البَلاغِ بِرابِطِه بَل بِتَذكِرَتِه (<see cref="Announces"/>) أَو بِوَسيلَتِه:
    /// <c>AccountErasure.EraseFrom(Notification…)</c> يَستَثني إشعاراتِ المُديرينَ مِن «ما يَخُصُّه بِالرابِط».</para>
    /// </summary>
    public static string NoticeUrl(string tenantSlug, Guid listingId) => $"/{tenantSlug}/listings/{listingId}";

    /// <summary>سَبَبُ البَلاغِ مِن مَوضوعِ تَذكِرَتِه — أَو لا شَيء إن لَم تَكُن تَذكِرَةَ بَلاغ.</summary>
    public static string? ReasonOf(string? ticketSubject)
        => ticketSubject is not null && ticketSubject.StartsWith(SubjectPrefix, StringComparison.Ordinal) ? ticketSubject[SubjectPrefix.Length..] : null;

    /// <summary>هَل يُعلِنُ هذا الإشعارُ تَذكِرَةَ البَلاغِ هذِه؟ — <paramref name="nextSameSubjectAt"/> وَقتُ التَذكِرَةِ التالِيَةِ بِالمَوضوعِ نَفسِه في المَتجَر (أَيّاً كانَ كاتِبُها)،
    /// و<paramref name="report"/> بِاسمِ كاتِبِها كَما كُتِب (قَبلَ أَيِّ مَحو).</summary>
    public static bool Announces(Notification n, Ticket report, DateTime? nextSameSubjectAt)
        => n.Type == NoticeType && ReasonOf(report.Subject) is { } reason
           && n.Title == NoticeTitle(reason) && n.Body == NoticeBody(report.AuthorName)
           && n.At >= report.CreatedAt && (nextSameSubjectAt is null || n.At < nextSameSubjectAt.Value);
}
