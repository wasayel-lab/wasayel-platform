using System.Reflection;
using System.Text.RegularExpressions;
using ACommerce.Kit.Auth;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>حَذفُ الحِسابِ داخِلَ التَطبيق — البَوّابَةُ والأَثَرُ
/// مَقيسانِ بِلا قاعِدَةِ بَيانات.</b> القَرارُ كُلُّه دالَّتانِ
/// نَقِيَّتان، فَيُقاسُ هُنا لا في اختِبارِ تَكامُل — نَفسُ حُجَّةِ
/// <c>DealCancelAuthorizationTests</c> حَرفاً.</para>
///
/// <para>ولِكُلِّ رَمزٍ في المَعجَمِ <b>اختِبارٌ موجِبٌ وسالِب</b>
/// (القاعِدَة ٤).</para>
/// </summary>
public class AccountDeletionTests
{
    private const string Word = "حذف";

    private static User Live() => new()
    {
        Id = Guid.NewGuid(),
        TenantSlug = "theme-demo",
        Phone = "0500000000",
        Email = "a@b.com",
        NationalId = "1234567890",
        FullName = "مُستَخدِمٌ حَيّ",
        AvatarUrl = "https://cdn/x.png",
        PhoneVerified = true,
        EmailVerified = true,
        ActiveRole = "vendor",
        AttributesJson = new() { ["bio"] = "نَصّ" },
        RoleAttributesJson = new() { ["vendor"] = new() { ["plate"] = "أ ب ج" } },
        PushSubscriptions = [new PushSubscription { Endpoint = "https://push/1" }],
        AnchorLat = 24.7,
        AnchorLng = 46.7,
        RadiusKm = 15,
    };

    // ─── المَعجَم ──────────────────────────────────────────────────

    [Fact]
    public void Exactly_four_violation_codes_and_they_are_these()
        => Assert.Equal(
            new[] { "not_authenticated", "user_not_found", "already_deleted", "confirmation_mismatch" },
            AccountDeletion.All);

    [Fact]
    public void A_code_outside_the_vocabulary_throws_at_composition_time()
        => Assert.Throws<ArgumentException>(() => AccountDeletion.Require("delete_everything"));

    [Fact]
    public void Every_code_in_the_vocabulary_is_accepted()
    {
        foreach (var c in AccountDeletion.All) Assert.Equal(c, AccountDeletion.Require(c));
        Assert.Equal(4, AccountDeletion.All.Count);
    }

    // ─── مُوجِب ────────────────────────────────────────────────────

    [Fact]
    public void A_live_user_typing_the_confirmation_word_may_delete()
        => Assert.Null(AccountDeletion.Validate(Live(), Word, Word));

    [Fact]
    public void Surrounding_whitespace_in_the_typed_word_is_forgiven()
        => Assert.Null(AccountDeletion.Validate(Live(), "  حذف  ", Word));

    [Fact]
    public void IsAllowed_agrees_with_Validate_on_the_allowed_case()
        => Assert.True(AccountDeletion.IsAllowed(Live(), Word, Word));

    // ─── سالِب ────────────────────────────────────────────────────

    [Fact]
    public void A_missing_user_document_is_refused()
        => Assert.Equal(AccountDeletion.UserNotFound,
            AccountDeletion.Validate(null, Word, Word)!.Code);

    [Fact]
    public void An_already_deleted_account_is_refused_not_silently_repeated()
    {
        var u = AccountDeletion.Erase(Live(), DateTime.UtcNow);
        Assert.Equal(AccountDeletion.AlreadyDeleted,
            AccountDeletion.Validate(u, Word, Word)!.Code);
    }

    [Fact]
    public void A_wrong_confirmation_word_is_refused()
        => Assert.Equal(AccountDeletion.ConfirmationMismatch,
            AccountDeletion.Validate(Live(), "نعم", Word)!.Code);

    [Fact]
    public void An_empty_confirmation_is_refused()
        => Assert.Equal(AccountDeletion.ConfirmationMismatch,
            AccountDeletion.Validate(Live(), null, Word)!.Code);

    /// <summary>التَرتيبُ مَقصود: الغِيابُ أَوَّلاً لِأَنَّه لا يُفشي
    /// شَيئاً، ثُمَّ الحالَة، ثُمَّ التَأكيد. ولَولاهُ لَصارَ خَطَأُ
    /// التَأكيدِ قِناعاً يَكشِفُ أَنَّ الحِسابَ مَوجود.</summary>
    [Fact]
    public void Absence_is_answered_before_the_confirmation_word()
        => Assert.Equal(AccountDeletion.UserNotFound,
            AccountDeletion.Validate(null, "خَطَأ", Word)!.Code);

    // ─── الأَثَر ───────────────────────────────────────────────────

    [Fact]
    public void Erasing_removes_every_field_that_points_at_a_person()
    {
        var at = new DateTime(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc);
        var u = AccountDeletion.Erase(Live(), at);

        Assert.Equal(AccountDeletion.ErasedName, u.FullName);
        Assert.Equal("", u.Phone);
        Assert.Equal("", u.Email);
        Assert.Null(u.NationalId);
        Assert.Null(u.AvatarUrl);
        Assert.False(u.PhoneVerified);
        Assert.False(u.EmailVerified);
        Assert.Equal("", u.ActiveRole);
        Assert.Empty(u.AttributesJson);
        Assert.Empty(u.RoleAttributesJson);
        Assert.Empty(u.PushSubscriptions);
        Assert.Equal(0, u.AnchorLat);
        Assert.Equal(0, u.AnchorLng);
        Assert.Equal(0, u.RadiusKm);
        Assert.Equal(at, u.DeletedAt);
        Assert.Equal(at, u.UpdatedAt);
    }

    /// <summary><b>والصَفُّ يَبقى</b>: المُعَرِّفُ ذاتُه لا يُمَسّ،
    /// لِأَنَّ الصَفَقاتِ والفَواتيرَ تُشيرُ إلَيه. ومَحوُ الوَثيقَةِ
    /// كانَ سَيَترُكُ سِجِلّاً مالِيّاً بِطَرَفٍ مَفقود.</summary>
    [Fact]
    public void Erasing_keeps_the_identifier_and_the_tenant_so_records_stay_whole()
    {
        var original = Live();
        var id = original.Id;
        var slug = original.TenantSlug;

        var u = AccountDeletion.Erase(original, DateTime.UtcNow);

        Assert.Equal(id, u.Id);
        Assert.Equal(slug, u.TenantSlug);
    }

    /// <summary>
    /// <para><b>حارِسُ التَسَرُّبِ المُستَقبَليّ</b> (القاعِدَة ٢):
    /// حَقلٌ يُضافُ إلى <see cref="User"/> ولا يُصَفَّرُ في
    /// <c>Erase</c> يَبقى بَعدَ الحَذفِ صامِتاً. فَتُعَدُّ الحُقولُ
    /// عَدّاً، ويُقابَلُ العَدَدُ بِما هُوَ مَقصودٌ إبقاؤُه.</para>
    ///
    /// <para>ولا يُقاسُ بِـ«لا شَيءَ فيه اسم»: الحُقولُ تُقارَنُ
    /// بِمَجموعَةٍ مُعلَنَة، فَإضافَةُ حَقلٍ تُحمِرُّ هذا الفَحصَ
    /// وتُجبِرُ صاحِبَه عَلى تَقريرِ مَصيرِه.</para>
    /// </summary>
    [Fact]
    public void Every_user_field_is_accounted_for_by_the_eraser()
    {
        var kept = new HashSet<string>(StringComparer.Ordinal)
        {
            // الهُوِيَّةُ التِقَنِيَّةُ والسِجِلُّ — يَبقَيانِ عَمداً.
            nameof(User.Id), nameof(User.TenantSlug), nameof(User.CreatedAt),
            nameof(User.UpdatedAt), nameof(User.DeletedAt),
            // ‏`Role` هُنا دَورُ المُصادَقَة (‏"user") لا دَورُ المَتجَر،
            // ولا يَدُلُّ عَلى شَخص.
            nameof(User.Role),
            // قَبولُ الشُروطِ: واقِعَةٌ قانونِيَّةٌ مُؤَرَّخَةٌ لا
            // بَيانٌ شَخصيّ، والاحتِفاظُ بِها هُوَ الَّذي يُثبِتُ
            // مَشروعِيَّةَ ما وَقَعَ قَبلَ الحَذف.
            nameof(User.AcceptedTermsAt), nameof(User.AcceptedTermsVersion),
            // مُشتَقٌّ مِن المَرساةِ المُصَفَّرَة، بِلا واضِع.
            nameof(User.HasAnchor),
        };

        var erased = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(User.FullName), nameof(User.Phone), nameof(User.Email),
            nameof(User.NationalId), nameof(User.AvatarUrl), nameof(User.PhoneVerified),
            nameof(User.EmailVerified), nameof(User.ActiveRole), nameof(User.AttributesJson),
            nameof(User.RoleAttributesJson), nameof(User.PushSubscriptions),
            nameof(User.AnchorLat), nameof(User.AnchorLng), nameof(User.RadiusKm),
        };

        var actual = typeof(User)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var unaccounted = actual.Except(kept).Except(erased).OrderBy(n => n).ToList();

        Assert.True(unaccounted.Count == 0,
            "حُقولٌ في User لا يَعرِفُ AccountDeletion.Erase مَصيرَها: " +
            string.Join("، ", unaccounted) +
            " — قَرِّر: أَتُمحى أَم تَبقى، ولا تُترَك صامِتَة.");

        // حارِسُ العَمى: الفَحصُ يَقيسُ شَيئاً فِعلاً.
        Assert.True(actual.Count >= 20, $"‏User فيه {actual.Count} حَقلاً فَقَط — الفَحصُ مَشكوكٌ فيه.");
    }

    // ─── الوُصولُ بِالنَقر (القاعِدَة ١٢) ───────────────────────────

    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static string Page(string name) => File.ReadAllText(Path.Combine(
        RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace",
        "Components", "Pages", name));

    /// <summary>
    /// <para><b>المَدخَلُ نِصفُ الميزَة.</b> شاشَةُ الحَذفِ ونُقطَتُها
    /// مَبنِيَّتانِ ومَقيسَتان، وبِلا رابِطٍ مِن «حِسابي»
    /// <b>غَيرُ مَوجودَتَين</b> — وشَرطُ المَتجَر ‏5.1.1(v) يوجِبُ أَن
    /// يَبلُغَها المُستَخدِمُ بِالنَقرِ داخِلَ التَطبيق.</para>
    ///
    /// <para>ولا يَكفي وُجودُ المَسارِ في جَدوَلِ التَوجيه: مَسارٌ
    /// يُكتَبُ في شَريطِ العُنوانِ لَيسَ مَسارَ نَقر.</para>
    /// </summary>
    [Fact]
    public void The_account_page_links_to_the_deletion_screen()
    {
        var me = Page("Me.razor");
        Assert.Matches(new Regex(@"me/delete"), me);
        Assert.Contains("account.delete.entry", me, StringComparison.Ordinal);
    }

    [Fact]
    public void The_deletion_screen_posts_to_the_endpoint_that_actually_deletes()
    {
        var page = Page("DeleteAccount.razor");
        Assert.Contains("/me/delete/confirm", page, StringComparison.Ordinal);
        Assert.Contains("account.delete.confirm_word", page, StringComparison.Ordinal);

        // وتَقولُ لِلمُستَخدِمِ ما يُحذَفُ وما يَبقى قَبلَ أَن يُؤَكِّد —
        // فَحَذفٌ يُخفي ما يُبقيه يَعِدُ بِما لا يَفي.
        Assert.Contains("account.delete.removed_items", page, StringComparison.Ordinal);
        Assert.Contains("account.delete.retained_note", page, StringComparison.Ordinal);
    }

    /// <summary>والنُقطَةُ تَمُرُّ مِن الدالَّتَينِ النَقِيَّتَينِ ولا
    /// تُعيدُ كِتابَةَ القَرار (القاعِدَة ٦).</summary>
    [Fact]
    public void The_endpoint_delegates_to_the_pure_gate_and_clears_the_session()
    {
        var src = File.ReadAllText(Path.Combine(
            RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace",
            "MarketplaceTemplateExtensions.cs"));

        var body = src[src.IndexOf("/{slug}/me/delete/confirm", StringComparison.Ordinal)..];
        body = body[..body.IndexOf("RequireStoreWritable", StringComparison.Ordinal)];

        Assert.Contains("AccountDeletion.Validate", body, StringComparison.Ordinal);
        Assert.Contains("AccountDeletion.Erase", body, StringComparison.Ordinal);
        Assert.Contains("ClearAllCookiesForTenant", body, StringComparison.Ordinal);
    }
}
