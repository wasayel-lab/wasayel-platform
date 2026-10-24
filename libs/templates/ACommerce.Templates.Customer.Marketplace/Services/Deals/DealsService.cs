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

    /// <summary>
    /// <para><b>مُلغي فاتورَةِ البَوّابَةِ بَعدَ الإلغاء</b> (‏2026-10-12، مُراجَعَةُ تَيّارِ S9 الثانِيَة؛ ‏ADR-032 §٨‑ز): يُسَجِّلُه القالِبُ
    /// بِجِوارِ <see cref="GatewayPayments"/> فَيَحقِنُه المُضيف، ويُناديه الإلغاءُ بَعدَ حِفظِه لِصَفقَةٍ عَلَيها فاتورَةٌ لَم يُقبَض بِها. و<c>null</c>
    /// حينَ لا يُحقَن — اختِباراتُ الخِدمَةِ والبُرهانُ الحَيّ — فَلا يُسأَلُ عَن فاتورَةٍ عِندَ الإلغاءِ كَما كانَ قَبلَه. (بَعدَ <c>payments</c>
    /// اختِيارِيّاً عَمداً: البِناءاتُ القائِمَةُ لا تَتَحَرَّك.)</para>
    /// </summary>
    private readonly GatewayInvoiceCloser? _invoices;

    public DealsService(IDocumentStore store, ACommerce.Kit.Payments.IPaymentProvider payments, GatewayInvoiceCloser? invoices = null)
    {
        _store = store;
        _payments = payments;
        _invoices = invoices;
    }

    /// <summary>مِفتاحُ مَرجِعِ مالِكِ الإعلان في <see cref="Deal.Refs"/> —
    /// يُلحِقُه مَسارُ إنشاءِ العَرض. مُعَرَّفٌ هُنا لِأَنّ الكاتِبَ
    /// والقارِئَ يَجِبُ أَن يَقرَآ نَفسَ الحَرف.</summary>
    public const string ListingOwnerRef = "listing_owner";

    /// <summary>
    /// <para><b>«تَغَيَّرَت الصَفقَةُ في الأَثناء» — رَمزُ رَفضٍ واحِدٌ لِكُلِّ بابِ كِتابَةٍ عَلى الصَفقَة</b> (‏ADR-036،
    /// ‏2026-10-12): وَثيقَةُ <see cref="Deal"/> مُسَجَّلَةٌ بِتَزامُنٍ تَفاؤُلِيّ (<see cref="RegisterDocument"/>)، فَحِفظُ
    /// صَفقَةٍ كَتَبَها غَيرُ هذا الطَلَبِ بَعدَ أَن قَرَأَها يَرمي <c>JasperFx.ConcurrencyException</c> — فَيَصيرُ هذا الرَمزَ
    /// عِندَ المُنادي، لا ‏500، ولا يُكتَبُ شَيء. وكانَت الكِتابَتانِ تُحفَظانِ والأَخيرَةُ تَمحو الأولى بِصَمت
    /// (<c>LiveDealConcurrencyProofTests</c>، ‏<c>bf01f30</c>).</para>
    /// </summary>
    public const string ChangedMeanwhile = "deal_changed_meanwhile";

    /// <summary>رِسالَةُ المُراجِعِ البَشَرِيِّ لِلرَمزِ — نَسَقُ <c>MessageAr</c> في مَعاجِمِ الرَفض؛ والشاشَةُ تَقرَأُ
    /// القامُوسَ بِالرَمز (<see cref="DealRefusalTexts"/>) لا هذا.</summary>
    private const string ChangedMeanwhileAr = "كَتَبَ غَيرُ هذا الطَلَبِ الصَفقَةَ بَعدَ أَن قَرَأَها — فَلَم يُكتَب شَيء.";

    /// <summary>رِسالَةُ المُراجِعِ البَشَرِيِّ لِـ<see cref="DealShipping.DealAwaitsCarrier"/> — نَسَقُ <see cref="ChangedMeanwhileAr"/>:
    /// الشاشَةُ تَقرَأُ القامُوسَ بِالرَمز، والـAPI مَعجَمَه.</summary>
    private const string AwaitsCarrierAr = "طَلَبُ الصَفقَةِ عِندَ OTO وتاليها «شَحن» — لا تَدخُلُها بِلا جَوابٍ مِن OTO يُثبِتُ شُحنَتَها وبوليصَتَها.";

    /// <summary>رِسالَةُ المُراجِعِ البَشَرِيِّ لِـ<see cref="GatewayPayments.DealAwaitsPayment"/> — نَسَقُ <see cref="AwaitsCarrierAr"/>:
    /// الشاشَةُ تَقرَأُ القامُوسَ بِالرَمز، والـAPI مَعجَمَه (‏ADR-032 الخُطوَة 10، تَيّارُ S9، ‏2026-10-12).</summary>
    private const string AwaitsPaymentAr = "عَلى الصَفقَةِ فاتورَةُ بَوّابَةِ المَتجَرِ وتاليها «دَفع» — لا تَدخُلُها حَتّى يَثبُتَ دَفعُ الفاتورَةِ مِن الخادِم.";

    /// <summary>
    /// <para><b>صَفقَةُ بِطاقَةٍ بِلا مَرجِعِ دَفعٍ وتاليها «دَفع»</b> (‏2026-10-13، مُراجَعَةُ بَوّابَةِ المَوجَة 3b، G5 (xv-g)؛ تَوصيفُ «اليَوم» <c>e1e74ab</c>):
    /// رَمزُ <see cref="AdvanceAsync"/> و<see cref="AdminPathRefusal"/> حينَ تَصدُقُ <see cref="MissesPayment"/>. صَفقَةُ بِطاقَةٍ عَلى طَريقِ مُزَوِّدِ النُسخَةِ
    /// رَمى حَجزُها عِندَ الوِلادَةِ (<c>payment_error</c> بِلا <c>payment_id</c>) كانَت تَدخُلُ «دَفع» والقَبضُ يَتَجاوَزُها — بِلا مال. ولا ‏ADR يُجيزُه.</para>
    /// </summary>
    public const string PaymentMissing = "deal_payment_missing";

    /// <summary>رِسالَةُ المُراجِعِ البَشَرِيِّ لِـ<see cref="PaymentMissing"/> — نَسَقُ <see cref="AwaitsPaymentAr"/>.</summary>
    private const string PaymentMissingAr = "صَفقَةُ بِطاقَةٍ بِلا مَرجِعِ دَفع — تَعَذَّرَ حَجزُ مَبلَغِها عِندَ الطَلَب — فَلا تَدخُلُ «دَفع» بِلا مال.";

    /// <summary>
    /// <para><b>أَصَفقَةُ بِطاقَةٍ تاليها «دَفع» ولا مَرجِعَ دَفعٍ عَلَيها؟</b> — دالَّةٌ نَقِيَّةٌ تَقرَؤُها <see cref="AdvanceAsync"/> بَعدَ حارِسِ البَوّابَة
    /// (<see cref="GatewayPayments.AwaitsPayment"/> يَسبِقُها: صَفقَةُ البَوّابَةِ بِرَمزِها)، و<see cref="AdminPathRefusal"/>، وصَفحَةُ العُضوِ فَلا تَرسُمُ
    /// «تَأكيد: دَفع» يُرَدّ. والمَرجِعُ <c>payment_id</c> — يَكتُبُه الحَجزُ وتَأكيدُ البَوّابَة؛ وصَفقَةٌ بِلا <c>pay_method</c> (عَرضٌ عَلى إعلان، بَذرَة)
    /// والنَقدُ (لا «دَفع» في طَريقِه) خارِجُها بِحَرفِهِما.</para>
    /// </summary>
    public static bool MissesPayment(Deal deal)
        => string.Equals(DealsPolicy.PayMethodOf(deal), CheckoutPayMethods.Card, StringComparison.Ordinal)
           && DealsPolicy.Next(deal) == DealStage.Paid
           && !(deal.Refs.TryGetValue("payment_id", out var pid) && !string.IsNullOrWhiteSpace(pid));

    /// <summary>
    /// <para><b>تَسجيلُ وَثيقَةِ الصَفقَةِ في Marten</b> — نِداءٌ واحِدٌ يُنادى مِن تَركيبِ القالِب
    /// (<c>ConfigureMarten</c> في <c>MarketplaceTemplateExtensions</c>) ومِن البُرهانِ الحَيّ، فَما يُقاسُ هُوَ ما يُشَغَّل
    /// (القاعِدَة ٢). <c>UseOptimisticConcurrency</c>: كُلُّ حِفظٍ لِصَفقَةٍ مَوجودَةٍ يَشتَرِطُ أَنّ نُسخَتَها (<c>mt_version</c>)
    /// هِيَ الَّتي قُرِئَت في الجَلسَةِ نَفسِها — وحِفظُ صَفقَةٍ لَم تُقرَأ في الجَلسَةِ يُرَدُّ أَيضاً (مَقيس).</para>
    ///
    /// <para><b>ولا تَرحيلَ في المُخَطَّط</b> (مَقيس ‏2026-10-12): مُخَطَّطُ <c>platform</c> كُلُّه ‏513 سَطرَ DDL مُتَطابِقَةٌ
    /// بِالتَسجيلِ وبِدونِه — العَمودُ <c>mt_version</c> قائِمٌ في كُلِّ جَدوَلِ وَثيقَة، والفَحصُ في جُملَةِ التَحديثِ لا في
    /// دالَّةٍ في القاعِدَة.</para>
    ///
    /// <para><b>دِقَّةٌ مُلحَقَةٌ ‏2026-10-12 — ولا يُعادُ حَرفٌ فَوقَها</b> (‏ADR-036 §٧): «مُخَطَّطُ <c>platform</c> كُلُّه» أَوسَعُ مِمّا قيس —
    /// المُفرَغانِ مِن قاعِدَتَينِ مُرمِيَّتَينِ لا تَحمِلانِ إلّا <c>mt_doc_deal</c> (‏<c>CREATE TABLE</c> واحِد) ودَوالَّ Marten المُساعِدَة
    /// (<c>mt_jsonb_*</c>، <c>mt_grams_*</c>، <c>mt_immutable_*</c>، <c>mt_safe_*</c>)؛ والنَتيجَةُ صادِقَةٌ عَلَيه: DDL جَدوَلِ الصَفقَةِ
    /// واحِدٌ بِالتَسجيلِ وبِدونِه، وصِفرُ دالَّةِ <c>mt_upsert</c>/<c>mt_update</c>/<c>mt_insert</c> لِكُلِّ نَوع في Marten 9.</para>
    /// </summary>
    public static void RegisterDocument(StoreOptions opts)
        => opts.Schema.For<Deal>().UseOptimisticConcurrency(true);

    /// <summary><para><b>عَرضٌ عَلى إعلانٍ لَم يُقبَل بَعد — بِلا طَرَفٍ مُقابِل — ومالِكُ إعلانِه
    /// <paramref name="ownerId"/> ولَيسَ هُوَ مَن بادَر</b> — دالَّةٌ <b>نَقِيَّة</b> فَتُختَبَر
    /// بِلا قاعِدَة بَيانات، بِمُوجَبٍ وسالِب.</para>
    ///
    /// <para><b>ولَيسَت «كُلَّ طَلَبٍ يَنتَظِرُ القَبول»</b> (أُعيدَت كِتابَتُه إلى الكودِ ‏2026-10-11 — كانَ
    /// يَصِفُ ما قَبلَ الرَبطِ عِندَ الوِلادَة): صَفقَةُ السَلَّةِ يُربَطُ مالِكُ إعلانِها طَرَفاً مِن الوِلادَة
    /// (‏ADR-033 §٦‑ب‑١) فَتَنتَظِرُ قَبولَه في <c>Offered</c> و<c>CounterpartyId</c>ها هُوَ — فَلا تَمُرُّ هُنا بَل
    /// في الشَطرِ الأَوَّلِ مِن <see cref="ListForListingOwnerAsync"/>. يَبقى هُنا العَرضُ عَلى إعلانٍ
    /// (<c>/listings/{id}/deal</c>) الَّذي لا طَرَفَ لَه حَتّى <c>/accept</c>.</para>
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
    /// <para><b>شَطرانِ لا شَطر</b> (أُعيدَت كِتابَتُه إلى الكودِ ‏2026-10-11 — كانَ يَقولُ «المَقبولَةُ يَحمِلُها
    /// <c>CounterpartyId</c> والوارِدُ قَبلَ القَبولِ بِلا طَرَف»، وهُوَ ما قَبلَ الرَبطِ عِندَ الوِلادَة):
    /// (١) صَفَقاتٌ المالِكُ طَرَفُها المُقابِل — <b>المَقبولَةُ، وصَفَقاتُ السَلَّةِ المَربوطَةُ بِه مِن الوِلادَةِ
    /// ولَم يَقبَلها بَعد</b> (<c>Offered</c>، ‏ADR-033 §٦‑ب‑١)؛ و(٢) عُروضٌ عَلى إعلاناتِه بِلا طَرَفٍ بَعد
    /// (<see cref="IsIncomingOfferFor"/>) — مالِكُها في <c>Refs[listing_owner]</c> وَحدَه حَتّى <c>/accept</c>.
    /// <b>والعَطَبُ الَّذي أُصلِحَ قَبلُ</b>: كانَ الشَطرُ الثاني مَوصوفاً في تَعليقِ هذِه الدالَّةِ و<b>غَيرَ
    /// مَكتوبٍ في جِسمِها</b>، فَلَم يَرَ مالِكُ إعلانٍ عَرضاً وارِداً في أَيّ شاشَة.</para>
    ///
    /// <para><b>والفَلتَرَةُ في الذاكِرَة نُقلَةٌ لا اختِيار</b>: ‏Marten
    /// لا يُفَلتِر <c>Dictionary</c> في LINQ — نَفسُ سَبَبِ
    /// <c>AccountQueries.MyListingsAsync</c> ونَفسُ شَكلِه.</para></summary>
    public async Task<List<Deal>> ListForListingOwnerAsync(
        string tenantSlug, Guid ownerId, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);
        // الشَطرُ الأَوَّل: صَفقاتٌ المالِكُ طَرَفُها المُقابِل — مَقبولَةً، أَو صَفقَةَ سَلَّةٍ رُبِطَ بِها
        // عِندَ الوِلادَةِ وتَنتَظِرُ قَبولَه.
        var settled = await s.Query<Deal>()
            .Where(d => d.CounterpartyId == ownerId)
            .OrderByDescending(d => d.UpdatedAt).Take(100).ToListAsync(ct);

        // الشَطرُ الثاني: عُروضٌ عَلى إعلاناتي لَم تُقبَل بَعد — بِلا طَرَفٍ
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
    /// الإداريِّ المُثَبَّتَةُ حَرفاً لا تَتَحَرَّك.) <b>ولا تُعَيِّنُ المُبادِرَ طَرَفاً</b> (‏2026-10-11):
    /// تُعيدُ الصَفقَةَ بِلا تَغييرٍ حينَ <c>counterpartyId == InitiatorId</c> — الثابِتُ في الكاتِبِ الوَحيد.</summary>
    public async Task<Deal?> AssignCounterpartyAsync(
        string tenantSlug, Guid dealId,
        Guid counterpartyId, string? counterpartyName,
        CancellationToken ct = default, string action = "assigned")
    {
        await using var s = _store.LightweightSession(tenantSlug);
        var deal = await s.LoadAsync<Deal>(dealId, ct);
        if (deal is null || deal.Status != DealStatus.Active) return deal;
        if (deal.CounterpartyId is not null) return deal;
        // ولا يَكونُ المَرءُ طَرَفَي صَفقَتِه — هُنا في الكاتِبِ الوَحيدِ لا في نِداءٍ بِعَينِه (مُراجَعَةُ الموجَةِ
        // الثانِيَةِ الرابِعَة، ‏2026-10-11): كانَ الفَحصُ في `BindListingOwnerAsync` وَحدَها، والمَسارُ الإداريُّ يُعَيِّنُ
        // مالِكَ التَطبيقِ طَرَفاً لِصَفقَةِ بَذرَتِه وهو مُبادِرُها. يُعادُ ما قُرِئَ بِلا كِتابَة.
        if (deal.InitiatorId == counterpartyId) return deal;
        deal.CounterpartyId = counterpartyId;
        deal.CounterpartyName = counterpartyName;
        deal.UpdatedAt = DateTime.UtcNow;
        deal.Timeline.Add(new(deal.Stage, deal.Stage, action,
            counterpartyId, counterpartyName, null, DateTime.UtcNow));
        s.Store(deal);
        // تَعارُضُ النُسخَة (‏ADR-036): لَم يُكتَب التَعيين — يُعادُ ما في المَخزَنِ الآن، ومُنادوها يُعيدونَ الفَحص
        // (القَبولُ يُعيدُ `Validate` عَلى وَثيقَةِ جَلسَةِ كِتابَتِه). لا قَناةَ رَفضٍ لَها: لا مُنادِيَ يَقرَأُ ما تُرجِعُه.
        try { await s.SaveChangesAsync(ct); }
        catch (JasperFx.ConcurrencyException) { return await LoadAsync(tenantSlug, dealId, ct); }
        return deal;
    }

    /// <summary>
    /// <para><b>البائِعُ طَرَفٌ مُقابِلٌ مِن الوِلادَة</b> (‏ADR-033 §٦، ‏2026-10-11):
    /// يُنادِيها <c>/checkout/submit</c> بَعدَ إلحاقِ <c>listing_owner</c>، فَتَربُطُ
    /// مالِكَ الإعلانِ طَرَفاً بِاسمِه مِن وَثيقَتِه وتَكتُبُ سَطرَ
    /// <see cref="DealEvent.BoundAtBirth"/> — لا <c>assigned</c>، فَالقَبولُ لَم يَقَع
    /// بَعد. ومَرجِعٌ لَيسَ مُعَرِّفاً لا يَربُطُ أَحَداً ويُرجِعُ <c>null</c>؛ ووَثيقَةُ
    /// مُستَخدِمٍ غائِبَةٌ تُربَطُ بِالهُوِيَّةِ وبِلا اسم — الهُوِيَّةُ مِن الإعلانِ
    /// وهي الحُكم، والاسمُ زينَةُ عَرض. («بِلا اسم» = <c>null</c> لا <c>""</c> مُنذُ
    /// ‏2026-10-11 — الغِيابُ الَّذي يَقرَؤُه كُلُّ قارِئ.)</para>
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
    ///
    /// <para><b>وانتَقَلَ الفَحصُ إلى الكاتِبِ الوَحيد</b> (‏2026-10-11، مُراجَعَةُ الموجَةِ الثانِيَةِ الرابِعَة):
    /// كانَ هُنا وَحدَه — فَالمَسارُ الإداريُّ يُعَيِّنُ مالِكَ التَطبيقِ طَرَفاً لِصَفقَةٍ هو مُبادِرُها — وصارَ في
    /// <see cref="AssignCounterpartyAsync"/>، الَّتي تَمُرُّ بِها هذِه وكُلُّ مَسارٍ يُعَيِّن. والنَتيجَةُ هُنا هي هي.</para>
    /// </summary>
    public async Task<Deal?> BindListingOwnerAsync(
        string tenantSlug, Guid dealId, string ownerRef, CancellationToken ct = default)
    {
        if (!Guid.TryParse(ownerRef, out var ownerId)) return null;
        await using var q = _store.QuerySession(tenantSlug);
        // ولا يَكونُ المَرءُ طَرَفَي صَفقَتِه — مالِكٌ هُوَ المُبادِرُ لا يُربَط (‏ADR-033 §٦‑ب‑١ مُلحَقاً)؛ والفَحصُ
        // انتَقَلَ ‏2026-10-11 إلى `AssignCounterpartyAsync` — الكاتِبِ الوَحيد — فَيَحرُسُ المَسارَ الإداريَّ أَيضاً.
        var owner = await q.LoadAsync<ACommerce.Kit.Auth.User>(ownerId, ct);
        // وَثيقَةٌ غائِبَة: الهُوِيَّةُ تُربَطُ والاسمُ غائِبٌ لا فارِغ — `null` ما يَقرَؤُه كُلُّ قارِئٍ غِياباً (`?? "—"`،
        // `is not null`)؛ وكانَ `?? ""` فَيُرسَمُ فَراغاً وسَهماً مُعَلَّقاً في قائِمَةِ الاستوديو (‏2026-10-11، مُراجَعَةٌ ثانِيَة).
        return await AssignCounterpartyAsync(tenantSlug, dealId, ownerId, owner?.FullName, ct, DealEvent.BoundAtBirth);
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
    ///
    /// <para><b>وأُغلِقَ «ما لا تُغلِقُه» أَعلاه ‏2026-10-12 — ولا يُعادُ حَرفٌ فَوقَه</b> (‏ADR-036): صارَت وَثيقَةُ <c>Deal</c> بِتَزامُنٍ
    /// تَفاؤُلِيّ، فَكِتابَةٌ تَقَعُ بَعدَ ذاكَ التَحميلِ وقَبلَ حِفظِه تَرُدُّ هذا الحِفظَ بِـ<see cref="ChangedMeanwhile"/> ولا تَضيع؛
    /// وقَبولانِ مُتَداخِلانِ تَماماً يُرجِعانِ <c>Accepted</c> مَرَّةً و<c>deal_changed_meanwhile</c> مَرَّة (‏<c>LiveDealConcurrencyProofTests</c>:
    /// «الثاني: ok=True · الأَوَّل: ok=False code=deal_changed_meanwhile · advanced=1»).</para>
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
        // **وصارَ الرَدُّ رَمزاً لا رَمياً** (‏ADR-036، ‏2026-10-12): بِالتَزامُنِ التَفاؤُلِيِّ لا كِتابَةَ تَضيع — فَالطَريقُ
        // الوَحيدُ إلى هُنا تَعيينٌ لَم يُكتَب لِأَنّ الصَفقَةَ تَغَيَّرَت بَينَ قِراءَتِه وحِفظِه؛ فَهو «تَغَيَّرَت في الأَثناء»
        // لا خَرقُ ثابِت. ولا تُكتَبُ `Booked` بِلا طَرَف.
        if (!IsActorAllowed(deal, by.ActorId, DealsPolicy.RequiredActorToLeave(deal)!))
            return DealAcceptResult.Refused(new DealAcceptViolation(ChangedMeanwhile, ChangedMeanwhileAr), deal);
        // ولا مُلاحَظَةَ بِحَرفِيَّةٍ عَرَبِيَّةٍ في السِجِلّ (مُراجَعَةُ المَوجَةِ الثانِيَة، ‏2026-10-11): كانَت «قُبِلَ
        // العَرض» تُكتَبُ في المَخزَنِ نَصّاً لا مِفتاحاً (القاعِدَة ١١) — وسَطرُ `advanced` نَفسُه يَقولُها:
        // «عَرض/طَلَب ← حَجز» مِن تَسمِيَتَي الكاتالوج.
        // وحِفظُ الانتِقالِ يَشتَرِطُ النُسخَةَ الَّتي قُرِئَت أَعلاه (‏ADR-036): إلغاءٌ أَو قَبولٌ ثانٍ حُفِظَ بَينَهُما يَرُدُّه.
        try { return DealAcceptResult.Accepted(await MoveAsync(s, deal, next, by.ActorId, by.ActorName, null, ct)); }
        catch (JasperFx.ConcurrencyException)
        {
            return DealAcceptResult.Refused(new DealAcceptViolation(ChangedMeanwhile, ChangedMeanwhileAr),
                await LoadAsync(tenantSlug, dealId, ct));
        }
    }

    /// <summary>اِنتَقِل لِلمَرحَلَة التالِيَة في النَّمَط. يَفحَص أَنّ الفاعِل
    /// يَملِك حَقّ <b>دُخولِ المَرحَلَةِ التالِيَة</b> (initiator/counterparty/either) —
    /// <see cref="DealsPolicy.RequiredActorToLeave(Deal)"/>، فاعِلُ التالي لا فاعِلُ الحالِيَّة
    /// (‏ADR-033 §٦‑ط، ‏2026-10-11؛ كانَ يَسأَلُ فاعِلَ الحالِيَّة فَيُحَرِّكُ المُشتَري طَلَبَه إلى
    /// «حَجز» ويُعلِنُ «شَحن»). والتالي بِوَعيِ طَريقَةِ الدَفع (<see cref="DealsPolicy.Next(Deal)"/>):
    /// صَفقَةُ النَقدِ لا تَزورُ <c>Paid</c> (‏ADR-033 §٦).</summary>
    /// <param name="from">
    /// <para><b>المَرحَلَةُ الَّتي قَرَأَها المُنادي فَقَرَّرَ عَلَيها</b> — اختِيارِيٌّ، و<c>null</c> افتِراضاً
    /// (بَعدَ <paramref name="ct"/> عَمداً: النِداءاتُ القائِمَةُ لا تَتَحَرَّك). مُنادِياهُ نُقطَةُ الشَحنِ والوارِدُ مِن
    /// OTO (‏ADR-033 الخُطوَة 3): يُنادِيانِ OTO بَينَ قِراءَتِهِما والانتِقال، وهذا التَحميلُ جَديدٌ فَلا تَراه
    /// النُسخَةُ المُتَفائِلَة — فَلَو أَعلَنَ البائِعُ «تَسليم» بِيَدِه أَثناءَ نِداءِ الوارِد لَمَضى هذا مِن
    /// <c>Delivered</c> إلى <c>Reviewed</c> بِاسمِه. فَمَرحَلَةٌ غَيرُ المَقروءَةِ تُرَدُّ بِـ<see cref="ChangedMeanwhile"/>
    /// قَبلَ أَيِّ كِتابَة — الرَمزُ نَفسُه لا رَمزٌ جَديد.</para>
    /// </param>
    /// <param name="shipment">
    /// <para><b>جَوابُ OTO عَن طَلَبِ الصَفقَة</b> — اختِيارِيٌّ، و<c>null</c> افتِراضاً (بَعدَ <paramref name="from"/> عَمداً: النِداءاتُ
    /// القائِمَةُ لا تَتَحَرَّك؛ مُراجَعَةُ تَيّارِ S7 الثانِيَة، ‏2026-10-12؛ ‏ADR-033 §٧‑ح). صَفقَةٌ تَصدُقُ عَلَيها
    /// <see cref="DealShipping.AwaitsCarrier"/> — تاليها «شَحن» وطَلَبُها عِندَ OTO — لا تَدخُلُ «شَحن» إلّا بِجَوابٍ يُثبِتُ شُحنَتَها
    /// وبوليصَتَها (<see cref="DealShipping.ShipmentConfirmed"/>، تُعادُ هُنا عَلى الصَفقَةِ الَّتي سَتُكتَب)، وإلّا
    /// <see cref="DealShipping.DealAwaitsCarrier"/> قَبلَ أَيِّ كِتابَة. ولا يَحمِلُه إلّا <c>DealShipping.ShipAsync</c> بَعدَ أَن سَأَلَ OTO؛
    /// فَزِرُّ «تَأكيد: شَحن» والمَسارُ الإداريُّ والـAPI — وكانَت تُخرِجُها إلى «شَحن» بِلا بوليصَة (تَوصيفُه <c>e52ba8f</c>) — تُرَدّ.</para>
    /// <para><b>دِقَّةٌ مُلحَقَةٌ ‏2026-10-13 (G4 (xxxi)؛ ‏ADR-033 §٧‑ح‑٨) — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق</b>: «تُعادُ هُنا» إعادَةُ <b>شَكلِ</b> الجَواب —
    /// مُعَرِّفُ الطَلَبِ مُعَرِّفُ طَلَبِ الصَفقَة، والحالُ مِن مَعجَمِ الشُحنات، ورابِطُ بوليصَةٍ بِـhttps — لا مَصدَرِه: أَنّ الجَوابَ مِن OTO يَحرُسُه الفاحِصُ
    /// <c>Only_the_OTO_paths_in_DealShipping_hand_an_OTO_answer_to_the_transition</c> (لا يَحمِلُه إلّا مَسارا OTO في <c>DealShipping</c> بَعدَ أَن سَأَلا
    /// «Order Tracking»)، لا هذا الكاتِب.</para>
    /// </param>
    public async Task<DealAdvanceResult> AdvanceAsync(
        string tenantSlug, Guid dealId, Guid actorId, string actorName,
        string? note = null, CancellationToken ct = default, DealStage? from = null,
        ACommerce.Kit.Delivery.OtoOrderState? shipment = null)
    {
        await using var s = _store.LightweightSession(tenantSlug);
        var deal = await s.LoadAsync<Deal>(dealId, ct);
        if (deal is null) return new(false, null, "deal not found");
        if (deal.Status != DealStatus.Active)
            return new(false, deal, $"الصَّفقَة في حالَة {deal.Status} — لا يُمكِن المُتابَعَة.");
        if (from is { } expected && deal.Stage != expected)
            return new(false, deal, ChangedMeanwhileAr, ChangedMeanwhile);

        var next = DealsPolicy.Next(deal);
        if (next is null)
            return new(false, deal, "هذه آخِر مَرحَلَة، لا تالٍ.");

        // فَحص الفاعِل: مَن يَدخُلُ المَرحَلَةَ التالِيَة؟ — فاعِلُها لا فاعِلُ الحالِيَّة (‏ADR-033 §٦‑ط)،
        // بِالدالَّةِ الَّتي تَقرَؤُها الشاشَةُ والـAPI؛ ولا تُرجِعُ `null` وثَمَّ تالٍ.
        var required = DealsPolicy.RequiredActorToLeave(deal)!;
        if (!IsActorAllowed(deal, actorId, required))
            return new(false, deal, $"يَتَطَلَّب فاعِلاً مِن نَوع: {required}");

        // وطَلَبُ الصَفقَةِ عِندَ OTO وتاليها «شَحن»: لا تَدخُلُها إلّا بِجَوابِ OTO يُثبِتُ شُحنَتَها (مُراجَعَةُ تَيّارِ S7 الثانِيَة،
        // ‏2026-10-12). بَعدَ الفاعِلِ لا قَبلَه — فَغَيرُ المُخَوَّلِ يُجابُ بِرَفضِ الفاعِلِ ولا يَعلَمُ أَنّ لِلصَفقَةِ طَلَباً هُناك.
        if (DealShipping.AwaitsCarrier(deal) && !DealShipping.ShipmentConfirmed(deal, shipment))
            return new(false, deal, AwaitsCarrierAr, DealShipping.DealAwaitsCarrier);

        // وفاتورَةُ بَوّابَةِ المَتجَرِ عَلى الصَفقَةِ وتاليها «دَفع»: لا تَدخُلُها حَتّى يَثبُتَ دَفعُها مِن الخادِم (‏ADR-032 الخُطوَة 10،
        // تَيّارُ S9، ‏2026-10-12) — بِالحارِسِ نَفسِه الَّذي أَمسَكَ الشَحنَ أَعلاه، وبَعدَ الفاعِلِ لا قَبلَه. فَالقَبضُ عِندَ «دَفع» يَتَجاوَزُ
        // صَفقَةً بِلا مَرجِعِ دَفع — وبِلا هذا السَطرِ كانَت صَفقَةُ بِطاقَةٍ لَم تُدفَع فاتورَتُها تَدخُلُ «دَفع» بِلا مال.
        if (GatewayPayments.AwaitsPayment(deal))
            return new(false, deal, AwaitsPaymentAr, GatewayPayments.DealAwaitsPayment);
        // وصَفقَةُ بِطاقَةٍ بِلا مَرجِعِ دَفعٍ لا تَدخُلُ «دَفع» (‏2026-10-13، G5 (xv-g)) — بَعدَ حارِسِ البَوّابَةِ لا قَبلَه: تِلكَ بِرَمزِها.
        if (MissesPayment(deal))
            return new(false, deal, PaymentMissingAr, PaymentMissing);

        // وحِفظُ الانتِقالِ يَشتَرِطُ النُسخَةَ الَّتي قُرِئَت (‏ADR-036): نَقرَتانِ مُتَداخِلَتانِ تَمضي أولاهُما وتُرَدُّ الأُخرى.
        Deal moved;
        try { moved = await MoveAsync(s, deal, next.Value, actorId, actorName, note, ct); }
        catch (JasperFx.ConcurrencyException)
        {
            return new(false, await LoadAsync(tenantSlug, dealId, ct), ChangedMeanwhileAr, ChangedMeanwhile);
        }
        // **والقَبضُ بَعدَ الحِفظِ المَفحوصِ لا قَبلَه — وخارِجَ التِقاطِ التَعارُض** (‏ADR-036 الخُطوَة 2، ‏2026-10-12): كانَ يَقَعُ
        // داخِلَ `MoveAsync` قَبلَ حِفظِها، فَانتِقالٌ يُرَدُّ هُنا بِـ`deal_changed_meanwhile` يَكونُ قَد قَبَضَ ولَم يُسَجِّل، و«أَعِد
        // المُحاوَلَة» تَقبِضُ ثانِيَةً (`DealMoneyClaimFirstTests`). ونَتيجَتُه بَعدَ هذا السَطرِ كِتابَةٌ ثانِيَةٌ تُعادُ عَلى ما كُتِب؛
        // فَإن غَلَبَها التَعارُضُ ثَلاثاً صَعِدَ إلى المُنادي بِصَوت (‏500) — لا يُقالُ «لَم يُكتَب» عَن انتِقالٍ كُتِبَ وقَبضٍ وَقَع.
        return new(true, await CaptureClaimedAsync(tenantSlug, moved, next.Value, actorId, actorName, ct), null);
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
    ///
    /// <para><b>ولا يَقبِضُ بَعدَ اليَوم — يُطالِب</b> (‏ADR-036 الخُطوَة 2، ‏2026-10-12؛ ولا يُعادُ حَرفٌ فَوقَ ما سَبَق): «ويَقبِضُ
    /// عِندَ <c>Paid</c>» أَعلاه كانَ قَبضاً قَبلَ الحِفظِ المَفحوص، فَحِفظٌ يَرُدُّه التَعارُضُ يَكونُ قَد تَلا قَبضاً لَم يُسَجَّل. فَصارَ
    /// حِفظُ هذِه الدالَّةِ <b>المُطالَبَةَ</b>: الانتِقالُ يُكتَبُ بِفَحصِ النُسخَة، ثُمَّ يَقبِضُ <see cref="CaptureClaimedAsync"/> مَن كَسَبَها،
    /// ثُمَّ تُكتَبُ نَتيجَتُه بِقِراءَةٍ وإعادَة. وبابُ <see cref="AcceptAsync"/> لا يُناديها لِأَنّه لا يُدخِلُ <c>Paid</c>: يُخرِجُ
    /// <c>Offered</c> إلى <c>Booked</c> في كُلِّ نَمَطٍ في الكاتالوج — مُثَبَّتٌ بِصَفٍّ يَعُدُّ الأَنماط.</para>
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
    /// <para><b>أَقُبِضَ ثَمَنُ هذِه الصَفقَة؟</b> — <c>payment_status</c> بِحَرفِ <c>PaymentStatus.Captured</c>: يَكتُبُه القَبضُ عِندَ «دَفع»
    /// (نَتيجَةُ مُزَوِّدِ النُسخَة)، ويَكتُبُه تَأكيدُ دَفعِ بَوّابَةِ المَتجَرِ (<see cref="ConfirmGatewayPaymentAsync"/>). يَقرَؤُه القَبضُ
    /// فَلا يَقبِضُ مَرَّتَين، والتَأكيدُ فَلا يَكتُبُ مَرَّتَين، وحارِسُ «دَفع» (<see cref="GatewayPayments.AwaitsPayment"/>).</para>
    /// <para><b>ولا صَفقَةَ قائِمَةٌ تَتَغَيَّرُ بِه</b> (مَقيس ‏2026-10-12): «Captured» لا يُكتَبُ قَبلَ اليَومِ إلّا نَتيجَةَ قَبضٍ عِندَ «دَفع»،
    /// والمَرحَلَةُ تُدخَلُ مَرَّة — فَلا صَفقَةَ تَبلُغُ «دَفع» وهي «Captured» إلّا ما أَثبَتَته البَوّابَة.</para>
    /// </summary>
    public static bool IsCaptured(Deal deal)
        => deal.Refs.TryGetValue("payment_status", out var status)
           && string.Equals(status, nameof(ACommerce.Kit.Payments.PaymentStatus.Captured), StringComparison.Ordinal);

    /// <summary>
    /// <para><b>القَبضُ عِندَ دُخولِ <c>Paid</c> — بَعدَ المُطالَبَةِ لا قَبلَها</b> (‏ADR-036 الخُطوَة 2، ‏2026-10-12): يُناديها
    /// <see cref="AdvanceAsync"/> <b>بَعدَ</b> أَن حَفِظَت <see cref="MoveAsync"/> الانتِقالَ بِفَحصِ النُسخَةِ وخارِجَ التِقاطِ تَعارُضِه —
    /// فَمَن رُدَّ حِفظُه لا يَبلُغُها ولا يَقبِضُ شَيئاً، ومَن كَسَبَه يَقبِضُ مَرَّةً، ونَتيجَتُه كِتابَةٌ ثانِيَةٌ عَبرَ
    /// <see cref="RecordAsync"/> تُقرَأُ فيها الصَفقَةُ وتُعادُ عَلى ما كُتِب (الأُنبوبُ القائِمُ لِلإلحاق، §٧ — لا رابِع).</para>
    ///
    /// <para><b>وما لا تُغلِقُه — يُقال</b>: نِداءٌ يَقَعُ ثُمَّ تَسقُطُ العَمَلِيَّةُ قَبلَ تَسجيلِ نَتيجَتِه يَترُكُ الصَفقَةَ عِندَ <c>Paid</c>
    /// بِلا سَطرِ <c>captured</c> ولا <c>capture_failed</c> — مَرئِيَّةً لِلمُراجَعَة، ولا تُقبَضُ ثانِيَةً لِأَنّ الدُخولَ مَرَّة. «مَرَّةً
    /// عَلى الأَكثَر» لا «مَرَّةً عَلى الأَقَلّ».</para>
    /// </summary>
    private async Task<Deal> CaptureClaimedAsync(
        string tenantSlug, Deal deal, DealStage next, Guid actorId, string actorName, CancellationToken ct)
    {
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
        //
        // (‏2026-10-12، ‏ADR-036 الخُطوَة 2 — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق: نُقِلَت هذِه الكُتلَةُ بِحَرفِها مِن `MoveAsync` قَبلَ حِفظِها
        // إلى هُنا بَعدَه، وصارَت النَتيجَةُ تُكتَبُ عَلى الصَفقَةِ المَقروءَةِ ثانِيَةً `stored` لا عَلى نُسخَةِ المُطالَبَة.)
        //
        // (‏2026-10-12، ‏ADR-032 الخُطوَة 10 — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق: ولا قَبضَ لِصَفقَةٍ قُبِضَت — `IsCaptured`. دَفعٌ أَثبَتَته بَوّابَةُ
        // المَتجَرِ يُكتَبُ «Captured» قَبلَ أَن تَبلُغَ الصَفقَةُ «دَفع»، ومُزَوِّدُ النُسخَةِ لا يَعرِفُ ذلكَ المَرجِعَ ولا يَقبِضُه.)
        if (next == DealStage.Paid && deal.AmountSar > 0 && !IsCaptured(deal))
        {
            if (deal.Refs.TryGetValue("payment_id", out var pid) && !string.IsNullOrEmpty(pid))
            {
                Action<Deal> outcome;
                try
                {
                    var cr = await _payments.CaptureAsync(pid, deal.AmountSar, ct);
                    outcome = stored =>
                    {
                        stored.Refs["payment_status"] = cr.Status.ToString();
                        stored.Timeline.Add(new(next, next, "captured",
                            actorId, actorName,
                            $"capture {cr.Status} {cr.AmountSar:0.00} SAR", DateTime.UtcNow));
                    };
                }
                catch (Exception ex)
                {
                    outcome = stored =>
                    {
                        stored.Refs["capture_error"] = ex.Message;
                        stored.Timeline.Add(new(next, next, "capture_failed",
                            actorId, actorName, ex.Message, DateTime.UtcNow));
                    };
                }
                return await RecordAsync(tenantSlug, deal.Id, outcome, ct) ?? deal;
            }
        }
        return deal;
    }

    /// <summary>
    /// <para><b>إلغاءُ الطَلَبِ كُلِّه — كُلُّ صَفَقاتِ فاتورَةِ البَوّابَةِ المَفتوحَةِ الَّتي تَدفَعُ لَها</b> (‏2026-10-13، مُراجَعَةُ بَوّابَةِ المَوجَة 3b،
    /// G5 M4؛ ‏ADR-032 §٨‑ز مُلحَقاً): البابُ الَّذي يَبقى حينَ يُرَدُّ إلغاءُ صَفقَةٍ واحِدَةٍ بِـ<see cref="GatewayInvoiceShare.Shared"/>. <b>التَخويلُ
    /// لِكُلِّها قَبلَ أَيِّ كِتابَة</b> (<see cref="DealCancelAuthorization.Validate"/> عَلى كُلِّ صَفقَة — فَالبائِعُ لا يُلغي صَفَقاتِ غَيرِه، والمُشتَري
    /// وصاحِبُ المَتجَرِ يُلغيانِها)، ثُمَّ تُلغى واحِدَةً واحِدَةً بِالكاتِبِ نَفسِه — والمَطلوبَةُ آخِرَها — فَتُلغي الأَخيرَةُ الفاتورَةَ بِالمُلغي القائِم.</para>
    /// <para><b>ولا ذَرِّيَّة</b>: تَعارُضٌ في وَسَطِها يُرجِعُ رَمزَه وما أُلغِيَ قَبلَه؛ والإعادَةُ تُلغي ما بَقِيَ قائِماً (ما أُلغِيَ لا يَدفَعُ لَها).
    /// وصَفقَةٌ لا أَخَواتِ لَها تُلغى وَحدَها كَما يُلغيها <see cref="CancelAsync"/>.</para>
    /// </summary>
    public async Task<DealOrderCancelResult> CancelWholeInvoiceAsync(
        string tenantSlug, Guid dealId, DealCanceller by, string reason, CancellationToken ct = default)
    {
        var deal = await LoadAsync(tenantSlug, dealId, ct);
        if (DealCancelAuthorization.Validate(deal, by) is { } refused)
            return new(DealCancelResult.Refused(refused, deal), Array.Empty<Deal>());

        var targets = GatewayInvoiceShare.OwedOnInvoice(deal!, await InvoiceSiblingsAsync(tenantSlug, deal!, ct));
        if (targets.Count == 0) targets = new[] { deal! };
        foreach (var t in targets)
            if (DealCancelAuthorization.Validate(t, by) is { } notTheirs)
                return new(DealCancelResult.Refused(notTheirs, deal), Array.Empty<Deal>());

        var cancelled = new List<Deal>();
        DealCancelResult? requested = null;
        foreach (var t in targets.OrderBy(t => t.Id == dealId))
        {
            var r = await CancelCoreAsync(tenantSlug, t.Id, by, reason, wholeInvoice: true, ct);
            if (!r.Ok) return new(r, cancelled);
            cancelled.Add(r.Deal!);
            if (t.Id == dealId) requested = r;
        }
        return new(requested ?? DealCancelResult.Cancelled(cancelled[^1]), cancelled);
    }

    /// <summary>أَخَواتُ الصَفقَةِ عَلى فاتورَتِها كَما سَمّاها <see cref="GatewayPayments.InvoiceDealsRef"/> — كُلٌّ بِجَلسَةِ قِراءَة، بِلا الصَفقَةِ نَفسِها،
    /// ثَلاثونَ عَلى الأَكثَر (حَدُّ الفاتورَة). فارِغَةٌ حينَ لا مَرجِع. يَقرَؤُها الإلغاءُ وصَفحَتا الصَفقَة.</summary>
    public async Task<IReadOnlyList<Deal>> InvoiceSiblingsAsync(string tenantSlug, Deal deal, CancellationToken ct = default)
    {
        var siblings = new List<Deal>();
        if (GatewayInvoiceShare.DealsOn(deal) is not { } listed) return siblings;
        foreach (var id in listed)
            if (id != deal.Id && await LoadAsync(tenantSlug, id, ct) is { } d) siblings.Add(d);
        return siblings;
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
        => await CancelCoreAsync(tenantSlug, dealId, by, reason, wholeInvoice: false, ct);

    /// <summary>
    /// <para><b>جِسمُ الإلغاءِ الواحِد</b> — <see cref="CancelAsync"/> بِـ<paramref name="wholeInvoice"/> <c>false</c>، و<see cref="CancelWholeInvoiceAsync"/>
    /// لِكُلِّ صَفقَةٍ مِن فاتورَتِها بِـ<c>true</c>: فَلا يُرَدُّ إلغاءُ صَفقَةٍ مِن فاتورَتِه بِـ<see cref="GatewayInvoiceShare.Shared"/> وهي تُلغى مَعَ أَخَواتِها.</para>
    /// <para>(‏2026-10-24، تَيّارُ S15 البَند I(3) — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق: <b>وَسيطٌ لا <c>AsyncLocal</c></b>. كانَ «إلغاءُ الطَلَبِ كُلِّه جارٍ»
    /// عَلَماً في <c>AsyncLocal&lt;bool&gt;</c> ساكِنٍ يَضبِطُه <see cref="CancelWholeInvoiceAsync"/> حَولَ نِداءاتِه لِـ<see cref="CancelAsync"/> ويُنزِلُه في
    /// <c>finally</c> (‏G5 M4) — حالَةٌ خَفِيَّةٌ عابِرَةٌ لِلنِداء: لا تُرى في التَوقيع، وتَرِثُها كُلُّ مُهِمَّةٍ تَبدَأُ داخِلَ ذلكَ التَدَفُّق. صارَ قَرارُ
    /// المُنادي وَسيطاً في التَوقيع (القاعِدَة ٦)، وبَقِيَ تَوقيعُ <see cref="CancelAsync"/> بِحَرفِه — فاحِصو الكاتِبِ يَقيسونَه بِه.)</para>
    /// </summary>
    private async Task<DealCancelResult> CancelCoreAsync(
        string tenantSlug, Guid dealId, DealCanceller by,
        string reason, bool wholeInvoice, CancellationToken ct)
    {
        var (actorId, actorName) = (by.ActorId, by.ActorName);
        await using var s = _store.LightweightSession(tenantSlug);
        var loaded = await s.LoadAsync<Deal>(dealId, ct);

        var violation = DealCancelAuthorization.Validate(loaded, by);
        if (violation is not null) return DealCancelResult.Refused(violation, loaded);
        // ‏`Validate` تَرُدّ `deal_not_found` عِندَ العَدَم، فَما بَعدَها غَيرُ عَدَم.
        var deal = loaded!;

        // ولا تُلغى وَحدَها صَفقَةٌ عَلى فاتورَةِ بَوّابَةٍ مَفتوحَةٍ تَدفَعُ لِأُختٍ قائِمَة — قَبلَ أَيِّ كِتابَة (‏2026-10-13، G5 M4؛ ‏ADR-032 §٨‑ز مُلحَقاً):
        // كانَت تُلغى فَتَبقى الفاتورَةُ قابِلَةً لِلدَّفعِ بِالمَبلَغِ كامِلاً (تَوصيفُه `411e120`). والطَلَبُ كُلُّه بِـ`CancelWholeInvoiceAsync`.
        if (!wholeInvoice
            && GatewayInvoiceShare.Validate(deal, await InvoiceSiblingsAsync(tenantSlug, deal, ct)).FirstOrDefault() is { } shared)
            return DealCancelResult.Refused(new DealCancelViolation(shared.Code, shared.MessageAr), deal);

        deal.Status = DealStatus.Cancelled;
        deal.CancelReason = reason;
        deal.UpdatedAt = DateTime.UtcNow;
        deal.Timeline.Add(new(deal.Stage, deal.Stage, "cancelled",
            actorId, actorName, reason, DateTime.UtcNow));
        s.Store(deal);
        // تَعارُضُ النُسخَة (‏ADR-036): قَبولٌ أَو تَقَدُّمٌ حُفِظَ بَعدَ قِراءَةِ هذا الإلغاءِ لا يُمحى بِه — يُرَدُّ بِرَمزِه.
        // **وما لا يُغلِقُه** (‏ADR-036 §٣): الاستِرجاعُ أَعلاه يَقَعُ قَبلَ هذا الحِفظ، فَإلغاءٌ مَردودٌ هُنا قَد يَتلو
        // استِرجاعاً وَقَع ولَم يُسَجَّل — خُطوَتُه في الـADR قَبلَ أَيِّ مُزَوِّدِ دَفعٍ حَقيقِيّ.
        // **وأُغلِقَ ‏2026-10-12 (‏ADR-036 الخُطوَة 2) — ولا يُعادُ حَرفٌ فَوقَه**: «أَعلاه» صارَ «أَدناه» — هذا الحِفظُ هُوَ المُطالَبَة،
        // والاستِرجاعُ لا يَبلُغُه إلّا إلغاءٌ كُتِب؛ فَالمَردودُ لا يَستَرجِعُ شَيئاً، و«أَعِد المُحاوَلَة» تَستَرجِعُ مَرَّةً.
        try { await s.SaveChangesAsync(ct); }
        catch (JasperFx.ConcurrencyException)
        {
            return DealCancelResult.Refused(new DealCancelViolation(ChangedMeanwhile, ChangedMeanwhileAr),
                await LoadAsync(tenantSlug, dealId, ct));
        }

        // إن كانَ في الصَّفقَة مَعامَلَة دَفع (Refs["payment_id"] مَوضوع
        // عِندَ /checkout/submit)، أَرجِع المَبلَغ كامِلاً قَبل تَعليم
        // الحالَة Cancelled. لَو الإسترِجاع فَشَل، لا نَكسِر الإلغاء —
        // نُسَجِّل خَطَأ في Timeline ونُسَلسِل (المُدير يَدخُل يَدَويّاً).
        // (‏2026-10-12، ‏ADR-036 الخُطوَة 2 — ولا يُعادُ حَرفٌ فَوقَه: «قَبل تَعليم الحالَة» صارَ «بَعدَ حِفظِها»؛ ونَتيجَةُ الاستِرجاعِ
        // كِتابَةٌ ثانِيَةٌ عَبرَ `RecordAsync` تُقرَأُ فيها الصَفقَةُ وتُعادُ عَلى ما كُتِب، وبَعدَ ثالِثِ تَعارُضٍ تَصعَدُ بِصَوت.)
        //
        // (‏2026-10-12، ‏ADR-032 الخُطوَة 10 — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق: **ودَفعٌ قَبَضَته بَوّابَةُ المَتجَرِ لا يَستَرجِعُه مُزَوِّدُ النُسخَة**.
        // مَرجِعُه فاتورَةٌ في حِسابِ المَتجَرِ عِندَ بَوّابَتِه — مُزَوِّدُ النُسخَةِ لا يَعرِفُها: في الإنتاجِ يَرُدُّها، وفي التَجرِبَةِ يَكتُبُ
        // «استُرجِع» عَن مالٍ لَم يَلمِسه. فَيُكتَبُ سَطرُ «استِرجاعُه مُستَحَقٌّ عِندَ بَوّابَةِ المَتجَر» بِحَلقَةِ التَسجيلِ نَفسِها، ويَستَرجِعُه
        // صاحِبُ المَتجَرِ مِن لَوحَةِ بَوّابَتِه — ولا نِداءَ هُنا.)
        if (GatewayPayments.CollectedByGateway(deal))
            deal = await RecordAsync(tenantSlug, dealId, stored => stored.Timeline.Add(new(stored.Stage, stored.Stage,
                DealEvent.GatewayRefundDue, actorId, actorName, stored.Refs.GetValueOrDefault("payment_id"), DateTime.UtcNow)), ct) ?? deal;
        else if (deal.Refs.TryGetValue("payment_id", out var pid) && !string.IsNullOrEmpty(pid))
        {
            Action<Deal> outcome;
            try
            {
                var rr = await _payments.RefundAsync(pid, deal.AmountSar, reason, ct);
                outcome = stored =>
                {
                    stored.Refs["refund_id"]     = rr.PaymentId;
                    stored.Refs["refund_status"] = rr.Status.ToString();
                    stored.Timeline.Add(new(stored.Stage, stored.Stage, "refunded",
                        actorId, actorName,
                        $"refund {rr.Status} {rr.AmountSar:0.00} SAR", DateTime.UtcNow));
                };
            }
            catch (Exception ex)
            {
                outcome = stored =>
                {
                    stored.Refs["refund_error"] = ex.Message;
                    stored.Timeline.Add(new(stored.Stage, stored.Stage, "refund_failed",
                        actorId, actorName, ex.Message, DateTime.UtcNow));
                };
            }
            deal = await RecordAsync(tenantSlug, dealId, outcome, ct) ?? deal;
        }
        // (‏2026-10-12، مُراجَعَةُ تَيّارِ S9 الثانِيَة؛ ‏ADR-032 §٨‑ز — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق: **وفاتورَةُ بَوّابَةٍ لَم يُقبَض بِها تُلغى عِندَ
        // البَوّابَةِ إن كانَت هذِه آخِرَ صَفقَةٍ قائِمَةٍ تَدفَعُ لَها**. كانَت تَبقى قابِلَةً لِلدَّفعِ فَيَدفَعُ المُشتَري ثَمَنَ طَلَبٍ مُلغًى مِن الصَفحَةِ
        // المَفتوحَةِ أَمامَه — تَوصيفُه `GatewayInvoiceCancelTests`. بَعدَ الحِفظِ كَالاستِرجاع، وما يَعودُ رَمزٌ في اللوغِ لا يَرُدُّ الإلغاء؛
        // والمُحَمِّلُ بِلا إلغاءِ الطَلَبِ عَمداً — الأَثَرُ يَتبَعُ ما حُفِظ.)
        else if (GatewayPayments.HasInvoice(deal) && _invoices is not null)
            await _invoices.CloseIfLastAsync(tenantSlug, deal, id => LoadAsync(tenantSlug, id));
        return DealCancelResult.Cancelled(deal);
    }

    /// <summary>
    /// <para><b>فَتحُ نِزاعٍ عَلى صَفقَةٍ فَعّالَة.</b> ومُناديه الوَحيدُ نُقطَةُ الاستوديو الإدارِيَّة.</para>
    ///
    /// <para><b>ولِماذا <see cref="DealDisputeResult"/> لا <c>Deal?</c> كَما كانَ</b> (‏2026-10-13، مُراجَعَةُ المَوجَة 3a — G4 (ii)؛ ‏ADR-036 §١٢‑٢):
    /// كانَت تُعيدُ المَخزونَ حينَ لا تَكتُب — غائِبَةً، أَو غَيرَ فَعّالَة، أَو بَعدَ تَعارُضِ نُسخَة — فَلا يُفَرِّقُ المُنادي «كُتِبَ» مِن «لَم
    /// يُكتَب»، فَكانَت النُقطَةُ تَكتُبُ سَطرَ التَدقيقِ لِنِزاعٍ لَم يَقَع (تَوصيفُه <c>1b73c48</c>). نَفسُ حُجَّةِ <see cref="DealCancelResult"/>
    /// حَرفاً؛ والرُموزُ مِن المَعاجِمِ القائِمَة: <see cref="DealCancelAuthorization.DealNotFound"/>،
    /// <see cref="DealCancelAuthorization.DealNotActive"/>، <see cref="ChangedMeanwhile"/>.</para>
    /// </summary>
    public async Task<DealDisputeResult> DisputeAsync(
        string tenantSlug, Guid dealId, Guid actorId, string actorName,
        string reason, CancellationToken ct = default)
    {
        await using var s = _store.LightweightSession(tenantSlug);
        var deal = await s.LoadAsync<Deal>(dealId, ct);
        if (deal is null) return new(false, null, DealCancelAuthorization.DealNotFound);
        if (deal.Status != DealStatus.Active) return new(false, deal, DealCancelAuthorization.DealNotActive);
        deal.Status = DealStatus.Disputed;
        deal.UpdatedAt = DateTime.UtcNow;
        deal.Timeline.Add(new(deal.Stage, deal.Stage, "disputed",
            actorId, actorName, reason, DateTime.UtcNow));
        s.Store(deal);
        // تَعارُضُ النُسخَة (‏ADR-036): لَم يُكتَب النِزاع — يُعادُ ما في المَخزَنِ الآن (المُنادي الإداريُّ يَعودُ إلى الصَفقَة).
        // (‏2026-10-13 — ولا يُعادُ حَرفٌ فَوقَه: ومَعَه رَمزُه، فَلا يُكتَبُ تَدقيقٌ ويُرسَمُ الشَريط.)
        try { await s.SaveChangesAsync(ct); }
        catch (JasperFx.ConcurrencyException) { return new(false, await LoadAsync(tenantSlug, dealId, ct), ChangedMeanwhile); }
        return new(true, deal, null);
    }

    /// <summary>كَم مَرَّةً يُحاوَلُ الإلحاقُ حينَ تُعارِضُه نُسخَة (‏ADR-036 §٧): الأولى، وإعادَتانِ عَلى ما كُتِبَ بَعدَها.
    /// ثَلاثُ كِتاباتٍ أُخرى تَقَعُ كُلٌّ مِنها بَينَ تَحميلٍ وحِفظٍ مُتَتالِيَين عَطَبٌ لا ازدِحام — فَيَصعَدُ الاستِثناءُ ولا يُبتلَع.</summary>
    private const int AttachAttempts = 3;

    /// <summary>
    /// <para>اِربِط كائِناً خارِجيّاً (مَثَلاً PaymentId بَعد الدَّفع).</para>
    ///
    /// <para><b>ويُعادُ تَطبيقُه عِندَ تَعارُضِ النُسخَة — لا يُسقَط</b> (‏ADR-036 §٧، ‏2026-10-12؛ مُراجَعَةُ تَيّارِ S6a الأولى):
    /// المَرجِعُ يُسَجِّلُ أَثَراً وَقَعَ خارِجَ الصَفقَة — إذنَ دَفعٍ حَجَزَه المُزَوِّد، أَو تَقييماً كُتِب، أَو مالِكَ الإعلان — ولا
    /// يَتَّخِذُ قَراراً عَلى حالِها؛ فَإعادَتُه عَلى الوَثيقَةِ الَّتي كَتَبَها غَيرُه دَمجُ مِفتاحٍ لا يُخفي مَن سَبَق ولا يُطَبِّقُ
    /// قَراراً قَديماً (عِلَّةُ رَفضِ الإعادَةِ في §٤ لا تَنطَبِق). وكانَ التَعارُضُ يُعيدُ المَخزَّنَ بِلا المَرجِعِ ولا يَقرَؤُه
    /// مُنادٍ — فَإذنُ دَفعٍ في <c>/checkout/submit</c> يَضيعُ إن قَبِلَ البائِعُ المَربوطُ أَو أَلغى بَينَ تَحميلِ الإلحاقِ وحِفظِه،
    /// فَلا يُقبَضُ ولا يُستَرجَع. وكُلُّ مُحاوَلَةٍ بِجَلسَةٍ جَديدَةٍ تَقرَأُ آخِرَ ما كُتِب؛ وبَعدَ <see cref="AttachAttempts"/>
    /// يَصعَدُ الاستِثناءُ إلى المُنادي بِصَوت.</para>
    ///
    /// <para><b>وسَطرُه لِلطَرَفَينِ تَسمِيَةٌ لا نَصّ</b> (‏2026-10-13، G4 (xxvi)): فِعلُه <see cref="DealEvent.RefNote"/> وبادِئَتُه
    /// <see cref="DealRefLabels.AttachPrefix"/> ثابِتانِ يَقرَؤُهُما <see cref="DealRefLabels"/> — فَصَفحَةُ العُضوِ تَرسُمُ مِفتاحَ قامُوسٍ
    /// لِلمَرجِعِ المَعروفِ ولا تَرسُمُ الباقي، والقيمَةُ لا تُرسَمُ لَهُما. والنَصُّ المَكتوبُ بِحَرفِه كَما كان.</para>
    /// </summary>
    public async Task<Deal?> AttachRefAsync(
        string tenantSlug, Guid dealId, string key, string value,
        CancellationToken ct = default)
        => await RecordAsync(tenantSlug, dealId, deal =>
        {
            deal.Refs[key] = value;
            deal.Timeline.Add(new(deal.Stage, deal.Stage, DealEvent.RefNote, null, null,
                $"{DealRefLabels.AttachPrefix}{key}={value}", DateTime.UtcNow));
        }, ct);

    /// <summary>
    /// <para><b>دَفعٌ أَثبَتَته بَوّابَةُ المَتجَرِ يُكتَبُ عَلى صَفَقاتِه</b> (‏ADR-032 §٢‑هـ الخُطوَة 10، تَيّارُ S9، ‏2026-10-12): لِكُلِّ صَفقَةٍ في
    /// <paramref name="payment"/> — <c>Refs["payment_id"]</c> مُعَرِّفُ الفاتورَة، و<c>Refs["payment_status"] = Captured</c>، وسَطرُ
    /// <see cref="DealEvent.GatewayPaid"/> بِاسمِ مُشتَريها (نَصُّه مِن القامُوسِ عِندَ العَرض). <b>والحارِسُ في التَوقيع</b> (القاعِدَة ٦):
    /// <see cref="ProvenGatewayPayment"/> لا يُبنى إلّا بِـ<see cref="GatewayPaymentProof.Gate"/> — فَلا يُكتَبُ «مَدفوع» بِلا فاتورَةٍ
    /// عَبَرَت البَوّابَة.</para>
    ///
    /// <para><b>ومَرَّةً لا مَرَّتَين</b>: صَفقَةٌ «Captured» (<see cref="IsCaptured"/>) لا يُكتَبُ عَلَيها شَيء — لا حِفظَ ولا سَطر — فَالتَحَقُّقُ
    /// الثاني لِلفاتورَةِ نَفسِها لا يَكتُب، ولا يُدهَسُ مَرجِعُ دَفعٍ قُبِضَ بِغَيرِها. والكِتابَةُ بِحَلقَةِ <see cref="RecordAsync(string, Guid, Func{Deal, bool}, CancellationToken)"/>
    /// — تَقرَأُ ثُمَّ تُعيدُ عِندَ تَعارُضِ النُسخَة (‏ADR-036 §٧) — لا حِفظٌ ثانٍ في الخِدمَة. ولا تَسأَلُ عَن حالِ الصَفقَة: دَفعٌ وَقَعَ
    /// عَلى صَفقَةٍ أُلغِيَت يُكتَبُ ولا يُبتلَع، ومَعَه سَطرُ <see cref="DealEvent.GatewayRefundDue"/>.</para>
    ///
    /// <para>تُرجِعُ الصَفَقاتِ الَّتي كُتِبَ عَلَيها الآن وَحدَها — فَيُشعَرُ بائِعوها مَرَّة.</para>
    /// </summary>
    public async Task<IReadOnlyList<Deal>> ConfirmGatewayPaymentAsync(
        string tenantSlug, ProvenGatewayPayment payment, CancellationToken ct = default)
    {
        var confirmed = new List<Deal>();
        foreach (var id in payment.DealIds)
        {
            var wrote = false;
            var deal = await RecordAsync(tenantSlug, id, d =>
            {
                wrote = false;
                if (IsCaptured(d)) return false;
                var at = DateTime.UtcNow;
                d.Refs["payment_id"] = payment.InvoiceId;
                d.Refs["payment_status"] = nameof(ACommerce.Kit.Payments.PaymentStatus.Captured);
                d.Timeline.Add(new(d.Stage, d.Stage, DealEvent.GatewayPaid, d.InitiatorId, d.InitiatorName, payment.InvoiceId, at));
                if (d.Status == DealStatus.Cancelled)
                    d.Timeline.Add(new(d.Stage, d.Stage, DealEvent.GatewayRefundDue, d.InitiatorId, d.InitiatorName, payment.InvoiceId, at));
                return wrote = true;
            }, ct);
            if (wrote && deal is not null) confirmed.Add(deal);
        }
        return confirmed;
    }

    /// <summary>
    /// <para><b>تَسجيلُ أَثَرٍ وَقَعَ خارِجَ الصَفقَة — بِقِراءَةٍ وإعادَة</b> (‏ADR-036 §٧، والخُطوَة 2): حَلقَةُ
    /// <see cref="AttachRefAsync"/> نَفسُها بِحَرفِها، أُخرِجَت مِنها ‏2026-10-12 لِأَنّ مُستَهلِكَينِ آخَرَينِ وُلِدا بِالحاجَةِ نَفسِها:
    /// نَتيجَةُ الاستِرجاعِ في <see cref="CancelAsync"/> ونَتيجَةُ القَبضِ في <see cref="CaptureClaimedAsync"/> — كِلاهُما يُسَجِّلُ
    /// ما رَدَّه المُزَوِّدُ بَعدَ أَن حُفِظَت المُطالَبَة، ولا يَتَّخِذُ قَراراً عَلى حالِ الصَفقَة. فَهي الأُنبوبُ القائِمُ لا رابِع
    /// (القاعِدَة ٨)، وعَدُّ مَواضِعِ الحِفظِ في الخِدمَةِ باقٍ سِتّاً.</para>
    ///
    /// <para>كُلُّ مُحاوَلَةٍ بِجَلسَةٍ جَديدَةٍ تَقرَأُ آخِرَ ما كُتِبَ وتُطَبِّقُ <paramref name="record"/> عَلَيه؛ وبَعدَ
    /// <see cref="AttachAttempts"/> يَصعَدُ الاستِثناءُ إلى المُنادي بِصَوت.</para>
    /// </summary>
    private Task<Deal?> RecordAsync(
        string tenantSlug, Guid dealId, Action<Deal> record, CancellationToken ct)
        => RecordAsync(tenantSlug, dealId, deal => { record(deal); return true; }, ct);

    /// <summary>
    /// <para><b>الحَلقَةُ نَفسُها بِأَثَرٍ قَد لا يَكتُب</b> (‏ADR-032 الخُطوَة 10، ‏2026-10-12): <paramref name="record"/> تُرجِعُ <c>false</c> حينَ لا
    /// شَيءَ يُكتَبُ عَلى ما قُرِئ — فَلا حِفظَ ولا نُسخَةً تَتَقَدَّم، وتُعادُ الصَفقَةُ كَما هي. مُستَهلِكُها الأَوَّلُ
    /// <see cref="ConfirmGatewayPaymentAsync"/>: التَأكيدُ الثاني لِدَفعٍ واحِدٍ لا يَكتُبُ شَيئاً. والحِملُ بِـ<c>Action</c> أَعلاه يُحيلُ إلَيها
    /// بِـ<c>true</c> دائِماً — حِفظٌ واحِدٌ في الخِدمَةِ لِلإلحاقِ كَما كان، لا ثانٍ.</para>
    /// </summary>
    private async Task<Deal?> RecordAsync(
        string tenantSlug, Guid dealId, Func<Deal, bool> record, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var s = _store.LightweightSession(tenantSlug);
            var deal = await s.LoadAsync<Deal>(dealId, ct);
            if (deal is null) return null;
            if (!record(deal)) return deal;
            deal.UpdatedAt = DateTime.UtcNow;
            s.Store(deal);
            // تَعارُضُ النُسخَة (‏ADR-036 §٧): كَتَبَ غَيرُ هذا الطَلَبِ الصَفقَةَ بَعدَ تَحميلِها — يُعادُ الإلحاقُ عَلى ما كُتِب.
            // (وكانَ هُنا: «ومُنادوها اليَومَ يُلحِقونَ بِصَفقَةٍ وُلِدَت في الطَلَبِ نَفسِه لا يَعرِفُ مُعَرِّفَها غَيرُه» — ولَم يَصدُق:
            // نُقطَةُ تَقييمِ الاستوديو تُلحِقُ بِصَفقَةٍ قائِمَة، و`/checkout/submit` تُلحِقُ مَراجِعَ الدَفعِ بَعدَ أَن رُبِطَ البائِعُ
            // فَصارَت الصَفقَةُ في قائِمَتِه؛ ولا يُعادُ حَرفٌ فَوقَه.)
            try { await s.SaveChangesAsync(ct); return deal; }
            catch (JasperFx.ConcurrencyException) when (attempt < AttachAttempts) { }
        }
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

    /// <summary>
    /// <para><b>أَيُحَرِّكُ المَسارُ الإداريُّ هذِه الصَفقَة؟</b> (‏ADR-033 §٦‑ط مُلحَقاً، ‏2026-10-12) — دالَّةٌ <b>نَقِيَّة</b>
    /// تَقرَؤُها صَفحَةُ الاستوديو لِتَرسُمَ زِرَّ «→ التالي»، ونُقطَتُه <c>POST /studio/apps/{slug}/deals/{id}/advance</c> قَبلَ أَيِّ
    /// كِتابَة: فَلا يُرسَمُ زِرٌّ يَرُدُّه المُحَرِّكُ بِلا كَلِمَة (القاعِدَة ١٢)، ولا يُكتَبُ تَعيينٌ يَتلوه رَفض. كانَت الصَفحَةُ
    /// تَرسُمُه لِكُلِّ صَفقَةٍ لَها تالٍ (‏72 زِرّاً مَردوداً في مَصفوفَةِ ‏2304 حالَة، و‏27 مِنها تَكتُبُ قَبلَ أَن تُرَدّ).</para>
    ///
    /// <para><b>وهي تَسَلسُلُ النُقطَةِ نَفسُه بِلا كِتابَة</b>: الطَرَفُ بَعدَ خُطوَةِ التَعيين — القائِمُ، أَو مالِكُ التَطبيقِ
    /// ما لَم يَكُن المُبادِرَ (<see cref="AssignCounterpartyAsync"/> تَرُدُّه) — والفاعِلُ <c>CounterpartyId ?? appOwnerId</c>
    /// كَما تُحَرِّكُ النُقطَة، ثُمَّ بَوّابَةُ <see cref="AdvanceAsync"/> نَفسُها: صَفقَةٌ فَعّالَة، لَها تالٍ، وفاعِلُ المَرحَلَةِ
    /// المَدخولَةِ بِـ<c>IsActorAllowed</c> الواحِدَة. ومُطابَقَتُها لِلتَسَلسُلِ الحَقيقِيِّ مَقيسَةٌ حالَةً حالَةً في
    /// <c>DealStudioAdvanceDoorTests</c> — فَإن انجَرَفَ أَحَدُهُما أَحمَرَّ الصَفّ.</para>
    ///
    /// <para><b>وطَلَبٌ عِندَ OTO لا يُدخِلُه المَسارُ الإداريُّ «شَحن»</b> (مُراجَعَةُ تَيّارِ S7 الثانِيَة، ‏2026-10-12؛ ‏ADR-033 §٧‑ح): صارَت
    /// بَوّابَةُ الانتِقالِ تَرُدُّ بِـ<see cref="DealShipping.DealAwaitsCarrier"/> صَفقَةً تَصدُقُ عَلَيها <see cref="DealShipping.AwaitsCarrier"/>
    /// والمَسارُ لا يَحمِلُ جَوابَ OTO — فَتَسأَلُها هذِه أَيضاً، وإلّا رَسَمَ الاستوديو «→ شَحن» يُرَدُّ بِلا كَلِمَة. وكانَ يُخرِجُها إلى «شَحن»
    /// بِلا بوليصَة (تَوصيفُه <c>e52ba8f</c>).</para>
    ///
    /// <para><b>ولا فاتورَةُ بَوّابَةٍ لَم تُدفَع</b> (‏ADR-032 الخُطوَة 10، تَيّارُ S9، ‏2026-10-12 — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق): صارَت بَوّابَةُ
    /// الانتِقالِ تَرُدُّ بِـ<see cref="GatewayPayments.DealAwaitsPayment"/> صَفقَةً تَصدُقُ عَلَيها <see cref="GatewayPayments.AwaitsPayment"/> —
    /// فَتَسأَلُها هذِه أَيضاً، نَسَقُ سَطرِ OTO حَرفاً. مَقيسٌ قَبلَ سَطرِها في المَصفوفَةِ نَفسِها: ‏12 زِرّاً «→ دَفع» يُرسَمُ ويُرَدّ — حَيثُ
    /// مالِكُ التَطبيقِ هُوَ المُبادِرُ فَفاعِلُ «دَفع» هُوَ — وصِفرٌ بَعدَه.</para>
    ///
    /// <para><b>وصارَت «لا رَمزَ» مِن <see cref="AdminPathRefusal"/></b> (‏2026-10-13، مُراجَعَةُ المَوجَة 3a — G4 (i)؛ ولا يُعادُ حَرفٌ فَوقَ ما سَبَق):
    /// الشُروطُ نَفسُها بِتَرتيبِها، مَنقولَةً إلى الدالَّةِ الَّتي تَقولُ أَيُّها رَدّ — فَالصَفحَةُ تَرسُمُ بِهذِه، والنُقطَةُ تَحمِلُ رَمزَ تِلكَ إلى
    /// الصَفحَة، ولا مُسنَدانِ يَنجَرِفان.</para>
    /// </summary>
    public static bool AdminPathCanMove(Deal deal, Guid appOwnerId) => AdminPathRefusal(deal, appOwnerId) is null;

    /// <summary>
    /// <para><b>«لا يُحَرِّكُ المَسارُ الإداريُّ هذِه الخُطوَة»</b> — رَمزُ ما تَرُدُّه <see cref="AdminPathRefusal"/> حينَ لا مَرحَلَةَ تالِيَة،
    /// أَو حينَ يُؤَدّي الخُطوَةَ غَيرُ جانِبِ المَتجَر: خُطوَةُ المُشتَري (دُخولُ «دَفع» و«استِلام»)، أَو صَفقَةٌ بادَرَ بِها مالِكُ التَطبيقِ
    /// فَلا يُعَيَّنُ طَرَفَها الآخَر (صَفقَةُ البَذرَةِ التَجريبِيَّةِ عِندَ «→ حَجز» — سِجِلُّ «ب» S6a·١١ مَفتوحٌ لِلمالِكِ ولَم يُحسَم هُنا).
    /// (‏2026-10-13، مُراجَعَةُ المَوجَة 3a — G4 (i).)</para>
    /// </summary>
    public const string AdminPathCannotMove = "admin_path_cannot_move";

    /// <summary>
    /// <para><b>لِماذا لا يُحَرِّكُ المَسارُ الإداريُّ هذِه الصَفقَة — رَمزاً</b> (‏2026-10-13، مُراجَعَةُ المَوجَة 3a — G4 (i)): <see cref="AdminPathCanMove"/>
    /// هي «لا رَمز»، فَالدالَّةُ واحِدَةٌ والنُقطَةُ تَحمِلُ رَمزَها إلى صَفحَةِ الاستوديو بِـ<c>?err=</c> — وكانَت تَعودُ إلَيها بِلا كَلِمَة
    /// (‏ADR-033 §٦‑ط‑٨ «طَلَبٌ مَصنوعٌ يَعودُ بِلا كَلِمَة»؛ تَوصيفُه <c>StudioDealRefusalTests</c>). والشُروطُ بِتَرتيبِ المُسنَدِ القائِمِ حَرفاً:
    /// غَيرُ فَعّالَةٍ ⇒ <see cref="DealCancelAuthorization.DealNotActive"/>؛ لا تالٍ أَو فاعِلُ المَدخولَةِ غَيرُ جانِبِ المَتجَرِ ⇒
    /// <see cref="AdminPathCannotMove"/>؛ طَلَبُها عِندَ OTO ⇒ <see cref="DealShipping.DealAwaitsCarrier"/>؛ فاتورَتُها لَم تُدفَع ⇒
    /// <see cref="GatewayPayments.DealAwaitsPayment"/>. ونَصُّ كُلٍّ في القامُوسِ بِبادِئَةِ الاستوديو (<see cref="DealRefusalTexts.StudioKeyFor"/>).</para>
    /// <para>(‏2026-10-13، G5 (xv-g) — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق: وصَفقَةُ بِطاقَةٍ بِلا مَرجِعِ دَفعٍ تاليها «دَفع» ⇒ <see cref="PaymentMissing"/>، بَعدَ
    /// سَطرِ البَوّابَةِ بِتَرتيبِ <see cref="AdvanceAsync"/> نَفسِه. ولا يَبلُغُه المَسارُ الإداريُّ إلّا ومالِكُ التَطبيقِ مُبادِرُها: فاعِلُ «دَفع» المُبادِر.)</para>
    /// </summary>
    public static string? AdminPathRefusal(Deal deal, Guid appOwnerId)
    {
        if (deal.Status != DealStatus.Active) return DealCancelAuthorization.DealNotActive;
        if (DealsPolicy.RequiredActorToLeave(deal) is not { } required) return AdminPathCannotMove;
        var counterparty = deal.CounterpartyId ?? (deal.InitiatorId == appOwnerId ? null : appOwnerId);
        if (!IsActorAllowed(new Deal { InitiatorId = deal.InitiatorId, CounterpartyId = counterparty },
                deal.CounterpartyId ?? appOwnerId, required))
            return AdminPathCannotMove;
        if (DealShipping.AwaitsCarrier(deal)) return DealShipping.DealAwaitsCarrier;
        if (GatewayPayments.AwaitsPayment(deal)) return GatewayPayments.DealAwaitsPayment;
        if (MissesPayment(deal)) return PaymentMissing;   // ‏2026-10-13، G5 (xv-g)
        return null;
    }

    /// <summary>
    /// <para><b>أَيُخرِجُ هذا الفاعِلُ الصَفقَةَ مِن مَرحَلَتِها؟</b> — بَوّابَةُ <see cref="AdvanceAsync"/> نَفسُها بِلا
    /// كِتابَة: فَعّالَة، لَها تالٍ، و<see cref="IsActorAllowed"/> بِفاعِلِ المَرحَلَةِ المَدخولَة. تَقرَؤُها بَوّابَةُ الشَحنِ
    /// (‏<c>DealShipAuthorization</c>، ‏ADR-033 الخُطوَة 3) قَبلَ أَن تُنادى OTO — فَلا تُنشَأُ بوليصَةٌ لِصَفقَةٍ
    /// سَيَرُدُّ المُحَرِّكُ انتِقالَها، ولا يُلتَفُّ عَلى فاحِصِ الفاعِل: السِياسَةُ تُقَرِّر.</para>
    ///
    /// <para><b>ولا تَسأَلُ <see cref="DealShipping.AwaitsCarrier"/></b> (مُراجَعَةُ تَيّارِ S7 الثانِيَة، ‏2026-10-12): هي البَوّابَةُ لِمُنادٍ
    /// يَحمِلُ جَوابَ OTO إلى الانتِقال — مَسارُ الشَحن، يَسأَلُ OTO بَعدَها؛ ولَو سَأَلَتها لَرُدَّ استِئنافُ طَلَبٍ قائِمٍ قَبلَ أَن تُسأَلَ OTO.
    /// والأَبوابُ العامَّةُ تَسأَلُها في الانتِقالِ نَفسِه (<see cref="DealShipping.DealAwaitsCarrier"/>).</para>
    ///
    /// <para><b>دِقَّةٌ مُلحَقَةٌ ‏2026-10-13 (G4 (xxviii) و(xxxi)) — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق</b>: «بَوّابَةُ <see cref="AdvanceAsync"/> نَفسُها» صادِقَةٌ
    /// عَلى شَطرِها — الحالِ والتالي والفاعِل — و<see cref="AdvanceAsync"/> لا تُناديها: تُعيدُ الثَلاثَةَ بِسُطورِها (لِكُلِّ شَرطٍ رَدُّه النَصِّيُّ، وفاعِلُ
    /// الرَفضِ يُسَمّي الفاعِلَ المَطلوب)، ثُمَّ تَرُدُّ ثَلاثَةَ مُمسِكاتٍ لا تَسأَلُها هذِه: <see cref="DealShipping.AwaitsCarrier"/> بِلا جَوابٍ يُثبِتُ الشُحنَة،
    /// و<see cref="GatewayPayments.AwaitsPayment"/>، و<see cref="MissesPayment"/>. والتَكافُؤُ مَقيسٌ لا مَوصوف: <c>DealAdvanceGateGridTests</c> يَمشي
    /// الشَبَكَةَ (‏20736 حالَة) ويَشتَرِطُ «تُحَرِّكُ ⇔ <c>CanMove ∧ ¬AwaitsCarrier ∧ ¬AwaitsPayment ∧ ¬MissesPayment</c>». ومُنادِياها بابُ الشَحنِ (<c>DealShipping</c>) والوارِدُ
    /// مِن OTO (<c>OtoOrderStatus</c>) — وتاليهِما «شَحن» أَو «تَسليم» فَلا يَمَسُّهُما حارِسا «دَفع».</para>
    /// </summary>
    public static bool CanMove(Deal deal, Guid actorId)
        => deal.Status == DealStatus.Active
           && DealsPolicy.RequiredActorToLeave(deal) is { } required
           && IsActorAllowed(deal, actorId, required);

    static bool IsActorAllowed(Deal deal, Guid actorId, string required) => required switch
    {
        "initiator"    => deal.InitiatorId == actorId,
        "counterparty" => deal.CounterpartyId == actorId,
        "either"       => deal.InitiatorId == actorId || deal.CounterpartyId == actorId,
        "platform"     => true,   // يَستَدعيها كود مَنصَّة — افتَرِض السَّماح
        _              => false
    };
}

/// <summary>نَتيجَةُ التَقَدُّم. <paramref name="Code"/> رَمزُ رَفضٍ مِن مَعجَمٍ حَيثُ يوجَد — اليَومَ
/// <see cref="DealsService.ChangedMeanwhile"/> وَحدَه (‏ADR-036)؛ وسائِرُ الرَفضِ في <paramref name="Reason"/> نَصّاً كَما كان.
/// (‏2026-10-12، مُراجَعَةُ تَيّارِ S7 الثانِيَة — ولا يُعادُ حَرفٌ فَوقَه: ومَعَه <see cref="DealShipping.DealAwaitsCarrier"/>.)
/// (‏2026-10-13، G5 (xv-g) — ولا يُعادُ حَرفٌ فَوقَه: ومَعَهُما <see cref="GatewayPayments.DealAwaitsPayment"/> مُنذُ ‏ADR-032 الخُطوَة 10، و<see cref="DealsService.PaymentMissing"/>.)</summary>
public sealed record DealAdvanceResult(bool Ok, Deal? Deal, string? Reason, string? Code = null);

/// <summary>نَتيجَةُ فَتحِ النِزاع (‏2026-10-13، مُراجَعَةُ المَوجَة 3a — G4 (ii)): <paramref name="Ok"/> «كُتِبَ النِزاع» وَحدَه — فَعَلَيه يُشتَرَطُ
/// سَطرُ التَدقيق؛ و<paramref name="Code"/> رَمزُ الرَفضِ حَيثُ لَم يُكتَب، تَحمِلُه النُقطَةُ إلى صَفحَتِها (<see cref="DealRefusalTexts.StudioKeyFor"/>).
/// نَفسُ شَكلِ <see cref="DealAdvanceResult"/>.</summary>
public sealed record DealDisputeResult(bool Ok, Deal? Deal, string? Code);
