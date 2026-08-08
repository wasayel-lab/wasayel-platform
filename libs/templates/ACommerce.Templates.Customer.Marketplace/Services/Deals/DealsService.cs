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

    /// <summary>نِسبَة عمولة المَنصَّة على قيمَة الصَّفقَة عِندَ الدَّفع.
    /// قابِلَة لِلتَّكوين لاحِقاً لِكُلّ مُستَأجِر/باقَة. حاليّاً ٢.٥٪.</summary>
    public const decimal PlatformCommissionRate = 0.025m;

    /// <summary>قائِمَة الصَّفقات على إعلانات يَملِكها مُستَخدِم مُعَيَّن
    /// (لِعَرض «عُروض على إعلاناتي»). يَعتَمِد على Listing.owner_id.</summary>
    public async Task<List<Deal>> ListForListingOwnerAsync(
        string tenantSlug, Guid ownerId, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);
        // الصَّفقات الَّتي صاحِبُها الطَّرَف الآخَر = المالِك، أَو الَّتي
        // لَم يُعَيَّن لَها طَرَف ثانٍ بَعد لكِنّ الإعلان لَه.
        var all = await s.Query<Deal>()
            .Where(d => d.CounterpartyId == ownerId)
            .OrderByDescending(d => d.UpdatedAt).Take(100).ToListAsync(ct);
        return all.ToList();
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
    /// عادَةً عِندَ مَرحَلَة Booked (بائِع يَقبَل، سائِق يَأخُذ الرحلَة، …).</summary>
    public async Task<Deal?> AssignCounterpartyAsync(
        string tenantSlug, Guid dealId,
        Guid counterpartyId, string counterpartyName,
        CancellationToken ct = default)
    {
        await using var s = _store.LightweightSession(tenantSlug);
        var deal = await s.LoadAsync<Deal>(dealId, ct);
        if (deal is null || deal.Status != DealStatus.Active) return deal;
        if (deal.CounterpartyId is not null) return deal;
        deal.CounterpartyId = counterpartyId;
        deal.CounterpartyName = counterpartyName;
        deal.UpdatedAt = DateTime.UtcNow;
        deal.Timeline.Add(new(deal.Stage, deal.Stage, "assigned",
            counterpartyId, counterpartyName, null, DateTime.UtcNow));
        s.Store(deal);
        await s.SaveChangesAsync(ct);
        return deal;
    }

    /// <summary>اِنتَقِل لِلمَرحَلَة التالِيَة في النَّمَط. يَفحَص أَنّ الفاعِل
    /// يَملِك حَقّ تَحريك هذه المَرحَلَة (initiator/counterparty/either).</summary>
    public async Task<DealAdvanceResult> AdvanceAsync(
        string tenantSlug, Guid dealId, Guid actorId, string actorName,
        string? note = null, CancellationToken ct = default)
    {
        await using var s = _store.LightweightSession(tenantSlug);
        var deal = await s.LoadAsync<Deal>(dealId, ct);
        if (deal is null) return new(false, null, "deal not found");
        if (deal.Status != DealStatus.Active)
            return new(false, deal, $"الصَّفقَة في حالَة {deal.Status} — لا يُمكِن المُتابَعَة.");

        var next = DealsPolicy.Next(deal.Pattern, deal.Stage);
        if (next is null)
            return new(false, deal, "هذه آخِر مَرحَلَة، لا تالٍ.");

        // فَحص الفاعِل: مَن المَسموح لَه بِتَحريك المَرحَلَة الحاليَّة؟
        var required = DealsPolicy.Actor(deal.Stage);
        if (!IsActorAllowed(deal, actorId, required))
            return new(false, deal, $"يَتَطَلَّب فاعِلاً مِن نَوع: {required}");

        var before = deal.Stage;
        deal.Stage = next.Value;
        deal.UpdatedAt = DateTime.UtcNow;
        deal.Timeline.Add(new(before, next.Value, "advanced",
            actorId, actorName, note, DateTime.UtcNow));

        // عِندَ الوُصول لِمَرحَلَة الدَّفع، اِحسُب عمولة المَنصَّة (نَموذَج
        // الإيراد: اشتِراك + عمولة تَشغيل). تُخزَّن على الصَّفقَة لِلتَّسوِيَة.
        if (next == DealStage.Paid && deal.CommissionSar is null && deal.AmountSar > 0)
        {
            deal.CommissionSar = Math.Round(deal.AmountSar * PlatformCommissionRate, 2);
            deal.Timeline.Add(new(next.Value, next.Value, "note", null, "المَنصَّة",
                $"عمولة {PlatformCommissionRate:P1} = {deal.CommissionSar} ر.س", DateTime.UtcNow));

            // اِسحَب الإذن المَحجوز في /checkout/submit (Authorize) →
            // Capture. لَو لا payment_id (دَفع نَقدا/COD) نَتَجاوَز.
            // فَشَل Capture لا يَكسِر التَّقَدُّم — يُسَجَّل لِلمُراجَعَة.
            if (deal.Refs.TryGetValue("payment_id", out var pid) && !string.IsNullOrEmpty(pid))
            {
                try
                {
                    var cr = await _payments.CaptureAsync(pid, deal.AmountSar, ct);
                    deal.Refs["payment_status"] = cr.Status.ToString();
                    deal.Timeline.Add(new(next.Value, next.Value, "captured",
                        actorId, actorName,
                        $"capture {cr.Status} {cr.AmountSar:0.00} SAR", DateTime.UtcNow));
                }
                catch (Exception ex)
                {
                    deal.Refs["capture_error"] = ex.Message;
                    deal.Timeline.Add(new(next.Value, next.Value, "capture_failed",
                        actorId, actorName, ex.Message, DateTime.UtcNow));
                }
            }
        }

        // اكتمال الـ Deal عِندَ Reviewed.
        if (next == DealStage.Reviewed && DealsPolicy.StagesFor(deal.Pattern).Last() == DealStage.Reviewed)
        {
            deal.Status = DealStatus.Completed;
        }

        s.Store(deal);
        await s.SaveChangesAsync(ct);
        return new(true, deal, null);
    }

    public async Task<Deal?> CancelAsync(
        string tenantSlug, Guid dealId, Guid actorId, string actorName,
        string reason, CancellationToken ct = default)
    {
        await using var s = _store.LightweightSession(tenantSlug);
        var deal = await s.LoadAsync<Deal>(dealId, ct);
        if (deal is null || deal.Status != DealStatus.Active) return deal;

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
        return deal;
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
