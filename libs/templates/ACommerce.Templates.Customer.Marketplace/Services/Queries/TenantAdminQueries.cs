using ACommerce.Kit.Auth;
using ACommerce.Kit.Chat;
using ACommerce.Kit.Listings;
using ACommerce.Kit.Notifications;
using ACommerce.Kit.Offers;
using ACommerce.Kit.Subscriptions;
using ACommerce.Kit.Support;
using ACommerce.Platform.Shared;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.Queries;

/// <summary>لَوحَةُ إدارَةِ المَتجَر لِمالِكِه — أَربَعَةُ عَدّادات،
/// جَلسَةٌ واحِدَة.</summary>
public sealed record TenantOverview(
    int Listings, int Conversations, int PendingOffers, int Users)
{
    public static readonly TenantOverview Empty = new(0, 0, 0, 0);
}

/// <summary>
/// <para>لَوحَةُ تَحَكُّم التَطبيق في الاستوديو. <b>وكُلُّ حَقلٍ لَه
/// سُقوطُه الخاصّ</b>: العَدَدُ يَرجِع صِفراً والقائِمَةُ <c>null</c> —
/// و<c>null</c> هُنا لَيسَت «فارِغَة» بَل «جَدوَلٌ غَير مُهَيَّأ في هذا
/// المُستَأجِر»، والصَفحَةُ تُفَرِّق بَينَهُما في العَرض. نُقِلَ كَما
/// هُوَ.</para>
/// </summary>
public sealed record AppConsole(
    int Users, int Listings,
    List<Plan>? Plans,
    List<Ticket>? RecentTickets, int TotalTickets, int OpenTickets,
    List<Notification>? RecentNotifications);

/// <summary>
/// <para><b>ما تَسأَلُه شاشاتُ إدارَةِ مَتجَرٍ واحِد</b> — مُستَخدِموه،
/// وعَدّاداتُه، وإعلاناتُه، وتَذاكِرُه، وسِجِلّاتُه المُستورَدَة
/// (المَناطِق والخَصائِص).</para>
///
/// <para><b>ولِماذا سابِعَةٌ بَعدَ سِتّ، والقاعِدَةُ تَقول وَسِّع لا
/// تُنشِئ</b>: وُسِّعَت ثَلاثٌ في هذِه المَوجَة (‏<see cref="TenantDirectory"/>
/// و<see cref="AccountQueries"/> و<see cref="StorefrontQueries"/>) لِأَنّ
/// مَعانِيَها احتَمَلَت الزِيادَة. وهذِه لا تَحتَمِلُها واحِدَةٌ مِنها:
/// لَيسَت سِجِلَّ المُستَأجِرين، ولا «ما لي أَنا»، ولا واجِهَةَ المَتجَر.
/// وأَقرَبُها شَبَهاً <see cref="StudioQueries"/> — و<b>إقحامُها فيها
/// يَنقُض شَرطَها المُعلَن</b>: «المُستَأجِرُ فيها ثابِتٌ لا وَسيط»،
/// وكُلُّ نِداءٍ هُنا يَأخُذ سلاجاً. فَالتَوسيعُ كانَ سَيَكسِر خاصِّيَّةً
/// مَفروضَة، لا أَن يُضيفَ سَطراً.</para>
///
/// <para><b>وواحِدَةٌ لا اثنَتان لِأَنّ الشاشات زَوجِيَّة</b>: ‏
/// <c>/admin/tenants/{slug}/…</c> و<c>/studio/apps/{slug}/…</c> هُما
/// نَفسُ الشاشَة لِجُمهورَين — يُثَبِّت ذلك
/// <c>AdminStudioPairCharacterizationTests</c>. فَخِدمَتانِ مُتَوازِيَتان
/// كانَتا سَتَنجَرِفانِ عَن بَعضِهِما، وهو عَينُ «تَعريفَين لِقَرارٍ
/// واحِد». والحارِسُ يَبقى في الصَفحَة: الإداريَّةُ تَسأَل
/// <c>TenantAdminGuard</c>، والاستوديو يُقارِن <c>OwnerUserId</c> —
/// حارِسانِ مُختَلِفانِ فِعلاً، وقِراءَةٌ واحِدَة بَعدَهُما.</para>
///
/// <para><b>والعَزلُ بُنيَويّ</b>: كُلُّ نِداءٍ يَفتَح
/// <c>QuerySession(tenantSlug)</c>، وكُلُّ الوَثائِق المَقروءَة هُنا
/// تَقَع تَحتَ <c>AllDocumentsAreMultiTenanted</c>.</para>
/// </summary>
public sealed class TenantAdminQueries
{
    private readonly IDocumentStore _store;

    public TenantAdminQueries(IDocumentStore store) => _store = store;

    /// <summary>مُستَخدِمو المَتجَر — مُشرِفوه أَوَّلاً ثُمَّ الأَحدَث
    /// إنشاءً، نَفسَ تَرتيب <c>Admin/TenantUsers.razor</c> حَرفاً.</summary>
    public async Task<IReadOnlyList<User>> UsersAsync(
        string tenantSlug, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);
        return (await s.Query<User>().ToListAsync(ct))
            .OrderBy(u => u.ActiveRole != "tenant_admin")
            .ThenByDescending(u => u.CreatedAt)
            .ToList();
    }

    /// <summary>عَدّاداتُ لَوحَة إدارَة المَتجَر. <b>ولا تُنادى إلّا
    /// بَعدَ قَرار الصَلاحِيَّة</b> — والقَرارُ يَبقى في الصَفحَة لِأَنَّه
    /// يَقرَأ أَدوارَ المُستَأجِر ودَورَ الـURL مَعاً، وذاكَ تَأليفٌ لا
    /// استِعلام.</summary>
    public async Task<TenantOverview> OverviewAsync(
        string tenantSlug, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);
        return new TenantOverview(
            Listings:      await s.Query<Listing>().CountAsync(l => !l.IsDeleted, ct),
            Conversations: await s.Query<Conversation>().CountAsync(ct),
            PendingOffers: await s.Query<Offer>()
                                  .CountAsync(o => o.Status == OfferStatus.Pending, ct),
            Users:         await s.Query<User>().CountAsync(ct));
    }

    /// <summary>سِجِلّاتٌ مُستورَدَة مِن جَداوِلَ مُعَيَّنَة — تَقرَؤُها
    /// شاشاتُ المَناطِق والخَصائِص بِزَوجَيها. <b>والتَحويلُ إلى شَجَرَةٍ
    /// أَو إلى نَصّ يَبقى في الصَفحَة</b>: هُوَ عَرضٌ لا استِعلام،
    /// والشاشَتانِ تُحَوِّلانِه تَحويلَين مُختَلِفَين.</summary>
    public async Task<IReadOnlyList<ImportedRecord>> ImportedAsync(
        string tenantSlug, IReadOnlyList<string> tables, CancellationToken ct = default)
    {
        var wanted = tables.ToList();
        await using var s = _store.QuerySession(tenantSlug);
        return (await s.Query<ImportedRecord>()
            .Where(r => wanted.Contains(r.Table))
            .ToListAsync(ct)).ToList();
    }

    /// <summary>إعلاناتُ المَتجَر لِشاشَة الاستوديو، الأَحدَثُ تَحديثاً
    /// أَوَّلاً. <c>null</c> = جَدوَلٌ غَير مُهَيَّأ — لا «لا إعلانات».</summary>
    public async Task<List<Listing>?> AllListingsAsync(
        string tenantSlug, int take = 200, CancellationToken ct = default)
    {
        try
        {
            await using var s = _store.QuerySession(tenantSlug);
            return (await s.Query<Listing>()
                .OrderByDescending(l => l.UpdatedAt).Take(take).ToListAsync(ct)).ToList();
        }
        catch { return null; }
    }

    /// <summary>تَذاكِرُ الدَعم في المَتجَر كُلِّه (لا تَذاكِري أَنا —
    /// تِلكَ في <see cref="AccountQueries.SupportTicketsAsync"/>).</summary>
    public async Task<List<Ticket>?> AllTicketsAsync(
        string tenantSlug, int take = 200, CancellationToken ct = default)
    {
        try
        {
            await using var s = _store.QuerySession(tenantSlug);
            return (await s.Query<Ticket>().Take(take).ToListAsync(ct)).ToList();
        }
        catch { return null; }
    }

    /// <summary>
    /// <para>تَذكِرَةٌ واحِدَة، مَبنِيَّةٌ مِن <b>مَجرى أَحداثِها</b>.</para>
    ///
    /// <para><b>ولِماذا خَرَجَت مِن قائِمَة «الكِتابات الأَربَع»</b>: كانَت
    /// الصَفحَةُ تَفتَح <c>LightweightSession</c> — جَلسَةً قابِلَةً
    /// لِلكِتابَة — ثُمَّ <b>لا تَكتُب شَيئاً</b>. فَتَصنيفُها كاتِبَةً
    /// كانَ صَحيحاً بِالشَكل خاطِئاً بِالمَعنى: لا مُعامَلَةَ فيها
    /// تُقَرَّر، ولا صُندوقَ صادِراً يُتَجاوَز. فَهي **نُقلَةُ مَوضِعٍ
    /// خالِصَة**، وتُرَحَّل مَعَ القِراءات لا مَعَ الكِتابات.</para>
    /// </summary>
    public async Task<Ticket?> TicketAsync(
        string tenantSlug, Guid id, CancellationToken ct = default)
    {
        try
        {
            await using var s = _store.QuerySession(tenantSlug);
            return await s.Events.AggregateStreamAsync<Ticket>(id, token: ct);
        }
        catch { return null; }
    }

    /// <summary>لَوحَةُ تَحَكُّم التَطبيق — سَبعُ قِراءات في جَلسَةٍ
    /// واحِدَة، وكُلٌّ بِسُقوطِها كَما كانَت في الصَفحَة.</summary>
    public async Task<AppConsole> ConsoleAsync(
        string tenantSlug, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);

        static async Task<int> SafeCount(Func<Task<int>> fn)
        { try { return await fn(); } catch { return 0; } }
        static async Task<List<T>?> SafeList<T>(Func<Task<IReadOnlyList<T>>> fn)
        { try { return (await fn()).ToList(); } catch { return null; } }

        var plans = await SafeList(() => s.Query<Plan>().OrderBy(p => p.Price).Take(10).ToListAsync(ct));

        return new AppConsole(
            Users:    await SafeCount(() => s.Query<User>().CountAsync(ct)),
            Listings: await SafeCount(() => s.Query<Listing>().Where(l => !l.IsDeleted).CountAsync(ct)),
            Plans:    plans,
            RecentTickets: await SafeList(() =>
                s.Query<Ticket>().OrderByDescending(t => t.CreatedAt).Take(5).ToListAsync(ct)),
            TotalTickets: await SafeCount(() => s.Query<Ticket>().CountAsync(ct)),
            OpenTickets:  await SafeCount(() => s.Query<Ticket>().Where(t => t.Status == "open").CountAsync(ct)),
            RecentNotifications: await SafeList(() =>
                s.Query<Notification>().OrderByDescending(n => n.At).Take(8).ToListAsync(ct)));
    }
}
