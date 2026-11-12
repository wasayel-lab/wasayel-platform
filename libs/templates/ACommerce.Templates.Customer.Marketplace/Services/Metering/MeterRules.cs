using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;

namespace ACommerce.Templates.Customer.Marketplace.Services.Metering;

/// <summary>وَثيقَةٌ في وَحدَةِ العَمَلِ قَبلَ الحِفظ، وهَل هي <b>وِلادَة</b> — يُقَرِّرُه المُستَمِعُ مِن التَغييراتِ المُعَلَّقَةِ وَحدَها
/// (وَثيقَةٌ مُتَفائِلَةٌ بِلا نُسخَة: <c>VersionFor == null</c>، مَقيس D6) لا مِن حالَةٍ مَخزونَة.</summary>
public sealed record PendingDocument(object Document, bool Created);

/// <summary>ما يَراه المُصَنِّفُ مِن الجَلسَة: مُستَأجِرُها والوَثائِقُ المُعَلَّقَة — قِيَمٌ لا جَلسَة.</summary>
public sealed record PendingSnapshot(string SessionTenant, IReadOnlyList<PendingDocument> Documents);

/// <summary>
/// <para><b>المُصَنِّفُ النَقِيّ</b> (التَصميم §5.1): تَغييراتٌ مُعَلَّقَة + لَقطَةُ الحامِل + المُعجَم ← سُطورُ الدَفتَر. لا جَلسَةَ ولا قاعِدَةَ ولا وَقت —
/// والمُدخَلُ نَفسُه يُعطي الخُرجَ نَفسَه، فَيُختَبَرُ بِلا قاعِدَة (<c>MeterRulesTests</c>).</para>
/// <para><b>ولا يَرمي لِفَجوَةِ تَصنيف</b>: بُعدٌ لا يُعرَفُ يُكتَبُ <see cref="MeterSentinels.Unresolved"/> فَيُعَدُّ ويُعرَض؛ وكُلُّ عَمودٍ قيمَةٌ أَو قيمَةٌ
/// حارِسَة، فَلا يُفشِلُ <c>NOT NULL</c> كِتابَةَ عَمَلٍ أَبَداً (§5.5).</para>
/// <para><b>تَرتيبُ الأَبعاد</b> (§5.4): مِن التَغييرِ أَوَّلاً (المَتجَرُ = <c>Deal.TenantSlug</c>)، ثُمَّ مِن الحامِلِ <b>إن كانَ لِلمَتجَرِ نَفسِه</b>
/// أَو كانَ حامِلَ البَذر، وإلّا <see cref="MeterSentinels.Unresolved"/>.</para>
/// <para><b>الوَحدَةُ اليَوم واحِدَة</b> (الهَيكَلُ الماشي، القاعِدَة ٩): <c>deal.create</c> — صَفقَةٌ وُلِدَت في هذا الحِفظ. وتَحديثُها لا يُعَدّ
/// (الانتِقالاتُ وَحدَةٌ لاحِقَة، §5.6).</para>
/// </summary>
public static class MeterRules
{
    public const string DealCreate = "deal.create";

    /// <summary>نَوعُ مَصدَرِ <c>deal.create</c> — ومُعَرِّفُه مُعَرِّفُ الصَفقَة (لا مُعَرِّفُ شَخص).</summary>
    public const string DealSource = "deal";

    public static IReadOnlyList<UsageEntry> Classify(PendingSnapshot pending, MeterScopeView? scope, MeterUnitCatalog catalog)
    {
        var entries = new List<UsageEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in pending.Documents)
        {
            if (p is { Document: Deal deal, Created: true } && catalog.Find(DealCreate) is { } unit)
            {
                var entry = Entry(unit, string.IsNullOrWhiteSpace(deal.TenantSlug) ? MeterSentinels.Unresolved : deal.TenantSlug,
                                  DealSource, deal.Id.ToString("D"), scope);
                if (seen.Add(entry.Id)) entries.Add(entry);
            }
        }
        return entries;
    }

    private static UsageEntry Entry(MeterUnit unit, string app, string sourceType, string sourceId, MeterScopeView? scope)
    {
        var (entrepreneur, role) = FromScope(app, scope);
        var feature = string.IsNullOrWhiteSpace(unit.Feature) || MeterFeatureAnchors.IsAnchor(unit.Feature)
            ? MeterSentinels.Unresolved : unit.Feature;
        return new UsageEntry(UsageEntryId.For(unit.Code, app, sourceId), unit.Code, 1, app, entrepreneur, role,
                              feature, MeterSentinels.None, sourceType, sourceId);
    }

    /// <summary>رائِدُ الأَعمالِ والدَورُ مِن الحامِل — <b>فَقَط</b> إن كانَ لِلمَتجَرِ نَفسِه أَو حامِلَ بَذر؛ فَنِطاقُ المَتجَرِ أ لا يَنسِبُ كِتابَةَ ب.</summary>
    public static (string Entrepreneur, string Role) FromScope(string app, MeterScopeView? scope)
    {
        var applies = scope is not null
            && (scope.IsSeed || (scope.App is not null && string.Equals(scope.App, app, StringComparison.OrdinalIgnoreCase)));
        return (applies && !string.IsNullOrEmpty(scope!.Entrepreneur) ? scope.Entrepreneur : MeterSentinels.Unresolved,
                applies && !string.IsNullOrEmpty(scope!.Role) ? scope.Role : MeterSentinels.Unresolved);
    }
}
