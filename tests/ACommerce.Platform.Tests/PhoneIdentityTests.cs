using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ د57 «أ»: رَقَمٌ واحِدٌ بِصيغَتَين (05… و+966…) — هُوِيَّةٌ واحِدَة (‏2026-10-28) ═══════════════════════════════════════════
//
// قَرارُ المالِك د57 (٣) «أ»: «تُدمَج… ترحيلٌ يَكتُبُ كُلَّ رَقَمٍ بِالصيغَةِ الدَولِيَّةِ المُوَحَّدَة… وعِندَ الدُخولِ يُبحَثُ أَوَّلاً بِالرَقَمِ كَما كُتِب، ثُمَّ
// بِالصيغَةِ المُوَحَّدَة… ويُوَحَّدُ في الخُطوَةِ نَفسِها رَقَمُ مُشرِفِ المَنَصَّةِ حينَ يُطابَق». وتَنفيذُه «يَسبِقُه اختِبارٌ يُثَبِّتُ السُلوكَ الحاليّ» (القاعِدَة ٣) —
// وهذا هُو: أَربَعَةُ أَبوابٍ تَجِدُ الجَوّالَ وتَكتُبُه **كَما كُتِب**، فَصيغَتانِ لِرَقَمٍ واحِدٍ حِسابان.
public class PhoneIdentityTests(ITestOutputHelper output)
{
    internal static string AuthHandlersCode() => WriteEndpointGuardTests.StripComments(StudioConsentCharacterizationTests.Source(
        "libs", "kits", "Auth", "ACommerce.Kit.Auth.Server", "AuthHandlers.cs"));

    internal static string StudioAuthCode() => WriteEndpointGuardTests.StripComments(StudioConsentCharacterizationTests.Source(
        "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Services", "Incubator", "StudioAuth.cs"));

    internal static string AdminSeederCode() => WriteEndpointGuardTests.StripComments(StudioConsentCharacterizationTests.Source(
        "apps", "V1.App", "Seed", "PlatformAdminSeeder.cs"));

    /// <summary><b>اليَوم</b>: دُخولُ العُضوِ بِالجَوّالِ يَبحَثُ <c>u.Phone == phone</c> بِالنَصِّ كَما كُتِبَ ويُنشِئُ <c>Phone = phone</c> — فَلا يَجِدُ
    /// حِسابَ <c>05…</c> مَن كَتَبَ <c>+966…</c>، ويُنشِئُ لَه ثانِياً.</summary>
    [Fact]
    public void Today_the_member_login_finds_and_creates_the_phone_exactly_as_typed()
    {
        var code = AuthHandlersCode();
        var at = code.IndexOf("private static async Task<AuthResult> GetOrCreateUserAsync(", StringComparison.Ordinal);
        Assert.True(at > 0, "لا GetOrCreateUserAsync — الأَداةُ عَمياء.");
        var body = code[at..code.IndexOf("public static bool RaiseVerified(", at, StringComparison.Ordinal)];
        output.WriteLine($"جِسمُ GetOrCreateUserAsync: {body.Length} مِحرَفاً");
        Assert.Contains("(nationalId == null && u.Phone == phone) ||", body, StringComparison.Ordinal);
        Assert.Contains("Phone = phone,", body, StringComparison.Ordinal);
        Assert.DoesNotContain("PhoneNumber", body, StringComparison.Ordinal);
    }

    /// <summary><b>اليَوم</b>: دُخولُ الاستوديو يَبحَثُ <c>u.Phone == subject</c> (المَوضوعُ مَقصوصاً وَحدَه) ويُنشِئُ <c>Phone = subject</c>.</summary>
    [Fact]
    public void Today_the_studio_login_finds_and_creates_the_phone_exactly_as_typed()
    {
        var code = StudioAuthCode();
        Assert.Contains(": (await s.Query<StudioUser>().Where(u => u.Phone == subject).ToListAsync())", code, StringComparison.Ordinal);
        Assert.Contains(": new StudioUser { Id = Guid.NewGuid(), Phone = subject };", code, StringComparison.Ordinal);
        Assert.Equal("+966 50 000 0057", StudioAuth.NormalizeSubject(StudioAuthMethod.Phone, " +966 50 000 0057 "));
    }

    /// <summary><b>اليَوم</b>: هاتِفُ مُشرِفِ المَنَصَّةِ مَقصوصٌ كَما كُتِبَ (<c>PlatformAdminGrant.Decide</c>)، والبَذّارُ يُطابِقُه بِالمُساواةِ النَصِّيَّة —
    /// فَحِسابُ استوديو رُحِّلَ إلى <c>+9665…</c> لا يُطابِقُه مُتَغَيِّرٌ مَكتوبٌ <c>05…</c>.</summary>
    [Fact]
    public void Today_the_platform_admin_phone_is_matched_trimmed_as_typed()
    {
        var grant = PlatformAdminGrant.Decide(" 0500000057 ", null, isDevelopment: true, bootstrapVar: null);
        var trimmedAsTyped = grant.Phone == "0500000057";
        output.WriteLine($"Decide(مَقصوصٌ كَما كُتِب): {trimmedAsTyped}");
        Assert.Equal("0500000057", grant.Phone);
        Assert.Contains(".Where(u => u.Phone == phone).ToListAsync(ct)).FirstOrDefault();", AdminSeederCode(), StringComparison.Ordinal);
        Assert.Contains("() => new StudioUser { Id = Guid.NewGuid(), Phone = phone }", AdminSeederCode(), StringComparison.Ordinal);
    }

    /// <summary><b>اليَوم</b>: «أَضِف جَوّالَك» (د27) يَكتُبُ الجَوّالَ المَربوطَ كَما كُتِبَ (مَقصوصاً).</summary>
    [Fact]
    public void Today_a_linked_phone_is_stored_as_typed()
    {
        var user = new User { Id = Guid.NewGuid() };
        ContactLink.Link(user, ContactLink.Phone, ContactLink.Normalize(ContactLink.Phone, " 0500000057 "), DateTime.UnixEpoch);
        Assert.Equal("0500000057", user.Phone);
        Assert.True(user.PhoneVerified);
    }
}

[Collection(LiveProofCollection.Name)]
public class LivePhoneIdentityProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>يَدخُلُ بِالجَوّالِ كَما يَدخُلُ البابُ (<c>IssueAttempt</c> ثُمَّ <c>VerifyPhoneOtpHandler</c>) ويُعيدُ مُعَرِّفَ الحِسابِ وجَوّالَه المُخَزَّن.</summary>
    internal static async Task<(Guid Id, string Stored)> SignInAsync(IDocumentStore store, string slug, string typed)
    {
        const string code = "575757";
        AuthHandlers.IssueAttempt(slug, typed, code, AuthHandlers.AuthKind.PhoneOtp);
        var r = await AuthHandlers.VerifyPhoneOtpHandler(new VerifyPhoneOtp(typed, code), new VendorRegisterBucketTests.ResolvedTenant(slug), store);
        Assert.NotNull(r);
        await using var q = store.QuerySession(slug);
        return (r!.UserId, (await q.LoadAsync<User>(r.UserId))!.Phone);
    }

    internal static async Task CleanAsync(IDocumentStore store, string slug)
    {
        await using var s = store.LightweightSession(slug);
        foreach (var u in await s.Query<User>().ToListAsync()) s.Delete(u);
        await s.SaveChangesAsync();
    }

    /// <summary><b>اليَوم</b>: <c>0500000057</c> ثُمَّ <c>+966500000057</c> — رَقَمٌ واحِد — حِسابانِ عَلى Postgres، كُلٌّ بِكِتابَتِه.</summary>
    [Fact]
    public async Task Today_two_spellings_of_one_number_sign_in_as_two_member_accounts_on_Postgres()
    {
        if (!Enabled) return;
        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "ph-id-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var local = await SignInAsync(store, slug, "0500000057");
            var intl = await SignInAsync(store, slug, "+966500000057");
            var localAsTyped = local.Stored == "0500000057";
            var intlAsTyped = intl.Stored == "+966500000057";
            output.WriteLine($"05… ⇒ {local.Id:N} (مُخَزَّنٌ كَما كُتِب: {localAsTyped}) · +966… ⇒ {intl.Id:N} (كَما كُتِب: {intlAsTyped}) · حِسابان: {local.Id != intl.Id}");
            Assert.NotEqual(local.Id, intl.Id);
            Assert.Equal("0500000057", local.Stored);
            Assert.Equal("+966500000057", intl.Stored);
        }
        finally { await CleanAsync(store, slug); }
    }
}
