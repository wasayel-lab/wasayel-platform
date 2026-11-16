namespace ACommerce.Platform.Providers;

/// <summary>
/// <para><b>مَعجَمُ «كَيفَ يُعَدُّ حُضورُ المُزَوِّد»</b> (‏2026-11-13، الموجَة ١٣ W13-6·١٦ — ‏ADR-051 §٢٠؛ التَصميم §7 و§3.4) — أَربَعُ قِيَمٍ لا
/// خامِسَةَ لَها، والجانِبُ جانِبُ التِقنِيَة: ما فَعَّلَه المَتجَرُ يُعَدُّ حُضوراً كُلَّ يَوم، وما يَفعَلُه يُعَدُّ عَمَلِيَّةً عِندَ نِدائِه.</para>
/// <para><b>قارِئُ القيمَةِ في وَقتِ التَشغيل</b> لَقطُ الحُضورِ اليَومِيُّ (البَند ١٩): يَختارُ بِها وَحدَةَ كُلِّ رَبط. وإلى أَن يُبنى فَقارِئُها بَوّابَةُ الإقلاعِ
/// وَحدَها (<see cref="ProviderDefinitionLoader"/>) — دَينٌ مُعلَنٌ في §٢٠ لا يُبتَلَع (القاعِدَة ١).</para>
/// </summary>
public static class ProviderMeterPresences
{
    /// <summary>رَبطُ مَتجَرٍ نَشِطٌ يُعَدُّ حُضوراً لِلمَتجَرِ كُلَّ يَوم (<c>presence.provider_binding</c>) — مُزَوِّدٌ يَربِطُه المُستَأجِرُ بِاعتِمادِه.</summary>
    public const string StoreBinding = "store_binding";

    /// <summary>مَداخِلُ البائِعينَ النَشِطَةُ تُعَدُّ حُضوراً بِعَدَدِها (<c>presence.vendor_binding</c>) — اعتِمادُ المَنَصَّةِ وتَحتَه حُقولٌ يَملَؤُها البائِع.</summary>
    public const string VendorBinding = "vendor_binding";

    /// <summary>اعتِمادُ المَنَصَّةِ وَحدَه: لا رَبطَ لِمَتجَرٍ يُعَدُّ حُضوراً، والمُزَوِّدُ يُعَدُّ عِندَ نِدائِه.</summary>
    public const string Platform = "platform";

    /// <summary>لا حُضورَ يُعَدّ — قيمَةٌ تُكتَبُ صَراحَةً لا غِياب.</summary>
    public const string None = "none";

    /// <summary>المَعجَمُ بِتَرتيبِه.</summary>
    public static readonly IReadOnlyList<string> All = new[] { StoreBinding, VendorBinding, Platform, None };

    private static readonly HashSet<string> Set = new(All, StringComparer.Ordinal);

    public static bool Contains(string? presence) => presence is not null && Set.Contains(presence);
}

/// <summary>شَكلُ القاعِدَة ٤ حَرفاً: رَمزٌ ثابِتٌ ورِسالَةٌ عَرَبِيَّة.</summary>
public sealed record ProviderMeterViolation(string Code, string MessageAr);

/// <summary>خانَةٌ في مَصفوفَةِ «الحُضورُ × الاعتِماد»: اسمُها، ووَصفُ الاعتِمادِ الَّذي يُدخِلُ إلَيها، وما يُقبَلُ فيها مِن الحُضور.</summary>
public sealed record ProviderMeterCell(string Name, string CredentialAr, IReadOnlyList<string> Allowed);

/// <summary>
/// <para><b>مُصادِقُ مِترِ المُزَوِّد — ثَلاثَةُ رُموزِ خَرق، ولِكُلٍّ اختِبارٌ موجِبٌ وسالِب</b> (القاعِدَة ٤؛ التَصميم §7). <b>دالَّةٌ مُنفَصِلَةٌ لا عَلَم</b>:
/// <see cref="ProviderDefinitionValidator"/> بِرُموزِه الاثنَي عَشَرَ لا يُمَسّ، فَتَعريفٌ تَركيبِيٌّ بِلا مِترٍ يَبقى صالِحاً عِندَه (اختِباراتُه وكاتالوجُه بِلا
/// حَرف)، والمِترُ يُسأَلُ هُنا — يَسأَلُهُما المُحَمِّلُ مَعاً عِندَ الإقلاع.</para>
///
/// <para><b>والمَصفوفَةُ مَكتوبَةٌ هُنا جَدوَلاً</b> (<see cref="Matrix"/>) لا مُشتَقَّةً مِن اسم: نَوعُ الاعتِمادِ يُدخِلُ التَعريفَ خانَتَه، والخانَةُ تَقبَلُ ما فيها
/// وتَرُدُّ ما سِواه بِـ<c>meter_presence_contradicts_credential</c>:</para>
/// <code>
/// الاعتِماد                                  الحُضورُ المَقبول
/// none                                       none
/// platform_key بِلا حَقل                     platform · none
/// platform_key وتَحتَه حَقلٌ فَأَكثَر          vendor_binding · none      (حُقولُ بائِعٍ تَحتَ اعتِمادِنا: حِسابُ الشَحن)
/// يَربِطُه مُستَأجِر (IsTenantBindable)       store_binding · none       (مِفتاحٌ سِرِّيٌّ أَو رابِطُ دَفع)
/// </code>
/// <para><b>ولا يُجيبُ عَن سُؤالِ المالِك</b> (§١٧ س٣: عَمَلِيّاتُ مَن يَحمِلُ الاعتِمادَ تُفَوتَرُ أَم لِلرُؤيَة؟): يَقولُ أَيُّ حُضورٍ يُعَدّ، ومَن يَحمِلُ الاعتِمادَ
/// مُشتَقٌّ مِن التَعريفِ والرَبطِ كَما كانَ.</para>
/// </summary>
public static class ProviderMeterValidator
{
    /// <summary>الرُموزُ الثَلاثَة — مُعلَنَةً لِيُقاسَ أَنّ لِكُلٍّ اختِبارَين.</summary>
    public static readonly IReadOnlyList<string> Codes = new[]
    {
        "meter_missing",
        "meter_presence_out_of_vocabulary",
        "meter_presence_contradicts_credential",
    };

    /// <summary>‏<c>none</c>: لا شَيءَ يُحمَل، فَلا شَيءَ يُعَدُّ حُضوراً.</summary>
    public static readonly ProviderMeterCell NoCredential =
        new("no_credential", "نَوعُ الاعتِماد none", new[] { ProviderMeterPresences.None });

    /// <summary>‏<c>platform_key</c> بِلا حَقل: اعتِمادُنا وَحدَه.</summary>
    public static readonly ProviderMeterCell PlatformKeyAlone =
        new("platform_key", "platform_key بِلا حَقل", new[] { ProviderMeterPresences.Platform, ProviderMeterPresences.None });

    /// <summary>‏<c>platform_key</c> وتَحتَه حَقلٌ فَأَكثَر: اعتِمادُنا السوقِيُّ وحُقولٌ يَملَؤُها البائِع.</summary>
    public static readonly ProviderMeterCell PlatformKeyWithVendorFields =
        new("platform_key_vendor_fields", "platform_key وتَحتَه حَقلٌ فَأَكثَر يَملَؤُه البائِع",
            new[] { ProviderMeterPresences.VendorBinding, ProviderMeterPresences.None });

    /// <summary>نَوعٌ يَربِطُه مُستَأجِر (<see cref="ProviderDefinition.IsTenantBindable"/>) — أَيُّ نَوعٍ غَيرِ <c>none</c> و<c>platform_key</c>.</summary>
    public static readonly ProviderMeterCell TenantBindable =
        new("tenant_bindable", "نَوعٌ يَربِطُه المُستَأجِرُ مِن شاشَتِه", new[] { ProviderMeterPresences.StoreBinding, ProviderMeterPresences.None });

    /// <summary>المَصفوفَةُ كامِلَةً بِتَرتيبِ الجَدوَلِ أَعلاه.</summary>
    public static readonly IReadOnlyList<ProviderMeterCell> Matrix = new[] { NoCredential, PlatformKeyAlone, PlatformKeyWithVendorFields, TenantBindable };

    /// <summary>خانَةُ التَعريفِ مِن نَوعِ اعتِمادِه وعَدَدِ حُقولِه — مِن البَياناتِ لا مِن اسمِه.</summary>
    public static ProviderMeterCell CellOf(ProviderDefinition d) =>
        d.IsTenantBindable ? TenantBindable
        : d.Credential.Kind != CredentialKinds.PlatformKey ? NoCredential
        : d.Credential.Fields.Count == 0 ? PlatformKeyAlone
        : PlatformKeyWithVendorFields;

    /// <summary>كُلُّ خُروقِ المِتر (‏ADR-038: مُصادِقُ بَياناتٍ يُجيبُ بِكُلِّ خُروقِه) — والثَلاثَةُ مُتَعاقِبَةٌ بِطَبعِها: لا مُفرَداتَ تُفحَصُ بِلا مِتر، ولا
    /// خانَةَ تُسأَلُ عَن قيمَةٍ خارِجَ المَعجَم.</summary>
    public static IReadOnlyList<ProviderMeterViolation> Validate(ProviderDefinition d)
    {
        var v = new List<ProviderMeterViolation>();
        var presence = d.Meter?.Presence;

        if (string.IsNullOrWhiteSpace(presence))
        {
            v.Add(new("meter_missing",
                $"المُزَوِّد «{d.Slug}» بِلا مِتر — كُلُّ تَعريفٍ يُعلِنُ كَيفَ يُعَدُّ حُضورُه (\"meter\": {{ \"presence\": … }})، " +
                $"و«{ProviderMeterPresences.None}» قيمَةٌ تُكتَبُ لا غِياب."));
            return v;
        }

        if (!ProviderMeterPresences.Contains(presence))
        {
            v.Add(new("meter_presence_out_of_vocabulary",
                $"حُضورُ «{d.Slug}» = «{presence}» خارِجَ مَعجَم ProviderMeterPresences. " +
                $"المَعجَم: {string.Join("، ", ProviderMeterPresences.All)}."));
            return v;
        }

        var cell = CellOf(d);
        if (!cell.Allowed.Contains(presence, StringComparer.Ordinal))
            v.Add(new("meter_presence_contradicts_credential",
                $"حُضورُ «{d.Slug}» = «{presence}» يُناقِضُ اعتِمادَه ({cell.CredentialAr}) — " +
                $"المَقبولُ في خانَتِه: {string.Join("، ", cell.Allowed)}."));

        return v;
    }

    public static bool IsValid(ProviderDefinition d) => Validate(d).Count == 0;
}
