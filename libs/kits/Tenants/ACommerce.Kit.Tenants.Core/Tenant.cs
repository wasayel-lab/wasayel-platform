namespace ACommerce.Kit.Tenants;

/// <summary>
/// سِجِلّ المُستَأجِرين — وَثيقَة Marten عَلى مُستَوى المنصّة (غير
/// مَحصورَة بـ tenancy). تُمَثِّل المُسَمَّيات الإداريّة لكلّ tenant
/// (slug، اسم، لَون، فِئات). كلّ بَيانات المُستَأجِر الأَخرى (الإعلانات،
/// الرَسائِل…) تَعيش في streams مَحصورَة بـ slug عَبر Marten conjoined
/// tenancy.
/// </summary>
public sealed class Tenant
{
    /// <summary>Marten primary key. نَستَخدِم الـ slug نَفسه لِيَكون
    /// التَوصُّل مُباشَراً عَبر URL slug بدون فَهرَسَة ثانيَة.</summary>
    public string Id { get; set; } = "";

    public string Slug => Id;
    public string Name { get; set; } = "";
    public string BrandColor { get; set; } = "#7C3AED";
    public string City { get; set; } = "";
    public string TagLine { get; set; } = "";
    /// <summary>"phone" أو "nafath" — يَختار التَطبيق طَريقَة الدُخول
    /// المُتاحَة لِهذا المُستَأجِر. صَفحَة Login تَقرَأ هذه القيمَة وتَعرِض
    /// واجهَة واحِدَة (لا tabs).</summary>
    public string AuthChannel { get; set; } = "phone";
    public List<Category> Categories { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class Category
{
    public string Slug { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Icon { get; set; }

    /// <summary>تَجميع رَئيسي (residential / commercial / events / vehicles / leisure / …).
    /// يُستَخدَم لِعَرض الفِئات على شَكل شَجَرَة مَجموعَة بِالـ Kind في
    /// المُستَأجِرين الَّذين يَحتَوون فِئات كَثيرَة (مَثَل إيجار). فارِغ
    /// يَعني فِئَة رَئيسيَّة بِلا تَجميع.</summary>
    public string Kind { get; set; } = "";

    /// <summary>slug الفِئَة الأَب — لِشَجَرَة حَقيقيَّة (parent → leaves).
    /// null = جَذر.</summary>
    public string? ParentSlug { get; set; }

    public int SortOrder { get; set; }

    public List<AttributeField> Attributes { get; set; } = new();
}

/// <summary>سِمَة ديناميكيّة في قَالِب الفِئَة (مثلاً غُرَف نَوم، مَساحَة).</summary>
public sealed class AttributeField
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>"text" | "number" | "bool" | "select"</summary>
    public string Type { get; set; } = "text";
    public List<string> Options { get; set; } = new();
}

// ─── Commands ─────────────────────────────────────────────────────────
public sealed record CreateTenant(
    string Slug, string Name, string BrandColor, string City, string TagLine);

public sealed record AddCategory(
    string TenantSlug, string CategorySlug, string Label, string? Icon,
    List<AttributeField>? Attributes);
