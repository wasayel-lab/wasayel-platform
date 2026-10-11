using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.Deals;

/// <summary>
/// خِدمَة الـ Deals — البَوّابَة الوَحيدَة لِتَغيير الحالَة. كُلّ المُتَطَلَّبات
/// تَمُرّ هُنا: فَحص الـ state machine، التَّحَقُّق مِن الفاعِل، الكِتابَة في
/// Timeline، حِفظ بِـ Marten. يُمكِن استِخدامُها مِن endpoints أَو
/// Wolverine handlers لاحِقاً.
/// </summary>
public sealed class DealsService
{
    private readonly IDocumentStore _store;
    private readonly ACommerce.Kit.Payments.IPaymentProvider _payments;
    public DealsService(IDocumentStore store, ACommerce.Kit.Payments.IPaymentProvider payments)
    {
        _store = store;
        _payments = payments;
    }

    /// <summary>مِفتاحُ مَرجِعِ مالِكِ الإعلان في <see cref="Deal.Refs"/> —
    /// يُلحِقُه مَسارُ إنشاءِ العَرض. مُعَرَّفٌ هُنا لِأَنّ الكاتِبَ
    /// والقارِئَ يَجِبُ أَن يَقرَآ نَفسَ الحَرف.</summary>
    public const string ListingOwnerRef = "listing_owner";

    /// <summary><para><b>عَرضٌ وارِدٌ لَم يُقبَل بَعد، ومالِكُ إعلانِه هو
    /// <paramref name="ownerId"/></b> — دالَّةٌ <b>نَقِيَّة</b> فَتُختَبَر
    /// بِلا قاعِدَة بَيانات، بِمُوجَبٍ وسالِب.</para>
    ///
    /// <para><b>ولِماذا دالَّةٌ لا سَطرٌ في الاستِعلام</b>: الشَرطُ نَفسُه
    /// كانَ مَكتوباً مَرَّةً في صَفحَة «صَفقاتي» فَوقَ قائِمَةٍ **تَستَثني
    /// بِبِنيَتِها** ما يَبحَثُ عَنه، فَما طابَقَ قَطّ ولا أَحمَرَّ
    /// شَيء. تَعريفٌ واحِدٌ مَقيسٌ يَمنَعُ تَكرارَ ذلك.</para></summary>
    public static bool IsIncomingOfferFor(Deal deal, Guid ownerId) =>
        deal.CounterpartyId is null
        && deal.InitiatorId != ownerId
        // بِقيمَةِ المُعَرِّفِ لا بِحَرفِ النَصّ — نَفسُ مُقارَنَةِ `Validate` والشاشَة
        // (‏ADR-033 §٦)؛ وكانَت `lo == ownerId.ToString()`.
        && DealAcceptAuthorization.OwnerIdOf(deal) == ownerId;

    /// <summary><para>قائِمَة الصَّفقات على إعلانات يَملِكها مُستَخدِم
    /// مُعَيَّن (لِعَرض «عُروض على إعلاناتي»).</para>
    ///
    /// <para><b>وشَطرانِ لا شَطر، وهذا هُوَ العَطَبُ الَّذي أُصلِح</b>:
    /// الصَّفقَةُ المَقبولَة يَحمِلُها <c>CounterpartyId</c>، أَمّا
    /// <b>العَرضُ الوارِدُ قَبلَ القَبول</b> فَـ<c>CounterpartyId</c>‏ه
    /// <c>null</c> ومالِكُه في <c>Refs[listing_owner]</c> وَحدَه. وكانَ
    /// الشَطرُ الثاني مَوصوفاً في تَعليقِ هذِه الدالَّة و<b>غَيرَ
    /// مَكتوبٍ في جِسمِها</b>، فَلَم يَرَ مالِكُ إعلانٍ عَرضاً وارِداً
    /// في أَيّ شاشَة — لا «صَفقاتي» ولا «عُروضي» ولا الإشعارات.</para>
    ///
    /// <para><b>والفَلتَرَةُ في الذاكِرَة نُقلَةٌ لا اختِيار</b>: ‏Marten
    /// لا يُفَلتِر <c>Dictionary</c> في LINQ — نَفسُ سَبَبِ
    /// <c>AccountQueries.MyListingsAsync</c> ونَفسُ شَكلِه.</para></summary>
    public async Task<List<Deal>> ListForListingOwnerAsync(
        string tenantSlug, Guid ownerId, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);
        // الشَطرُ الأَوَّل: صَفقاتٌ صارَ المالِكُ طَرَفَها المُقابِل.
        var settled = await s.Query<Deal>()
            .Where(d => d.CounterpartyId == ownerId)
            .OrderByDescending(d => d.UpdatedAt).Take(100).ToListAsync(ct);

        // الشَطرُ الثاني: عُروضٌ وارِدَةٌ لَم تُقبَل بَعد — بِلا طَرَفٍ
        // ثانٍ، ومالِكُ إعلانِها أَنا، ولَستُ أَنا مَن بادَرَ بِها.
        var pending = (await s.Query<Deal>()
            .Where(d => d.CounterpartyId == null && d.InitiatorId != ownerId)
            .OrderByDescending(d => d.UpdatedAt).Take(200).ToListAsync(ct))
            .Where(d => IsIncomingOfferFor(d, ownerId));

        return settled
            .Concat(pending.Where(p => settled.All(x => x.Id != p.Id)))
            .OrderByDescending(d => d.UpdatedAt)
            .Take(100)
            .ToList();
    }

    public async Task<Deal> StartAsync(
        string tenantSlug, string pattern,
        Guid initiatorId, string initiatorName,
        Guid? listingId, string listingTitle, decimal amountSar,
        Dictionary<string, string>? attributes = null,
        CancellationToken ct = default)
    {
        var deal = new Deal
        {
            Id = Guid.NewGuid(),
            TenantSlug = tenantSlug,
            Pattern = pattern,
            ListingId = listingId,
            ListingTitle = listingTitle,
            InitiatorId = initiatorId,
            InitiatorName = initiatorName,
            AmountSar = amountSar,
            Attributes = attributes ?? new(),
            Stage = DealStage.Offered,
            Status = DealStatus.Active,
            Timeline = new()
            {
                new(DealStage.Offered, DealStage.Offered, "created",
                    initiatorId, initiatorName, null, DateTime.UtcNow)
            }
        };
        await using var s = _store.LightweightSession(tenantSlug);
        s.Store(deal);
        await s.SaveChangesAsync(ct);
        return deal;
    }

    /// <summary>عَيِّن الطَّرَف الثاني (الـ counterparty) عَلى الـ Deal —
    /// عادَةً عِندَ مَرحَلَة Booked (بائِع يَقبَل، سائِق يَأخُذ الرحلَة، …).
    /// <paramref name="action"/> اسمُ سَطرِ السِجِلّ: <c>assigned</c> لِلقَبولِ
    /// والمَسارِ الإداريّ، و<see cref="DealEvent.BoundAtBirth"/> لِرَبطِ البائِعِ عِندَ
    /// وِلادَةِ صَفقَةِ السَلَّة (‏ADR-033 §٦) — فَلا تُسَمّي الشاشَةُ رَبطاً «قُبِلَ
    /// العَرض». (بَعدَ <paramref name="ct"/> عَمداً: نِداءاتُ القَبولِ والمَسارِ
    /// الإداريِّ المُثَبَّتَةُ حَرفاً لا تَتَحَرَّك.)</summary>
    public async Task<Deal?> AssignCounterpartyAsync(
        string tenantSlug, Guid dealId,
        Guid counterpartyId, string counterpartyName,
        CancellationToken ct = default, string action = "assigned")
    {
        await using var s = _store.LightweightSession(tenantSlug);
        var deal = await s.LoadAsync<Deal>(dealId, ct);
        if (deal is null || deal.Status != DealStatus.Active) return deal;
        if (deal.CounterpartyId is not null) return deal;
        deal.CounterpartyId = counterpartyId;
        deal.CounterpartyName = counterpartyName;
        deal.UpdatedAt = DateTime.UtcNow;
        deal.Timeline.Add(new(deal.Stage, deal.Stage, action,
            counterpartyId, counterpartyName, null, DateTime.UtcNow));
        s.Store(deal);
        await s.SaveChangesAsync(ct);
        return deal;
    }

    /// <summary>
    /// <para><b>البائِعُ طَرَفٌ مُقابِلٌ مِن الوِلادَة</b> (‏ADR-033 §٦، ‏2026-10-11):
    /// يُنادِيها <c>/checkout/submit</c> بَعدَ إلحاقِ <c>listing_owner</c>، فَتَربُطُ
    /// مالِكَ الإعلانِ طَرَفاً بِاسمِه مِن وَثيقَتِه وتَكتُبُ سَطرَ
    /// <see cref="DealEvent.BoundAtBirth"/> — لا <c>assigned</c>، فَالقَبولُ لَم يَقَع
    /// بَعد. ومَرجِعٌ لَيسَ مُعَرِّفاً لا يَربُطُ أَحَداً ويُرجِعُ <c>null</c>؛ ووَثيقَةُ
    /// مُستَخدِمٍ غائِبَةٌ تُربَطُ بِالهُوِيَّةِ وبِلا اسم — الهُوِيَّةُ مِن الإعلانِ
    /// وهي الحُكم، والاسمُ زينَةُ عَرض.</para>
    ///
    /// <para><b>ولِماذا هُنا لا في جِسمِ النُقطَة</b>: تَحليلُ المُعَرِّفِ وقِراءَةُ
    /// الاسمِ حِسابٌ، وجِسمُ النُقطَةِ مُثَبَّتُ السَقف (<c>EndpointBodyBleedTests</c>).
    /// وتَمُرُّ بِـ<see cref="AssignCounterpartyAsync"/> لا حَولَها — فَعَدُّ «مَن
    /// يُعَيِّنُ طَرَفاً» في <c>DealCounterpartyCharacterizationTests</c> يَراها.</para>
    ///
    /// <para><b>إلّا أَن يَكونَ المالِكُ هُوَ المُبادِرَ نَفسَه — فَلا يُربَطُ أَحَد</b> (المُراجَعَةُ
    /// الثالِثَةُ عَلى <c>8769fa4</c>، ‏2026-10-11؛ ‏ADR-033 §٦‑ب‑١ مُلحَقاً): كانَت تَربُطُه ولَو
    /// اشتَرى إعلانَه هُوَ — وبابُ ذلك طَلَبٌ مَصنوعٌ إلى <c>cart/add</c> الَّذي لا يَقرَأُ
    /// <c>owner_id</c> — فَتولَدُ صَفقَةٌ طَرَفاها واحِدٌ يَمشيها وَحدَه إلى <c>Completed</c> ثُمَّ
    /// يَصيرُ هَدَفَ تَقييمِ نَفسِه، خِلافَ «ولا يَكونُ المَرءُ طَرَفَي صَفقَتِه»
    /// (<see cref="DealAcceptAuthorization.ActorIsInitiator"/>). فَتولَدُ الآنَ بِلا طَرَفٍ كَما
    /// كانَت قَبلَ <c>8769fa4</c>: يُحَرِّكُ المُبادِرُ <c>Offered</c> ويَقِفُ عِندَ <c>Booked</c>،
    /// والقَبولُ يَرُدُّه بِـ<c>actor_is_initiator</c>. ويُعادُ ما قُرِئَ بِلا كِتابَة؛ ولَقطَةُ القِراءَةِ
    /// لا تَتَقادَمُ هُنا لِأَنّ المُبادِرَ لا يَتَغَيَّرُ بَعدَ الوِلادَة.</para>
    /// </summary>
    public async Task<Deal?> BindListingOwnerAsync(
        string tenantSlug, Guid dealId, string ownerRef, CancellationToken ct = default)
    {
        if (!Guid.TryParse(ownerRef, out var ownerId)) return null;
        await using var q = _store.QuerySession(tenantSlug);
        // ولا يَكونُ المَرءُ طَرَفَي صَفقَتِه — مالِكٌ هُوَ المُبادِرُ لا يُربَط (‏ADR-033 §٦‑ب‑١ مُلحَقاً).
        var deal = await q.LoadAsync<Deal>(dealId, ct);
        if (deal is null || deal.InitiatorId == ownerId) return deal;
        var owner = await q.LoadAsync<ACommerce.Kit.Auth.User>(ownerId, ct);
        return await AssignCounterpartyAsync(tenantSlug, dealId, ownerId, owner?.FullName ?? "", ct, DealEvent.BoundAtBirth);
    }

    /// <summary>
    /// <para><b>مالِكُ الإعلانِ يَقبَلُ العَرض</b> — يَصيرُ الطَرَفَ المُقابِلَ (إن
    /// لَم يَكُن رُبِطَ عِندَ الوِلادَة) وتَمضي الصَفقَةُ مِن <c>Offered</c>.
    /// <b>والحارِسُ في التَوقيع</b> (القاعِدَة ٦): <see cref="DealAcceptor"/>
    /// إلزامِيّ، ولا حِملٌ زائِدٌ بِـ<c>Guid actorId</c>؛ والقَرارُ كُلُّه في
    /// <see cref="DealAcceptAuthorization.Validate"/> النَقِيَّة، والجِسمُ
    /// يُطَبِّقُه ولا يَحمِلُه — عَلى نَسَقِ <see cref="CancelAsync"/>.</para>
    ///
    /// <para><b>والانتِقالُ بِهُوِيَّةِ القابِلِ لا بِاستِعارَةِ المُبادِر</b>
    /// (‏ADR-033 §٦، ‏2026-10-11): كانَ ما بَعدَ التَخويلِ يُنادي
    /// <see cref="AdvanceAsync"/> بِـ<c>deal.InitiatorId</c> بِاسمِ القابِل — لِأَنّ
    /// صَفَّ <c>Offered</c> في الكاتالوج يَطلُبُ <c>initiator</c> — ويُرجِعُ
    /// <c>Accepted</c> بِلا قِراءَةِ النَتيجَة. فَصارَ القَبولُ انتِقالاً لَه
    /// بَوّابَتُه: <c>Validate</c> أَثبَتَت أَنّ الفاعِلَ مالِكُ الإعلانِ وأَنّ
    /// الصَفقَةَ في <c>Offered</c> فَعّالَةً طَرَفُها هُوَ أَو لا طَرَفَ لَها، ثُمَّ
    /// <see cref="MoveAsync"/> نَفسُها الَّتي تَكتُبُ لِـ<c>AdvanceAsync</c> — بِفاعِلٍ
    /// هُوَ القابِل. وبَوّابَةُ فاعِلِ المَرحَلَةِ لا تُسأَلُ هُنا لِأَنّ سُؤالَها
    /// «مَن يُحَرِّكُ Offered بِلا قَبول» وجَوابُه المُبادِرُ كَما كان. ولا نَتيجَةَ
    /// تُهمَل: كُلُّ رَفضٍ رَمزٌ قَبلَ أَيِّ كِتابَة، و<c>Accepted</c> لا تُرجَعُ إلّا
    /// بَعدَ أَن حُفِظَ الانتِقال.</para>
    ///
    /// <para><b>وصارَت تُسأَلُ — ولا استِثناءَ لِلقَبولِ بَعدَ اليَوم</b> (‏ADR-033 §٦‑ط، ‏2026-10-11؛
    /// ولا يُعادُ حَرفٌ فَوقَه): «جَوابُه المُبادِر» كانَ إزاحَةَ مَرحَلَةٍ في قِراءَةِ عَمودِ الفاعِل —
    /// الخِدمَةُ تَسأَلُ فاعِلَ المَرحَلَةِ الحالِيَّة. والسُؤالُ اليَومَ فاعِلُ المَرحَلَةِ المَدخولَة:
    /// <c>Booked</c> = <c>counterparty</c>، والقابِلُ صارَ الطَرَفَ قَبلَه (بِالتَعيينِ أَو مِن الوِلادَة)،
    /// فَالقَبولُ يَمُرُّ مِن البَوّابَةِ نَفسِها الَّتي يَمُرُّ مِنها <see cref="AdvanceAsync"/>. وما بَقِيَ
    /// خاصّاً بِه لَيسَ استِثناءً مِن القاعِدَة بَل شَرطُها: مَن يَحِقُّ لَه أَن يَصيرَ الطَرَفَ
    /// (<c>Validate</c>)، وتَعيينُه حَيثُ لا طَرَف، وإشعارُ المُشتَري عِندَ النُقطَة.</para>
    ///
    /// <para><b>واللَقطَةُ قَد تَتَقادَم — فَيُعادُ الفَحصُ في جَلسَةِ الكِتابَة</b>
    /// (مُراجَعَةُ ‏2026-10-11؛ ‏ADR-033 §٦‑ب‑٢ مُلحَقاً): <c>Validate</c> الأولى تَفحَصُ لَقطَةَ
    /// جَلسَةِ قِراءَة، وما يُحفَظُ بَعدَها — إلغاءُ المُشتَري، أَو قَبولٌ ثانٍ مِن نَقرَةٍ
    /// مُزدَوِجَة — لا تَراه: فَكانَ <c>advanced</c> يُكتَبُ فَوقَ صَفقَةٍ مُلغاةٍ ويُرجَعُ
    /// <c>Accepted</c>، ونَقرَةٌ ثانِيَةٌ تُحَرِّكُ <c>Booked → Confirmed</c>. فَتُعادُ
    /// <c>Validate</c> نَفسُها عَلى الوَثيقَةِ الَّتي سَتُكتَب، قَبلَ كِتابَةِ الانتِقال،
    /// بِرَمزَيها القائِمَين — لا سابِعَ. <b>وما لا تُغلِقُه، ويُقال</b>: كِتابَةٌ تَقَعُ بَعدَ
    /// ذاكَ التَحميلِ وقَبلَ حِفظِه تَضيعُ بِآخِرِ كاتِب (لا تَزامُنَ تَفاؤُلِيَّ عَلى
    /// <c>Deal</c> — مَوجَةٌ لاحِقَةٌ بِقَرارِها)، فَقَبولانِ مُتَداخِلانِ تَماماً يُرجِعانِ
    /// <c>Accepted</c> مَرَّتَين؛ وفي العَرضِ بِلا طَرَفٍ تَسبِقُ كِتابَةُ التَعيينِ — عَلى صَفقَةٍ
    /// فَعّالَةٍ ساعَتَها — إعادَةَ الفَحص.</para>
    /// </summary>
    public async Task<DealAcceptResult> AcceptAsync(
        string tenantSlug, Guid dealId, DealAcceptor by, CancellationToken ct = default)
    {
        var loaded = await LoadAsync(tenantSlug, dealId, ct);

        var violation = DealAcceptAuthorization.Validate(loaded, by);
        if (violation is not null) return DealAcceptResult.Refused(violation, loaded);

        // بِلا أَثَرٍ حَيثُ رُبِطَ البائِعُ عِندَ الوِلادَة (صَفقَةُ السَلَّة) — ويُعَيِّنُ
        // القابِلَ حَيثُ لا طَرَفَ (عَرضٌ عَلى إعلان).
        await AssignCounterpartyAsync(tenantSlug, dealId, by.ActorId, by.ActorName, ct);

        await using var s = _store.LightweightSession(tenantSlug);
        // اللَقطَةُ أَعلاه مِن جَلسَةِ قِراءَةٍ وقَد تَقادَمَت: إلغاءٌ أَو قَبولٌ ثانٍ حُفِظَ بَعدَها
        // لا تَراه `Validate` الأولى. فَتُعادُ نَفسُها عَلى وَثيقَةِ هذِه الجَلسَةِ — الَّتي
        // سَتُكتَب — قَبلَ كِتابَةِ الانتِقال، بِرَمزَيها القائِمَين `deal_not_active` و
        // `deal_already_accepted` (‏ADR-033 §٦‑ب‑٢ مُلحَقاً، ‏2026-10-11).
        var fresh = await s.LoadAsync<Deal>(dealId, ct);
        var stale = DealAcceptAuthorization.Validate(fresh, by);
        if (stale is not null) return DealAcceptResult.Refused(stale, fresh);

        // ‏`Validate` عَلى هذِه الوَثيقَةِ رَدَّت العَدَمَ وغَيرَ الفَعّالَةِ وغَيرَ `Offered`، فَما هُنا
        // صَفقَةٌ فَعّالَةٌ في `Offered` ولَها تالٍ في كُلِّ تَعريفٍ يَعرِفُه الكاتالوج (`Booked`) —
        // فَالعَدَمُ خَرقُ ثابِتٍ لا حالَةُ مُستَخدِم، ولا يُلَبَّسُ رَمزاً مِن مَعجَمِ التَخويل. (وكانَ
        // هذا التَعليقُ يَقولُ ذلك عَن لَقطَةِ القِراءَة، وهي لا تَضمَنُه — مُراجَعَةُ ‏2026-10-11.)
        var deal = fresh!;
        var next = DealsPolicy.Next(deal)
            ?? throw new InvalidOperationException($"‏Offered بِلا تالٍ في النَمَط «{deal.Pattern}» — خَرقُ ثابِتِ الكاتالوج.");
        // والبَوّابَةُ نَفسُها الَّتي تَسأَلُها `AdvanceAsync` — فاعِلُ المَرحَلَةِ المَدخولَة (`Booked` =
        // `counterparty`) — لا استِثناءَ لِلقَبول (‏ADR-033 §٦‑ط). `Validate` عَلى هذِه الوَثيقَةِ أَثبَتَت أَنّ
        // طَرَفَها القابِلُ أَو لا طَرَف، والتَعيينُ أَعلاه كَتَبَه — فَلا تَرُدُّ إلّا إن مَحَت كِتابَةٌ ضائِعَةٌ
        // تَعيينَه (لا تَزامُنَ تَفاؤُلِيَّ عَلى `Deal`، §٦‑ب‑٢): خَرقُ ثابِتٍ يُرمى، ولا تُكتَبُ `Booked` بِلا طَرَف.
        if (!IsActorAllowed(deal, by.ActorId, DealsPolicy.RequiredActorToLeave(deal)!))
            throw new InvalidOperationException($"القابِلُ لَيسَ طَرَفَ الصَفقَةِ «{deal.Id}» بَعدَ تَعيينِه — خَرقُ ثابِت.");
        return DealAcceptResult.Accepted(await MoveAsync(s, deal, next, by.ActorId, by.ActorName, "قُبِلَ العَرض", ct));
    }

    /// <summary>اِنتَقِل لِلمَرحَلَة التالِيَة في النَّمَط. يَفحَص أَنّ الفاعِل
    /// يَملِك حَقّ <b>دُخولِ المَرحَلَةِ التالِيَة</b> (initiator/counterparty/either) —
    /// <see cref="DealsPolicy.RequiredActorToLeave(Deal)"/>، فاعِلُ التالي لا فاعِلُ الحالِيَّة
    /// (‏ADR-033 §٦‑ط، ‏2026-10-11؛ كانَ يَسأَلُ فاعِلَ الحالِيَّة فَيُحَرِّكُ المُشتَري طَلَبَه إلى
    /// «حَجز» ويُعلِنُ «شَحن»). والتالي بِوَعيِ طَريقَةِ الدَفع (<see cref="DealsPolicy.Next(Deal)"/>):
    /// صَفقَةُ النَقدِ لا تَزورُ <c>Paid</c> (‏ADR-033 §٦).</summary>
    public async Task<DealAdvanceResult> AdvanceAsync(
        string tenantSlug, Guid dealId, Guid actorId, string actorName,
        string? note = null, CancellationToken ct = default)
    {
        await using var s = _store.LightweightSession(tenantSlug);
        var deal = await s.LoadAsync<Deal>(dealId, ct);
        if (deal is null) return new(false, null, "deal not found");
        if (deal.Status != DealStatus.Active)
            return new(false, deal, $"الصَّفقَة في حالَة {deal.Status} — لا يُمكِن المُتابَعَة.");

        var next = DealsPolicy.Next(deal);
        if (next is null)
            return new(false, deal, "هذه آخِر مَرحَلَة، لا تالٍ.");

        // فَحص الفاعِل: مَن يَدخُلُ المَرحَلَةَ التالِيَة؟ — فاعِلُها لا فاعِلُ الحالِيَّة (‏ADR-033 §٦‑ط)،
        // بِالدالَّةِ الَّتي تَقرَؤُها الشاشَةُ والـAPI؛ ولا تُرجِعُ `null` وثَمَّ تالٍ.
        var required = DealsPolicy.RequiredActorToLeave(deal)!;
        if (!IsActorAllowed(deal, actorId, required))
            return new(false, deal, $"يَتَطَلَّب فاعِلاً مِن نَوع: {required}");

        return new(true, await MoveAsync(s, deal, next.Value, actorId, actorName, note, ct), null);
    }

    /// <summary>
    /// <para><b>الانتِقالُ نَفسُه بَعدَ أَن حُسِمَ حَقُّ الفاعِل</b> — يَكتُبُ
    /// المَرحَلَةَ والسِجِلَّ، ويَقبِضُ عِندَ <c>Paid</c>، ويُسَجِّلُ القَبضَ النَقديَّ
    /// عِندَ <c>Delivered</c>، ويُكمِلُ عِندَ <c>Reviewed</c>، ويَحفَظ. مُشتَرَكٌ بَينَ
    /// بَوّابَتَين: <see cref="AdvanceAsync"/> (فاعِلُ المَرحَلَةِ مِن الكاتالوج) و
    /// <see cref="AcceptAsync"/> (<c>DealAcceptAuthorization</c>) — فَكِتابَةُ الحالَةِ
    /// في مَوضِعٍ واحِدٍ وإن تَعَدَّدَت أَبوابُها (‏ADR-033 §٦). ولا يَرُدُّ
    /// نَتيجَةً ذاتَ عَلَم: ما يُرفَضُ يُرفَضُ عِندَ البَوّابَةِ قَبلَه، فَعَلَمٌ هُنا
    /// شَرطٌ لا يَكذِبُ أَبَداً.</para>
    /// </summary>
    private async Task<Deal> MoveAsync(
        IDocumentSession s, Deal deal, DealStage next, Guid actorId, string actorName,
        string? note, CancellationToken ct)
    {
        var before = deal.Stage;
        deal.Stage = next;
        deal.UpdatedAt = DateTime.UtcNow;
        deal.Timeline.Add(new(before, next, "advanced",
            actorId, actorName, note, DateTime.UtcNow));

        // عِندَ الوُصول لِمَرحَلَة الدَّفع: اِسحَب الإذن المَحجوز في
        // /checkout/submit (Authorize) → Capture.
        //
        // **ولا عَمولَةَ مَنصَّة هُنا بَعدَ اليَوم** (‏ADR-032 §٢‑ب، ‏2026-10-11):
        // كانَت هذِه الكُتلَةُ تَحسُبُ `CommissionSar = 2.5%` وتُلحِقُ سَطرَ
        // «عمولة» بِاسمِ «المَنصَّة» عَلى كُلِّ صَفقَةٍ تَبلُغُ Paid — بِما فيها
        // النَقد — وإيرادُ وَسايِل اشتِراكُ المُستَأجِرِ وَحدَه (‏ق‑٦). فَأُسقِطَت
        // النِسبَةُ والحَقلُ والسَطر، **وبَقِيَ القَبضُ كَما هُوَ بِحَرفِه**:
        // حارِسُ `AmountSar > 0` كانَ يَحرُسُ الكُتلَةَ كُلَّها فَبَقِيَ عَلى
        // القَبض؛ أَمّا `CommissionSar is null` فَكانَ حارِسَ «مَرَّةً لا
        // مَرَّتَين» لِلعَمولَة وَحدَها، والمَرحَلَةُ تُدخَلُ مَرَّةً في
        // مَكينَةِ الحالَةِ — فَلا أَثَرَ لِسُقوطِه عَلى القَبض.
        //
        // لَو لا payment_id (دَفع نَقدا/COD) نَتَجاوَز.
        // فَشَل Capture لا يَكسِر التَّقَدُّم — يُسَجَّل لِلمُراجَعَة.
        if (next == DealStage.Paid && deal.AmountSar > 0)
        {
            if (deal.Refs.TryGetValue("payment_id", out var pid) && !string.IsNullOrEmpty(pid))
            {
                try
                {
                    var cr = await _payments.CaptureAsync(pid, deal.AmountSar, ct);
                    deal.Refs["payment_status"] = cr.Status.ToString();
                    deal.Timeline.Add(new(next, next, "captured",
                        actorId, actorName,
                        $"capture {cr.Status} {cr.AmountSar:0.00} SAR", DateTime.UtcNow));
                }
                catch (Exception ex)
                {
                    deal.Refs["capture_error"] = ex.Message;
                    deal.Timeline.Add(new(next, next, "capture_failed",
                        actorId, actorName, ex.Message, DateTime.UtcNow));
                }
            }
        }

        // نَقدٌ عِندَ الاستِلام: بُلوغُ Delivered هُوَ لَحظَةُ القَبضِ عِندَ البائِع
        // — يُكتَبُ سَطرٌ بِاسمِه في السِجِلّ، ونَصُّه مِن القامُوسِ عِندَ العَرض
        // (`deals.detail.event_cash_on_delivery`) لا هُنا (القاعِدَة ١١). ولا مالَ
        // يَتَحَرَّكُ: المَنصَّةُ لا تَقبِضُ ثَمَنَ الصَفقَةِ ولا تَرُدُّه (‏ق‑٦).
        // ‏ADR-033 §٦ — والمَرحَلَةُ `Paid` لَم تُزَر لِهذِه الصَفقَةِ أَصلاً.
        if (next == DealStage.Delivered && DealsPolicy.IsCashOnDelivery(deal))
            deal.Timeline.Add(new(next, next, DealEvent.CashOnDelivery,
                actorId, actorName, null, DateTime.UtcNow));

        // اكتمال الـ Deal عِندَ Reviewed.
        if (next == DealStage.Reviewed && DealsPolicy.StagesFor(deal.Pattern).Last() == DealStage.Reviewed)
        {
            deal.Status = DealStatus.Completed;
        }

        s.Store(deal);
        await s.SaveChangesAsync(ct);
        return deal;
    }

    /// <summary>
    /// <para><b>الحارِسُ في التَوقيع</b> (القاعِدَة ٦): الوَسيطُ
    /// <see cref="DealCanceller"/> إلزامِيٌّ ويَحمِل السُلطَة، ولا
    /// يوجَد حِملٌ زائِدٌ بِـ<c>Guid actorId</c> — فَلا يُمكِن نِداءُ
    /// الإلغاءِ بِلا تَصريح.</para>
    ///
    /// <para><b>وكانَت هذِه الدالَّةُ لا تَفحَصُ الفاعِلَ إطلاقاً</b>
    /// (‏§١١٫٦): أَيُّ مُستَخدِمٍ في المَتجَر كانَ يُلغي صَفقَةَ أَيّ
    /// أَحَد — <b>ومَعَ الإلغاءِ يَقَع <c>RefundAsync</c></b>. القَرارُ
    /// كُلُّه في <see cref="DealCancelAuthorization.Validate"/> النَقِيَّة،
    /// والجِسمُ يُطَبِّقُه ولا يَحمِلُه.</para>
    /// </summary>
    public async Task<DealCancelResult> CancelAsync(
        string tenantSlug, Guid dealId, DealCanceller by,
        string reason, CancellationToken ct = default)
    {
        var (actorId, actorName) = (by.ActorId, by.ActorName);
        await using var s = _store.LightweightSession(tenantSlug);
        var loaded = await s.LoadAsync<Deal>(dealId, ct);

        var violation = DealCancelAuthorization.Validate(loaded, by);
        if (violation is not null) return DealCancelResult.Refused(violation, loaded);
        // ‏`Validate` تَرُدّ `deal_not_found` عِندَ العَدَم، فَما بَعدَها غَيرُ عَدَم.
        var deal = loaded!;

        // إن كانَ في الصَّفقَة مَعامَلَة دَفع (Refs["payment_id"] مَوضوع
        // عِندَ /checkout/submit)، أَرجِع المَبلَغ كامِلاً قَبل تَعليم
        // الحالَة Cancelled. لَو الإسترِجاع فَشَل، لا نَكسِر الإلغاء —
        // نُسَجِّل خَطَأ في Timeline ونُسَلسِل (المُدير يَدخُل يَدَويّاً).
        if (deal.Refs.TryGetValue("payment_id", out var pid) && !string.IsNullOrEmpty(pid))
        {
            try
            {
                var rr = await _payments.RefundAsync(pid, deal.AmountSar, reason, ct);
                deal.Refs["refund_id"]     = rr.PaymentId;
                deal.Refs["refund_status"] = rr.Status.ToString();
                deal.Timeline.Add(new(deal.Stage, deal.Stage, "refunded",
                    actorId, actorName,
                    $"refund {rr.Status} {rr.AmountSar:0.00} SAR", DateTime.UtcNow));
            }
            catch (Exception ex)
            {
                deal.Refs["refund_error"] = ex.Message;
                deal.Timeline.Add(new(deal.Stage, deal.Stage, "refund_failed",
                    actorId, actorName, ex.Message, DateTime.UtcNow));
            }
        }

        deal.Status = DealStatus.Cancelled;
        deal.CancelReason = reason;
        deal.UpdatedAt = DateTime.UtcNow;
        deal.Timeline.Add(new(deal.Stage, deal.Stage, "cancelled",
            actorId, actorName, reason, DateTime.UtcNow));
        s.Store(deal);
        await s.SaveChangesAsync(ct);
        return DealCancelResult.Cancelled(deal);
    }

    public async Task<Deal?> DisputeAsync(
        string tenantSlug, Guid dealId, Guid actorId, string actorName,
        string reason, CancellationToken ct = default)
    {
        await using var s = _store.LightweightSession(tenantSlug);
        var deal = await s.LoadAsync<Deal>(dealId, ct);
        if (deal is null || deal.Status != DealStatus.Active) return deal;
        deal.Status = DealStatus.Disputed;
        deal.UpdatedAt = DateTime.UtcNow;
        deal.Timeline.Add(new(deal.Stage, deal.Stage, "disputed",
            actorId, actorName, reason, DateTime.UtcNow));
        s.Store(deal);
        await s.SaveChangesAsync(ct);
        return deal;
    }

    /// <summary>اِربِط كائِناً خارِجيّاً (مَثَلاً PaymentId بَعد الدَّفع).</summary>
    public async Task<Deal?> AttachRefAsync(
        string tenantSlug, Guid dealId, string key, string value,
        CancellationToken ct = default)
    {
        await using var s = _store.LightweightSession(tenantSlug);
        var deal = await s.LoadAsync<Deal>(dealId, ct);
        if (deal is null) return null;
        deal.Refs[key] = value;
        deal.UpdatedAt = DateTime.UtcNow;
        deal.Timeline.Add(new(deal.Stage, deal.Stage, "note", null, null,
            $"attach {key}={value}", DateTime.UtcNow));
        s.Store(deal);
        await s.SaveChangesAsync(ct);
        return deal;
    }

    public async Task<Deal?> LoadAsync(string tenantSlug, Guid dealId, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);
        return await s.LoadAsync<Deal>(dealId, ct);
    }

    public async Task<List<Deal>> ListForUserAsync(
        string tenantSlug, Guid userId, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);
        var all = await s.Query<Deal>()
            .Where(d => d.InitiatorId == userId || d.CounterpartyId == userId)
            .OrderByDescending(d => d.UpdatedAt)
            .Take(100).ToListAsync(ct);
        return all.ToList();
    }

    public async Task<List<Deal>> ListForTenantAsync(
        string tenantSlug, int take = 200, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);
        var all = await s.Query<Deal>()
            .OrderByDescending(d => d.UpdatedAt).Take(take).ToListAsync(ct);
        return all.ToList();
    }

    static bool IsActorAllowed(Deal deal, Guid actorId, string required) => required switch
    {
        "initiator"    => deal.InitiatorId == actorId,
        "counterparty" => deal.CounterpartyId == actorId,
        "either"       => deal.InitiatorId == actorId || deal.CounterpartyId == actorId,
        "platform"     => true,   // يَستَدعيها كود مَنصَّة — افتَرِض السَّماح
        _              => false
    };
}

public sealed record DealAdvanceResult(bool Ok, Deal? Deal, string? Reason);
