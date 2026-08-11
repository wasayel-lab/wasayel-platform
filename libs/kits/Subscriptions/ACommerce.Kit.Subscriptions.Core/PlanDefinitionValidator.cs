using System.Text.RegularExpressions;

namespace ACommerce.Kit.Subscriptions;

/// <summary>خَرق واحِد في تَعريف باقَة. <c>Code</c> مِفتاح ثابِت
/// لِلاختِبارات واللوغ، و<c>MessageAr</c> لِلمُراجِع البَشَريّ. نَفس
/// شَكل <c>RoleDefinitionViolation</c> — القالِب المَرجِعيّ.</summary>
public sealed record PlanDefinitionViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>بَوّابَة تَعريفات الباقات</b> كَدَوالّ نَقِيَّة: لا قاعِدَة
/// بَيانات، لا وَقت، لا عَشوائيَّة — نَفس المُدخَل يُعطي نَفس القائِمَة
/// دائِماً. نَفس نَمَط <c>RoleDefinitionValidator</c> و
/// <c>ThemeDefinitionValidator</c>.</para>
///
/// <para><b>وهي مَفروضَة لا مُتاحَة</b>: تُنادى عِندَ الاقتِراح وعِندَ
/// الاعتِماد وعِندَ بِناء اللَقطَة — ثَلاث مَرّات، لِأَنّ وَثيقَةً قَد
/// تُكتَب بِيَد أَو تَنجو مِن تَرحيل.</para>
///
/// <para><b>ولِماذا تُفحَص الأَرقام هُنا بِالذات</b>: الباقَة <b>مال
/// وحِصَّة</b> — لا تَسمِيَة ولا لَون. حِصَّةٌ سالِبَة تُعطي رَصيداً
/// سالِباً مِن أَوَّل يَوم، ومُدَّةٌ صِفريّة تُعطي اشتِراكاً يَنتَهي
/// قَبل أَن يَبدَأ. وهذه أَخطاء لا يَراها المُصادِق البَصَريّ ولا
/// يَشتَكي مِنها مُتَرجِم.</para>
/// </summary>
public static class PlanDefinitionValidator
{
    /// <summary>نَفس نَمَط سلاج الأَدوار حَرفاً: ASCII صَغير يَبدَأ
    /// بِحَرف.</summary>
    private static readonly Regex SlugPattern =
        new("^[a-z][a-z0-9_]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>سَقفٌ مُعلَن لِلمُدَّة — سَنَتان. ما فَوقَه خَطَأ
    /// إدخال لا باقَة.</summary>
    public const int MaxDaysPeriod = 730;

    /// <summary>القائِمَة فارِغَة تَعني تَعريفاً صالِحاً.</summary>
    public static IReadOnlyList<PlanDefinitionViolation> Validate(PlanDefinition d)
    {
        var v = new List<PlanDefinitionViolation>();

        // ─── الهُوِيَّة ────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(d.Slug))
            v.Add(new("slug_empty", "الباقَة بِلا slug."));
        else if (!SlugPattern.IsMatch(d.Slug))
            v.Add(new("slug_pattern",
                $"الـ slug «{d.Slug}» خارِج النَّمَط ^[a-z][a-z0-9_]*$."));

        // ─── حاوِيات التَوطين — العَرَبِيَّة إلزامِيَّة ────────────────
        CheckArabic(v, d.Label,       $"تَسمِيَة الباقَة «{d.Slug}»");
        CheckArabic(v, d.Description, $"وَصف الباقَة «{d.Slug}»");

        // ─── المال ───────────────────────────────────────────────────
        if (d.Price < 0)
            v.Add(new("price_negative",
                $"سِعر الباقَة «{d.Slug}» سالِب: {d.Price}."));

        // ─── الحِصَّة ─────────────────────────────────────────────────
        // الصِفر مَسموح (باقَة عَرضٍ بِلا نَشر)، والسالِب لا.
        if (d.ListingsQuota < 0)
            v.Add(new("quota_negative",
                $"حِصَّة الباقَة «{d.Slug}» سالِبَة: {d.ListingsQuota} — " +
                "وهي رَصيدٌ سالِب مِن أَوَّل يَوم."));

        // ─── المُدَّة ─────────────────────────────────────────────────
        if (d.DaysPeriod <= 0)
            v.Add(new("period_not_positive",
                $"مُدَّة الباقَة «{d.Slug}» = {d.DaysPeriod} — اشتِراكٌ يَنتَهي قَبل أَن يَبدَأ."));
        else if (d.DaysPeriod > MaxDaysPeriod)
            v.Add(new("period_too_long",
                $"مُدَّة الباقَة «{d.Slug}» = {d.DaysPeriod} يَوماً، والسَقف {MaxDaysPeriod}."));

        return v;
    }

    /// <summary>هَل يَجتاز البَوّابَة؟</summary>
    public static bool IsValid(PlanDefinition d) => Validate(d).Count == 0;

    /// <summary>
    /// <para><b>بَوّابَة تَعريفٍ يُؤَلِّفُه مُستَأجِر</b> — كُلّ ما في
    /// <see cref="Validate"/> حَرفِيّاً، <b>وزِيادَةٌ واحِدَة</b>: أَن لا
    /// يُصادِم سلاجُه سلاجَ باقات البَذر المَحجوزَة.</para>
    ///
    /// <para><b>ولِماذا دالَّة مُنفَصِلَة لا عَلَم في
    /// <see cref="Validate"/></b>: باقات البَذر نَفسُها تَمُرّ مِن
    /// <see cref="Validate"/> — ولَو كانَ الفَحص فيها لَرَفَضَت
    /// كُلٌّ مِنها نَفسَها. نَفس مُبَرِّر
    /// <c>RoleDefinitionValidator.ValidateTenantDefinition</c>.</para>
    /// </summary>
    public static IReadOnlyList<PlanDefinitionViolation> ValidateTenantDefinition(PlanDefinition d)
    {
        var v = new List<PlanDefinitionViolation>(Validate(d));

        if (!string.IsNullOrWhiteSpace(d.Slug) && ReservedSlugs.Contains(d.Slug))
            v.Add(new("slug_shadows_seeded_plan",
                $"الـ slug «{d.Slug}» مَحجوز لِباقَة مَبذورَة — " +
                "باقات المُستَأجِر تُضاف فَوقَها ولا تُظَلِّلُها. اِختَر اسماً آخَر."));

        return v;
    }

    /// <summary>هَل يَجتاز بَوّابَة المُستَأجِر؟</summary>
    public static bool IsValidTenantDefinition(PlanDefinition d) =>
        ValidateTenantDefinition(d).Count == 0;

    /// <summary>سلاجات باقات البَذر (‏<c>PlatformSeed</c>) — مَحجوزَة
    /// كَي لا يُغَيِّر مُستَأجِرٌ مَعنى «مَجّانيّ» مِن تَحت مَن
    /// يَقرَؤُه.</summary>
    public static readonly IReadOnlyList<string> ReservedSlugs =
        new[] { "basic", "free", "pro" };

    private static void CheckArabic(
        List<PlanDefinitionViolation> v, PlanText t, string whereAr)
    {
        if (string.IsNullOrWhiteSpace(t?.Ar))
            v.Add(new("localized_arabic_missing",
                $"{whereAr}: العَرَبيَّة مَفقودَة في حاوِيَة التَوطين."));
    }
}
