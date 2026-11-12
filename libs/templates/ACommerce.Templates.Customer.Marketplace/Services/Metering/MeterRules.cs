using ACommerce.Kit.Auth;
using ACommerce.Kit.Listings;
using ACommerce.Kit.Offers;
using ACommerce.Kit.Reviews;
using ACommerce.Kit.Subscriptions;
using ACommerce.Kit.Support;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using JasperFx.Events;

namespace ACommerce.Templates.Customer.Marketplace.Services.Metering;

/// <summary>وَثيقَةٌ في وَحدَةِ العَمَلِ قَبلَ الحِفظ، وهَل هي <b>وِلادَة</b> — يُقَرِّرُه المُستَمِعُ مِن التَغييراتِ المُعَلَّقَةِ وَحدَها
/// (وَثيقَةٌ مُتَفائِلَةٌ بِلا نُسخَة: <c>VersionFor == null</c>، مَقيس D6) لا مِن حالَةٍ مَخزونَة.</summary>
public sealed record PendingDocument(object Document, bool Created);

/// <summary>‏(‏2026-11-12، W13-6·٧) حَدَثٌ في مَجرىً مُعَلَّق: مُعَرِّفُه — وهُوَ ما يُكتَبُ في <c>mt_events.id</c>، ثابِتٌ قَبلَ الإيداعِ ويَراه المُستَمِع
/// (مَقيس E11) — وحِملُه.</summary>
public sealed record PendingEvent(Guid Id, object Data);

/// <summary>‏(‏2026-11-12، W13-6·٧) مَجرىً في وَحدَةِ العَمَل كَما يَراه المُستَمِعُ مِن <c>PendingChanges.Streams()</c>: مُستَأجِرُه (<c>StreamAction.TenantId</c>)،
/// وهَل <b>يَبدَأُ</b> في هذا الحِفظ (<c>StreamActionType.Start</c>) أَم يُلحَقُ بِمَجرىً قائِم، وأَحداثُه — قِيَمٌ لا جَلسَة.</summary>
public sealed record PendingStream(string? Tenant, bool Started, IReadOnlyList<PendingEvent> Events);

/// <summary>ما يَراه المُصَنِّفُ مِن الجَلسَة: مُستَأجِرُها والوَثائِقُ المُعَلَّقَة — قِيَمٌ لا جَلسَة.
/// ‏(‏2026-11-12، W13-6·٧ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) <b>وقَناةُ المَجرى</b> (<paramref name="Streams"/>): كانَ بِدءُ المَجرى يُنشَأُ ولا يَبلُغُ المُصَنِّفَ
/// مِنه شَيء (التَصميم §12 G3، RULES-05)، فَصارَت المَجاري المُعَلَّقَةُ مُدخَلاً ثالِثاً. وغِيابُها لا مَجرى.</summary>
public sealed record PendingSnapshot(string SessionTenant, IReadOnlyList<PendingDocument> Documents, IReadOnlyList<PendingStream>? Streams = null);

/// <summary>
/// <para><b>المُصَنِّفُ النَقِيّ</b> (التَصميم §5.1): تَغييراتٌ مُعَلَّقَة + لَقطَةُ الحامِل + المُعجَم ← سُطورُ الدَفتَر. لا جَلسَةَ ولا قاعِدَةَ ولا وَقت —
/// والمُدخَلُ نَفسُه يُعطي الخُرجَ نَفسَه، فَيُختَبَرُ بِلا قاعِدَة (<c>MeterRulesTests</c>).</para>
/// <para><b>ولا يَرمي لِفَجوَةِ تَصنيف</b>: بُعدٌ لا يُعرَفُ يُكتَبُ <see cref="MeterSentinels.Unresolved"/> فَيُعَدُّ ويُعرَض؛ وكُلُّ عَمودٍ قيمَةٌ أَو قيمَةٌ
/// حارِسَة، فَلا يُفشِلُ <c>NOT NULL</c> كِتابَةَ عَمَلٍ أَبَداً (§5.5).</para>
/// <para><b>تَرتيبُ الأَبعاد</b> (§5.4): مِن التَغييرِ أَوَّلاً (المَتجَرُ = <c>Deal.TenantSlug</c>)، ثُمَّ مِن الحامِلِ <b>إن كانَ لِلمَتجَرِ نَفسِه</b>
/// أَو كانَ حامِلَ البَذر، وإلّا <see cref="MeterSentinels.Unresolved"/>.</para>
/// <para><b>الوَحدَةُ اليَوم واحِدَة</b> (الهَيكَلُ الماشي، القاعِدَة ٩): <c>deal.create</c> — صَفقَةٌ وُلِدَت في هذا الحِفظ. وتَحديثُها لا يُعَدّ
/// (الانتِقالاتُ وَحدَةٌ لاحِقَة، §5.6).</para>
/// <para>‏(‏2026-11-12، W13-6·٧ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) <b>وصارَت الوَحَداتُ خَمساً</b>: وَحَداتُ الأَحداثِ الأَربَع (التَصميم §3.4 و§5.2) —
/// حَدَثٌ بِنَوعِه في مَجرىً <b>يَبدَأُ</b> في هذا الحِفظ (<c>StreamAction.Start</c>) سَطرٌ واحِد: <c>ListingCreated</c> ← <c>listing.create</c>،
/// و<c>OfferSubmitted</c> ← <c>offer.submit</c>، و<c>TicketCreated</c> ← <c>ticket.open</c>، و<c>SubscriptionCreated</c> ← <c>member_plan.subscribe</c>.
/// والمَتجَرُ <b>مُستَأجِرُ المَجرى</b> (وإلّا مُستَأجِرُ الجَلسَة، و<c>*DEFAULT*</c> أَو الفارِغُ <c>_unresolved</c>)، والمَصدَرُ <b>مُعَرِّفُ الحَدَث</b>
/// (<c>mt_events.id</c>) لا مُعَرِّفُ مَن فَعَلَه — فَإعادَةُ الحِفظِ بِالحَدَثِ نَفسِه لا تُضاعِف، وبِدءُ مَجرىً قائِمٍ يَرتَدُّ بِسَطرِه. والإلحاقُ بِمَجرىً قائِمٍ
/// لا يُعَدّ (تَحريرٌ أَو انتِقال، §5.6)، واللَقطَةُ المُباشِرَةُ الَّتي يَكتُبُها الإسقاطُ مُشتَقَّةٌ لا تُعَدّ (§5.3).</para>
/// <para>‏(‏2026-11-12، W13-6·٨ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) <b>وصارَت تِسعاً بِوَحَداتِ الوَثائِقِ الأَربَع</b> (التَصميم §3.4 و§5.2):
/// <c>store.create</c> — <c>Tenant</c> في <c>Inserts</c> (الوِلاداتُ الثَلاثُ في البَذرِ قُلِبَت <c>Insert</c> في هذا البَندِ خَلفَ تَوصيفِها)، مَتجَرُه
/// <c>Tenant.Id</c> ورائِدُ أَعمالِه <b>مِن التَغيير</b> <c>Tenant.OwnerUserId</c> (و<c>_unowned</c> لِلفارِغ)؛ و<c>studio.account.create</c> — <c>StudioUser</c>
/// بِلا نُسخَة، مَتجَرُه <c>_platform</c> ورائِدُ أَعمالِه الحِسابُ نَفسُه؛ و<c>member.join</c> — <c>User</c> بِلا نُسخَة، مَتجَرُه مُستَأجِرُ الجَلسَة
/// (الوَثيقَةُ مُقتَرِنَة)، <b>ومَصدَرُه اسمٌ مُستَعار</b> (<see cref="UsageEntryId.Pseudonym"/>) لا مُعَرِّفُ الشَخص؛ و<c>review.create</c> — <c>Review</c>
/// في <c>Inserts</c>، مَتجَرُه مُستَأجِرُ الجَلسَة ومَصدَرُه مُعَرِّفُ التَقييم. و«في <c>Inserts</c>» يُقَرِّرُه المُستَمِعُ لا المُصَنِّف: يُمَرِّرُ وَثيقَةَ
/// الإدراجِ وِلادَةً، و<c>Store</c> نَوعٍ بِلا تَزامُنٍ تَفاؤُلِيٍّ لا يَبلُغُه أَصلاً.</para>
/// <para>‏(‏2026-11-12، W13-6·٨) <b>والدَورُ مِن حامِلٍ بِلا مَتجَر</b>: حامِلٌ لَم يَملَأه مَتجَرٌ ويَحمِلُ دَوراً (مُشرِفُ المَنَصَّة، <c>FillPlatformRole</c>)
/// دَورُه لِكُلِّ كِتابَةٍ في طَلَبِه — ورائِدُ الأَعمالِ لا يُؤخَذُ مِنه أَبَداً (<see cref="FromScope"/>).</para>
/// </summary>
public static class MeterRules
{
    public const string DealCreate = "deal.create";

    /// <summary>نَوعُ مَصدَرِ <c>deal.create</c> — ومُعَرِّفُه مُعَرِّفُ الصَفقَة (لا مُعَرِّفُ شَخص).</summary>
    public const string DealSource = "deal";

    public const string ListingCreate = "listing.create";
    public const string OfferSubmit = "offer.submit";
    public const string TicketOpen = "ticket.open";
    public const string MemberPlanSubscribe = "member_plan.subscribe";

    /// <summary>نَوعُ مَصدَرِ وَحَداتِ الأَحداث — ومُعَرِّفُه مُعَرِّفُ الحَدَثِ في <c>mt_events</c> (لا مُعَرِّفُ المُعلِنِ ولا العارِضِ ولا العُضو).</summary>
    public const string EventSource = "event";

    // ‏(‏2026-11-12، W13-6·٨) وَحَداتُ الوَثائِقِ الأَربَع ومَصادِرُها.
    public const string StoreCreate = "store.create";
    public const string StudioAccountCreate = "studio.account.create";
    public const string MemberJoin = "member.join";
    public const string ReviewCreate = "review.create";

    /// <summary>مَصدَرُ <c>store.create</c> — ومُعَرِّفُه مُعَرِّفُ المَتجَر (اسمُه، لا شَخص).</summary>
    public const string TenantSource = "tenant";

    /// <summary>مَصدَرُ <c>studio.account.create</c> — ومُعَرِّفُه الحِسابُ نَفسُه، وهُوَ رائِدُ الأَعمالِ المَحفوظُ صَريحاً (التَصميم §2، س٩).</summary>
    public const string StudioUserSource = "studio_user";

    /// <summary>مَصدَرُ <c>member.join</c> — ومُعَرِّفُه <b>اسمٌ مُستَعار</b> لِمُعَرِّفِ العُضو (<see cref="UsageEntryId.Pseudonym"/>)، لا المُعَرِّفُ نَفسُه.</summary>
    public const string UserSource = "user";

    /// <summary>مَصدَرُ <c>review.create</c> — ومُعَرِّفُه مُعَرِّفُ التَقييم (لا الكاتِبُ ولا المُقَيَّم).</summary>
    public const string ReviewSource = "review";

    /// <summary><b>الحَدَثُ الَّذي يولَدُ بِه مَجراه وَحدَةً</b> — بِالنَوعِ لا بِاسمِه (إعادَةُ تَسمِيَةٍ تَكسِرُ البِناء). وقارِئُ مُطابَقَةِ كُلِّ وَحدَةٍ في المُعجَمِ
    /// <c>event_ids:{النَوع}</c> بِهذا النَوعِ نَفسِه (<c>MeterRulesTests</c>).</summary>
    public static IReadOnlyDictionary<Type, string> StreamBirths { get; } = new Dictionary<Type, string>
    {
        [typeof(ListingCreated)] = ListingCreate,
        [typeof(OfferSubmitted)] = OfferSubmit,
        [typeof(TicketCreated)] = TicketOpen,
        [typeof(SubscriptionCreated)] = MemberPlanSubscribe,
    };

    /// <summary><b>المَجاري المُعَلَّقَةُ قِيَماً</b> — ما يُمَرِّرُه المُستَمِعُ مِن <c>PendingChanges.Streams()</c> بِلا قِراءَة: المُستَأجِرُ وبِدءُ المَجرى ومُعَرِّفُ
    /// كُلِّ حَدَثٍ وحِملُه. ويَبني بِه حارِسُ التَصنيفِ (G3) مُدخَلَه نَفسَه، فَما يُقاسُ هُوَ ما يَراه المُستَمِع.</summary>
    public static IReadOnlyList<PendingStream> StreamsOf(IEnumerable<StreamAction> actions)
        => [.. actions.Select(a => new PendingStream(a.TenantId, a.ActionType == StreamActionType.Start,
                                                     [.. a.Events.Select(e => new PendingEvent(e.Id, e.Data))]))];

    public static IReadOnlyList<UsageEntry> Classify(PendingSnapshot pending, MeterScopeView? scope, MeterUnitCatalog catalog)
    {
        var entries = new List<UsageEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in pending.Documents)
        {
            if (!p.Created) continue;
            var entry = p.Document switch
            {
                Deal deal when catalog.Find(DealCreate) is { } unit
                    => Entry(unit, string.IsNullOrWhiteSpace(deal.TenantSlug) ? MeterSentinels.Unresolved : deal.TenantSlug,
                             DealSource, deal.Id.ToString("D"), scope),
                Tenant tenant when catalog.Find(StoreCreate) is { } unit
                    => StoreEntry(unit, tenant, scope),
                StudioUser account when catalog.Find(StudioAccountCreate) is { } unit
                    => Entry(unit, MeterSentinels.Platform, StudioUserSource, account.Id.ToString("D"), scope, entrepreneur: account.Id.ToString("D")),
                User member when catalog.Find(MemberJoin) is { } unit
                    => MemberEntry(unit, SessionApp(pending.SessionTenant), member, scope),
                Review review when catalog.Find(ReviewCreate) is { } unit
                    => Entry(unit, SessionApp(pending.SessionTenant), ReviewSource, review.Id.ToString("D"), scope),
                _ => null,
            };
            if (entry is not null && seen.Add(entry.Id)) entries.Add(entry);
        }
        foreach (var stream in pending.Streams ?? [])
        {
            if (!stream.Started) continue;
            var app = StreamApp(stream.Tenant, pending.SessionTenant);
            foreach (var ev in stream.Events)
            {
                if (ev.Data is null || !StreamBirths.TryGetValue(ev.Data.GetType(), out var code) || catalog.Find(code) is not { } unit) continue;
                var entry = Entry(unit, app, EventSource, ev.Id.ToString("D"), scope);
                if (seen.Add(entry.Id)) entries.Add(entry);
            }
        }
        return entries;
    }

    /// <summary>مَتجَرُ سَطرِ الحَدَث: مُستَأجِرُ المَجرى، وإن غابَ فَمُستَأجِرُ الجَلسَة (الإيداعُ المُقتَرِنُ يَكتُبُ الحَدَثَ بِه)؛ والمُستَأجِرُ الافتِراضِيُّ
    /// (<c>*DEFAULT*</c>) والفارِغُ لَيسا مَتجَراً ← <see cref="MeterSentinels.Unresolved"/> — يُعَدُّ ويُعرَضُ ولا يَرمي.</summary>
    public static string StreamApp(string? streamTenant, string? sessionTenant)
    {
        var tenant = string.IsNullOrWhiteSpace(streamTenant) ? sessionTenant : streamTenant;
        return string.IsNullOrWhiteSpace(tenant) || string.Equals(tenant, JasperFx.StorageConstants.DefaultTenantId, StringComparison.Ordinal)
            ? MeterSentinels.Unresolved : tenant;
    }

    /// <summary>‏(‏2026-11-12، W13-6·٨) مَتجَرُ وَثيقَةٍ مُقتَرِنَة: مُستَأجِرُ الجَلسَةِ — بِقاعِدَةِ <see cref="StreamApp"/> نَفسِها بِلا مَجرى.</summary>
    public static string SessionApp(string? sessionTenant) => StreamApp(null, sessionTenant);

    /// <summary>‏(‏2026-11-12، W13-6·٨) سَطرُ <c>store.create</c>: المَتجَرُ والمَصدَرُ مُعَرِّفُ المَتجَر (والفارِغُ <c>_unresolved</c> فيهِما)، ورائِدُ الأَعمالِ مالِكُه
    /// مِن الوَثيقَةِ نَفسِها (<see cref="MeterScope.OwnerOf"/>) — لا مِن الحامِل: المَتجَرُ يولَدُ في طَلَبٍ لا مَتجَرَ فيه بَعد.</summary>
    private static UsageEntry StoreEntry(MeterUnit unit, Tenant tenant, MeterScopeView? scope)
    {
        var slug = string.IsNullOrWhiteSpace(tenant.Id) ? MeterSentinels.Unresolved : tenant.Id;
        return Entry(unit, slug, TenantSource, slug, scope, entrepreneur: MeterScope.OwnerOf(tenant.OwnerUserId));
    }

    /// <summary>‏(‏2026-11-12، W13-6·٨) سَطرُ <c>member.join</c>: المَصدَرُ <b>اسمٌ مُستَعارٌ</b> لِمُعَرِّفِ العُضو، فَلا يَدخُلُ مُعَرِّفُ الشَخصِ الدَفتَرَ لا في المُعَرِّفِ
    /// ولا في المَصدَر (التَصميم §2، G17).</summary>
    private static UsageEntry MemberEntry(MeterUnit unit, string app, User member, MeterScopeView? scope)
        => Entry(unit, app, UserSource, UsageEntryId.Pseudonym(unit.Code, app, member.Id.ToString("D")), scope);

    /// <param name="entrepreneur">‏(‏2026-11-12، W13-6·٨) رائِدُ الأَعمالِ <b>مِن التَغيير</b> حينَ تَحمِلُه الوَثيقَة (<c>Tenant.OwnerUserId</c>، الحِسابُ نَفسُه) —
    /// يَسبِقُ الحامِلَ كَما يَسبِقُه المَتجَر (التَصميم §5.4، الخُطوَةُ ١)؛ و<c>null</c>: مِن الحامِل.</param>
    private static UsageEntry Entry(MeterUnit unit, string app, string sourceType, string sourceId, MeterScopeView? scope, string? entrepreneur = null)
    {
        var (holderEntrepreneur, role) = FromScope(app, scope);
        entrepreneur ??= holderEntrepreneur;
        var feature = string.IsNullOrWhiteSpace(unit.Feature) || MeterFeatureAnchors.IsAnchor(unit.Feature)
            ? MeterSentinels.Unresolved : unit.Feature;
        return new UsageEntry(UsageEntryId.For(unit.Code, app, sourceId), unit.Code, 1, app, entrepreneur, role,
                              feature, MeterSentinels.None, sourceType, sourceId);
    }

    /// <summary>رائِدُ الأَعمالِ والدَورُ مِن الحامِل — <b>فَقَط</b> إن كانَ لِلمَتجَرِ نَفسِه أَو حامِلَ بَذر؛ فَنِطاقُ المَتجَرِ أ لا يَنسِبُ كِتابَةَ ب.
    /// ‏(‏2026-11-12، W13-6·٨ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) <b>وحامِلٌ بِلا مَتجَرٍ يَحمِلُ دَوراً</b> (فاعِلُ المَنَصَّة: <c>FillPlatformRole</c>) دَورُه لِكُلِّ مَتجَرٍ
    /// يَكتُبُ فيه طَلَبُه — ورائِدُ أَعمالِه لا يُؤخَذُ أَبَداً، فَهُوَ لا يَملِكُه إلّا مَن مَلَأَه بِمَتجَرِه.</summary>
    public static (string Entrepreneur, string Role) FromScope(string app, MeterScopeView? scope)
    {
        var applies = scope is not null
            && (scope.IsSeed || (scope.App is not null && string.Equals(scope.App, app, StringComparison.OrdinalIgnoreCase)));
        var platformActor = scope is { IsSeed: false, App: null } && !string.IsNullOrEmpty(scope.Role);
        return (applies && !string.IsNullOrEmpty(scope!.Entrepreneur) ? scope.Entrepreneur : MeterSentinels.Unresolved,
                (applies || platformActor) && !string.IsNullOrEmpty(scope!.Role) ? scope.Role : MeterSentinels.Unresolved);
    }
}
