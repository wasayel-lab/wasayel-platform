using System.Text.RegularExpressions;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using ACommerce.Templates.Customer.Marketplace.Services;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>شَخصٌ واحِدٌ بِحِسابَين — دَخَلَ مَرَّةً بِالجَوّالِ ومَرَّةً بِالبَريد. قَرارُ المالِك د27 (‏2026-10-28، «ج»).</b> «لا دَمجَ تِلقائيّ. يُضافُ في
/// «حِسابي» قِسمُ «أَضِف بَريدَك»: يَدخُلُ الشَخصُ بِجَوّالِه، ثُمَّ يُثبِتُ بَريدَه بِرَمزٍ يَصِلُه، فَيُربَطُ البَريدُ بِالحِسابِ نَفسِه… وبِالمِثلِ يُضيفُ مَن
/// بَدَأَ بِالبَريدِ جَوّالَه. وإن كانَ البَريدُ مُستَعمَلاً في حِسابٍ ثانٍ يُرفَضُ الرَبطُ بِرِسالَةٍ تَشرَحُ الحَلّ… ولا يُدمَجُ شَيءٌ بِصَمت.» ومَرجِعُه
/// NIST 800-63B: لا تُضافُ وَسيلَةُ دُخولٍ إلّا مِن جَلسَةٍ مُثبَتَةٍ وبَعدَ إثباتِ مِلكِيَّتِها.</para>
///
/// <para><b>«اليَوم» قَبلَ البِناء</b> (القاعِدَة ٣): لا بابَ يَربِطُ وَسيلَةً ثانِيَةً بِحِسابٍ قائِم، وصَفحَةُ «حِسابي» لا تَعرِضُ شَيئاً مِنه، وأَنواعُ
/// المُحاوَلَةِ ثَلاثَةٌ (جَوّالٌ ونَفاذٌ وبَريدٌ لِلدُخول)، والدُخولُ بِالبَريدِ يَبحَثُ بِالبَريدِ وَحدَه ويُنشِئُ حِساباً جَديداً إن لَم يَجِد. وكُلُّ صَفٍّ
/// «Today_» يَنقَلِبُ بِاسمِه في كوميتِ البِناء.</para>
/// </summary>
public class AccountContactLinkTests(ITestOutputHelper output)
{
    internal static string Tpl(params string[] parts) => File.ReadAllText(Path.Combine(
        new[] { ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace" }.Concat(parts).ToArray()));

    internal static string AuthHandlersSource() => WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(
        ThemeZeroEquivalenceTests.RepoRoot, "libs", "kits", "Auth", "ACommerce.Kit.Auth.Server", "AuthHandlers.cs")));

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-28، د27)</b> — كانَ <c>Today_no_endpoint_links_a_second_contact_to_an_existing_account</c> (‏<c>bad4a73</c>).
    /// والآن بابانِ — البَدءُ والتَأكيد — كُلٌّ حارِسُه جَلسَةُ صاحِبِه ثُمَّ نِداءٌ واحِدٌ لِـ<c>AccountContacts</c>، والرَدُّ بِمُعَرِّفِ المُحاوَلَةِ لا بِالوَسيلَة؛
    /// و«حِسابي» يَرسُمُ الاستِمارَتَينِ بِحَقلِ نَوعِهِما واستِمارَةَ الرَمزِ بِمُعَرِّفِ المُحاوَلَة.</summary>
    [Fact]
    public void Two_doors_link_a_second_contact_from_the_account_page_and_no_link_carries_the_contact()
    {
        var mte = WriteEndpointGuardTests.StripComments(Tpl("MarketplaceTemplateExtensions.cs"));
        string Body(string route)
        {
            var at = mte.IndexOf($"app.MapPost(\"{route}\"", StringComparison.Ordinal);
            Assert.True(at > 0, $"لا {route} — الأَداةُ عَمياء.");
            return mte[at..mte.IndexOf("}).RequireFormBody().DisableAntiforgery();", at, StringComparison.Ordinal)];
        }
        var start = Body("/{slug}/me/contact/start");
        var verify = Body("/{slug}/me/contact/verify");
        foreach (var body in new[] { start, verify })
        {
            var guard = body.IndexOf("AuthHandlers.ParseToken(AuthSession.ResolveToken(req, slug))", StringComparison.Ordinal);
            var call = body.IndexOf("Services.AccountContacts.", StringComparison.Ordinal);
            Assert.True(guard > 0 && call > guard, "الحارِسُ قَبلَ النِداء.");
            Assert.DoesNotContain("Uri.EscapeDataString(req.Form", body, StringComparison.Ordinal);
        }
        Assert.Contains("$\"me?contact=verify&attempt={Uri.EscapeDataString(id)}\"", start, StringComparison.Ordinal);
        Assert.Contains("Services.AccountContacts.AfterVerify(slug, parsed.Value.UserId, attempt, refusal)", verify, StringComparison.Ordinal);

        var me = WriteEndpointGuardTests.StripMarkupComments(Tpl("Components", "Pages", "Me.razor"));
        output.WriteLine($"«حِسابي»: استِمارَتا بَدء {Regex.Matches(me, "me/contact/start").Count} · تَأكيد {Regex.Matches(me, "me/contact/verify").Count}");
        Assert.Equal(2, Regex.Matches(me, @"action=""@\(\$""/\{Slug\}/me/contact/start""\)""").Count);
        Assert.Single(Regex.Matches(me, @"action=""@\(\$""/\{Slug\}/me/contact/verify""\)"""));
        Assert.Contains("<input type=\"hidden\" name=\"attempt\" value=\"@Attempt\" />", me, StringComparison.Ordinal);
        Assert.Contains("offerEmail = string.IsNullOrEmpty(me.Email) && Sp.GetService(typeof(ACommerce.Kit.Auth.IEmailOtpChannel)) is not null;", me, StringComparison.Ordinal);
        Assert.Contains("offerPhone = string.IsNullOrEmpty(me.Phone) && Sp.GetService(typeof(ACommerce.Kit.Auth.IOtpChannel)) is not null;", me, StringComparison.Ordinal);
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-28، د27)</b> — كانَ <c>Today_the_attempt_kinds_are_phone_nafath_and_email_login_alone</c>. والآن خَمسَة:
    /// رَمزُ الرَبطِ نَوعٌ مُستَقِلٌّ عَن رَمزِ الدُخول (<c>AddEmail</c>، <c>AddPhone</c>) — ويُضافانِ بَعدَ الثَلاثَةِ فَلا تَتَغَيَّرُ قيَمُها.</summary>
    [Fact]
    public void The_attempt_kinds_add_two_link_kinds_after_the_three_sign_in_kinds()
    {
        var names = Enum.GetNames<AuthHandlers.AuthKind>();
        output.WriteLine($"أَنواعُ المُحاوَلَة: {string.Join(", ", names)}");
        Assert.Equal(new[] { "PhoneOtp", "Nafath", "EmailOtp", "AddEmail", "AddPhone" }, names);
        Assert.Equal((0, 1, 2), ((int)AuthHandlers.AuthKind.PhoneOtp, (int)AuthHandlers.AuthKind.Nafath, (int)AuthHandlers.AuthKind.EmailOtp));
    }

    /// <summary>كانَ <c>Today_email_sign_in_looks_up_the_email_alone_and_creates_a_new_account_when_none_holds_it</c> (‏<c>bad4a73</c>) — وبَقِيَ صادِقاً
    /// بَعدَ البِناءِ عَمداً: «لا دَمجَ تِلقائيّ» (د27). فَقُلِبَ اسمُه مِن «اليَوم» إلى الدائِم، والجِسمُ بِحَرفِه.</summary>
    [Fact]
    public void Email_sign_in_still_looks_up_the_email_alone_and_merges_nothing()
    {
        var src = AuthHandlersSource();
        var at = src.IndexOf("private static async Task<AuthResult> GetOrCreateUserAsync(", StringComparison.Ordinal);
        Assert.True(at > 0, "لا GetOrCreateUserAsync — الأَداةُ عَمياء.");
        var body = src[at..src.IndexOf("public static bool RaiseVerified(", at, StringComparison.Ordinal)];
        Assert.Contains("? await session.Query<User>().FirstOrDefaultAsync(u => u.Email == email)", body, StringComparison.Ordinal);
        Assert.Contains("if (existing is null)", body, StringComparison.Ordinal);
        Assert.Contains("existing = new User", body, StringComparison.Ordinal);
    }

    // ─── المُصادِق: لِكُلِّ رَمزٍ مُوجِبٌ وسالِب (القاعِدَة ٤) ─────────────────────────────────────────────

    internal static User Phoned(string phone = "0512345678") => new() { Id = Guid.NewGuid(), Phone = phone, PhoneVerified = true };
    internal static User Emailed(string email = "sara@example.com") => new() { Id = Guid.NewGuid(), Email = email, EmailVerified = true };

    [Fact]
    public void A_phone_account_may_add_a_valid_email_that_no_other_account_holds()
    {
        var user = Phoned();
        Assert.Null(ContactLink.Validate(user, ContactLink.Email, "sara@example.com"));
        Assert.Null(ContactLink.Validate(user, ContactLink.Email, "sara@example.com", Array.Empty<User>()));
        Assert.True(ContactLink.IsAllowed(user, ContactLink.Email, "sara@example.com", new[] { user }));   // نَفسُه لا يُعَدُّ حامِلاً
        var deleted = Emailed(); deleted.DeletedAt = DateTime.UtcNow;
        Assert.True(ContactLink.IsAllowed(user, ContactLink.Email, "sara@example.com", new[] { deleted })); // المَحذوفُ لا يَحمِلُ شَيئاً
    }

    [Theory]
    [InlineData(ContactLink.NoAccount)]
    [InlineData(ContactLink.KindUnknown)]
    [InlineData(ContactLink.Invalid)]
    [InlineData(ContactLink.AlreadySet)]
    [InlineData(ContactLink.InUse)]
    public void Each_refusal_code_has_its_case(string code)
    {
        var user = Phoned();
        var deleted = Phoned(); deleted.DeletedAt = DateTime.UtcNow;
        var v = code switch
        {
            ContactLink.NoAccount => ContactLink.Validate(deleted, ContactLink.Email, "a@b.co"),
            ContactLink.KindUnknown => ContactLink.Validate(user, "fax", "a@b.co"),
            ContactLink.Invalid => ContactLink.Validate(user, ContactLink.Email, "not-an-email"),
            ContactLink.AlreadySet => ContactLink.Validate(user, ContactLink.Phone, "0599999999"),
            _ => ContactLink.Validate(user, ContactLink.Email, "a@b.co", new[] { Emailed("a@b.co") }),
        };
        Assert.Equal(code, v?.Code);
        Assert.Contains(code, ContactLink.All);
        Assert.Equal(ContactLink.NoAccount, ContactLink.Validate(null, ContactLink.Email, "a@b.co")?.Code);
        Assert.Equal(ContactLink.Invalid, ContactLink.Validate(Emailed(), ContactLink.Phone, "  ")?.Code);
    }

    [Fact]
    public void Linking_writes_the_contact_verified_and_the_normaliser_matches_sign_in()
    {
        var user = Phoned();
        var at = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        ContactLink.Link(user, ContactLink.Email, ContactLink.Normalize(ContactLink.Email, "  Sara@Example.COM "), at);
        Assert.Equal(("sara@example.com", true, at), (user.Email, user.EmailVerified, user.UpdatedAt));
        var mail = Emailed();
        ContactLink.Link(mail, ContactLink.Phone, ContactLink.Normalize(ContactLink.Phone, " 0512345678 "), at);
        Assert.Equal(("0512345678", true), (mail.Phone, mail.PhoneVerified));
        Assert.Equal(EmailAddress.Normalize(" X@Y.Co "), ContactLink.Normalize(ContactLink.Email, " X@Y.Co "));
    }

    [Fact]
    public void A_link_attempt_is_bound_to_its_account()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var subject = ContactLink.Subject(a, "sara@example.com");
        Assert.Equal("sara@example.com", ContactLink.ValueFor(subject, a));
        Assert.Null(ContactLink.ValueFor(subject, b));
        Assert.Null(ContactLink.ValueFor(null, a));
        Assert.Null(ContactLink.ValueFor($"{a:N}|", a));
    }

    private sealed class CapturingEmail : IEmailOtpChannel
    {
        public string? Code;
        public string? To;
        public string ChannelName => "capture";
        public string? DevHintCode => null;
        public Task SendOtpAsync(string email, string code, CancellationToken ct) { To = email; Code = code; return Task.CompletedTask; }
    }

    /// <summary>رَمزُ الرَبطِ لا يُدخِل ورَمزُ الدُخولِ لا يَربِط: المُحاوَلَةُ بِنَوعِ <c>AddEmail</c> ومَوضوعِ الحِسابِ والوَسيلَة، والدُخولُ بِالبَريدِ
    /// (نَوعُ <c>EmailOtp</c> ومَوضوعُه البَريد) لا يَجِدُها؛ ونَوعٌ غَيرُ نَوعَي الرَبطِ يُرفَضُ عِندَ الإصدار.</summary>
    [Fact]
    public async Task A_link_code_is_its_own_kind_and_does_not_sign_in()
    {
        var slug = "link-" + Guid.NewGuid().ToString("N")[..8];
        var user = Guid.NewGuid();
        var channel = new CapturingEmail();
        var subject = ContactLink.Subject(user, "sara@example.com");
        var id = await AuthHandlers.RequestContactCodeAsync(slug, AuthHandlers.AuthKind.AddEmail, subject, "sara@example.com", null, channel.SendOtpAsync, default);
        Assert.Equal("sara@example.com", channel.To);
        Assert.Equal(subject, AuthHandlers.SubjectOf(slug, id, AuthHandlers.AuthKind.AddEmail));
        Assert.Null(AuthHandlers.SubjectOf(slug, id, AuthHandlers.AuthKind.EmailOtp));
        Assert.False(AuthHandlers.ConsumeAttempt(slug, "sara@example.com", channel.Code!, AuthHandlers.AuthKind.EmailOtp));
        Assert.True(AuthHandlers.ConsumeAttempt(slug, subject, channel.Code!, AuthHandlers.AuthKind.AddEmail));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            AuthHandlers.RequestContactCodeAsync(slug, AuthHandlers.AuthKind.EmailOtp, subject, "sara@example.com", null, channel.SendOtpAsync, default));
    }

    [Fact]
    public void After_verifying_the_account_page_is_told_by_code_and_never_by_contact()
    {
        var user = Guid.NewGuid();
        Assert.Equal("me?contact=linked", AccountContacts.AfterVerify("s", user, "x", null));
        Assert.Equal("me?err=contact_in_use", AccountContacts.AfterVerify("s", user, "x", ContactLink.InUse));
        Assert.Equal("me?err=contact_code_invalid", AccountContacts.AfterVerify("s", user, "dead-attempt", AccountContacts.CodeInvalid));
        Assert.Equal(ContactLink.All.Count + 3, AccountContacts.Codes.Distinct().Count());
        foreach (var code in AccountContacts.Codes)
            Assert.False(string.IsNullOrWhiteSpace(ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, $"me.contact.err_{code}")),
                $"لا نَصَّ في القامُوسِ لِرَمزِ «{code}».");
    }
}

/// <summary>
/// <para><b>بُرهانُ د27 حَيّاً عَلى Postgres</b> (‏<c>WASAYEL_LIVE_PROOF=1</c>): حِسابٌ بِالجَوّالِ يَربِطُ بَريدَه بِرَمزٍ يَصِلُه فَيَدخُلُ بِه بَعدَها إلى الحِسابِ
/// <b>نَفسِه</b>؛ وحِسابٌ ثانٍ يُحاوِلُ رَبطَ البَريدِ نَفسِه فَيُرَدُّ بِـ<c>contact_in_use</c> ولا يُدمَجُ شَيء.</para>
/// </summary>
[Collection(LiveProofCollection.Name)]
public class LiveAccountContactLinkProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private sealed class CapturingEmail : IEmailOtpChannel
    {
        public string? Code;
        public string ChannelName => "capture";
        public string? DevHintCode => null;
        public Task SendOtpAsync(string email, string code, CancellationToken ct) { Code = code; return Task.CompletedTask; }
    }

    [Fact]
    public async Task A_phone_account_links_its_email_signs_in_with_it_and_a_second_account_is_refused_on_Postgres()
    {
        if (!Enabled) return;
        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "cl-zz-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            const string code = "424242";
            async Task<ACommerce.Kit.Auth.AuthResult> PhoneIn(string phone)
            {
                AuthHandlers.IssueAttempt(slug, phone, code, AuthHandlers.AuthKind.PhoneOtp);
                return (await AuthHandlers.VerifyPhoneOtpHandler(new VerifyPhoneOtp(phone, code), new VendorRegisterBucketTests.ResolvedTenant(slug), store))!;
            }
            var a = await PhoneIn("966500000271");
            var b = await PhoneIn("966500000272");
            var mail = new CapturingEmail();

            var startA = await AccountContacts.StartAsync(store, slug, a.UserId, ContactLink.Email, " Sara.Link@Example.com ", null, mail, default);
            var linkedA = await AccountContacts.VerifyAsync(store, slug, a.UserId, startA.AttemptId, mail.Code, DateTime.UtcNow, default);

            AuthHandlers.IssueAttempt(slug, "sara.link@example.com", code, AuthHandlers.AuthKind.EmailOtp);
            var byEmail = await AuthHandlers.VerifyEmailOtpHandler(new VerifyEmailOtp("sara.link@example.com", code), new VendorRegisterBucketTests.ResolvedTenant(slug), store);

            var startB = await AccountContacts.StartAsync(store, slug, b.UserId, ContactLink.Email, "sara.link@example.com", null, mail, default);
            var linkedB = await AccountContacts.VerifyAsync(store, slug, b.UserId, startB.AttemptId, mail.Code, DateTime.UtcNow, default);

            int accounts;
            await using (var q = store.QuerySession(slug)) accounts = (await Marten.QueryableExtensions.ToListAsync(q.Query<User>())).Count;
            output.WriteLine($"أ رَبَطَ بَريدَه: {(linkedA is null ? "رُبِط" : linkedA)} · الدُخولُ بِالبَريد ← الحِسابُ نَفسُه: {byEmail?.UserId == a.UserId} · ب: {linkedB} · الحِسابات: {accounts}");
            Assert.Null(startA.Refusal);
            Assert.Null(linkedA);
            Assert.Equal(a.UserId, byEmail!.UserId);
            Assert.Null(startB.Refusal);
            Assert.Equal(ContactLink.InUse, linkedB);
            Assert.Equal(2, accounts);
        }
        finally { await LiveZeroRoleAdminProofTests.CleanAsync(store, slug); }
    }
}
