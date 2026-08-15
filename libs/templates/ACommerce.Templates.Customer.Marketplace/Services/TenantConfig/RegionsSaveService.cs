using ACommerce.Platform.Shared;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.TenantConfig;

/// <summary>مَناطِقُ الاكتِشاف كَما كَتَبَها المُستَخدِم — سَطرٌ لِكُلّ
/// مَدينَة، وأَحياؤُها بَعدَ <c>&gt;</c> يَفصِلُها فاصِلَة.</summary>
public sealed record RegionsSaveRequest(string Raw);

/// <summary>مَدينَةٌ وأَحياؤُها — ناتِجُ التَحليل، قَبلَ أَن يَصير
/// وَثائِق.</summary>
public sealed record ParsedCity(string Name, IReadOnlyList<string> Districts);

/// <summary>
/// <para><b>حِفظُ مَناطِق الاكتِشاف — وهُنا لَم يَكُن الانحِرافُ
/// تَكراراً، بَل عَطَباً حَيّاً.</b> المَسارانِ كانا يَكتُبانِ
/// <see cref="ImportedRecord"/> <b>بِشَكلَينِ غَير
/// مُتَوافِقَين</b>، وكُلّ صَفحَةِ قِراءَةٍ تَفهَم كاتِبَها
/// وَحدَه:</para>
///
/// <list type="bullet">
///   <item><c>/admin</c> كَتَبَ <c>Id="DiscoveryRegions/{guid}"</c>
///   و<c>SourceId={guid}</c>، و<c>Data</c> فيها
///   <c>Name/ParentId/Level</c> — و<b>لا</b> <c>Data.Id</c> ولا
///   <c>SortOrder</c>. وصَفحَةُ الإدارَة تُفَهرِس بِـ
///   <c>SourceId</c>.</item>
///   <item><c>/studio</c> كَتَبَ <c>Id={guid}</c> بِلا
///   <c>SourceId</c>، و<c>Data</c> فيها <c>Id/Level/ParentId/SortOrder</c>.
///   وصَفحَةُ الاستوديو تُفَهرِس بِـ<c>Data.Id</c> وتُرَتِّب
///   بِـ<c>SortOrder</c>.</item>
/// </list>
///
/// <para><b>فَالأَثَرُ المَقيس</b>: مَناطِقُ كُتِبَت مِن الإدارَة
/// تَظهَر في الاستوديو <b>بِمُدُنٍ بِلا أَحياء</b> (لِأَنّ
/// <c>Data.Id</c> غائِبٌ فَيَصير <c>Guid.Empty</c> ولا يُطابِقُه
/// <c>ParentId</c>)؛ ومَناطِقُ كُتِبَت مِن الاستوديو تَنهار في
/// الإدارَة إلى <b>مِفتاحٍ واحِد</b> (لِأَنّ <c>SourceId</c> فارِغٌ
/// لِلجَميع). صَفحَتانِ لِنَفس البَيان، وكُلُّ واحِدَةٍ تَكذِب على
/// كاتِبِ الأُخرى.</para>
///
/// <para><b>ولِذلك لَم يَغلِب طَرَف</b>: الشَكلُ هُنا <b>جامِع</b> —
/// <c>SourceId</c> و<c>Data.Id</c> و<c>Level</c> و<c>ParentId</c>
/// و<c>SortOrder</c> مَعاً. والبُرهانُ أَنَّه لَم تُعَدَّل صَفحَةُ
/// قِراءَةٍ واحِدَة: <c>Level</c> يَبقى نَصّاً فَيَقرَؤُه
/// <c>int.TryParse</c> في الصَفحَتَين، و<c>ParentId</c> يَبقى
/// <c>null</c> لِلمُدُن فَتَراهُ الإدارَةُ فارِغاً ويَراهُ
/// الاستوديو <c>Guid.Empty</c>.</para>
/// </summary>
public static class RegionsSaveService
{
    public const string AuditAction = "tenant.regions_save";

    public const string Table = "DiscoveryRegions";

    /// <summary><b>دالَّةُ القَرار، نَقِيَّة</b> (ق٣): نَصٌّ ← مُدُنٌ
    /// وأَحياء، أَو رَمزُ رَفض.</summary>
    public static (IReadOnlyList<ParsedCity>? Cities, string? Code) Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, TenantConfigCodes.Empty);

        var cities = new List<ParsedCity>();
        foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var l = line.Trim();
            if (l.Length == 0) continue;

            if (!l.Contains('>'))
            {
                cities.Add(new ParsedCity(l, Array.Empty<string>()));
                continue;
            }

            var parts = l.Split('>', 2);
            var cityName = parts[0].Trim();
            if (string.IsNullOrEmpty(cityName)) return (null, TenantConfigCodes.BadFormat);

            var districts = parts[1]
                .Split(new[] { '،', ',' },
                       StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(d => !string.IsNullOrEmpty(d))
                .ToArray();

            cities.Add(new ParsedCity(cityName, districts));
        }

        return cities.Count == 0
            ? (null, TenantConfigCodes.Empty)
            : (cities, null);
    }

    /// <summary><b>الشَكل الجامِع، دالَّةً نَقِيَّة</b> — فَيُفحَص
    /// بِلا قاعِدَةِ بَيانات أَنّ كُلّ مِفتاحٍ يَقرَؤُه أَيٌّ مِن
    /// القارِئَين مَوجود.</summary>
    public static List<ImportedRecord> ToRecords(IReadOnlyList<ParsedCity> cities, DateTime now)
    {
        var records = new List<ImportedRecord>();
        var cityOrder = 0;

        foreach (var city in cities)
        {
            var cityId = Guid.NewGuid().ToString();
            records.Add(Record(cityId, city.Name, parentId: null, level: 1, sortOrder: cityOrder++, now));

            var distOrder = 0;
            foreach (var d in city.Districts)
                records.Add(Record(Guid.NewGuid().ToString(), d, cityId, level: 2, sortOrder: distOrder++, now));
        }

        return records;
    }

    private static ImportedRecord Record(
        string id, string name, string? parentId, int level, int sortOrder, DateTime now) =>
        new()
        {
            // ‏Id بِبادِئَة الجَدوَل (شَكل /admin)، وSourceId مَملوء
            // (تُفَهرِس بِه صَفحَةُ الإدارَة).
            Id = $"{Table}/{id}",
            Table = Table,
            SourceId = id,
            ImportedAt = now,
            Data = new Dictionary<string, object?>
            {
                ["Id"]        = id,                  // تُفَهرِس بِه صَفحَةُ الاستوديو
                ["Name"]      = name,
                ["ParentId"]  = parentId,            // null لِلمَدينَة — يَقرَؤُه القارِئان
                ["Level"]     = level.ToString(),    // نَصّاً: int.TryParse يَقبَلُه في الصَفحَتَين
                ["SortOrder"] = sortOrder.ToString(),
            },
        };

    /// <summary>
    /// <para><b>الجَلسَة هُنا مَحصورَةٌ بِالمُستَأجِر</b> — ولِذلك لا
    /// <c>slug</c> في التَوقيع: الوَثيقَةُ مُتَعَدِّدَةُ الإيجار،
    /// والحَصرُ في الجَلسَة الَّتي تَفتَحُها النُقطَة. وتَمريرُ
    /// <c>slug</c> هُنا كانَ سَيوحي بِأَنّ الخِدمَةَ تَحصُر، وهي لا
    /// تَفعَل.</para>
    /// </summary>
    public static async Task<TenantConfigResult> SaveAsync(
        IDocumentSession session, RegionsSaveRequest r, CancellationToken ct = default)
    {
        var (cities, code) = Parse(r.Raw);
        if (code is not null) return TenantConfigResult.Reject(code);

        var existing = await session.Query<ImportedRecord>()
            .Where(x => x.Table == Table).ToListAsync(ct);
        foreach (var x in existing) session.Delete(x);

        foreach (var rec in ToRecords(cities!, DateTime.UtcNow))
            session.Store(rec);

        return TenantConfigResult.Saved;
    }
}
