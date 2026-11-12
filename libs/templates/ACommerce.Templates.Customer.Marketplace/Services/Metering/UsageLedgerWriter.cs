using ACommerce.Kit.Tenants;
using ACommerce.Platform.Shared.Metering;
using Marten;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ACommerce.Templates.Customer.Marketplace.Services.Metering;

/// <summary>
/// <para><b>كاتِبُ الصادِر</b> (‏2026-11-12، W13-6·١١ — ADR-051 §١٨؛ التَصميم §6.2) — <see cref="OutboundRecorder"/> الَّذي يَسأَلُ عَنه <see cref="ProviderCalls"/>.
/// <b>مُراقِبٌ عَلى اتِّصالِه لا مُحَوِّلٌ في أُنبوب</b> (القاعِدَة ٧): لا يَرُدُّ نِداءً ولا يَرمي، وما يَتَعَذَّرُ عَلَيه سَطرٌ بِاسمِه.</para>
/// <list type="number">
/// <item><b>النِيَّةُ قَبلَ النِداء</b> (<see cref="OpenAsync"/>): <c>pending</c> في مُعامَلَتِها القَصيرَة؛ وفَشَلُها ⇐ <c>[meter] outbound-intent-failed …</c> ويَمضي النِداء.</item>
/// <item><b>النَتيجَةُ بَعدَه</b> (<see cref="DecideAsync"/>، بِلا رَمزِ إلغاءِ المُنادي): المَقبولُ <b>سَطرٌ ونَتيجَةٌ في مُعامَلَةٍ واحِدَة</b> عَلى اتِّصالِ الكاتِب؛
/// والمَرفوضُ والمَجهولُ نَتيجَةٌ عَلى النِيَّةِ وَحدَها، والمَجهولُ يَطبَعُ سَطراً واحِداً <c>[meter] outbound-unknown …</c>. ويُعادُ مَرَّةً، ثُمَّ
/// <c>[meter] outbound-unrecorded …</c> وتَبقى النِيَّةُ مُعَلَّقَةً تَعُدُّها المُطابَقَة.</item>
/// </list>
/// <para><b>والأَبعادُ</b>: رائِدُ الأَعمالِ والدَورُ مِن الحامِلِ حينَ يَكونُ لِمَتجَرِ النِداءِ (<see cref="MeterRules.FromScope"/> بِقاعِدَتِه)، وإلّا فَرائِدُ الأَعمالِ
/// <c>Tenant.OwnerUserId</c> لِلمَتجَرِ مِن ذاكِرَةِ الحَلّالِ ذاتِ الدَقائِقِ الخَمس (المِفتاحُ نَفسُه، فَلا قِراءَةَ حينَ قَرَأَ الوَسيط) والدَورُ <c>_unresolved</c>؛
/// و<see cref="MeterSentinels.Platform"/> رائِدُ أَعمالِه نَفسُه. <b>والقِراءَةُ الوَحيدَةُ لِحالَةٍ مَخزونَةٍ هُنا</b> — في مُراقِبٍ عَلى اتِّصالِه، لا في المُستَمِع.</para>
/// </summary>
public sealed class UsageLedgerWriter(IDocumentStore store, IMemoryCache cache, ILogger<UsageLedgerWriter> logger) : OutboundRecorder
{
    /// <summary>نَوعُ مَصدَرِ السَطرِ الصادِر — ومُعَرِّفُه مُعَرِّفُ الطَلَب.</summary>
    public const string RequestSource = "request";

    /// <summary>ذاكِرَةُ المالِك: ذاكِرَةُ الحَلّالِ بِحَرفِها (<c>TenantResolverMiddleware</c>: المِفتاحُ <c>tenant:{slug}</c> لِخَمسِ دَقائِق) — فَانجِرافُ المالِكِ بَعدَ نَقلِه
    /// خَمسُ دَقائِقَ كَما في الحِفظ (التَصميم §16 الخَطَر ٨).</summary>
    internal static readonly TimeSpan OwnerMemory = TimeSpan.FromMinutes(5);

    public override async Task OpenAsync(ProviderCall call, CancellationToken ct)
    {
        try
        {
            await store.Storage.Database.EnsureStorageExistsAsync(typeof(UsageLedgerFeature), ct);
            await using var conn = store.Storage.Database.CreateConnection();
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(MeterSql.IntentOpen, conn);
            Bind(cmd, call);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning("[meter] outbound-intent-failed unit={Unit} app={App} request={Request}: {Error}",
                call.Unit, call.App, call.RequestId, ex.GetType().Name);
        }
    }

    public override async Task DecideAsync(ProviderCall call, ProviderAcceptance acceptance)
    {
        if (acceptance.Outcome == ProviderOutcome.Unknown)
            logger.LogWarning("[meter] outbound-unknown unit={Unit} app={App} request={Request}", call.Unit, call.App, call.RequestId);
        var scope = MeterScope.View();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await WriteAsync(call, acceptance, scope);
                return;
            }
            catch (Exception ex) when (attempt < 2)
            {
                logger.LogDebug("[meter] outbound-retry unit={Unit} request={Request}: {Error}", call.Unit, call.RequestId, ex.GetType().Name);
            }
            catch (Exception ex)
            {
                logger.LogWarning("[meter] outbound-unrecorded unit={Unit} app={App} request={Request} outcome={Outcome}: {Error}",
                    call.Unit, call.App, call.RequestId, acceptance.Outcome, ex.GetType().Name);
                return;
            }
        }
    }

    private async Task WriteAsync(ProviderCall call, ProviderAcceptance acceptance, MeterScopeView? scope)
    {
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(UsageLedgerFeature), CancellationToken.None);
        await using var conn = store.Storage.Database.CreateConnection();
        await conn.OpenAsync(CancellationToken.None);

        if (acceptance.Outcome != ProviderOutcome.Accepted)
        {
            await using var decide = new NpgsqlCommand(MeterSql.IntentDecide, conn);
            Bind(decide, call);
            decide.Parameters.AddWithValue("outcome", acceptance.Outcome == ProviderOutcome.Refused ? "refused" : "unknown");
            await decide.ExecuteNonQueryAsync(CancellationToken.None);
            return;
        }

        var entry = await EntryAsync(call, acceptance, scope);
        await using var tx = await conn.BeginTransactionAsync(CancellationToken.None);
        await using (var insert = new NpgsqlCommand(MeterSql.InsertPositional, conn, tx))
        {
            foreach (var value in new object[] { entry.Id, entry.Unit, entry.Quantity, entry.App, entry.Entrepreneur, entry.Role, entry.Feature,
                                                 entry.Provider, entry.SourceType, entry.SourceId, Generation() })
                insert.Parameters.Add(new NpgsqlParameter { Value = value });
            await insert.ExecuteNonQueryAsync(CancellationToken.None);
        }
        await using (var accept = new NpgsqlCommand(MeterSql.IntentAccept, conn, tx))
        {
            Bind(accept, call);
            await accept.ExecuteNonQueryAsync(CancellationToken.None);
        }
        await tx.CommitAsync(CancellationToken.None);
    }

    /// <summary>سَطرُ النِداءِ المَقبول — مُعَرِّفُه مُعَرِّفُ النِيَّة، ومِيزَتُه مِن صَفِّ الوَحدَة، ومَصدَرُه مُعَرِّفُ الطَلَب.</summary>
    internal async Task<UsageEntry> EntryAsync(ProviderCall call, ProviderAcceptance acceptance, MeterScopeView? scope)
    {
        var (entrepreneur, role) = MeterRules.FromScope(call.App, scope);
        if (entrepreneur == MeterSentinels.Unresolved)
            entrepreneur = call.App == MeterSentinels.Platform ? MeterSentinels.Platform : await OwnerAsync(call.App);
        var feature = MeterUnitCatalog.Default.Find(call.Unit)?.Feature;
        if (string.IsNullOrWhiteSpace(feature) || MeterFeatureAnchors.IsAnchor(feature)) feature = MeterSentinels.Unresolved;
        return new UsageEntry(call.Id, call.Unit, acceptance.Quantity, call.App, entrepreneur, role, feature, call.Provider, RequestSource, call.RequestId);
    }

    /// <summary>مالِكُ المَتجَرِ مِن ذاكِرَةِ الحَلّال، وإلّا قِراءَةٌ واحِدَةٌ تَملَؤُها؛ ومَتجَرٌ لا وَثيقَةَ لَه — أَو قيمَةٌ حارِسَة — <c>_unresolved</c>.</summary>
    private async Task<string> OwnerAsync(string app)
    {
        if (string.IsNullOrWhiteSpace(app) || MeterSentinels.IsSentinel(app)) return MeterSentinels.Unresolved;
        var key = $"tenant:{app.ToLowerInvariant()}";
        if (!cache.TryGetValue(key, out Tenant? tenant) || tenant is null)
        {
            await using var session = store.QuerySession();
            tenant = await session.LoadAsync<Tenant>(app.ToLowerInvariant());
            if (tenant is not null) cache.Set(key, tenant, OwnerMemory);
        }
        return tenant is null ? MeterSentinels.Unresolved : MeterScope.OwnerOf(tenant.OwnerUserId);
    }

    /// <summary>الجيلُ الَّذي خَبَّأَه الإقلاعُ في مُستَمِعِ هذا المَخزَن — و<c>0</c> قَبلَه، فَيَأخُذُ الإدخالُ آخِرَ جيلٍ مَفتوح (<see cref="MeterSql.Insert"/>).</summary>
    private int Generation() => (store as DocumentStore)?.Options.Listeners.OfType<UsageListener>().FirstOrDefault()?.Generation ?? 0;

    private static void Bind(NpgsqlCommand cmd, ProviderCall call)
    {
        cmd.Parameters.AddWithValue("id", call.Id);
        cmd.Parameters.AddWithValue("unit", call.Unit);
        cmd.Parameters.AddWithValue("provider", call.Provider);
        cmd.Parameters.AddWithValue("app", call.App);
        cmd.Parameters.AddWithValue("request", call.RequestId);
    }
}
