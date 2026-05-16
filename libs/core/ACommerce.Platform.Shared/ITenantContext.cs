namespace ACommerce.Platform.Shared;

/// <summary>
/// المُستَأجِر النَشِط في طَلَب الـ HTTP الحاليّ. يَملَؤه middleware
/// عَن طَريق slug في الـ URL (مثل <c>/ashare/...</c>) ثُمّ يَستَهلِكه
/// أيّ handler أو صَفحَة لاحقاً. scoped في DI حَتى يَكون مَحصوراً
/// بالطَلَب.
/// </summary>
public interface ITenantContext
{
    string Slug { get; }
    string Name { get; }
    string BrandColor { get; }
    bool IsResolved { get; }
}

/// <summary>
/// قابِل للتَعديل من middleware فقط. الـ handlers تَستَهلِك
/// <see cref="ITenantContext"/> فقط (مَنع التَعديل في طَبَقات لاحقَة).
/// </summary>
public sealed class TenantContext : ITenantContext
{
    public string Slug { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string BrandColor { get; private set; } = "#000000";
    public bool IsResolved { get; private set; }

    public void Resolve(string slug, string name, string brandColor)
    {
        Slug = slug;
        Name = name;
        BrandColor = brandColor;
        IsResolved = true;
    }
}
