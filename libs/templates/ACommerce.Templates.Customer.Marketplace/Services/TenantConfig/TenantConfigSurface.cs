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

    // ─── نَتيجَة ← ردّ ─────────────────────────────────────────────

    /// <summary>
    /// <para>ثَلاثُ حالاتٍ وثَلاثَةُ مَسارات. و<c>errBase</c> يَقبَل
    /// استِعلاماً سابِقاً (‏<c>?scope=…</c>) فَيُضاف إلَيه
    /// <c>&amp;err=</c> لا <c>?err=</c>.</para>
    /// </summary>
    public static IResult Outcome(
        TenantConfigResult result, string savedUrl, string errBase, string rootUrl) =>
        result.Status switch
        {
            TenantConfigStatus.Saved         => Results.Redirect(savedUrl),
            TenantConfigStatus.TenantMissing => Results.Redirect(rootUrl),
            _ => Results.Redirect(errBase + (errBase.Contains('?') ? "&" : "?") + "err=" + result.Code),
        };
}
