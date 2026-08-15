using ACommerce.Platform.Shared;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.TenantConfig;

/// <summary>تَعريفُ خاصِّيَّةٍ واحِدَة كَما حُلِّلَ مِن سَطر:
/// <c>code | الاسم | النَوع | req|opt | قيمَة=تَسمِيَة، …</c>.</summary>
public sealed record ParsedAttribute(
    string Code, string Name, string Type, bool IsRequired,
    IReadOnlyList<(string Value, string Label)> Options);

/// <summary>
/// <para>خَصائِصُ نِطاقٍ واحِد. و<c>Scope</c> نَوعُه <c>Guid?</c> لا
/// <c>string</c> عَمداً: تَحويلُ نَصِّ النَموذَج إلى نَوعِه شَأنُ
/// المُهايِئ، ورَفضُ الغِياب شَأنُ الخِدمَة. وهذا يُغني السَطحَ عَن
/// تَحليلِ الـ<c>Guid</c> مَرَّتَين — مَرَّةً لِلمَنطِق ومَرَّةً
/// لِبِناء رابِط العَودَة.</para>
/// </summary>
public sealed record AttributesSaveRequest(Guid? Scope, string DefsRaw);

/// <summary>
/// <para><b>تَعريفاتُ الخَصائِص لِنِطاق — أَثقَلُ الأَزواج
/// السِتَّة</b> (‏140 سَطراً في <c>/admin</c> و‏135 في
/// <c>/studio</c>)، وأَقَلُّها انحِرافاً: رُموزُ الخَطَأ واحِدَة
/// والمَنطِقُ واحِد، والفَرقُ الحارِسُ والتَدقيقُ والمَسار.</para>
///
/// <para><b>والعَمَلِيَّة إعادَةُ كِتابَةٍ كامِلَة لِلنِطاق</b>:
/// تُحذَف رَوابِطُ النِطاق، ويُحذَف كُلّ تَعريفٍ صارَ يَتيماً (لا
/// نِطاقَ آخَر يَستَعمِلُه) مَعَ قيَمِه، ثُمَّ تُكتَب التَعريفاتُ
/// الجَديدَة. والحَذفُ اليَتيم مَقصود — وإلّا تَراكَمَت تَعريفاتٌ لا
/// يُشير إلَيها شَيء.</para>
///
/// <para><b>وثَلاثُ قِراءاتٍ لِلجَدوَل كامِلاً</b> تَبقى كَما هِيَ:
/// المُرشِّحاتُ تَقَع في الذاكِرَة لِأَنّ <c>Data</c> حَقلٌ حُرّ
/// (‏<c>JsonElement</c>)، وفَلتَرَتُه في SQL تَحتاج فَهرَسَةً لَيسَت
/// جُزءَ هذِه المَوجَة. نُقِلَ المَوضِعُ ولَم تُغَيَّر
/// الخوارِزمِيَّة — وذلك شَرطُ التَبديل لا نَقصُه.</para>
/// </summary>
public static class AttributesSaveService
{
    public const string AuditAction = "tenant.attributes_save";

    public const string DefinitionsTable = "AttributeDefinitions";
    public const string MappingsTable    = "CategoryAttributeMappings";
    public const string ValuesTable      = "AttributeValues";

    /// <summary><b>دالَّةُ القَرار، نَقِيَّة</b> (ق٣): نَصٌّ ←
    /// تَعريفات، أَو رَمزُ رَفض.</summary>
    public static (IReadOnlyList<ParsedAttribute>? Rows, string? Code) Parse(string defsRaw)
    {
        var rows = new List<ParsedAttribute>();

        foreach (var line in defsRaw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var l = line.Trim();
            if (l.Length == 0) continue;

            var parts = l.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length < 4) return (null, TenantConfigCodes.BadFormat);

            var code = parts[0];
            var name = parts[1];
            var type = parts[2];
            var required = parts[3].Equals("req", StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type))
                return (null, TenantConfigCodes.BadFormat);

            var opts = new List<(string Value, string Label)>();
            if (parts.Length >= 5 && !string.IsNullOrEmpty(parts[4]))
            {
                foreach (var pair in parts[4].Split(
                             new[] { '،', ',' },
                             StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var kv = pair.Split('=', 2);
                    if (kv.Length != 2) return (null, TenantConfigCodes.BadFormat);
                    opts.Add((kv[0].Trim(), kv[1].Trim()));
                }
            }

            rows.Add(new ParsedAttribute(code, name, type, required, opts));
        }

        return (rows, null);
    }

    /// <summary>الجَلسَة مَحصورَةٌ بِالمُستَأجِر — فَلا <c>slug</c> في
    /// التَوقيع (نَفس عِلَّة <see cref="RegionsSaveService"/>).</summary>
    public static async Task<TenantConfigResult> SaveAsync(
        IDocumentSession session, AttributesSaveRequest r, CancellationToken ct = default)
    {
        if (r.Scope is not { } scopeId) return TenantConfigResult.Reject(TenantConfigCodes.NoScope);

        var (rows, code) = Parse(r.DefsRaw);
        if (code is not null) return TenantConfigResult.Reject(code);

        var allMappings = await session.Query<ImportedRecord>()
            .Where(x => x.Table == MappingsTable).ToListAsync(ct);
        var allDefs = await session.Query<ImportedRecord>()
            .Where(x => x.Table == DefinitionsTable).ToListAsync(ct);
        var allValues = await session.Query<ImportedRecord>()
            .Where(x => x.Table == ValuesTable).ToListAsync(ct);

        var scopeMappings = allMappings.Where(m => GuidFromData(m, "CategoryId") == scopeId).ToList();
        var defIdsInScope = scopeMappings
            .Select(m => GuidFromData(m, "AttributeDefinitionId"))
            .Where(g => g != Guid.Empty).Distinct().ToList();
        foreach (var m in scopeMappings) session.Delete(m);

        var stillUsedDefs = allMappings
            .Where(m => GuidFromData(m, "CategoryId") != scopeId)
            .Select(m => GuidFromData(m, "AttributeDefinitionId"))
            .ToHashSet();
        var orphans = defIdsInScope.Where(id => !stillUsedDefs.Contains(id)).ToHashSet();
        if (orphans.Count > 0)
        {
            foreach (var d in allDefs)
                if (orphans.Contains(GuidFromData(d, "Id"))) session.Delete(d);
            foreach (var v in allValues)
                if (orphans.Contains(GuidFromData(v, "AttributeDefinitionId"))) session.Delete(v);
        }

        var now = DateTime.UtcNow;
        var order = 0;
        foreach (var row in rows!)
        {
            var defId = Guid.NewGuid();
            session.Store(new ImportedRecord
            {
                Id = $"{DefinitionsTable}/{defId}",
                Table = DefinitionsTable,
                SourceId = defId.ToString(),
                ImportedAt = now,
                Data = new Dictionary<string, object?>
                {
                    ["Id"]         = defId.ToString(),
                    ["Code"]       = row.Code,
                    ["Name"]       = row.Name,
                    ["Type"]       = row.Type,
                    ["IsRequired"] = row.IsRequired ? "true" : "false",
                },
            });
            session.Store(new ImportedRecord
            {
                Id = $"{MappingsTable}/{defId}-{scopeId}",
                Table = MappingsTable,
                SourceId = $"{defId}-{scopeId}",
                ImportedAt = now,
                Data = new Dictionary<string, object?>
                {
                    ["CategoryId"]            = scopeId.ToString(),
                    ["AttributeDefinitionId"] = defId.ToString(),
                    ["SortOrder"]             = order.ToString(),
                },
            });

            var voi = 0;
            foreach (var (value, label) in row.Options)
            {
                var vid = Guid.NewGuid();
                session.Store(new ImportedRecord
                {
                    Id = $"{ValuesTable}/{vid}",
                    Table = ValuesTable,
                    SourceId = vid.ToString(),
                    ImportedAt = now,
                    Data = new Dictionary<string, object?>
                    {
                        ["Id"]                    = vid.ToString(),
                        ["AttributeDefinitionId"] = defId.ToString(),
                        ["Value"]                 = value,
                        ["DisplayName"]           = label,
                        ["SortOrder"]             = voi.ToString(),
                    },
                });
                voi++;
            }
            order++;
        }

        return TenantConfigResult.Saved;
    }

    /// <summary>‏<c>Data</c> حَقلٌ حُرّ، فَقيمَتُه قَد تَعود
    /// <c>JsonElement</c> مِن Marten أَو <c>string</c> مِن الذاكِرَة —
    /// والقِراءَةُ تَقبَل الاثنَين. مَنقولَةٌ حَرفاً مِن النُقطَة.</summary>
    internal static Guid GuidFromData(ImportedRecord r, string key)
    {
        if (!r.Data.TryGetValue(key, out var v) || v is null) return Guid.Empty;
        string? str = v is System.Text.Json.JsonElement el
            ? (el.ValueKind == System.Text.Json.JsonValueKind.String ? el.GetString() : el.ToString())
            : v.ToString();
        return Guid.TryParse(str, out var g) ? g : Guid.Empty;
    }
}
