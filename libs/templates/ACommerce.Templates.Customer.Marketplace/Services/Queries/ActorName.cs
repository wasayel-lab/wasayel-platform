namespace ACommerce.Templates.Customer.Marketplace.Services.Queries;

/// <summary>
/// <para><b>اسمُ الفاعِلِ الَّذي يُكتَبُ في سِجِلٍّ يَراه غَيرُه — مِن وَثيقَتِه وَقتَ الكِتابَة</b> (‏2026-10-24، تَدقيقُ الإطلاق lens-security-11).
/// كانَت النِقاطُ تَأخُذُه مِن كوكي <c>.name</c> — خارِجَ رَمزِ HMAC، يَكتُبُه العَميلُ كَما يَشاء — فَيَصيرُ سَطرَ الخَطِّ الزَمَنِيِّ في الصَفقَة،
/// واسمَ كاتِبِ تَقييمٍ «موثَّق»، وفاعِلَ سَطرِ تَدقيق. صارَ يُقرَأُ مِن <c>User.FullName</c> في مَتجَرِه (<c>AccountQueries.ActorNameAsync</c>)
/// أَو <c>StudioUser.FullName</c> (<c>StudioQueries.ActorNameAsync</c>)، وهذِه القاعِدَةُ الواحِدَةُ بَينَهُما: الاسمُ مَقصوصاً، والفارِغُ غِياب —
/// فَيَسقُطُ كُلُّ بابٍ إلى ما كانَ يَسقُطُ إلَيه حينَ يَغيبُ الكوكي.</para>
///
/// <para><b>والكوكي باقٍ لِلعَرضِ وَحدَه</b>: <c>AuthSession.UserName</c> و<c>StudioAuth.UserName</c> يُرسَمانِ لِصاحِبِ الجَلسَةِ نَفسِه («مَرحَباً
/// …»)، ولا يُكتَبانِ في سِجِلٍّ يَراه غَيرُه — مَحروسٌ في <c>ActorNameSourceTests</c>.</para>
/// </summary>
public static class ActorName
{
    public static string? Of(string? fullName)
        => string.IsNullOrWhiteSpace(fullName) ? null : fullName.Trim();

    /// <summary>رَمزُ حَقلِ اسمِ النَشاطِ في تَعريفِ الدَور (<c>vendor.role.json</c>: إلزامِيّ) — يُخَزَّنُ في <c>User.RoleAttributesJson[role][code]</c>.</summary>
    public const string BusinessNameField = "business_name";

    /// <summary>اسمُ النَشاطِ إن كَتَبَه في أَيِّ دَور — بِتَرتيبِ رَمزِ الدَورِ لِيَكونَ الجَوابُ حَتمِيّاً — أَو <c>null</c>.</summary>
    public static string? BusinessName(ACommerce.Kit.Auth.User user)
        => user.RoleAttributesJson
            .OrderBy(r => r.Key, StringComparer.Ordinal)
            .Select(r => r.Value.TryGetValue(BusinessNameField, out var v) ? v : null)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    /// <summary>
    /// <para><b>الاسمُ الَّذي يَراه غَيرُه — اسمُ النَشاطِ حَيثُ وُجِد، وإلّا اسمُه</b> (‏2026-10-29، قَرارُ المالِك د40 «ج»: «ويُقدَّم اسم النشاط حيث وُجد في
    /// الإشعار وصفحة الصفقة وصفحة البائع»). قاعِدَةٌ واحِدَةٌ يَقرَؤُها كاتِبُ اسمِ الفاعِلِ (<c>AccountQueries.ActorNameAsync</c> — سُطورُ الصَفقَةِ وإشعارُ
    /// القَبولِ والمُحادَثاتُ والتَقييمات)، ورَبطُ البائِعِ عِندَ وِلادَةِ صَفقَةِ السَلَّة (<c>DealsService.BindListingOwnerAsync</c>)، وصَفحَةُ البائِع.
    /// ولا إعدادَ لِلمَتجَرِ حَتّى يَطلُبَ مَتجَرٌ الاسمَ الشَخصِيَّ بَدَلَه.</para>
    /// </summary>
    public static string? Of(ACommerce.Kit.Auth.User? user)
        => user is null ? null : BusinessName(user) ?? Of(user.FullName);

    /// <summary>كُلُّ اسمٍ قَد تَحمِلُه نُسخَةٌ عِندَ غَيرِه — لِلمَحوِ عِندَ الحَذف (<c>AccountErasure.EraseCopiesAsync</c>).</summary>
    public static IReadOnlyList<string> NamesOf(ACommerce.Kit.Auth.User user)
        => new[] { Of(user.FullName), BusinessName(user) }
            .Where(n => n is not null).Select(n => n!).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    /// <para><b>أَيُطلَبُ اسمُه؟</b> — فارِغٌ أَو ما زالَ الافتِراضِيَّ «عُضو جَديد» الَّذي يُكتَبُ لِلحِسابِ الجَديدِ بِالهاتِفِ أَو البَريد
    /// (<c>AuthHandlers.NewMemberNameKey</c> بِالعَرَبِيَّةِ صَراحَةً، كَما يُخَزَّن). يَسأَلُه بابا التَحَقُّقِ بَعدَ الدُخولِ فَيُحَوِّلانِ إلى خُطوَةِ الاسم
    /// (‏2026-10-29، د40). ونَفاذٌ لا يَبلُغُه: اسمُه مُشتَقٌّ حَتمِيّاً مِن هُوِيَّتِه.</para>
    /// </summary>
    public static bool NeedsName(ACommerce.Kit.Auth.User user)
        => string.IsNullOrWhiteSpace(user.FullName)
           || string.Equals(user.FullName.Trim(),
                ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, ACommerce.Kit.Auth.Server.AuthHandlers.NewMemberNameKey),
                StringComparison.Ordinal);
}

/// <summary>
/// <para><b>خُطوَةُ الاسمِ بَعدَ الدُخول</b> (‏2026-10-29، قَرارُ المالِك د40 «ج»): بابا التَحَقُّقِ بِالهاتِفِ والبَريدِ يُحَوِّلانِ إلَيها حينَ
/// <see cref="ActorName.NeedsName"/>، ومَسارُ ما بَعدَ الدُخولِ في <c>next</c> — والصَفحَةُ (<c>MemberName.razor</c>) تَحسِمُه بِـ<c>LocalRedirect.Resolve</c>
/// كَما تَحسِمُه نُقطَةُ الحِفظ.</para>
/// </summary>
public static class MemberNameStep
{
    public const string Path = "me/name";

    public static string Route(bool needsName, string nextRoute, Func<string, string> link)
        => needsName ? link($"{Path}?next={Uri.EscapeDataString(nextRoute)}") : nextRoute;
}
