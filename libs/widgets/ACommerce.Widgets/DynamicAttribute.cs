namespace ACommerce.Widgets;

/// <summary>
/// قيمة سمة ديناميكية مخزّنة على لقطة كيان النطاق.
/// نمط Template+Snapshot: التسمية والنوع والأيقونة مجمّدة وقت الإنشاء.
/// </summary>
public class DynamicAttribute
{
    public string Key { get; set; } = default!;
    public string Label { get; set; } = default!;
    public string? LabelAr { get; set; }
    /// <summary>"text" | "number" | "decimal" | "bool" | "select" | "multi" | "date"</summary>
    public string Type { get; set; } = "text";
    /// <summary>القيمة الخام (string / number / bool / array). عند الإلغاء تكون null.</summary>
    public object? Value { get; set; }
    /// <summary>اسم أيقونة Bootstrap-icons / lucide بدون البادئة.</summary>
    public string? Icon { get; set; }
    /// <summary>وحدة عرض اختيارية ("م²", "غرفة", ...).</summary>
    public string? Unit { get; set; }
    /// <summary>صياغة قابلة للعرض مباشرة (لقيم select الموسومة بنص ثنائي اللغة).</summary>
    public string? DisplayValue { get; set; }
    public string? DisplayValueAr { get; set; }
    /// <summary>للترتيب داخل البطاقة/الشبكة.</summary>
    public int SortOrder { get; set; }
    /// <summary>هل تظهر في البطاقات المختصرة (chips). افتراضي false.</summary>
    public bool ShowInCard { get; set; }
}
