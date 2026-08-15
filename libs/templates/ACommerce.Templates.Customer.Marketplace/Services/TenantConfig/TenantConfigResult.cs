namespace ACommerce.Templates.Customer.Marketplace.Services.TenantConfig;

/// <summary>
/// <para><b>ماذا حَدَثَ لِعَمَلِيَّة إعداد مُستَأجِر — بِثَلاث حالاتٍ
/// لا رابِعَة.</b> والخِدمَة تُرجِع هذا ولا تَرمي: الرَفضُ لِمُدخَلٍ
/// غَير صالِح <b>نَتيجَةٌ مُتَوَقَّعَة</b> لا استِثناء، ورَميُه
/// يَجعَل مَسارَ الخَطَأ الشائِع يَمُرّ بِآلَةٍ صُمِّمَت
/// لِلنادِر.</para>
///
/// <para><b>ولِماذا لا <c>bool</c> ولا سِلسِلَة</b>: السَطح يَحتاج أَن
/// يُفَرِّق ثَلاثاً — حُفِظ (‏أَعِد التَوجيه بِـ<c>saved=1</c>)،
/// ورُفِضَ بِرَمز (‏أَعِد التَوجيه بِـ<c>err=</c>)، والمُستَأجِر
/// غَير مَوجود (‏أَعِد إلى الجَذر). و<c>bool</c> يَخلِط
/// الأَخيرَتَين، والسِلسِلَة تَفتَح المُعجَم فَيَصير كُلّ سَطحٍ
/// يَختَرِع رَمزَه — وهُوَ بِعَينِه الانحِراف الَّذي وُحِّدَ هُنا.</para>
/// </summary>
public enum TenantConfigStatus
{
    /// <summary>المُدخَل صالِح وطُبِّق على الجَلسَة — والإيداع على
    /// النُقطَة، لا هُنا.</summary>
    Saved,

    /// <summary>مُدخَلٌ غَير صالِح — <c>Code</c> عُضوٌ في
    /// <see cref="TenantConfigCodes"/> ولا شَيءَ سِواه.</summary>
    Rejected,

    /// <summary>لا مُستَأجِرَ بِهذا الـ<c>slug</c>. مُتَمَيِّزَةٌ عَن
    /// الرَفض لِأَنّ عَرضَها يَختَلِف: لا رِسالَةَ خَطَأٍ في صَفحَةٍ
    /// لا مُستَأجِرَ لَها.</summary>
    TenantMissing,
}

/// <summary>نَتيجَةُ عَمَلِيَّة إعدادٍ واحِدَة. <c>Code</c> غَير فارِغٍ
/// إلّا مَع <see cref="TenantConfigStatus.Rejected"/>.</summary>
public sealed record TenantConfigResult(TenantConfigStatus Status, string? Code)
{
    public static readonly TenantConfigResult Saved = new(TenantConfigStatus.Saved, null);
    public static readonly TenantConfigResult TenantMissing = new(TenantConfigStatus.TenantMissing, null);

    /// <summary>رَفضٌ بِرَمزٍ مِن المُعجَم المُغلَق. يُلقي إن كانَ
    /// الرَمز خارِجَه — فَالمُعجَم يَنمو بِقَرارٍ في
    /// <see cref="TenantConfigCodes"/> لا بِسِلسِلَةٍ عابِرَة.</summary>
    public static TenantConfigResult Reject(string code)
    {
        if (!TenantConfigCodes.All.Contains(code))
            throw new ArgumentOutOfRangeException(nameof(code), code,
                "رَمزُ رَفضٍ خارِجَ المُعجَم المُغلَق — أَضِفه إلى TenantConfigCodes أَوَّلاً.");
        return new TenantConfigResult(TenantConfigStatus.Rejected, code);
    }

    public bool Ok => Status == TenantConfigStatus.Saved;
}

/// <summary>
/// <para><b>المُعجَم المُغلَق لِرُموز الرَفض</b> — تَعريفٌ واحِد
/// يَقرَؤُه السَطحانِ، بَدَل مُعجَمَين يَنجَرِفان. وهذا هو الانحِراف
/// الثالِث مِن الخَمسَة، مَحسوماً: كانَت الفِئات تَرُدّ
/// <c>bad_categories</c>/<c>no_categories</c> مِن <c>/admin</c>
/// و<c>format</c>/<c>empty</c> مِن <c>/studio</c>، والمَناطِق
/// <c>bad_format</c> مُقابِل <c>format</c>، والهُوِيَّة
/// <c>name_required</c>/<c>color_invalid</c> مُقابِل
/// <c>name</c>/<c>color</c> — <b>لِنَفس المَعنى</b>.</para>
///
/// <para><b>ولِماذا هذِه الأَسماء بِالذات</b>: لَم يَغلِب سَطحٌ على
/// سَطح، بَل غَلَبَ <b>الاسم المُستَعمَل في أَكثَر مِن مَوضِع
/// أَصلاً</b> — <c>bad_format</c> و<c>empty</c> كانَتا لُغَةَ
/// المَناطِق والخَصائِص على الطَرَفَين مَعاً، فَامتَدَّتا إلى
/// الفِئات؛ و<c>name_required</c>/<c>color_invalid</c> غَلَبَتا
/// <c>name</c>/<c>color</c> لِأَنَّهُما تَقولانِ العِلَّة لا
/// الحَقل.</para>
/// </summary>
public static class TenantConfigCodes
{
    /// <summary>سَطرُ مُدخَلٍ لا يُطابِق الصيغَة المُعلَنَة.</summary>
    public const string BadFormat = "bad_format";

    /// <summary>لا سَطرَ صالِحاً واحِداً — والحِفظ سَيَمحو الكُلّ.</summary>
    public const string Empty = "empty";

    /// <summary>اسم المُستَأجِر مَطلوب.</summary>
    public const string NameRequired = "name_required";

    /// <summary>لَون العَلامَة لَيسَ <c>#RRGGBB</c>.</summary>
    public const string ColorInvalid = "color_invalid";

    /// <summary>نِطاق الخَصائِص غائِبٌ أَو لَيسَ <c>Guid</c>.</summary>
    public const string NoScope = "no_scope";

    /// <summary>أَيقونَة PWA تَتَجاوَز السَقف.</summary>
    public const string IconTooLarge = "icon_too_large";

    /// <summary>نَوع أَيقونَة PWA غَير مَدعوم.</summary>
    public const string IconBadType = "icon_bad_type";

    /// <summary>المُعجَم كامِلاً — مَقروءٌ في <see cref="TenantConfigResult.Reject"/>
    /// وفي فاحِصِ الانغِلاق.</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        BadFormat, Empty, NameRequired, ColorInvalid, NoScope, IconTooLarge, IconBadType,
    };
}
