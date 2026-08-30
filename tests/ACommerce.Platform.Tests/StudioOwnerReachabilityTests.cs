using System.Text.RegularExpressions;
using ACommerce.Platform.I18n;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ ما يَملِكُه صاحِبُ المَتجَرِ ولا يَبلُغُه ═════════════════════════
//
// **القاعِدَة ١٢**: شاشَةٌ مَبنِيَّةٌ لا يَصِلُ إلَيها المُستَخدِمُ
// **غَيرُ مَوجودَة**. وهذا المِلَفُّ يَقيسُ مَسارَ النَقرِ نَفسَه: صَفٌّ
// في لَوحَةِ التَطبيقِ ← صَفحَةٌ بِمَسارِها ← نُقطَةٌ تَقبَلُ
// صاحِبَها. **وطَرَفٌ واحِدٌ أَخضَرُ وَحدَه كودٌ مَيِّت.**
//
// **العُيوبُ المَقيسَةُ يَومَ ‏2026-08-30**:
//
// ‏١) **لوحَةُ المَتجَرِ تولَدُ مَيِّتَة**: أَوَّلُ مَن يَدخُلُ المَتجَرَ
//    يَأخُذُ الدَورَ الافتِراضِيَّ، **ولا أَحَدَ يَحمِلُ
//    `tenant_admin`**؛ و`/{slug}/manage` تَشتَرِط `tenant.manage`.
//    والمَنحُ يَقَعُ في `/admin/tenants/{slug}/users/{id}/grant-admin`
//    بِحارِسٍ **يَقبَلُ مالِكَ المَتجَرِ مِن الاستوديو**
//    (`TenantAdminGuard.IsStudioOwner`) — أَي أَنّ البابَ مَفتوحٌ لَه
//    فِعلاً، **ولا رابِطَ إلَيه مِن `/studio/apps/{slug}` إطلاقاً**.
//    والرابِطُ الوَحيدُ في `TenantManage.razor` وهُوَ خَلفَ البابِ
//    المُقفَلِ نَفسِه. **حَلقَةٌ مُغلَقَةٌ تُفتَحُ بِـ`<a>` واحِد.**
public class StudioOwnerReachabilityTests
{
    private const string TemplateRoot =
        "libs/templates/ACommerce.Templates.Customer.Marketplace";

    private static string Read(string relative)
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {relative} — الأَداةُ عَمياءُ بِلا طَرَفٍ مَقروء.");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 300, $"أَداةٌ عَمياء: {relative} طولُه {text.Length} مِحرَفاً.");
        return text;
    }

    private static string Board()     => Read($"{TemplateRoot}/Components/Pages/StudioApp.razor");
    private static string Endpoints() => Read($"{TemplateRoot}/MarketplaceTemplateExtensions.cs");

    // ═══ ١) تَعيينُ أَوَّلِ مُشرِفِ مَتجَر ══════════════════════════════

    /// <summary>
    /// <para><b>صَفٌّ في لَوحَةِ التَطبيقِ يَفتَحُ شاشَةَ
    /// المُستَخدِمين.</b> والحارِسُ يَقبَلُه أَصلاً — فَالناقِصُ
    /// الرابِطُ وَحدَه، وهو نِصفُ الميزَةِ لا مُهِمَّةٌ تالِيَة.</para>
    /// </summary>
    [Fact]
    public void The_store_users_screen_is_reachable_by_a_click_from_the_app_board()
    {
        var board = Board();

        Assert.Contains("/admin/tenants/{Slug}/users", board, StringComparison.Ordinal);

        // والصَفحَةُ قائِمَةٌ بِمَسارِها، ومَحروسَةٌ بِالحارِسِ الَّذي
        // يَقبَلُ مالِكَ الاستوديو.
        var page = Read($"{TemplateRoot}/Components/Pages/Admin/TenantUsers.razor");
        Assert.Contains("@page \"/admin/tenants/{slug}/users\"", page, StringComparison.Ordinal);
        Assert.Contains("<RequireTenantAdmin", page, StringComparison.Ordinal);

        // والنُقطَتانِ تَقبَلانِ نَفسَ الحارِس.
        var endpoints = Endpoints();
        Assert.Contains("MapPost(\"/admin/tenants/{slug}/users/{userId:guid}/grant-admin\"",
            endpoints, StringComparison.Ordinal);
        Assert.Contains("TenantAdminGuard.CanAdministerAsync", endpoints, StringComparison.Ordinal);
    }

    /// <summary><b>والعَودَةُ إلى حَيثُ جاء.</b> صاحِبُ المَتجَرِ يَصِلُ
    /// مِن الاستوديو، فَزِرُّ الرُجوعِ يُعيدُه إلَيه لا إلى لَوحَةِ
    /// المَنَصَّة — وإلّا خَرَجَ مِن سِياقِه بِنَقرَة.</summary>
    [Fact]
    public void The_store_users_screen_returns_the_owner_to_the_studio_it_came_from()
    {
        Assert.Contains("from=studio", Board(), StringComparison.Ordinal);

        var page = Read($"{TemplateRoot}/Components/Pages/Admin/TenantUsers.razor");
        Assert.Contains("\"studio\"", page, StringComparison.Ordinal);
        Assert.Contains("/studio/apps/", page, StringComparison.Ordinal);
    }

    // ═══ ٢) اقتِراحُ المَظهَر — دَورَةُ اعتِمادٍ بِلا طَرَفٍ يَقتَرِح ════
    //
    // **المَقيس**: ‏`theme/propose` و`theme/apply` و`theme/{slug}/decide`
    // ثَلاثَتُها تَحتَ `/admin` بِـ`PlatformAdminGuard`، ولا
    // `@page "/studio/apps/{slug}/theme"` في المُستَودَع. فَالمُستَأجِرُ
    // يَضبُطُ `BrandColor` مِن `branding` **ولا يَبلُغُ ثيماً أَبَداً**.
    // والاعتِمادُ نَفسُه قَرارُ مَنَصَّةٍ يَبقى (يُبَثُّ في `<head>`
    // لِكُلِّ زائِر) — **والناقِصُ هُوَ الاقتِراحُ لا القَرار**.

    /// <summary><b>شاشَةُ المَظهَرِ في الاستوديو تُبلَغُ بِنَقرَة، ولَها
    /// نُقطَةٌ يَحرُسُها مالِكُ المَتجَر.</b></summary>
    [Fact]
    public void The_theme_screen_is_reachable_by_a_click_and_has_an_owner_guarded_endpoint()
    {
        var board = Board();
        Assert.Contains("/studio/apps/{Slug}/theme", board, StringComparison.Ordinal);

        var page = Read($"{TemplateRoot}/Components/Pages/StudioAppTheme.razor");
        Assert.Contains("@page \"/studio/apps/{slug}/theme\"", page, StringComparison.Ordinal);
        Assert.Contains("/studio/apps/{Slug}/theme/propose", page, StringComparison.Ordinal);
        Assert.Contains("method=\"post\"", page, StringComparison.Ordinal);

        var endpoints = Endpoints();
        Assert.Contains("MapPost(\"/studio/apps/{slug}/theme/propose\"", endpoints, StringComparison.Ordinal);
    }

    /// <summary>
    /// <para><b>والاقتِراحُ اقتِراحٌ لا اعتِماد — وهذا هُوَ الحَدُّ الَّذي
    /// لا يُتَجاوَز.</b> الاعتِمادُ يَبُثُّ الثيمَ في <c>&lt;head&gt;</c>
    /// لِكُلِّ زائِر، فَيَبقى قَرارَ مَنَصَّةٍ بِحارِسِها.</para>
    ///
    /// <para>والفَحصُ نَصِّيّ: جِسمُ النُقطَةِ لا يَحوي <c>DecideAsync</c>
    /// ولا <c>ApplyPreset</c> ولا <c>Approved</c>.</para>
    /// </summary>
    [Fact]
    public void The_studio_may_propose_a_theme_but_never_approve_one()
    {
        var text = Endpoints();
        var start = text.IndexOf("MapPost(\"/studio/apps/{slug}/theme/propose\"", StringComparison.Ordinal);
        Assert.True(start > 0, "نُقطَةُ اقتِراحِ المَظهَرِ غَير مَوجودَة — الأَداةُ عَمياء.");
        var body = text.Substring(start, Math.Min(1600, text.Length - start));

        Assert.True(body.Contains("StudioOwnsAsync", StringComparison.Ordinal),
            "نُقطَةُ الاقتِراحِ بِلا حارِسِ مِلكِيَّة.");

        foreach (var forbidden in new[] { "DecideAsync", "ApplyPreset", "TenantThemeStatuses.Approved" })
            Assert.False(body.Contains(forbidden, StringComparison.Ordinal),
                $"نُقطَةُ الاستوديو تَعتَمِدُ الثيمَ بِنَفسِها («{forbidden}») — "
                + "والاعتِمادُ يَبُثُّ في <head> لِكُلِّ زائِرٍ فَيَبقى قَرارَ مَنَصَّة.");
    }

    /// <summary><b>والجِسمُ يُقرَأُ مِن كاتالوجِ المَنَصَّةِ لا مِن
    /// الطَلَب.</b> نَفسُ حُجَّةِ <c>ApplyPresetAsync</c> حَرفاً: لا
    /// يَملِكُ صاحِبُ جَلسَةٍ مُخَوَّلَةٍ أَن يَحقِنَ ثيماً بِصِياغَةِ
    /// طَلَب، <b>ولَيسَ لِأَنّ المُصادِقَ سَيَرُدُّه بَل لِأَنَّه لا
    /// يُقرَأُ أَصلاً</b>.</summary>
    [Fact]
    public void The_proposed_theme_body_is_read_from_the_platform_catalog_not_from_the_request()
    {
        var service = Read($"{TemplateRoot}/Services/TenantThemeService.cs");
        Assert.Contains("ProposePresetAsync", service, StringComparison.Ordinal);

        var text = Endpoints();
        var start = text.IndexOf("MapPost(\"/studio/apps/{slug}/theme/propose\"", StringComparison.Ordinal);
        var body = text.Substring(start, Math.Min(1600, text.Length - start));

        Assert.True(body.Contains("ProposePresetAsync", StringComparison.Ordinal),
            "النُقطَةُ لا تَمُرُّ بِمَسارِ الحُزَمِ المُنَسَّقَة.");
        Assert.False(body.Contains("\"definition\"", StringComparison.Ordinal),
            "النُقطَةُ تَقرَأُ نَصَّ تَعريفٍ مِن الطَلَب — وذاكَ حَقنُ ثيمٍ بِصِياغَةِ طَلَب.");
    }

    // ═══ المَفاتيحُ الَّتي تَقرَؤُها الصُفوفُ الجَديدَة ═════════════════

    /// <summary><b>ولا نَصَّ خارِجَ القامُوس</b> (القاعِدَة ١١) —
    /// ومِفتاحٌ ناقِصٌ يُطبَعُ خاماً عَلى شاشَةِ المالِك.</summary>
    [Fact]
    public void Every_key_the_app_board_reads_exists_in_the_arabic_lexicon()
    {
        var keys = Regex.Matches(Board(), @"L(?:\.Markup)?\(?\[?""(?<k>[a-z0-9_.]+)""")
            .Select(m => m.Groups["k"].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(keys.Count >= 25, $"أَداةٌ عَمياء: {keys.Count} مِفتاحاً فَقَط في لَوحَةِ التَطبيق.");

        var lexicon = LocaleCatalog.Lexicon.ToHashSet(StringComparer.Ordinal);
        var missing = keys.Where(k => !lexicon.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.True(missing.Length == 0, $"مَفاتيحُ خارِجَ المَعجَم: {string.Join("، ", missing)}");

        var placeholders = keys.Where(k => LocaleCatalog.IsPlaceholderKey("ar", k)).ToArray();
        Assert.True(placeholders.Length == 0, $"قيَمٌ نائِبَة: {string.Join("، ", placeholders)}");
    }
}
