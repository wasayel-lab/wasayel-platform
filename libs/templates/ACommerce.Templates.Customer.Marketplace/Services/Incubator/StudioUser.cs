namespace ACommerce.Templates.Customer.Marketplace.Services.Incubator;

/// <summary>
/// مُستَخدِم على مُستَوى المَنصَّة (صاحِب مَشروع/فِكرَة) — قَبل أَن يَملِك
/// متجراً. وَثيقَة Marten تَحت tenant ثابِت "_studio". المُصادَقَة وَهميَّة:
/// أَيّ رَقم هاتِف + الرَّمز "123456". الـ Id هو OwnerUserId المُستَخدَم في
/// <see cref="IncubatorSession"/>.
/// </summary>
public sealed class StudioUser
{
    public Guid Id { get; set; }
    public string Phone { get; set; } = "";
    public string FullName { get; set; } = "صاحِب المَشروع";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastLoginAt { get; set; } = DateTime.UtcNow;

    // ─── Tier / billing ─────────────────────────────────────────────
    /// <summary>الباقَة الحالِيَّة: spark | lite | growth | scale.</summary>
    public string Tier { get; set; } = "spark";

    /// <summary>بِدايَة فَترَة الحِصَّة الحاليَّة. الحِصَّة تُعاد كُلّ ٣٠ يَوم.</summary>
    public DateTime PeriodStart { get; set; } = DateTime.UtcNow;

    public int AnalysesUsed { get; set; }
    public int RefinesUsed { get; set; }
    public int StoresBuilt { get; set; }

    /// <summary>مُشرِف المَنصَّة — يَستَطيع الوُصول لِـ /admin ولوحَة
    /// المُراقَبَة. أَوَّل مُستَخدِم يُسَجِّل يَحصُل عَلَيها تِلقائيّاً (ميزَة
    /// MVP لِيَكون لَدَيك platform-admin مَوجود مِن البِدايَة).</summary>
    public bool IsPlatformAdmin { get; set; }
}
