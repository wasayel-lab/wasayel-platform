using System.Text.Json;

namespace ACommerce.Widgets;

/// <summary>
/// أدوات تحويل وتركيب لقطات السمات الديناميكية.
/// تتعامل مع كل من DynamicAttribute (لقطة على العرض) و AttributeTemplate (مخطط الفئة).
/// </summary>
public static class DynamicAttributeHelper
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static List<DynamicAttribute> ParseAttributes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<DynamicAttribute>>(json, Json) ?? new(); }
        catch { return new(); }
    }

    public static string SerializeAttributes(IEnumerable<DynamicAttribute>? attrs)
        => JsonSerializer.Serialize(attrs ?? Enumerable.Empty<DynamicAttribute>(), Json);

    public static AttributeTemplate? ParseTemplate(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<AttributeTemplate>(json, Json); }
        catch { return null; }
    }

    public static string SerializeTemplate(AttributeTemplate? template)
        => JsonSerializer.Serialize(template ?? new AttributeTemplate(), Json);

    /// <summary>
    /// يدمج قيم نموذج إدخال (key→raw value) مع قالب الفئة لإنتاج لقطات مكتملة المعنى.
    /// تُهمل المفاتيح غير المعرّفة في القالب، وتُضاف الحقول المطلوبة الفارغة كـ null.
    /// </summary>
    public static List<DynamicAttribute> BuildSnapshot(
        AttributeTemplate template,
        IDictionary<string, object?> values)
    {
        var result = new List<DynamicAttribute>();
        foreach (var f in template.Fields)
        {
            values.TryGetValue(f.Key, out var raw);
            var attr = new DynamicAttribute
            {
                Key = f.Key,
                Label = f.Label,
                LabelAr = f.LabelAr,
                Type = f.Type,
                Icon = f.Icon,
                Unit = f.Unit,
                ShowInCard = f.ShowInCard,
                SortOrder = f.SortOrder,
                Value = raw
            };

            if (f.Type is "select" or "multi" && raw != null)
                ApplyOptionLabel(attr, f, raw);

            result.Add(attr);
        }
        return result;
    }

    private static void ApplyOptionLabel(DynamicAttribute attr, AttributeFieldDefinition field, object raw)
    {
        if (field.Type == "select")
        {
            var match = field.Options.FirstOrDefault(o => o.Value == raw.ToString());
            if (match != null)
            {
                attr.DisplayValue = match.Label;
                attr.DisplayValueAr = match.LabelAr;
                if (string.IsNullOrEmpty(attr.Icon)) attr.Icon = match.Icon;
            }
        }
        else if (field.Type == "multi" && raw is IEnumerable<object> list)
        {
            var matched = list.Select(v => field.Options.FirstOrDefault(o => o.Value == v?.ToString())).Where(o => o != null).ToList();
            attr.DisplayValue   = string.Join(", ", matched.Select(o => o!.Label));
            attr.DisplayValueAr = string.Join("، ", matched.Select(o => o!.LabelAr ?? o.Label));
        }
    }
}
