using ACommerce.Platform.Shared;
using Marten;
using System.Security.Cryptography;
using System.Text;

namespace ACommerce.Templates.Customer.Marketplace.Services;

/// <summary>
/// يَقرَأ <c>AttributeDefinitions + CategoryAttributeMappings + AttributeValues</c>
/// المُستَورَدَة كَـ <see cref="ImportedRecord"/> ويُحَوِّلها إلى قائِمَة
/// حُقول جاهِزَة لِلعَرض/الإدخال في صَفَحات إنشاء الإعلان وتَعديل
/// البروفايل. لا تَخزين إضافيّ ولا قَوالِب مُسبَقَة — يَعتَمِد كُلّيّاً
/// عَلى ما أَنتَجَه الـ Importer.
/// </summary>
public sealed class DynamicAttributesService
{
    private readonly IDocumentStore _store;

    public DynamicAttributesService(IDocumentStore store) => _store = store;

    /// <summary>Sentinel scope لِلبروفايل (مُتَّفَق عَليه بَين Ashare V3 و Ejar V1).</summary>
    public static readonly Guid ProfileScopeId = Guid.Parse("00000000-0000-0000-0000-000000000F01");

    /// <summary>
    /// scope id لِفِئَة إعلان.
    /// <list type="bullet">
    ///   <item>Ejar V1: MD5("ejar-listing:" + slug) (راجِع EjarListingScopes.DeriveScopeId).</item>
    ///   <item>Ashare V3: literal Guid مِن ProductCategory.Id المُطابِق لِلـ slug.
    ///         الـ Importer جَلَب ProductCategory كَ ImportedRecord — نَستَعلِم
    ///         الجَدول لِلعُثور على الـ Guid، وَنُسَكِّن ذاكِرَة لِكُلّ tenant.</item>
    /// </list>
    /// يُرجِع Guid.Empty إذا لَم نَستَطِع تَحديد الـ scope (فِئَة غَير مَعروفَة).
    /// </summary>
    public async Task<Guid> DeriveListingScopeAsync(string tenantSlug, string categorySlug)
    {
        if (tenantSlug == "ejar") return Md5Guid("ejar-listing:" + categorySlug.ToLowerInvariant());

        if (tenantSlug == "ashare")
        {
            // ProductCategory.Id الفِعلي مِن DB — نُسَكِّنه أوّل مَرَّة.
            if (!_ashareCategoryMap.TryGetValue(categorySlug.ToLowerInvariant(), out var id))
            {
                if (!_ashareMapLoaded)
                {
                    await using var s = _store.QuerySession(tenantSlug);
                    var pcs = await s.Query<ImportedRecord>()
                        .Where(r => r.Table == "ProductCategory")
                        .ToListAsync();
                    foreach (var r in pcs)
                    {
                        var slug = GetString(r, "Slug");
                        var pcid = GetGuid(r, "Id");
                        if (!string.IsNullOrEmpty(slug) && pcid != Guid.Empty)
                            _ashareCategoryMap[slug.ToLowerInvariant()] = pcid;
                    }
                    _ashareMapLoaded = true;
                    _ashareCategoryMap.TryGetValue(categorySlug.ToLowerInvariant(), out id);
                }
            }
            return id;
        }

        // tenant غَير مَعروف — احتِياط: نَفس مَنطِق Ejar.
        return Md5Guid(tenantSlug + ":listing:" + categorySlug.ToLowerInvariant());
    }

    private static readonly Dictionary<string, Guid> _ashareCategoryMap = new(StringComparer.OrdinalIgnoreCase);
    private static bool _ashareMapLoaded;

    public async Task<IReadOnlyList<DynField>> GetForListingCategoryAsync(string tenantSlug, string categorySlug)
    {
        var scope = await DeriveListingScopeAsync(tenantSlug, categorySlug);
        if (scope == Guid.Empty) return Array.Empty<DynField>();
        return await GetForScopeAsync(tenantSlug, scope);
    }

    public Task<IReadOnlyList<DynField>> GetForProfileAsync(string tenantSlug)
        => GetForScopeAsync(tenantSlug, ProfileScopeId);

    /// <summary>
    /// يُحَوِّل قاموس خام (Code → Value) إلى عَناصِر <see cref="ResolvedAttr"/>
    /// مَع تَسمِيَات عَرَبيَّة:
    /// <list type="bullet">
    ///   <item>Label يَأتي مِن AttributeDefinitions.Name (تَسمِيَة الحَقل).</item>
    ///   <item>DisplayValue يَأتي مِن AttributeValues.DisplayName حينَ
    ///         يَكون النَّوع SingleSelect/MultiSelect (تَرجَمَة القِيمَة:
    ///         "male" ⟶ "ذَكَر", "riyadh" ⟶ "الرِياض"). الأَنواع الحُرَّة
    ///         (Number/Text/…) تُعرَض كَما هي.</item>
    /// </list>
    /// مَفاتيح لا تَملِك تَعريفاً مُقابِلاً في DB تُرَدّ بِالـ Code كَ Label
    /// والـ Value كَما هو — لا نَفقِدها مِن الواجِهَة.
    /// </summary>
    public async Task<IReadOnlyList<ResolvedAttr>> ResolveLabelsAsync(
        string tenantSlug, IReadOnlyDictionary<string, string> attrs)
    {
        if (attrs.Count == 0) return Array.Empty<ResolvedAttr>();

        await using var s = _store.QuerySession(tenantSlug);
        var all = (await s.Query<ImportedRecord>()
            .Where(r => r.Table == "AttributeDefinitions" || r.Table == "AttributeValues")
            .ToListAsync()).ToList();

        var defs = all.Where(r => r.Table == "AttributeDefinitions").ToList();
        var defByCode = defs
            .Where(d => !string.IsNullOrEmpty(GetString(d, "Code")))
            .GroupBy(d => GetString(d, "Code")!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // AttributeValues: key = (DefId, Value) ⟶ DisplayName
        var valLabel = new Dictionary<(Guid DefId, string Value), string>();
        foreach (var v in all.Where(r => r.Table == "AttributeValues"))
        {
            var defId = GetGuid(v, "AttributeDefinitionId");
            var raw   = GetString(v, "Value") ?? GetString(v, "Code") ?? "";
            var disp  = GetString(v, "DisplayName") ?? GetString(v, "Name") ?? raw;
            if (defId != Guid.Empty && !string.IsNullOrEmpty(raw))
                valLabel[(defId, raw)] = disp;
        }

        var result = new List<ResolvedAttr>();
        foreach (var (code, value) in attrs)
        {
            if (string.IsNullOrEmpty(value)) continue;

            if (!defByCode.TryGetValue(code, out var d))
            {
                // مَفتاح بِلا تَعريف — نَعرِضه بِالـ Code كَتَسمِيَة
                // والقِيمَة الخام؛ يَكشِف لِلمُطَوِّر مَفاتيح غَير مَوصولَة.
                result.Add(new ResolvedAttr(code, code, value, DynFieldType.Text));
                continue;
            }

            var defId = GetGuid(d, "Id");
            var label = GetString(d, "Name") ?? code;
            var type  = ParseType(GetString(d, "Type"));

            string display = value;
            if (type == DynFieldType.SingleSelect)
            {
                if (valLabel.TryGetValue((defId, value), out var v)) display = v;
            }
            else if (type == DynFieldType.MultiSelect)
            {
                // قَد يَكون JSON array أَو CSV — نُجَرِّب الاثنَين.
                var items = TryParseList(value);
                display = string.Join("، ",
                    items.Select(it => valLabel.TryGetValue((defId, it), out var t) ? t : it));
            }
            else if (type == DynFieldType.Boolean)
            {
                display = value.Equals("true", StringComparison.OrdinalIgnoreCase) ? "نَعَم" : "لا";
            }

            result.Add(new ResolvedAttr(code, label, display, type));
        }
        return result;
    }

    private static List<string> TryParseList(string raw)
    {
        raw = raw.Trim();
        if (raw.StartsWith("["))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                    return doc.RootElement.EnumerateArray()
                        .Select(e => e.ValueKind == System.Text.Json.JsonValueKind.String
                                     ? e.GetString() ?? "" : e.GetRawText())
                        .Where(s => !string.IsNullOrEmpty(s)).ToList();
            }
            catch { }
        }
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private async Task<IReadOnlyList<DynField>> GetForScopeAsync(string tenantSlug, Guid scopeId)
    {
        await using var s = _store.QuerySession(tenantSlug);

        // كُلّ صُفوف الـ AttributeDefinitions + Mappings + Values — قِراءَة
        // كامِلَة ثُمّ فَلتَرَة في الذاكِرَة (الحَجم: مِئات الصُفوف بِالحَدّ
        // الأَقصى لِكُلّ tenant، يَكفي scale تَطبيق صَغير).
        var all = (await s.Query<ImportedRecord>()
            .Where(r => r.Table == "CategoryAttributeMappings"
                     || r.Table == "AttributeDefinitions"
                     || r.Table == "AttributeValues")
            .ToListAsync()).ToList();

        var mappings = all.Where(r => r.Table == "CategoryAttributeMappings").ToList();
        var defs     = all.Where(r => r.Table == "AttributeDefinitions").ToList();
        var values   = all.Where(r => r.Table == "AttributeValues").ToList();

        // الـ AttributeDefinitionIds الَّتي تَنطَبِق على هذا الـ scope.
        var defIdsForScope = mappings
            .Where(m => GetGuid(m, "CategoryId") == scopeId)
            .Select(m => (GetGuid(m, "AttributeDefinitionId"), GetInt(m, "SortOrder")))
            .OrderBy(t => t.Item2)
            .Select(t => t.Item1)
            .Where(g => g != Guid.Empty)
            .ToList();
        if (defIdsForScope.Count == 0) return Array.Empty<DynField>();

        // Definitions مُرَتَّبَة حَسَب نَفس تَرتيب الـ mapping.
        var defById = defs.ToDictionary(d => GetGuid(d, "Id"));
        var valuesByDef = values
            .Where(v => GetGuid(v, "AttributeDefinitionId") != Guid.Empty)
            .GroupBy(v => GetGuid(v, "AttributeDefinitionId"))
            .ToDictionary(g => g.Key,
                          g => g.OrderBy(v => GetInt(v, "SortOrder"))
                                .Select(v => new DynOption(
                                    Value: GetString(v, "Code") ?? GetGuid(v, "Id").ToString(),
                                    Label: GetString(v, "Name") ?? GetString(v, "Label") ?? "—"))
                                .ToList() as IReadOnlyList<DynOption>);

        var fields = new List<DynField>();
        foreach (var id in defIdsForScope)
        {
            if (!defById.TryGetValue(id, out var d)) continue;
            var code = GetString(d, "Code") ?? "";
            if (string.IsNullOrEmpty(code)) continue;
            fields.Add(new DynField(
                Code:        code,
                Label:       GetString(d, "Name") ?? code,
                Type:        ParseType(GetString(d, "Type")),
                IsRequired:  GetBool(d, "IsRequired"),
                DefaultValue: GetString(d, "DefaultValue"),
                Options:     valuesByDef.TryGetValue(id, out var opts) ? opts : Array.Empty<DynOption>()
            ));
        }
        return fields;
    }

    // ── ImportedRecord.Data helpers (Marten/STJ يُعيد JsonElement لِكلّ قيمَة) ──

    private static string? Raw(ImportedRecord r, string key)
    {
        if (!r.Data.TryGetValue(key, out var v) || v is null) return null;
        if (v is System.Text.Json.JsonElement el)
        {
            return el.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => el.GetString(),
                System.Text.Json.JsonValueKind.Null   => null,
                System.Text.Json.JsonValueKind.True   => "true",
                System.Text.Json.JsonValueKind.False  => "false",
                System.Text.Json.JsonValueKind.Number => el.GetRawText(),
                _ => el.GetRawText()
            };
        }
        return v.ToString();
    }
    private static Guid GetGuid(ImportedRecord r, string key)
        => Guid.TryParse(Raw(r, key), out var g) ? g : Guid.Empty;
    private static int GetInt(ImportedRecord r, string key)
        => int.TryParse(Raw(r, key), out var i) ? i : 0;
    private static bool GetBool(ImportedRecord r, string key)
        => bool.TryParse(Raw(r, key), out var b) && b;
    private static string? GetString(ImportedRecord r, string key) => Raw(r, key);

    private static DynFieldType ParseType(string? raw) => raw switch
    {
        "Number"       => DynFieldType.Number,
        "Boolean"      => DynFieldType.Boolean,
        "SingleSelect" => DynFieldType.SingleSelect,
        "MultiSelect"  => DynFieldType.MultiSelect,
        "LongText"     => DynFieldType.LongText,
        "Date"         => DynFieldType.Date,
        "DateTime"     => DynFieldType.Date,
        _              => DynFieldType.Text
    };

    private static Guid Md5Guid(string s)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(s));
        return new Guid(hash);
    }
}

public enum DynFieldType { Text, LongText, Number, Boolean, SingleSelect, MultiSelect, Date }
public sealed record DynOption(string Value, string Label);
public sealed record DynField(
    string Code,
    string Label,
    DynFieldType Type,
    bool IsRequired,
    string? DefaultValue,
    IReadOnlyList<DynOption> Options);

/// <summary>
/// سِمَة مُحَلَّلَة جاهِزَة لِلعَرض: التَّسمِيَة عَرَبيّاً + قِيمَة العَرض
/// (مُتَرجَمَة لِلـ SingleSelect/MultiSelect/Boolean).
/// </summary>
public sealed record ResolvedAttr(string Code, string Label, string DisplayValue, DynFieldType Type);
