namespace ACommerce.Templates.Customer.Marketplace.Services.Incubator;

/// <summary>
/// سِجِلّ مُوافَقَة المُستَخدِم على شُروط الحاضِنَة (المَرحَلَة ٧).
/// يُحفَظ تَحت tenant "_studio". وُجود سِجِلّ بِنَفس <c>Version</c> الحالي
/// = المُستَخدِم وافَق. تَغيير الـ Version يُجبِر القَبول مَرَّة أُخرى.
/// </summary>
public sealed class ConsentRecord
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public int Version { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string Ip { get; set; } = "";
    public string UserAgent { get; set; } = "";
}

public static class ConsentPolicy
{
    /// <summary>إصدار الشُروط الحاليّ — بَدِّلها لِتُجبِر القَبول مَرَّة أُخرى.</summary>
    public const int CurrentVersion = 1;
}
