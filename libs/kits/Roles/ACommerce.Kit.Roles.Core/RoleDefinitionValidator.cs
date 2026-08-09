using System.Text.RegularExpressions;

namespace ACommerce.Kit.Roles;

/// <summary>خَرق واحِد في تَعريف دَور. <c>Code</c> مِفتاح ثابِت
/// لِلاختِبارات واللوغ، و<c>MessageAr</c> لِلمُراجِع البَشَريّ. نَفس
/// شَكل <c>DealPatternViolation</c> — القالِب المَرجِعيّ.</summary>
public sealed record RoleDefinitionViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>بَوّابَة تَعريفات الأَدوار</b> كَدَوالّ نَقِيَّة فَوق
/// <see cref="RoleDefinition"/>: لا قاعِدَة بَيانات، لا وَقت، لا
/// عَشوائيَّة — نَفس المُدخَل يُعطي نَفس القائِمَة دائِماً. نَفس نَمَط
/// <c>DealPatternValidator</c> (TESTING-PROTOCOL كُتلَة ب).</para>
///
/// <para>وهي <b>مَفروضَة لا مُتاحَة</b>: <see cref="RoleCatalog"/> يُمَرِّر
/// كُلّ تَعريف مُحَمَّل مِن هُنا ويَرمي عِند أَيّ خَرق — فَتَعريف فاسِد
/// يُفشِل الإقلاع بِرَمزِه، ولا يَمُرّ صامِتاً إلى مَتجَر.</para>
///
/// <para><b>ما لا تَفحَصُه عَمداً</b>: أَن يَملِك الدَور صَلاحِيَّة
/// مُعَيَّنَة، أَو أَن يُوافِق تَركيبُه صَلاحِيّاتِه (مَثَلاً: <c>vendor</c>
/// لا يَملِك <c>listing.browse</c> ومَع ذلك تَركيبُه
/// <c>defaultExplore</c>). هذا واقِع الكاتالوج اليَوم، وجَعلُه خَرقاً
/// يَرفُض قالِباً قِياسيّاً قائِماً — وهو بِالضَبط ما رَفَضَه
/// <c>DealPatternValidator</c> حينَ لَم يَشتَرِط انتِهاء النَّمَط بِـ
/// <c>Reviewed</c>.</para>
/// </summary>
public static class RoleDefinitionValidator
{
    /// <summary>نَمَط الـ slug: ASCII صَغير يَبدَأ بِحَرف، ثُمَّ حُروف
    /// أَو أَرقام أَو شَرطَة سُفلِيَّة. (<c>tenant_admin</c> يُوافِقُه،
    /// و<c>Customer</c> لا — والمُقارَنَة في <c>Find</c> حَسّاسَة
    /// لِلحالَة أَصلاً.)</summary>
    private static readonly Regex SlugPattern =
        new("^[a-z][a-z0-9_]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>القائِمَة فارِغَة تَعني تَعريفاً صالِحاً.</summary>
    public static IReadOnlyList<RoleDefinitionViolation> Validate(RoleDefinition d)
    {
        var v = new List<RoleDefinitionViolation>();

        // ─── الهُوِيَّة ────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(d.Slug))
            v.Add(new("slug_empty", "الدَور بِلا slug."));
        else if (!SlugPattern.IsMatch(d.Slug))
            v.Add(new("slug_pattern",
                $"الـ slug «{d.Slug}» خارِج النَّمَط ^[a-z][a-z0-9_]*$."));

        if (string.IsNullOrWhiteSpace(d.Icon))
            v.Add(new("icon_missing", $"الدَور «{d.Slug}» بِلا أَيقونَة."));

        // فارِغ مَسموح (= الصَفحَة الافتِراضِيَّة)، وغَير الفارِغ يَبدَأ
        // بِـ / بِلا مَسافات ولا استِعلام.
        if (!string.IsNullOrEmpty(d.HomeRoute) &&
            (!d.HomeRoute.StartsWith('/') ||
             d.HomeRoute.Any(char.IsWhiteSpace) ||
             d.HomeRoute.Contains('?') || d.HomeRoute.Contains('#')))
            v.Add(new("home_route_malformed",
                $"مَسار الدَور «{d.Slug}» شاذّ: «{d.HomeRoute}»."));

        // ─── حاوِيات التَّوطين — العَرَبيَّة إلزامِيَّة ────────────────
        CheckArabic(v, d.Label, $"تَسمِيَة الدَور «{d.Slug}»");
        CheckArabic(v, d.Description, $"وَصف الدَور «{d.Slug}»");

        // ─── الصَلاحِيّات — مِن المَعجَم المُغلَق حَصراً ──────────────
        var seenPerm = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in d.Permissions)
        {
            if (!PermissionCatalog.Contains(p))
                v.Add(new("permission_out_of_vocabulary",
                    $"الصَلاحِيَّة «{p}» في الدَور «{d.Slug}» خارِج مَعجَم PermissionCatalog."));
            if (!seenPerm.Add(p))
                v.Add(new("permission_duplicate",
                    $"الصَلاحِيَّة «{p}» مُكَرَّرَة في الدَور «{d.Slug}»."));
        }

        // ─── الحُقول ─────────────────────────────────────────────────
        var seenCode = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in d.Fields)
        {
            if (string.IsNullOrWhiteSpace(f.Code))
                v.Add(new("field_code_empty", $"حَقل بِلا رَمز في الدَور «{d.Slug}»."));
            else if (!seenCode.Add(f.Code))
                v.Add(new("field_code_duplicate",
                    $"رَمز الحَقل «{f.Code}» مُكَرَّر في الدَور «{d.Slug}»."));

            CheckArabic(v, f.Label, $"تَسمِيَة الحَقل «{f.Code}» في «{d.Slug}»");

            if (!RoleFieldTypes.Contains(f.Type))
                v.Add(new("field_type_out_of_vocabulary",
                    $"نَوع الحَقل «{f.Code}» = «{f.Type}» خارِج تَعداد RoleFieldTypes."));
            else if (RoleFieldTypes.RequiresOptions(f.Type) && f.Options.Count == 0)
                v.Add(new("select_without_options",
                    $"الحَقل الاختِياريّ «{f.Code}» في «{d.Slug}» بِلا خِيارات."));

            var seenOpt = new HashSet<string>(StringComparer.Ordinal);
            foreach (var o in f.Options)
            {
                if (string.IsNullOrWhiteSpace(o.Value))
                    v.Add(new("option_value_empty",
                        $"خِيار بِلا قيمَة في الحَقل «{f.Code}» مِن «{d.Slug}»."));
                else if (!seenOpt.Add(o.Value))
                    v.Add(new("option_value_duplicate",
                        $"قيمَة الخِيار «{o.Value}» مُكَرَّرَة في الحَقل «{f.Code}»."));

                CheckArabic(v, o.Label, $"تَسمِيَة الخِيار «{o.Value}» في الحَقل «{f.Code}»");
            }
        }

        // ─── التَّركيب — مِن مَعجَم المُكَوِّنات المُغلَق حَصراً ────────
        foreach (var c in d.Composition.AllComponents())
            if (!RoleComponents.Contains(c))
                v.Add(new("composition_component_out_of_vocabulary",
                    $"المُكَوِّن «{c}» في تَركيب الدَور «{d.Slug}» خارِج مَعجَم RoleComponents."));

        return v;
    }

    /// <summary>هَل يَجتاز البَوّابَة؟</summary>
    public static bool IsValid(RoleDefinition d) => Validate(d).Count == 0;

    private static void CheckArabic(
        List<RoleDefinitionViolation> v, LocalizedText t, string whereAr)
    {
        if (string.IsNullOrWhiteSpace(t.Ar))
            v.Add(new("localized_arabic_missing",
                $"{whereAr}: العَرَبيَّة مَفقودَة في حاوِيَة التَّوطين."));
    }
}
