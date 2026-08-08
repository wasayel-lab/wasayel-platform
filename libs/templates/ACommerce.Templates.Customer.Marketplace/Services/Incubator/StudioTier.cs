using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.Incubator;

/// <summary>حُدود الباقَة لِمُدَّة ٣٠ يَوم. <c>int.MaxValue</c> = بِلا حَدّ.</summary>
public sealed record TierLimits(
    string Tier, string LabelAr, int MonthlyPriceSar,
    int AnalysesPerMonth, int RefinesPerMonth, int StoresMax,
    bool AllowExport, bool AllowCustomPattern);

public static class TierCatalog
{
    public static readonly IReadOnlyDictionary<string, TierLimits> All = new Dictionary<string, TierLimits>
    {
        ["spark"]  = new("spark",  "Spark",   99,  AnalysesPerMonth: 1, RefinesPerMonth: 3,
                         StoresMax: 1, AllowExport: false, AllowCustomPattern: false),
        ["lite"]   = new("lite",   "Lite",    199, AnalysesPerMonth: 3, RefinesPerMonth: 10,
                         StoresMax: 3, AllowExport: true,  AllowCustomPattern: false),
        ["growth"] = new("growth", "Growth",  399, AnalysesPerMonth: 10, RefinesPerMonth: 50,
                         StoresMax: 10, AllowExport: true, AllowCustomPattern: true),
        ["scale"]  = new("scale",  "Scale",   999, AnalysesPerMonth: int.MaxValue,
                         RefinesPerMonth: int.MaxValue, StoresMax: int.MaxValue,
                         AllowExport: true, AllowCustomPattern: true),
    };

    public static TierLimits For(string tier)
        => All.TryGetValue(tier, out var t) ? t : All["spark"];
}

/// <summary>
/// خِدمَة الـ tier gates — تَفحَص الحُدود قَبل العَمَلِيّات وتَكتُب الـ
/// counters. كُلّ ٣٠ يَوم تُعاد الفَترَة تِلقائيّاً.
/// </summary>
public sealed class StudioTierService
{
    private readonly IDocumentStore _store;
    public StudioTierService(IDocumentStore store) => _store = store;

    public async Task<StudioUser?> GetUserAsync(Guid userId, CancellationToken ct = default)
    {
        await using var qs = _store.QuerySession(StudioAuth.Tenant);
        return await qs.LoadAsync<StudioUser>(userId, ct);
    }

    /// <summary>يُحَمِّل المُستَخدِم ويُعيد فَترَته إن انتَهَت (>٣٠ يَوم).</summary>
    public async Task<(StudioUser User, TierLimits Limits)> LoadWithLimitsAsync(
        Guid userId, CancellationToken ct = default)
    {
        await using var s = _store.LightweightSession(StudioAuth.Tenant);
        var user = await s.LoadAsync<StudioUser>(userId, ct)
                   ?? throw new InvalidOperationException("user not found");
        if ((DateTime.UtcNow - user.PeriodStart).TotalDays >= 30)
        {
            user.PeriodStart = DateTime.UtcNow;
            user.AnalysesUsed = 0;
            user.RefinesUsed = 0;
            s.Store(user);
            await s.SaveChangesAsync(ct);
        }
        return (user, TierCatalog.For(user.Tier));
    }

    public sealed record GateCheck(bool Allowed, int Used, int Limit, string? Reason);

    public async Task<GateCheck> CheckAnalyzeAsync(Guid uid, CancellationToken ct = default)
    {
        var (u, l) = await LoadWithLimitsAsync(uid, ct);
        if (u.AnalysesUsed >= l.AnalysesPerMonth)
            return new(false, u.AnalysesUsed, l.AnalysesPerMonth,
                "بَلَغتَ حَدّ تَحاليل هذه الباقَة لِهذا الشَّهر.");
        return new(true, u.AnalysesUsed, l.AnalysesPerMonth, null);
    }

    public async Task<GateCheck> CheckRefineAsync(Guid uid, CancellationToken ct = default)
    {
        var (u, l) = await LoadWithLimitsAsync(uid, ct);
        if (u.RefinesUsed >= l.RefinesPerMonth)
            return new(false, u.RefinesUsed, l.RefinesPerMonth,
                "بَلَغتَ حَدّ التَّحسينات لِهذا الشَّهر.");
        return new(true, u.RefinesUsed, l.RefinesPerMonth, null);
    }

    public async Task<GateCheck> CheckBuildAsync(Guid uid, CancellationToken ct = default)
    {
        var (u, l) = await LoadWithLimitsAsync(uid, ct);
        if (u.StoresBuilt >= l.StoresMax)
            return new(false, u.StoresBuilt, l.StoresMax,
                "بَلَغتَ حَدّ التَّطبيقات لِهذه الباقَة.");
        return new(true, u.StoresBuilt, l.StoresMax, null);
    }

    public Task RecordAnalysisAsync(Guid uid, CancellationToken ct = default)
        => Bump(uid, u => u.AnalysesUsed++, ct);

    public Task RecordRefineAsync(Guid uid, CancellationToken ct = default)
        => Bump(uid, u => u.RefinesUsed++, ct);

    public Task RecordStoreBuiltAsync(Guid uid, CancellationToken ct = default)
        => Bump(uid, u => u.StoresBuilt++, ct);

    private async Task Bump(Guid uid, Action<StudioUser> mutate, CancellationToken ct)
    {
        await using var s = _store.LightweightSession(StudioAuth.Tenant);
        var u = await s.LoadAsync<StudioUser>(uid, ct);
        if (u is null) return;
        mutate(u);
        s.Store(u);
        await s.SaveChangesAsync(ct);
    }
}
