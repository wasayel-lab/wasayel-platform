using Microsoft.AspNetCore.Http;

namespace ACommerce.Templates.Customer.Marketplace.Services.TenantConfig;

/// <summary>
/// <para><b>المُهايِئ — وهُوَ المِلَفّ الوَحيد في هذا المُجَلَّد
/// الَّذي يَعرِف HTTP.</b> مَهَمَّتُه اثنَتان: تَحويلُ نَموذَجٍ
/// مُرسَل إلى طَلَبٍ مَكتوبٍ بِأَنواعِه (سَلاسِل ← <c>Guid</c>،
/// <c>"1"</c> ← <c>bool</c>)، وتَحويلُ نَتيجَةٍ إلى ردٍّ يَراه
/// الجُمهور.</para>
///
/// <para><b>ولِماذا هُنا لا في الخِدمَة</b>: القِسمَةُ هي بِعَينِها
/// ما يَجعَل الخِدمَة صالِحَةً لِسَطحٍ ثانٍ. لَو قَرَأَت الخِدمَةُ
/// <c>req.Form</c> لَما نادَتها نُقطَةُ JSON ولا تَطبيقٌ أَصيل إلّا
/// بِتَزييف طَلَبِ ويب. فَالحَدُّ لَيسَ تَجميلاً: هُوَ الفَرق بَينَ
/// مَنطِقٍ يُعادُ استِعمالُه ومَنطِقٍ يُعادُ كِتابَتُه — وقَد
/// أُعيدَت كِتابَتُه هُنا سِتَّ مَرّات فِعلاً.</para>
///
/// <para><b>والعَرضُ يَبقى لِجُمهورِه</b>: القَرار واحِد (نَجَح /
/// رُفِضَ بِرَمز / لا مُستَأجِر)، وتَحويلُه إلى مَسارٍ يُعاد إلَيه
/// شَأنُ السَطح — <c>/admin</c> يَعود إلى صَفَحات الإدارَة
/// و<c>/studio</c> إلى صَفَحات الاستوديو. جُمهورانِ، ومَنطِقٌ
/// واحِد.</para>
/// </summary>
public static class TenantConfigSurface
{
    // ─── نَموذَج ← طَلَب ───────────────────────────────────────────

    /// <summary><b>القَناة تُقرَأ بِوُجودِ المِفتاح لا بِقيمَتِه.</b>
    /// نَموذَجٌ لا يَحمِل <c>channel</c> يُعطي <c>null</c> = «لا
    /// تُغَيِّر»؛ ونَموذَجٌ يَحمِلُها فارِغَةً يُعطي <c>""</c> فَتُطَبَّع
    /// إلى الافتِراضيّ. الفَرقُ مَقصود: صَفحَةُ الاستوديو لا تُدير
    /// القَناة، فَلا يَجوز أَن يَمحُوَها حِفظُ الاسم.</summary>
    public static BrandingSaveRequest ReadBranding(HttpRequest req) =>
        new(req.Form["name"].ToString(),
            req.Form["tagline"].ToString(),
            req.Form["city"].ToString(),
            req.Form["color"].ToString(),
            req.Form.ContainsKey("channel") ? req.Form["channel"].ToString() : null);

    public static CategoriesSaveRequest ReadCategories(HttpRequest req) =>
        new(req.Form["categories"].ToString());

    /// <summary>النَموذَج يُرسِل <c>role_{catalogSlug}=1</c> لِكُلّ
    /// دَورٍ مُختار. والمُهايِئ يَقرَأ <b>المَفاتيح</b> فَقَط ولا
    /// يَعرِف الكاتالوج — مُطابَقَتُها بِالكاتالوج وتَرتيبُها شَأنُ
    /// الخِدمَة.</summary>
    public static RolesSaveRequest ReadRoles(HttpRequest req)
    {
        var selected = req.Form
            .Where(kv => kv.Key.StartsWith("role_", StringComparison.Ordinal) &&
                         kv.Value.ToString() == "1")
            .Select(kv => kv.Key["role_".Length..])
            .ToArray();

        var def = req.Form["default_role"].ToString().Trim().ToLowerInvariant();
        return new RolesSaveRequest(selected, string.IsNullOrEmpty(def) ? null : def);
    }

    public static RegionsSaveRequest ReadRegions(HttpRequest req) =>
        new(req.Form["regions"].ToString());

    /// <summary>
    /// <para>صَفحَةُ PWA تُرسِل ثَلاثَةَ حُقولٍ لِكُلّ دَور:
    /// <c>name_{slug}</c> و<c>clear_{slug}</c> و<c>icon_{slug}</c>.
    /// والمُهايِئُ يَجمَع الأَدوارَ مِن البادِئات الثَلاث — ولا
    /// يَقرَأ قاعِدَةَ بَيانات لِيَعرِفَ الأَدوار: تِلكَ مَعرِفَةُ
    /// الخِدمَة.</para>
    ///
    /// <para>والمِلَفُّ لا يُقرَأ هُنا</b>: يُمَرَّر نَوعُه وطولُه
    /// ودالَّةُ قِراءَتِه، فَتَرفُض الخِدمَةُ الحَجمَ قَبلَ أَن
    /// يَدخُلَ بايتٌ الذاكِرَة.</para>
    /// </summary>
    public static PwaSaveRequest ReadPwa(HttpRequest req)
    {
        var slugs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in req.Form.Keys)
        {
            if (key.StartsWith("name_", StringComparison.Ordinal)) slugs.Add(key["name_".Length..]);
            else if (key.StartsWith("clear_", StringComparison.Ordinal)) slugs.Add(key["clear_".Length..]);
        }
        foreach (var f in req.Form.Files)
            if (f.Name.StartsWith("icon_", StringComparison.Ordinal)) slugs.Add(f.Name["icon_".Length..]);

        var roles = new List<PwaRoleInput>();
        foreach (var slug in slugs)
        {
            var file = req.Form.Files[$"icon_{slug}"];
            UploadedIcon? icon = file is { Length: > 0 }
                ? new UploadedIcon(file.ContentType, file.Length, async ct =>
                    {
                        using var ms = new MemoryStream();
                        await file.CopyToAsync(ms, ct);
                        return ms.ToArray();
                    })
                : null;

            roles.Add(new PwaRoleInput(
                slug,
                req.Form[$"name_{slug}"].ToString(),
                req.Form[$"clear_{slug}"].ToString() == "1",
                icon));
        }

        return new PwaSaveRequest(roles);
    }

    /// <summary>النِطاقُ يُحَوَّل هُنا إلى <c>Guid?</c> — فَالسَطحُ
    /// يَحتاجُه مَكتوباً لِبِناء رابِط العَودَة، والخِدمَةُ تَرفُض
    /// غِيابَه بِـ<c>no_scope</c>. تَحليلٌ واحِد لا اثنان.</summary>
    public static AttributesSaveRequest ReadAttributes(HttpRequest req) =>
        new(Guid.TryParse(req.Form["scope"].ToString().Trim(), out var scope) ? scope : null,
            req.Form["defs"].ToString());

    // ─── نَتيجَة ← ردّ ─────────────────────────────────────────────

    /// <summary>
    /// <para>ثَلاثُ حالاتٍ وثَلاثَةُ مَسارات. والقاعِدَتانِ تَقبَلانِ
    /// استِعلاماً سابِقاً (‏<c>?scope=…</c>) فَيُضاف إلَيهِما
    /// <c>&amp;</c> لا <c>?</c> — ولا يُترَك ذلك لِكُلّ نُقطَةٍ
    /// تَبنيه بِيَدِها.</para>
    /// </summary>
    public static IResult Outcome(
        TenantConfigResult result, string savedBase, string errBase, string rootUrl) =>
        result.Status switch
        {
            TenantConfigStatus.Saved         => Results.Redirect(WithQuery(savedBase, "saved=1")),
            TenantConfigStatus.TenantMissing => Results.Redirect(rootUrl),
            _ => Results.Redirect(WithQuery(errBase, "err=" + result.Code)),
        };

    private static string WithQuery(string url, string q) =>
        url + (url.Contains('?') ? "&" : "?") + q;
}
