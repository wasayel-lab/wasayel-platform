using System.Text.RegularExpressions;
using ACommerce.Platform.I18n;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ شاشَةُ مُستَخدِمي المَتجَرِ كانَت تَعِدُ بِأَوَّلِ مُشرِفٍ ثُمَّ تَعرِضُ فَراغاً ═══
//
// **المَقيسُ** (موجَزُ الفَجَوات P0-8، ‏2026-10-10؛ ‏ADR-032 §٦‑هـ): صَفُّ لَوحَةِ
// التَطبيقِ (`StudioApp.razor`) يَربِطُ إلى `/admin/tenants/{slug}/users?from=studio`
// بِعُنوانِ «عَيِّن أَوَّلَ مُشرِفٍ لِمَتجَرِك — بِدونِه لا تُفتَحُ لوحَةُ إدارَتِه».
// والمَتجَرُ الجَديدُ يولَدُ بِصِفرِ مُستَخدِم — المَصنَعُ يَضبُطُ `OwnerUserId`
// **ولا يُنشِئُ لِلمالِكِ مُستَخدِماً في واجِهَةِ مَتجَرِه** — فَكانَت الشاشَةُ تَرسُمُ
// `<AcEmptyState IconName="user" Title="لا مُستَخدِمين بَعد" />` وَحدَها: عُنوانٌ بِلا
// سَبيل. والسَبيلُ قائِمٌ ولا يُقال: المُستَخدِمُ يولَدُ بِأَوَّلِ دُخولٍ مِن `/{slug}/login`
// (`AuthHandlers.GetOrCreateUserAsync` تُخَزِّنُ `new User` حينَ لا نَظيرَ له)، ثُمَّ
// يَعودُ المالِكُ إلى هذِه الصَفحَةِ — وحارِسُها يَقبَلُه مِن الاستوديو
// (`TenantAdminGuard.IsStudioOwner`) — فَيَضغَطُ «مَنح إدارَة» بِجِوارِ اسمِه.
//
// **القَرارُ المُنَفَّذُ تَحتَ التَفويضِ العامّ** (سِجِلُّ أَسئِلَةِ المالِك «أ»): نَصٌّ لا
// زِرّ — مِفتاحٌ واحِدٌ `admin.tenant_users.empty_hint` يُرسَمُ رِسالَةً تَحتَ العُنوانِ
// القائِم، يُسَمّي البابَ بِمَسارِه (`/{slug}/login` بِالسلاج الحَيّ) والزِرَّ بِحَرفِ
// قامُوسِه. و**لا زِرَّ «اجعَلني المُشرِف»** قَبلَ أَن يُقاسَ عَدَدُ النَقراتِ ومَوضِعُ
// التَعَثُّر (‏ADR-032 §٦‑د و§٦‑هـ بِحَرفِهِما: «يُبنى فَقَط إن أَثبَتَ القِياسُ مُستَهلِكَه»).

public class TenantUsersEmptyStateTests
{
    private const string HintKey  = "admin.tenant_users.empty_hint";
    private const string TitleKey = "admin.tenant_users.empty";
    private const string GrantKey = "admin.tenant_users.grant";

    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static string Source(params string[] parts)
    {
        var path = Path.Combine(RepoRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياءُ بِلا طَرَفٍ مَقروء.");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 500, $"أَداةٌ عَمياء: {path} طولُه {text.Length} مِحرَفاً — لَم يُقرَأ.");
        return text;
    }

    private static string UsersPage() => Source(
        "libs", "templates", "ACommerce.Templates.Customer.Marketplace",
        "Components", "Pages", "Admin", "TenantUsers.razor");

    /// <summary>فَرعُ «لا مُستَخدِمين» وَحدَه — مِن شَرطِه إلى <c>else</c> الَّتي تَليه.</summary>
    private static string EmptyBranch()
    {
        var page = UsersPage();
        var start = page.IndexOf("@if (users.Count == 0)", StringComparison.Ordinal);
        Assert.True(start > 0, "فَرعُ الحالَةِ الفارِغَةِ غَيرُ مَوجود — الأَداةُ عَمياء.");
        var end = page.IndexOf("else", start, StringComparison.Ordinal);
        Assert.True(end > start, "فَرعُ الحالَةِ الفارِغَةِ بِلا `else` — الأَداةُ عَمياء.");
        return page[start..end];
    }

    private static string Ar(string key) => LocaleCatalog.Find(LocaleCatalog.Arabic, key)!;

    private static readonly Regex QuotedLabel = new("«([^»]+)»", RegexOptions.Compiled);

    // ─── الأَخضَرُ الجَديد ──────────────────────────────────────────────

    /// <summary>
    /// <para><b>الحالَةُ الفارِغَةُ تَقولُ كَيفَ يَظهَرُ المُستَخدِمُ الأَوَّل</b>:
    /// العُنوانُ القَديمُ باقٍ، وتَحتَه رِسالَةٌ مِن القامُوسِ تُسَمّي بابَ الدُخولِ
    /// بِسلاجِ المَتجَرِ الحَيّ (وَسيطُ <c>{0}</c> عَبرَ <c>L.Format</c>) ثُمَّ العَودَةَ
    /// إلى هذِه الصَفحَةِ والزِرَّ الَّذي يُضغَط.</para>
    /// </summary>
    [Fact]
    public void The_empty_state_tells_the_owner_how_the_first_user_appears()
    {
        var branch = EmptyBranch();
        Assert.Contains("Title=\"@L[\"admin.tenant_users.empty\"]\"", branch, StringComparison.Ordinal);
        Assert.Contains("Message=\"@(L.Format(\"admin.tenant_users.empty_hint\", Slug))\"", branch, StringComparison.Ordinal);

        Assert.Contains(HintKey, LocaleCatalog.Lexicon);
        Assert.False(LocaleCatalog.IsPlaceholderKey(LocaleCatalog.Arabic, HintKey), "قيمَةٌ نائِبَة.");
        Assert.Null(LocaleCatalog.Find("en", HintKey));

        var hint = Ar(HintKey);
        Assert.Contains("/{0}/login", hint, StringComparison.Ordinal);   // البابُ بِمَسارِه
        Assert.Contains("أَوَّلاً", hint, StringComparison.Ordinal);     // التَرتيب: هُناكَ أَوَّلاً ثُمَّ هُنا
        Assert.Contains("عُد", hint, StringComparison.Ordinal);

        // والوَسيطُ سَليمٌ: يُدمَجُ بِالسلاجِ بِلا استِثناءٍ ويُخرِجُ المَسارَ الحَيّ.
        var rendered = string.Format(hint, "owner-test");
        Assert.Contains("/owner-test/login", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("{", rendered, StringComparison.Ordinal);
    }

    /// <summary>
    /// <para><b>كُلُّ «…» في الرِسالَةِ قيمَةُ مِفتاحٍ حَيّ</b> — والزِرُّ المُسَمّى هُوَ
    /// زِرُّ استِمارَةِ <c>grant-admin</c> في الصَفحَةِ نَفسِها، لا اسمٌ مُختَرَع.
    /// نَفسُ مِقياسِ <c>ReturnsPolicyTextTests</c>: العَدُّ يُطبَع، وصِفرُ تَسمِيَةٍ
    /// أَداةٌ عَمياء.</para>
    /// </summary>
    [Fact]
    public void Every_label_the_hint_quotes_is_a_live_dictionary_value_and_its_door_is_the_grant_form()
    {
        var quoted = QuotedLabel.Matches(Ar(HintKey)).Select(m => m.Groups[1].Value).ToList();
        Assert.True(quoted.Count >= 1, $"أَداةٌ عَمياء: {quoted.Count} تَسمِيَةً مُقتَبَسَةً في النَصّ.");

        var values = LocaleCatalog.Raw(LocaleCatalog.Arabic).Select(e => e.Value).ToHashSet(StringComparer.Ordinal);
        foreach (var label in quoted)
            Assert.True(values.Contains(label), $"«{label}» لَيسَت قيمَةَ أَيِّ مِفتاحٍ في القامُوس — بابٌ مُسَمّىً لا وُجودَ لَه.");

        Assert.Equal(Ar(GrantKey), quoted[0]);

        // والزِرُّ يُرسَمُ داخِلَ استِمارَةِ المَنحِ بِالمِفتاحِ نَفسِه.
        var page = UsersPage();
        var form = page.IndexOf("/grant-admin\")", StringComparison.Ordinal);
        Assert.True(form > 0, "استِمارَةُ المَنحِ غَيرُ مَوجودَة — الأَداةُ عَمياء.");
        var button = page.IndexOf("@L.Markup(\"admin.tenant_users.grant\")", form, StringComparison.Ordinal);
        Assert.True(button > form && button - form < 400, "زِرُّ «مَنح إدارَة» لَيسَ داخِلَ استِمارَةِ المَنح.");
    }

    /// <summary>
    /// <para><b>بابُ الدُخولِ الَّذي تُسَمّيه الرِسالَةُ مُسَجَّلٌ ويولِدُ المُستَخدِم</b>:
    /// <c>/{slug}/login</c> في جَدوَلِ المَساراتِ المَمسوحِ بِالماسِحِ الواحِد
    /// (القاعِدَة ٨)، و<c>GetOrCreateUserAsync</c> تُخَزِّنُ <c>new User</c> حينَ لا
    /// نَظيرَ — فَأَوَّلُ دُخولٍ هُوَ أَوَّلُ صَفٍّ في هذِه الشاشَة.</para>
    /// </summary>
    [Fact]
    public void The_login_door_the_hint_names_is_a_registered_route_that_creates_the_user()
    {
        Assert.Contains("/{slug}/login", ComplianceInspectorTests.RepoRoutes);

        var handlers = Source("libs", "kits", "Auth", "ACommerce.Kit.Auth.Server", "AuthHandlers.cs");
        var create = handlers.IndexOf("GetOrCreateUserAsync(", StringComparison.Ordinal);
        Assert.True(create > 0, "`GetOrCreateUserAsync` غَيرُ مَوجودَة — الأَداةُ عَمياء.");
        Assert.Contains("existing = new User", handlers, StringComparison.Ordinal);
        Assert.Contains("session.Store(existing);", handlers, StringComparison.Ordinal);
    }

    /// <summary>
    /// <para><b>ولا زِرَّ أُضيف</b> — ‏ADR-032 §٦‑د: زِرُّ «اجعَلني البائِع/المُشرِف»
    /// يُبنى فَقَط إن أَثبَتَ قِياسُ §٦‑هـ مُستَهلِكَه. الحالَةُ الفارِغَةُ نَصٌّ
    /// وعُنوانٌ وأَيقونَة، بِلا <c>ActionHref</c> ولا <c>ActionLabel</c> ولا
    /// استِمارَة. فَيَومَ يُقاسُ ويُبنى يَحمَرُّ هذا الفَحصُ ويُرفَعُ بِقَرارٍ مَرئِيّ.</para>
    /// </summary>
    [Fact]
    public void No_button_was_added_before_the_click_count_was_measured()
    {
        var branch = EmptyBranch();
        Assert.DoesNotContain("ActionHref", branch, StringComparison.Ordinal);
        Assert.DoesNotContain("ActionLabel", branch, StringComparison.Ordinal);
        Assert.DoesNotContain("<form", branch, StringComparison.Ordinal);
        Assert.DoesNotContain("<a ", branch, StringComparison.Ordinal);
    }

    /// <summary><b>ولا نَصَّ خارِجَ القامُوس</b> (القاعِدَة ١١) — كُلُّ مِفتاحٍ تَقرَؤُه
    /// الشاشَةُ (بِـ<c>L[…]</c> أَو <c>L.Markup</c> أَو <c>L.Format</c>) في المَعجَمِ
    /// وبِقيمَةٍ حَقيقِيَّة. والعَدُّ يُطبَع.</summary>
    [Fact]
    public void Every_key_the_users_screen_reads_exists_in_the_arabic_lexicon()
    {
        var keys = Regex.Matches(UsersPage(), @"L(?:\.Markup|\.Format)?\(?\[?""(?<k>[a-z0-9_.]+)""")
            .Select(m => m.Groups["k"].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(keys.Count >= 9, $"أَداةٌ عَمياء: {keys.Count} مِفتاحاً فَقَط في شاشَةِ المُستَخدِمين.");
        Assert.Contains(HintKey, keys);
        Assert.Contains(TitleKey, keys);

        var lexicon = LocaleCatalog.Lexicon.ToHashSet(StringComparer.Ordinal);
        var missing = keys.Where(k => !lexicon.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.True(missing.Length == 0, $"مَفاتيحُ خارِجَ المَعجَم: {string.Join("، ", missing)}");

        var placeholders = keys.Where(k => LocaleCatalog.IsPlaceholderKey(LocaleCatalog.Arabic, k)).ToArray();
        Assert.True(placeholders.Length == 0, $"قيَمٌ نائِبَة: {string.Join("، ", placeholders)}");
    }
}
