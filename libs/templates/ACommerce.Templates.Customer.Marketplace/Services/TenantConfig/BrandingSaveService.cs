using System.Text.RegularExpressions;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Tenants;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.TenantConfig;

/// <summary>
/// <para><b>ما يُرسِلُه سَطحٌ لِيَحفَظ الهُوِيَّة البَصَرِيَّة</b> — لا
/// <c>HttpRequest</c> ولا <c>IFormCollection</c>. وهذا هُوَ الشَرط
/// الَّذي يَجعَل الخِدمَة صالِحَةً غَداً لِـAPI ولِتَطبيقٍ أَصيل:
/// نُقطَةُ الويب تَقرَأ النَموذَج وتَبني هذا، ونُقطَةُ JSON
/// تُفَكِّكُه، والخِدمَةُ لا تَعرِف أَيَّهُما نادى.</para>
///
/// <para><b>و<c>AuthChannel</c> يَقبَل <c>null</c> عَمداً</b>، وهذا
/// حَسمُ الانحِراف الرابِع بِتَوصِيَة المالِك «الأَكمَل يَغلِب».
/// فَـ<c>null</c> تَعني <b>لا تُغَيِّر</b>، وقيمَةٌ مُرسَلَة
/// تُطَبَّق — فَنَفسُ المُدخَل مِن السَطحَين يُعطي نَفسَ الأَثَر،
/// وغِيابُ الحَقل لا يُتلِف شَيئاً.</para>
///
/// <para><b>‏2026-08-30 — وكانَ التَعليلُ هُنا «صَفحَةُ هُوِيَّةِ
/// الاستوديو لا تُدير القَناة»، فَصارَت تُديرُها.</b> ولَم يَتَغَيَّر
/// العَقد: السَبَبُ الحَقيقيُّ لِقَبولِ <c>null</c> لَيسَ تِلكَ
/// الصَفحَةَ بَل <b>كُلُّ سَطحٍ لا يُرسِل الحَقل</b> — نُقطَةُ JSON،
/// تَطبيقٌ أَصيل، نَموذَجٌ جُزئيّ. ولَولا ذلكَ لَأَعادَ حِفظُ الاسمِ
/// مِن أَيٍّ مِنها مُستَأجِراً على «نَفاذ» إلى «هاتِف» صامِتاً.
/// والصَفحَةُ صارَت تُرسِلُها لِأَنّ عَدَمَ إرسالِها كانَ العَطَب:
/// مَتجَرٌ بَناهُ عَميلٌ بِنَفسِه على قَناةٍ غَيرِ مُسَجَّلَةٍ في
/// النُسخَة، ولا يَملِكُ صاحِبُه تَبديلَها. راجِع
/// <c>TenantAuthChannelDoor</c>.</para>
/// </summary>
public sealed record BrandingSaveRequest(
    string Name,
    string TagLine,
    string City,
    string BrandColor,
    string? AuthChannel);

/// <summary>
/// <para><b>حِفظُ الهُوِيَّة البَصَرِيَّة — تَعريفٌ واحِد يُنادِيه
/// <c>/admin</c> و<c>/studio</c>.</b> كانَ مَكتوباً مَرَّتَين:
/// ‏37 سَطراً في الإدارَة و‏23 في الاستوديو، والفَرقُ بَينَهُما
/// <b>ثَلاثَةُ عُيوب</b> لا ثَلاثَةُ خيارات — تَدقيقٌ يُكتَب في
/// مَسارٍ ويُهمَل في آخَر، وقَناةُ دُخولٍ تُحفَظ هُنا وتُهمَل
/// هُناك، ورَمزا خَطَأٍ لِنَفس المَعنى.</para>
///
/// <para><b>وتَأخُذ الجَلسَة ولا تَفتَحُها</b> (القَرار الهَجين، ق٢):
/// المُعامَلَة تَبقى لِلنُقطَة، فَتَستَطيع أَن تَضُمّ هذا الحِفظ إلى
/// غَيرِه في إيداعٍ واحِد، ولا تُجبَر على مُعامَلَتَين. والنَفيُ
/// قابِلٌ لِلقِياس: لا <c>*Session(</c> ولا <c>SaveChangesAsync</c>
/// في هذا المِلَفّ، ويَحرُسُه <c>TenantConfigServiceShapeTests</c>.</para>
///
/// <para><b>والتَدقيق لا يُكتَب هُنا — وهذا قَرارٌ لا نِسيان.</b>
/// حُمولَةُ سَطرِ التَدقيق كُلُّها مِن الطَلَب: عُنوان IP، ووَكيل
/// المُستَخدِم، ولَقطَةُ النَموذَج، وهُوِيَّةُ الفاعِل المُستَخرَجَة
/// مِن كوكي الاستوديو أَو مِن رَمز المُستَأجِر. فَكِتابَتُه هُنا
/// تَعني تَمرير <c>HttpRequest</c> إلى الخِدمَة — أَي هَدمَ
/// الخاصِّيَّة الَّتي تُبَرِّر وُجودَها. والَّذي يَسكُن هُنا هو
/// <b>اسمُ الفِعل</b> (<see cref="AuditAction"/>) فَلا يَختَرِعُه
/// سَطحٌ ولا يَنجَرِف؛ و<b>أَنّ السَطحَين يَكتُبانِه</b> مَحروسٌ في
/// <c>AdminStudioPairCharacterizationTests</c>.</para>
/// </summary>
public static class BrandingSaveService
{
    /// <summary>اسمُ فِعل التَدقيق — يَسكُن مَع المَنطِق ويُنادى مِن
    /// السَطحَين، فَلا يَنجَرِف أَحَدُهُما عَن الآخَر.</summary>
    public const string AuditAction = "tenant.branding_save";

    private static readonly Regex HexColor =
        new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    /// <summary><b>دالَّةُ القَرار، نَقِيَّة</b> (ق٣): بِلا Marten وبِلا
    /// HTTP — تُنادى مِن اختِبارٍ بِلا قاعِدَةِ بَيانات.</summary>
    public static string? WhyInvalid(BrandingSaveRequest r)
    {
        if (string.IsNullOrEmpty(r.Name.Trim())) return TenantConfigCodes.NameRequired;
        if (!HexColor.IsMatch(r.BrandColor.Trim())) return TenantConfigCodes.ColorInvalid;
        return null;
    }

    public static async Task<TenantConfigResult> SaveAsync(
        IDocumentSession session, string slug, BrandingSaveRequest r,
        CancellationToken ct = default)
    {
        if (WhyInvalid(r) is { } code) return TenantConfigResult.Reject(code);

        var t = await session.LoadAsync<Tenant>(slug, ct);
        if (t is null) return TenantConfigResult.TenantMissing;

        t.Name       = r.Name.Trim();
        t.TagLine    = r.TagLine.Trim();
        t.City       = r.City.Trim();
        t.BrandColor = r.BrandColor.Trim();

        // غِيابُ القَناة يَعني «لا تُغَيِّر» — لا «أَعِدها إلى
        // الافتِراضيّ». راجِع شَرح BrandingSaveRequest.
        if (r.AuthChannel is not null)
            t.AuthChannel = AuthChannels.NormalizeOrDefault(r.AuthChannel.Trim());

        session.Store(t);
        return TenantConfigResult.Saved;
    }
}
