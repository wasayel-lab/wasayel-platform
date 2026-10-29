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

    [Fact]
    public void Today_linking_a_contact_notifies_no_one_and_writes_no_audit_line()
    {
        var contacts = Read($"{Tpl}/Services/AccountContacts.cs");
        var verify = contacts[contacts.IndexOf("VerifyAsync(", StringComparison.Ordinal)..];
        verify = verify[..verify.IndexOf("public static string AfterVerify(", StringComparison.Ordinal)];
        var signs = new[] { "SendNoticeAsync", "Notification", "AuditWriter" }.Count(t => verify.Contains(t, StringComparison.Ordinal));
        output.WriteLine($"اليَوم: عَلاماتُ إشعارٍ أَو تَدقيقٍ في الرَبط {signs}");
        Assert.Equal(0, signs);
    }

    [Fact]
    public void Today_no_door_removes_a_linked_contact()
    {
        var removeDoors = WriteEndpointGuardTests.AllMinimalApiEndpoints().Count(e => e.Route.Contains("/me/contact/remove", StringComparison.Ordinal));
        var unlink = typeof(ContactLink).GetMethods(BindingFlags.Public | BindingFlags.Static).Count(m => m.Name.StartsWith("Unlink", StringComparison.Ordinal));
        output.WriteLine($"اليَوم: أَبوابُ حَذفِ وَسيلَة {removeDoors} · دالّاتُ الفَكّ {unlink}");
        Assert.Equal((0, 0), (removeDoors, unlink));
    }

    [Fact]
    public void Today_the_two_channels_can_only_send_a_code()
    {
        var sms = typeof(IOtpChannel).GetMethods().Where(m => m.Name.StartsWith("Send", StringComparison.Ordinal)).Select(m => m.Name).ToList();
        var email = typeof(IEmailOtpChannel).GetMethods().Where(m => m.Name.StartsWith("Send", StringComparison.Ordinal)).Select(m => m.Name).ToList();
        output.WriteLine($"اليَوم: IOtpChannel {string.Join(",", sms)} · IEmailOtpChannel {string.Join(",", email)}");
        Assert.Equal(new[] { "SendOtpAsync" }, sms);
        Assert.Equal(new[] { "SendOtpAsync" }, email);
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
    }

    [Fact]
    public async Task Today_a_planted_email_still_signs_in_to_the_account_after_sign_out_everywhere_on_Postgres()
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
            var offered = LoginChannels.Offered(AuthChannels.Phone, phoneRegistered: true, emailRegistered: true);
            var start = await AccountContacts.StartAsync(store, slug, victim.UserId, ContactLink.Email, "planted.a3@example.com", null, mail, offered, default);
            var linked = await AccountContacts.VerifyAsync(store, slug, victim.UserId, start.AttemptId, mail.Code, DateTime.UtcNow, default);
            await ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.RevokeTenantAsync(store, cache, slug, victim.UserId);

            AuthHandlers.IssueAttempt(slug, "planted.a3@example.com", code, AuthHandlers.AuthKind.EmailOtp);
            var attacker = await AuthHandlers.VerifyEmailOtpHandler(new VerifyEmailOtp("planted.a3@example.com", code), new VendorRegisterBucketTests.ResolvedTenant(slug), store);
            output.WriteLine($"اليَوم: الزَرع {(linked is null ? "رُبِط" : linked)} · بَعدَ «اخرج من كل الأجهزة» يَدخُلُ البَريدُ المَزروعُ حِسابَ الضَحِيَّة: {attacker?.UserId == victim.UserId}");
            Assert.Null(linked);
            Assert.Equal(victim.UserId, attacker!.UserId);
        }
        finally { await LiveZeroRoleAdminProofTests.CleanAsync(store, slug); }
    }
}
