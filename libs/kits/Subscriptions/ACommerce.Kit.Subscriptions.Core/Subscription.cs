namespace ACommerce.Kit.Subscriptions;

/// <summary>خُطّة (Plan) — وَثيقَة عامّة على مُستَوى المُستَأجِر.</summary>
public sealed class Plan
{
    public string Id { get; set; } = "";            // slug، مثلاً "free", "basic", "pro"
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public int ListingsQuota { get; set; }          // عَدَد الإعلانات المَسموح بها شَهريّاً
    public int DaysPeriod { get; set; } = 30;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>اشتِراك مُستَخدِم — event-sourced لِتَتَبُّع استِهلاك الحِصَّة.</summary>
public sealed class Subscription
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string PlanId { get; set; } = "";
    public int QuotaRemaining { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public string Status { get; set; } = "active";   // active | expired | cancelled

    public void Apply(SubscriptionCreated e)
    { Id = e.Id; UserId = e.UserId; PlanId = e.PlanId; QuotaRemaining = e.Quota;
      StartsAt = e.At; EndsAt = e.At.AddDays(e.DaysPeriod); Status = "active"; }
    public void Apply(QuotaConsumed e) => QuotaRemaining -= e.Amount;
    public void Apply(QuotaRefunded e) => QuotaRemaining += e.Amount;
    public void Apply(SubscriptionRenewed e) { QuotaRemaining = e.Quota; EndsAt = e.NewEndsAt; Status = "active"; }
    public void Apply(SubscriptionCancelled e) => Status = "cancelled";
    public void Apply(SubscriptionExpired e) => Status = "expired";
}

public sealed record SubscriptionCreated(Guid Id, Guid UserId, string PlanId, int Quota, int DaysPeriod, DateTime At);
public sealed record QuotaConsumed(Guid SubscriptionId, string Resource, int Amount, DateTime At);
public sealed record QuotaRefunded(Guid SubscriptionId, string Resource, int Amount, DateTime At);
public sealed record SubscriptionRenewed(Guid Id, int Quota, DateTime NewEndsAt, DateTime At);
public sealed record SubscriptionCancelled(Guid Id, string? Reason, DateTime At);
public sealed record SubscriptionExpired(Guid Id, DateTime At);

public sealed record StartSubscription(Guid UserId, string PlanId);
public sealed record ConsumeQuota(Guid SubscriptionId, string Resource, int Amount = 1);
public sealed record CancelSubscription(Guid SubscriptionId, string? Reason);
