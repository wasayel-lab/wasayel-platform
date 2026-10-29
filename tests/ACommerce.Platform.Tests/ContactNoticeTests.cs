using System.Reflection;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using ACommerce.Templates.Customer.Marketplace.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>جَلسَةٌ مُستَعارَةٌ تَزرَعُ وَسيلَةَ دُخولٍ ثانِيَةً تَبقى بَعدَ «اخرج من كل الأجهزة»</b> (‏2026-10-29، تَدقيقُ الموجَة ١١ A3). بابا الرَبطِ
/// مَحروسانِ بِالجَلسَةِ وَحدَها، و<c>AccountContacts.VerifyAsync</c> يَربِطُ ويَحفَظُ بِلا إشعارٍ لِلوَسيلَةِ المُتَحَقَّقِ مِنها ولا إشعارٍ في التَطبيقِ ولا
/// سَطرِ تَدقيق؛ ولا بابَ يَعرِضُ وَسيلَةً مَربوطَةً أَو يَحذِفُها، والقَناتانِ لا تُرسِلانِ إلّا رَمزاً. ومَرجِعُ P1: ‏OWASP ASVS 2.2.3 (L1) وNIST 800-63B —
/// يُخطَرُ صاحِبُ الحِسابِ حينَ يُربَطُ بِه مُصادِق. <b>تَوصيفُ «اليَوم» (القاعِدَة ٣)</b>: صُفوفُ <c>Today_</c> تُقلَبُ بِأَسمائِها في كوميتِ التَبديل.</para>
/// </summary>
public class ContactNoticeTests(ITestOutputHelper output)
{
    private const string Tpl = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    private static string Read(string relative)
        => WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar))));

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-29، A3 — كوميتُ التَبديل)</b> — كانَ <c>Today_linking_a_contact_notifies_no_one_and_writes_no_audit_line</c>
    /// (‏<c>ed9ffac</c>): «عَلاماتُ إشعارٍ أَو تَدقيقٍ في الرَبط 0». والآن الرَبطُ يُنادي <c>NoticeLinkedAsync</c>: رِسالَةٌ إلى الوَسيلَةِ الأُخرى المُتَحَقَّقِ مِنها،
    /// وإشعارٌ في التَطبيق، وسَطرُ تَدقيق.</summary>
    [Fact]
    public void Linking_a_contact_notifies_the_other_verified_contact_and_writes_an_audit_line()
    {
        var contacts = Read($"{Tpl}/Services/AccountContacts.cs");
        var verify = contacts[contacts.IndexOf("VerifyAsync(", StringComparison.Ordinal)..];
        verify = verify[..verify.IndexOf("public static async Task<string> NoticeLinkedAsync(", StringComparison.Ordinal)];
        var notice = contacts[contacts.IndexOf("public static async Task<string> NoticeLinkedAsync(", StringComparison.Ordinal)..];
        notice = notice[..notice.IndexOf("public static async Task<StartOutcome> StartRemoveAsync(", StringComparison.Ordinal)];
        var signs = new[] { "SendNoticeAsync", "Notification", "AuditWriter" }.Count(t => notice.Contains(t, StringComparison.Ordinal));
        var calls = verify.Contains("NoticeLinkedAsync(", StringComparison.Ordinal);
        output.WriteLine($"بَعدَ التَبديل: الرَبطُ يُنادي الإشعار {calls} · عَلاماتُه {signs}");
        Assert.Contains("await NoticeLinkedAsync(store, slug, user, kind, p.Value, now, phoneChannel, emailChannel, ct);", verify, StringComparison.Ordinal);
        Assert.Equal(3, signs);
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-29، A3)</b> — كانَ <c>Today_no_door_removes_a_linked_contact</c>: «أَبوابُ حَذفِ وَسيلَة 0 · دالّاتُ الفَكّ 0».
    /// والآن بابانِ — البَدءُ والتَأكيد — كُلٌّ حارِسُه جَلسَةُ صاحِبِه ثُمَّ نِداءٌ واحِدٌ لِـ<c>AccountContacts</c>، والقَرارُ <c>ContactLink.ValidateUnlink</c>.</summary>
    [Fact]
    public void Two_doors_remove_the_added_contact_behind_a_code_to_the_remaining_one()
    {
        var doors = WriteEndpointGuardTests.AllMinimalApiEndpoints().Where(e => e.Route.Contains("/me/contact/remove", StringComparison.Ordinal)).ToList();
        var unlink = typeof(ContactLink).GetMethods(BindingFlags.Public | BindingFlags.Static).Count(m => m.Name.StartsWith("Unlink", StringComparison.Ordinal));
        output.WriteLine($"بَعدَ التَبديل: أَبوابُ حَذفِ وَسيلَة {doors.Count} ({string.Join(" · ", doors.Select(d => d.Route))}) · دالّاتُ الفَكّ {unlink}");
        Assert.Equal(new[] { "/{slug}/me/contact/remove/start", "/{slug}/me/contact/remove/verify" }, doors.Select(d => d.Route).Order(StringComparer.Ordinal));
        Assert.Equal(1, unlink);
        foreach (var d in doors)
        {
            var guard = d.Body.IndexOf("AuthHandlers.ParseToken(AuthSession.ResolveToken(req, slug))", StringComparison.Ordinal);
            var call = d.Body.IndexOf("Services.AccountContacts.", StringComparison.Ordinal);
            Assert.True(guard > 0 && call > guard, $"«{d.Route}»: الحارِسُ قَبلَ النِداء.");
        }
        var me = WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, Tpl, "Components", "Pages", "Me.razor")));
        Assert.Contains("action=\"@($\"/{Slug}/me/contact/remove/start\")\"", me, StringComparison.Ordinal);
        Assert.Contains("action=\"@($\"/{Slug}/me/contact/remove/verify\")\"", me, StringComparison.Ordinal);
        Assert.Contains("ContactLink.IsUnlinkAllowed(me, kind)", me, StringComparison.Ordinal);
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-29، A3)</b> — كانَ <c>Today_the_two_channels_can_only_send_a_code</c>. والآن لِكُلِّ قَناةٍ إشعار، ويُعلِنُه
    /// كُلُّ مُزَوِّدٍ حَيٍّ مِن الخَمسَة (القَناةُ الحَيَّةُ تُمَدّ — القاعِدَة ٨).</summary>
    [Fact]
    public void Each_channel_sends_a_notice_and_each_live_provider_implements_it()
    {
        var sms = typeof(IOtpChannel).GetMethods().Where(m => m.Name.StartsWith("Send", StringComparison.Ordinal)).Select(m => m.Name).Order().ToList();
        var email = typeof(IEmailOtpChannel).GetMethods().Where(m => m.Name.StartsWith("Send", StringComparison.Ordinal)).Select(m => m.Name).Order().ToList();
        // مَصدَرُ المُزَوِّدينَ الخَمسَة لا أَنواعُهم: مَشروعُ الاختِبارِ لا يُرَجِّعُ Twilio ولا MockSms، والتَطبيقُ يُرَجِّعُ الخَمسَة.
        var providers = new[] { "Twilio/TwilioSmsChannel.cs", "MockSms/MockSmsChannel.cs", "Smtp/SmtpEmailChannel.cs", "Brevo/BrevoEmailChannel.cs", "MockEmail/MockEmailChannel.cs" };
        var declaring = providers.Count(f => System.Text.RegularExpressions.Regex.IsMatch(
            Read($"libs/kits/Auth/ACommerce.Kit.Auth.Providers.{f}"), @"public (async )?Task SendNoticeAsync\("));
        output.WriteLine($"بَعدَ التَبديل: IOtpChannel {string.Join(",", sms)} · IEmailOtpChannel {string.Join(",", email)} · مُزَوِّدونَ يُعلِنونَ الإشعار {declaring} مِن {providers.Length}");
        Assert.Equal(new[] { "SendNoticeAsync", "SendOtpAsync" }, sms);
        Assert.Equal(new[] { "SendNoticeAsync", "SendOtpAsync" }, email);
        Assert.Equal(providers.Length, declaring);
    }

    // ─── قَرارُ الفَكِّ النَقِيّ — لِكُلِّ رَمزٍ مُوجَبٌ وسالِب (القاعِدَة ٤) ───────────────────────────────────

    private static User Account(string phone = "+966500000310", string email = "", string added = "", DateTime? deleted = null)
        => new() { Id = Guid.NewGuid(), TenantSlug = "zz-cn", Phone = phone, Email = email, AddedContactKind = added, DeletedAt = deleted, PhoneVerified = true };

    [Theory]
    [InlineData("no_account")]
    [InlineData("deleted")]
    [InlineData("kind")]
    [InlineData("not_set")]
    [InlineData("not_added")]
    [InlineData("last")]
    [InlineData("last_nid")]
    public void Each_unlink_refusal_has_its_case(string @case)
    {
        var (user, kind, code) = @case switch
        {
            "no_account" => ((User?)null, ContactLink.Email, ContactLink.NoAccount),
            "deleted" => (Account(email: "a@b.co", added: "email", deleted: DateTime.UtcNow), ContactLink.Email, ContactLink.NoAccount),
            "kind" => (Account(email: "a@b.co", added: "email"), "fax", ContactLink.KindUnknown),
            "not_set" => (Account(added: "email"), ContactLink.Email, ContactLink.NotSet),
            "not_added" => (Account(email: "a@b.co", added: "email"), ContactLink.Phone, ContactLink.NotAdded),
            "last" => (Account(phone: "", email: "a@b.co", added: "email"), ContactLink.Email, ContactLink.Last),
            _ => (Account(phone: "NID-1122334455", email: "a@b.co", added: "email"), ContactLink.Email, ContactLink.Last),
        };
        Assert.Equal(code, ContactLink.ValidateUnlink(user, kind)?.Code);
        Assert.False(ContactLink.IsUnlinkAllowed(user, kind));
        Assert.Contains(code, ContactLink.UnlinkAll);
    }

    /// <summary>السالِبُ لِكُلِّ رَمز: المُضافُ في حِسابٍ تَبقى فيه وَسيلَةٌ أُخرى يُحذَف، والفَكُّ يُفرِغُ حَقلَه ومُضافَه ويُبقي الأُخرى.</summary>
    [Theory]
    [InlineData("email")]
    [InlineData("phone")]
    public void The_added_contact_unlinks_and_the_original_stays(string added)
    {
        var user = Account(email: "a@b.co", added: added);
        user.EmailVerified = true;
        Assert.Null(ContactLink.ValidateUnlink(user, added));
        var at = new DateTime(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);
        ContactLink.Unlink(user, added, at);
        Assert.Equal("", added == ContactLink.Email ? user.Email : user.Phone);
        Assert.False(added == ContactLink.Email ? user.EmailVerified : user.PhoneVerified);
        Assert.NotEqual("", added == ContactLink.Email ? user.Phone : user.Email);
        Assert.Equal(("", at), (user.AddedContactKind, user.UpdatedAt));
        Assert.Equal(ContactLink.NotSet, ContactLink.ValidateUnlink(user, added)!.Code);
    }

    [Fact]
    public void Linking_names_the_added_kind_and_masks_show_no_whole_contact()
    {
        var user = Account();
        ContactLink.Link(user, ContactLink.Email, "sara@example.com", DateTime.UtcNow);
        Assert.Equal(ContactLink.Email, user.AddedContactKind);
        Assert.Equal("s•••@example.com", ContactLink.Mask(ContactLink.Email, "sara@example.com"));
        Assert.Equal("•••4567", ContactLink.Mask(ContactLink.Phone, "+966551234567"));
        Assert.Equal("•••", ContactLink.Mask(ContactLink.Email, "broken"));
        foreach (var key in new[] { AccountContacts.AddedTitleKey, AccountContacts.AddedBodyKey, AccountContacts.RemovedTitleKey, AccountContacts.RemovedBodyKey })
            Assert.False(string.IsNullOrWhiteSpace(ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, key)), key);
    }
}

// ═══ البُرهانُ الحَيّ — بَريدٌ مَزروعٌ بَعدَ «اخرج من كل الأجهزة» (‏WASAYEL_LIVE_PROOF=1، ‏2026-10-29، A3) ═══
//
// حِسابٌ بِالجَوّالِ في مَتجَرِ جَوّال؛ «مَن يَحمِلُ جَلسَتَه» يَربِطُ بَريدَه هُوَ بِرَمزٍ يَصِلُه؛ صاحِبُ الحِسابِ يَضغَطُ «اخرج من كل الأجهزة»؛ ثُمَّ يَدخُلُ
// المُهاجِمُ بِالبَريدِ المَزروع. مُستَأجِرٌ مُؤَقَّتٌ يُمسَحُ بَعدَه.

[Collection(LiveProofCollection.Name)]
public class LiveContactNoticeProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    internal sealed class Capture : IEmailOtpChannel
    {
        public string? Code;
        public string ChannelName => "capture";
        public string? DevHintCode => null;
        public Task SendOtpAsync(string email, string code, CancellationToken ct) { Code = code; return Task.CompletedTask; }
        public readonly List<(string To, string Subject, string Text)> Notices = new();
        public Task SendNoticeAsync(string email, string subject, string text, CancellationToken ct) { Notices.Add((email, subject, text)); return Task.CompletedTask; }
    }

    internal sealed class CaptureSms : IOtpChannel
    {
        public string? Code;
        public readonly List<(string To, string Text)> Notices = new();
        public string ChannelName => "capture-sms";
        public string? DevHintCode => null;
        public Task SendOtpAsync(string phone, string code, CancellationToken ct) { Code = code; return Task.CompletedTask; }
        public Task SendNoticeAsync(string phone, string text, CancellationToken ct) { Notices.Add((phone, text)); return Task.CompletedTask; }
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-29، A3 — كوميتُ التَبديل)</b> — كانَ
    /// <c>Today_a_planted_email_still_signs_in_to_the_account_after_sign_out_everywhere_on_Postgres</c> (‏<c>ed9ffac</c>): «يَدخُلُ البَريدُ المَزروعُ حِسابَ
    /// الضَحِيَّة: True». والآن الرَبطُ يُخطِرُ جَوّالَ الضَحِيَّةِ المُتَحَقَّقَ مِنه، ويُسَجَّلُ إشعارٌ في التَطبيقِ وسَطرُ تَدقيق؛ والضَحِيَّةُ تَحذِفُ المَزروعَ بِرَمزٍ إلى
    /// جَوّالِها — فَالبَريدُ لا يُدخِلُ حِسابَها بَعدَه. والزارِعُ لا يَحذِفُ جَوّالَ الضَحِيَّةِ بِرَمزٍ إلى بَريدِه (<c>contact_not_added</c>).</summary>
    [Fact]
    public async Task A_planted_email_is_noticed_and_once_removed_no_longer_signs_in_to_the_account_on_Postgres()
    {
        if (!Enabled) return;
        await using var app = LiveAccountConcurrencyProofTests.HostApp();
        var store = app.Services.GetRequiredService<Marten.IDocumentStore>();
        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var slug = "cn-zz-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            const string code = "424242";
            AuthHandlers.IssueAttempt(slug, "966500000301", code, AuthHandlers.AuthKind.PhoneOtp);
            var victim = (await AuthHandlers.VerifyPhoneOtpHandler(new VerifyPhoneOtp("966500000301", code), new VendorRegisterBucketTests.ResolvedTenant(slug), store))!;
            var mail = new Capture();
            var sms = new CaptureSms();
            var offered = LoginChannels.Offered(AuthChannels.Phone, phoneRegistered: true, emailRegistered: true);
            var start = await AccountContacts.StartAsync(store, slug, victim.UserId, ContactLink.Email, "planted.a3@example.com", sms, mail, offered, default);
            var linked = await AccountContacts.VerifyAsync(store, slug, victim.UserId, start.AttemptId, mail.Code, DateTime.UtcNow, sms, mail, default);
            await ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.RevokeTenantAsync(store, cache, slug, victim.UserId);

            int notices, audits;
            await using (var q = store.QuerySession(slug))
            {
                notices = await Marten.QueryableExtensions.CountAsync(q.Query<ACommerce.Kit.Notifications.Notification>(),
                    n => n.UserId == victim.UserId && n.Type == AccountContacts.NoticeType);
                audits = await Marten.QueryableExtensions.CountAsync(q.Query<ACommerce.Templates.Customer.Marketplace.Services.Audit.AuditEntry>(),
                    a => a.EntityId == victim.UserId.ToString() && a.Action == AccountContacts.AddedAction);
            }

            var reverse = await AccountContacts.StartRemoveAsync(store, slug, victim.UserId, ContactLink.Phone, sms, mail, default);
            var removeStart = await AccountContacts.StartRemoveAsync(store, slug, victim.UserId, ContactLink.Email, sms, mail, default);
            var removed = await AccountContacts.VerifyRemoveAsync(store, slug, victim.UserId, removeStart.AttemptId, sms.Code, DateTime.UtcNow, default);

            AuthHandlers.IssueAttempt(slug, "planted.a3@example.com", code, AuthHandlers.AuthKind.EmailOtp);
            var attacker = await AuthHandlers.VerifyEmailOtpHandler(new VerifyEmailOtp("planted.a3@example.com", code), new VendorRegisterBucketTests.ResolvedTenant(slug), store);
            output.WriteLine($"بَعدَ التَبديل: الزَرع {(linked is null ? "رُبِط" : linked)} · رِسالَةٌ إلى الجَوّال {sms.Notices.Count} · إشعارٌ في التَطبيق {notices} · تَدقيق {audits}"
                + $" · حَذفُ الجَوّالِ الأَصلِيّ {reverse.Refusal} · حَذفُ المَزروع {(removed is null ? "حُذِف" : removed)} · البَريدُ يَدخُلُ حِسابَ الضَحِيَّة: {attacker?.UserId == victim.UserId}");
            Assert.Null(linked);
            Assert.Single(sms.Notices);
            Assert.Contains("p•••@example.com", sms.Notices[0].Text, StringComparison.Ordinal);
            Assert.Equal((1, 1), (notices, audits));
            Assert.Equal(ContactLink.NotAdded, reverse.Refusal);
            Assert.Null(removed);
            Assert.NotEqual(victim.UserId, attacker!.UserId);
        }
        finally { await LiveZeroRoleAdminProofTests.CleanAsync(store, slug); }
    }
}
