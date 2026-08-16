using ACommerce.Kit.Auth;
using ACommerce.Kit.Chat;
using ACommerce.Kit.Notifications;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.Queries;

/// <summary>
/// <para><b>ما يَحتاجُه شَريطُ التَنَقُّل عَن صاحِب الجَلسَة</b> — ثَلاثُ
/// قيَمٍ لا وَثيقَة. و<c>record</c> لا كائِنُ Marten عَمداً: الشَريطُ
/// يَعرِض عَدَدَين ودَوراً، ولا شَأنَ لَه بِهاتِف المُستَخدِم ولا
/// بِهُوِيَّتِه — وتَمريرُ الوَثيقَة كامِلَةً إلى التَخطيط يَجعَل كُلَّ
/// حَقلٍ فيها سَطحاً يَعتَمِد عَلَيه أَحَدٌ يَوماً.</para>
///
/// <para><c>StoredActiveRole</c> هُوَ ما في القاعِدَة وَحدَه. و<b>الـURL
/// يَتَفَوَّق عَلَيه</b> — لكِنّ ذلك قَرارُ تَوجيهٍ لا استِعلام، فَيَبقى
/// في الصَفحَة حَيثُ كانَ حَرفاً.</para>
/// </summary>
public sealed record ShellState(string StoredActiveRole, int UnreadMessages, int UnreadNotifications)
{
    /// <summary>حالَةُ «لا جَلسَة» أَو «تَعَذَّرَت القِراءَة» — نَفسُ
    /// قيَم البَدء في <c>MainLayout</c> حَرفاً، فَالسُقوطُ الآمِن
    /// يُعطي شَريطاً كامِلاً بِلا شارات.</summary>
    public static readonly ShellState Empty = new("", 0, 0);
}

/// <summary>
/// <para><b>استِعلاماتُ الشِلّ — أَوسَعُ سَطحٍ في المُنتَج.</b>
/// ‏<c>MainLayout</c> يُصَيَّر مَع <b>كُلّ</b> صَفحَةِ مَتجَر، فَجَلسَةُ
/// Marten المَفتوحَة فيه كانَت أَكثَرَ الجَلسات تَكراراً في المُستَودَع
/// وأَشَدَّها مَنعاً لِتَطبيق MAUI: شِلٌّ لا يُصَيَّر بِلا قاعِدَةِ
/// بَيانات يَعني أَنّ **لا شاشَةَ واحِدَة** تُصَيَّر.</para>
///
/// <para><b>والعَزلُ بُنيَويّ</b>: الجَلسَةُ تُفتَح بِـ
/// <c>QuerySession(tenantSlug)</c>، و<see cref="Conversation"/> و
/// <see cref="Notification"/> تَحتَ سِياسَة
/// <c>AllDocumentsAreMultiTenanted</c> — فَـMarten يَضَع
/// <c>tenant_id</c> في الاستِعلام. مُستَخدِمٌ لَه رَسائِلُ غَير
/// مَقروءَةٍ في مَتجَرٍ لا تَظهَر شارَتُها في مَتجَرٍ آخَر، ولَيسَ
/// ذلكَ سَطرَ شَرطٍ يُنسى بَل خاصِّيَّةٌ لا سَبيلَ إلى خَرقِها مِن
/// هُنا.</para>
/// </summary>
public sealed class ShellQueries
{
    private readonly IDocumentStore _store;

    public ShellQueries(IDocumentStore store) => _store = store;

    /// <summary>
    /// <para>الدَورُ المَحفوظ وعَدّادا الشارات — بِنِداءٍ واحِد وجَلسَةٍ
    /// واحِدَة، كَما كانَ في <c>MainLayout</c> حَرفاً.</para>
    ///
    /// <para><b>وعَدُّ الرَسائِل يَقَع في الذاكِرَة لا في القاعِدَة</b>،
    /// وهذا نَقلٌ لا اختِيار: <c>Conversation</c> تَخزِن
    /// <c>OwnerUnread</c>/<c>PartnerUnread</c> مُنفَصِلَين، والعَدُّ
    /// المَطلوب «مُحادَثاتٌ فيها جَديدٌ <b>لي</b>» — أَي شَرطٌ يَختَلِف
    /// بِاختِلاف طَرَفي المُحادَثَة. تَحويلُه إلى شَرطٍ واحِدٍ في
    /// القاعِدَة تَحسينٌ لَه مَوجَتُه، والمَوجَةُ هذِه تَنقُل
    /// المَوضِعَ ولا تُعَدِّل المَنطِق.</para>
    /// </summary>
    public async Task<ShellState> LoadAsync(
        string tenantSlug, Guid userId, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);

        var me = await s.LoadAsync<User>(userId, ct);

        var convs = await s.Query<Conversation>()
            .Where(c => c.OwnerId == userId || c.PartnerId == userId)
            .ToListAsync(ct);
        var unreadMessages = convs.Count(c =>
            (c.OwnerId == userId && c.OwnerUnread > 0) ||
            (c.PartnerId == userId && c.PartnerUnread > 0));

        var unreadNotifications = await s.Query<Notification>()
            .CountAsync(n => n.UserId == userId && !n.IsRead, ct);

        return new ShellState(me?.ActiveRole ?? "", unreadMessages, unreadNotifications);
    }
}
