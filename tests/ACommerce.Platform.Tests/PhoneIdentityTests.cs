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
//
// ‏(‏2026-10-28، كوميتُ التَبديل؛ ‏ADR-044 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه): صُفوفُ «Today_» الخَمسَةُ قُلِبَت بِأَسمائِها — الأَبوابُ الأَربَعَةُ تَسأَلُ
// `PhoneNumber` (كَما كُتِبَ ثُمَّ مُوَحَّداً، والجَديدُ مُوَحَّداً)، وصُفوفُ الدالَّةِ النَقِيَّةِ مُوجَبَةً وسالِبَةً بَعدَها.
public class PhoneIdentityTests(ITestOutputHelper output)
{
    internal static string AuthHandlersCode() => WriteEndpointGuardTests.StripComments(StudioConsentCharacterizationTests.Source(
        "libs", "kits", "Auth", "ACommerce.Kit.Auth.Server", "AuthHandlers.cs"));

    internal static string StudioAuthCode() => WriteEndpointGuardTests.StripComments(StudioConsentCharacterizationTests.Source(
        "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Services", "Incubator", "StudioAuth.cs"));

    internal static string AdminSeederCode() => WriteEndpointGuardTests.StripComments(StudioConsentCharacterizationTests.Source(
        "apps", "V1.App", "Seed", "PlatformAdminSeeder.cs"));

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-28، ‏ADR-044)</b> — كانَ <c>Today_the_member_login_finds_and_creates_the_phone_exactly_as_typed</c>
    /// (‏<c>d72e34e</c>): <c>u.Phone == phone</c> و<c>Phone = phone</c>. والآن صِيَغُ <c>PhoneNumber.LookupForms</c> في الاستِعلامِ و<c>Pick</c> يُقَدِّمُ
    /// ما كُتِب، والجَديدُ <c>PhoneNumber.Canonical</c>؛ ونَفاذٌ والبَريدُ عَلى حالِهِما.</summary>
    [Fact]
    public void The_member_login_finds_the_phone_as_typed_then_canonical_and_creates_it_canonical()
    {
        var code = AuthHandlersCode();
        var at = code.IndexOf("private static async Task<AuthResult> GetOrCreateUserAsync(", StringComparison.Ordinal);
        Assert.True(at > 0, "لا GetOrCreateUserAsync — الأَداةُ عَمياء.");
        var body = code[at..code.IndexOf("public static bool RaiseVerified(", at, StringComparison.Ordinal)];
        output.WriteLine($"جِسمُ GetOrCreateUserAsync: {body.Length} مِحرَفاً");
        Assert.Contains("var phoneForms = PhoneNumber.LookupForms(phone).ToArray();", body, StringComparison.Ordinal);
        Assert.Contains(": PhoneNumber.Pick(await session.Query<User>().Where(u => phoneForms.Contains(u.Phone)).ToListAsync(), u => u.Phone, phone);", body, StringComparison.Ordinal);
        Assert.Contains("Phone = PhoneNumber.Canonical(phone),", body, StringComparison.Ordinal);
        Assert.Contains("await session.Query<User>().FirstOrDefaultAsync(u => u.NationalId == nationalId)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("u.Phone == phone", body, StringComparison.Ordinal);
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-28، ‏ADR-044)</b> — كانَ <c>Today_the_studio_login_finds_and_creates_the_phone_exactly_as_typed</c>
    /// (‏<c>d72e34e</c>). والآن قاعِدَةُ دُخولِ العُضوِ بِعَينِها؛ والمَوضوعُ (المُحاوَلَةُ والحِصَّة) مَقصوصاً كَما كُتِبَ كَما كان.</summary>
    [Fact]
    public void The_studio_login_finds_the_phone_as_typed_then_canonical_and_creates_it_canonical()
    {
        var code = StudioAuthCode();
        Assert.Contains("var phoneForms = PhoneNumber.LookupForms(subject).ToArray();", code, StringComparison.Ordinal);
        Assert.Contains(": PhoneNumber.Pick(await s.Query<StudioUser>().Where(u => phoneForms.Contains(u.Phone)).ToListAsync(), u => u.Phone, subject);", code, StringComparison.Ordinal);
        Assert.Contains(": new StudioUser { Id = Guid.NewGuid(), Phone = PhoneNumber.Canonical(subject) };", code, StringComparison.Ordinal);
        Assert.DoesNotContain("u.Phone == subject", code, StringComparison.Ordinal);
        Assert.Equal("+966 50 000 0057", StudioAuth.NormalizeSubject(StudioAuthMethod.Phone, " +966 50 000 0057 "));
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-28، ‏ADR-044)</b> — كانَ <c>Today_the_platform_admin_phone_is_matched_trimmed_as_typed</c>
    /// (‏<c>d72e34e</c>). «ويُوَحَّدُ رَقَمُ مُشرِفِ المَنَصَّةِ حينَ يُطابَق»: المُتَغَيِّرُ مَقصوصٌ كَما كان، والبَذّارُ يُطابِقُه كَما كُتِبَ ثُمَّ مُوَحَّداً ويَكتُبُ
    /// الجَديدَ مُوَحَّداً — فَحِسابُ استوديو رُحِّلَ إلى <c>+9665…</c> يَبقى مُشرِفاً ومُتَغَيِّرُه <c>05…</c>.</summary>
    [Fact]
    public void The_platform_admin_phone_is_matched_as_typed_then_canonical_and_created_canonical()
    {
        var grant = PlatformAdminGrant.Decide(" 0500000057 ", null, isDevelopment: true, bootstrapVar: null);
        var trimmedAsTyped = grant.Phone == "0500000057";
        var migrated = new StudioUser { Id = Guid.NewGuid(), Phone = "+966500000057" };
        var matchesMigrated = ACommerce.Kit.Auth.PhoneNumber.Pick(new[] { migrated }, u => u.Phone, grant.Phone) is not null;
        output.WriteLine($"Decide(مَقصوصٌ كَما كُتِب): {trimmedAsTyped} · يُطابِقُ حِساباً مُرَحَّلاً: {matchesMigrated}");
        Assert.True(trimmedAsTyped);
        Assert.True(matchesMigrated);
        var seeder = AdminSeederCode();
        Assert.Contains("var phoneForms = ACommerce.Kit.Auth.PhoneNumber.LookupForms(phone).ToArray();", seeder, StringComparison.Ordinal);
        Assert.Contains(".Where(u => phoneForms.Contains(u.Phone)).ToListAsync(ct), u => u.Phone, phone);", seeder, StringComparison.Ordinal);
        Assert.Contains("() => new StudioUser { Id = Guid.NewGuid(), Phone = ACommerce.Kit.Auth.PhoneNumber.Canonical(phone) }", seeder, StringComparison.Ordinal);
        Assert.DoesNotContain("u.Phone == phone", seeder, StringComparison.Ordinal);
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-28، ‏ADR-044)</b> — كانَ <c>Today_a_linked_phone_is_stored_as_typed</c> (‏<c>d72e34e</c>).</summary>
    [Fact]
    public void A_linked_phone_is_stored_canonical()
    {
        var user = new User { Id = Guid.NewGuid() };
        ContactLink.Link(user, ContactLink.Phone, ContactLink.Normalize(ContactLink.Phone, " 0500000057 "), DateTime.UnixEpoch);
        Assert.Equal("+966500000057", user.Phone);
        Assert.True(user.PhoneVerified);
    }

    // ─── الدالَّةُ النَقِيَّة — مُوجَبٌ وسالِب ─────────────────────────────────────────────────────

    /// <summary>صِيَغُ البَحث: سُعودِيٌّ مَحَلِّيٌّ ⇒ كَما كُتِبَ ثُمَّ المُوَحَّد؛ ومُوَحَّدٌ أَصلاً أَو خارِجَ السُعودِيَّةِ أَو <c>NID-…</c> ⇒ صيغَةٌ واحِدَة؛ والفارِغُ ⇒ لا شَيء.</summary>
    [Theory]
    [InlineData("0500000057", "0500000057|+966500000057")]
    [InlineData(" 050 000 0057 ", "050 000 0057|+966500000057")]
    [InlineData("966500000057", "966500000057|+966500000057")]
    [InlineData("+966500000057", "+966500000057")]
    [InlineData("+12025550123", "+12025550123")]
    [InlineData("NID-1052001001", "NID-1052001001")]
    [InlineData("   ", "")]
    public void Lookup_forms_are_as_typed_then_canonical_without_repeats(string typed, string expected)
        => Assert.Equal(expected, string.Join("|", ACommerce.Kit.Auth.PhoneNumber.LookupForms(typed)));

    /// <summary><c>Pick</c> يُقَدِّمُ مَن يَحمِلُ الرَقَمَ كَما كُتِب، ثُمَّ المُوَحَّد، ولا يَختارُ غَيرَهُما (سالِب).</summary>
    [Fact]
    public void Pick_prefers_the_holder_of_the_typed_form_then_the_canonical_one_and_nothing_else()
    {
        var canonical = new User { Id = Guid.NewGuid(), Phone = "+966500000057" };
        var local = new User { Id = Guid.NewGuid(), Phone = "0500000057" };
        var other = new User { Id = Guid.NewGuid(), Phone = "0500000058" };
        var all = new[] { canonical, local, other };
        Assert.Same(local, ACommerce.Kit.Auth.PhoneNumber.Pick(all, u => u.Phone, "0500000057"));
        Assert.Same(canonical, ACommerce.Kit.Auth.PhoneNumber.Pick(all, u => u.Phone, "+966 50 000 0057"));
        Assert.Same(canonical, ACommerce.Kit.Auth.PhoneNumber.Pick(new[] { canonical, other }, u => u.Phone, "0500000057"));
        Assert.Null(ACommerce.Kit.Auth.PhoneNumber.Pick(new[] { other }, u => u.Phone, "0500000057"));
        Assert.Null(ACommerce.Kit.Auth.PhoneNumber.Pick(all, u => u.Phone, ""));
    }

    /// <summary>دالَّةٌ واحِدَة: <c>AuthHandlers.PhoneDestination</c> (وِجهَةُ الرَمزِ ومِفتاحُ حِصَّتِه) هِيَ <c>PhoneNumber.Canonical</c> لِكُلِّ عَيِّنَةٍ مِن
    /// صُفوفِ «وِجهَةِ الرَمز» — فَلا تَعريفانِ يَنجَرِفان. يُطبَعُ العَدُّ ويَفشَلُ عَلى الصِفر.</summary>
    [Fact]
    public void The_code_destination_and_the_identity_are_one_function()
    {
        var samples = new[] { "0551234567", "551234567", "966551234567", "00966551234567", "+966551234567", "+966 55 123 4567", "055-123-4567",
                              "(055) 1234567", "+12025550123", "abc", "", "NID-1", "05512345678" };
        var same = samples.Count(x => AuthHandlers.PhoneDestination(x) == ACommerce.Kit.Auth.PhoneNumber.Canonical(x));
        output.WriteLine($"عَيِّنات: {samples.Length} · مُتَطابِقَة: {same}");
        Assert.NotEmpty(samples);
        Assert.Equal(samples.Length, same);
        Assert.Contains("public static string PhoneDestination(string? typed) => PhoneNumber.Canonical(typed);", AuthHandlersCode(), StringComparison.Ordinal);
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

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-28، ‏ADR-044)</b> — كانَ <c>Today_two_spellings_of_one_number_sign_in_as_two_member_accounts_on_Postgres</c>
    /// (‏<c>d72e34e</c>: حِسابان). والآن <c>0500000057</c> يُنشِئُ <c>+966500000057</c>، و<c>+966500000057</c> يَجِدُه — حِسابٌ واحِد؛ وحِسابٌ قائِمٌ لَم يُرَحَّل
    /// (<c>05…</c> مَكتوبٌ بِيَدٍ كَما كانَ يَكتُبُه الدُخول) يَجِدُه دُخولُه بِالكِتابَةِ نَفسِها ولا يُعادُ كَتبُ جَوّالِه.</summary>
    [Fact]
    public async Task Two_spellings_of_one_number_sign_in_as_one_member_account_and_an_unmigrated_one_still_signs_in_on_Postgres()
    {
        if (!Enabled) return;
        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "ph-id-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var local = await SignInAsync(store, slug, "0500000057");
            var intl = await SignInAsync(store, slug, "+966500000057");
            var legacyId = Guid.NewGuid();
            await using (var w = store.LightweightSession(slug))
            {
                w.Store(new User { Id = legacyId, TenantSlug = slug, Phone = "0500000058", FullName = "قائِم", PhoneVerified = true });
                await w.SaveChangesAsync();
            }
            var legacy = await SignInAsync(store, slug, "0500000058");
            var storedCanonical = local.Stored == "+966500000057";
            var legacyKept = legacy.Stored == "0500000058";
            output.WriteLine($"05… ⇒ {local.Id:N} · +966… ⇒ {intl.Id:N} · حِسابٌ واحِد: {local.Id == intl.Id} · مُخَزَّنٌ مُوَحَّداً: {storedCanonical} · "
                + $"القائِمُ وَجَدَه دُخولُه: {legacy.Id == legacyId} · جَوّالُه كَما كان: {legacyKept}");
            Assert.Equal(local.Id, intl.Id);
            Assert.True(storedCanonical);
            Assert.Equal(legacyId, legacy.Id);
            Assert.True(legacyKept);
        }
        finally { await CleanAsync(store, slug); }
    }
}
