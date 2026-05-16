namespace ACommerce.Widgets;

/// <summary>
/// قالب سمات لفئة. يصف الحقول التي يجب أن تظهر في نموذج إنشاء/تعديل عرض في هذه الفئة،
/// إلى جانب أنواعها وقيودها وخياراتها (للحقول من نوع select).
///
/// هذا الكائن هو "المخطط" — تُسجّل لقطات منه على كل عرض كقائمة <see cref="DynamicAttribute"/>،
/// لذلك تغيير القالب لاحقاً لا يعدّل الإعلانات القديمة.
/// </summary>
public class AttributeTemplate
{
    public List<AttributeFieldDefinition> Fields { get; set; } = new();
}

public class AttributeFieldDefinition
{
    public string Key { get; set; } = default!;
    public string Label { get; set; } = default!;
    public string? LabelAr { get; set; }
    /// <summary>"text" | "number" | "decimal" | "bool" | "select" | "multi" | "date"</summary>
    public string Type { get; set; } = "text";
    public string? Icon { get; set; }
    public string? Unit { get; set; }
    public string? Placeholder { get; set; }
    public string? PlaceholderAr { get; set; }
    public bool Required { get; set; }
    public bool ShowInCard { get; set; }
    public int SortOrder { get; set; }
    /// <summary>للحقول الرقمية فقط.</summary>
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }
    /// <summary>قائمة الخيارات لـ select / multi.</summary>
    public List<AttributeOption> Options { get; set; } = new();
    /// <summary>قيمة افتراضية اختيارية.</summary>
    public object? Default { get; set; }
}

public class AttributeOption
{
    public string Value { get; set; } = default!;
    public string Label { get; set; } = default!;
    public string? LabelAr { get; set; }
    public string? Icon { get; set; }
}
