using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>«أَضِف بَريدَك/جَوّالَك» يَعِدُ بِالدُخولِ بِأَيِّهِما — وصَفحَةُ الدُخولِ تَرسُمُ قَناةً واحِدَة</b> (‏2026-10-29، تَدقيقُ الموجَة ١١ A2).
/// <c>Login.razor</c> يَختارُ استِمارَةً واحِدَةً مِن <c>Tenant.AuthChannel</c> (نَفاذ، أَو بَريد، أَو الجَوّالُ لِما سِواهُما)، و«حِسابي» يَعرِضُ
/// «أَضِف بَريدَك» في كُلِّ مَتجَرٍ قَناةُ بَريدِه مُسَجَّلَةٌ — ومِنها مَتاجِرُ الجَوّالِ ونَفاذ — والتَلميحُ يَقولُ «فَتَدخُلُ بَعدَها بِجَوّالِكَ أَو
/// بِبَريدِك». فَالبَريدُ المَربوطُ لا يُستَعمَلُ بِنَقرَة (القاعِدَة ١٢)، وأَبوابُ الدُخولِ بِالبَريدِ والجَوّالِ لا تَسأَلُ قَناةَ المَتجَرِ أَصلاً — بابٌ
/// خَفِيٌّ يَدخُلُ مِنه بَريدٌ مَزروعٌ مَتجَرَ جَوّال (A3). <b>تَوصيفُ «اليَوم» (القاعِدَة ٣)</b>: صُفوفُ <c>Today_</c> تُقلَبُ بِأَسمائِها في كوميتِ التَبديل.</para>
/// </summary>
public class LoginChannelsTests(ITestOutputHelper output)
{
    private const string Pages = "libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages";

    private static string Read(string relative)
        => File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string Door(string route)
    {
        var doors = WriteEndpointGuardTests.AllMinimalApiEndpoints().Where(e => e.Route == route).ToList();
        Assert.True(doors.Count == 1, $"«{route}»: {doors.Count} باباً — الأَداةُ عَمياء.");
        return doors[0].Body;
    }

    internal static readonly string[] SignInDoors =
        ["/{slug}/auth/phone/login", "/{slug}/auth/phone/verify", "/{slug}/auth/email/login", "/{slug}/auth/email/verify"];

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-29، A2 — كوميتُ التَبديل)</b> — كانَ <c>Today_the_login_page_draws_one_form_chosen_by_the_store_channel_alone</c>
    /// (‏<c>beacb18</c>): «استِماراتُ الدُخولِ 1 · 1 · 1 · مَفتاحُ قَناةٍ ثانِيَة False». والآن الاستِمارَةُ المَرسومَةُ <c>Active</c> (مِن <c>LoginChannels</c>) والمِفتاحُ
    /// إلى الأُخرى في الخُطوَةِ الأُولى.</summary>
    [Fact]
    public void The_login_page_draws_the_offered_channel_and_a_switch_to_the_other()
    {
        var page = Read($"{Pages}/Login.razor");
        var forms = new[] { "<PhoneLoginForm", "<EmailLoginForm", "<NafathLoginForm" }.Select(f => Regex.Matches(page, Regex.Escape(f)).Count).ToList();
        output.WriteLine($"بَعدَ التَبديل: استِماراتُ الدُخولِ {string.Join(" · ", forms)} · مَفتاحُ قَناةٍ ثانِيَة {page.Contains("\"via\"", StringComparison.Ordinal)}");
        Assert.Equal(new[] { 1, 1, 1 }, forms);
        Assert.Contains("else if (Active == ACommerce.Kit.Auth.AuthChannels.Email)", page, StringComparison.Ordinal);
        Assert.Contains("[SupplyParameterFromQuery(Name = \"via\")]", page, StringComparison.Ordinal);
        Assert.Contains("Services.LoginChannels.OfferedIn(Tenant.AuthChannel, Sp)", page, StringComparison.Ordinal);
        Assert.Contains("L.Markup(\"auth.login.switch_to_email\")", page, StringComparison.Ordinal);
        Assert.Contains("L.Markup(\"auth.login.switch_to_phone\")", page, StringComparison.Ordinal);
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-29، A2)</b> — كانَ <c>Today_the_account_page_offers_an_email_in_every_store_whose_email_channel_is_registered</c>.
    /// والآن «أَضِف» لِنَوعٍ تَعرِضُه صَفحَةُ دُخولِ المَتجَر وَحدَه.</summary>
    [Fact]
    public void The_account_page_offers_a_contact_only_for_a_kind_the_store_signs_in_with()
    {
        var page = Read($"{Pages}/Me.razor");
        Assert.Contains("LoginChannels.OfferedIn(Tenant.AuthChannel, Sp)", page, StringComparison.Ordinal);
        Assert.Contains("signIn.Contains(ACommerce.Kit.Auth.ContactLink.Email)", page, StringComparison.Ordinal);
        Assert.Contains("signIn.Contains(ACommerce.Kit.Auth.ContactLink.Phone)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("offerEmail = string.IsNullOrEmpty(me.Email) && Sp.GetService(", page, StringComparison.Ordinal);
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-29، A2)</b> — كانَ <c>Today_the_email_and_phone_sign_in_doors_never_read_the_store_channel</c>: «تَسأَلُ قَناةَ
    /// المَتجَر 0». والآن الأَبوابُ الأَربَعَةُ تَسأَلُ <c>LoginChannels.Offers</c> قَبلَ أَن تَقرَأَ حَقلاً.</summary>
    [Fact]
    public void Every_email_and_phone_sign_in_door_asks_the_store_channel_before_it_reads_a_field()
    {
        var asking = 0;
        foreach (var route in SignInDoors)
        {
            var body = Door(route);
            var ask = body.IndexOf("Services.LoginChannels.Offers(tenant.AuthChannel", StringComparison.Ordinal);
            var field = body.IndexOf("req.Form[", StringComparison.Ordinal);
            var json = body.IndexOf("ReadFromJsonAsync", StringComparison.Ordinal);
            Assert.True(ask >= 0, $"«{route}» لا يَسأَلُ قَناةَ المَتجَر.");
            Assert.True(field < 0 || ask < field, $"«{route}» يَقرَأُ حَقلاً قَبلَ أَن يَسأَل.");
            Assert.True(json < 0 || ask < json, $"«{route}» يَقرَأُ الجِسمَ قَبلَ أَن يَسأَل.");
            Assert.Contains("login?err={Services.LoginChannels.NotOffered}", body, StringComparison.Ordinal);
            asking++;
        }
        output.WriteLine($"بَعدَ التَبديل: أَبوابُ الدُخولِ بِالجَوّالِ والبَريد {SignInDoors.Length} · تَسأَلُ قَناةَ المَتجَرِ أَوَّلاً {asking}");
        Assert.Equal(4, asking);
    }

    // ─── القَرارُ النَقِيّ — مَتجَرٌ × نَوع، مُوجَبٌ وسالِب ──────────────────────────────────────────────────

    [Theory]
    [InlineData("phone", "phone", true, true)]
    [InlineData("phone", "phone", false, true)]      // الأُولى مَعروضَةٌ دائِماً — وغِيابُ قَناتِها رَمزُه phone_unavailable
    [InlineData("phone", "email", true, true)]
    [InlineData("phone", "email", false, false)]
    [InlineData("phone", "nafath", true, false)]
    [InlineData("email", "email", false, true)]
    [InlineData("email", "phone", true, true)]
    [InlineData("email", "phone", false, false)]
    [InlineData("nafath", "nafath", true, true)]
    [InlineData("nafath", "phone", true, false)]
    [InlineData("nafath", "email", true, false)]
    [InlineData("sms", "phone", false, true)]        // قيمَةٌ خارِجَ المَعجَمِ تُرسَمُ جَوّالاً كَما كانَت
    [InlineData(null, "email", true, true)]
    public void A_store_offers_its_own_channel_and_the_other_one_only_when_registered(string? store, string kind, bool registered, bool expected)
        => Assert.Equal(expected, ACommerce.Templates.Customer.Marketplace.Services.LoginChannels.Offers(store, kind, registered));

    [Fact]
    public void The_page_draws_an_offered_via_and_falls_back_to_the_store_channel()
    {
        var phoneStore = ACommerce.Templates.Customer.Marketplace.Services.LoginChannels.Offered("phone", phoneRegistered: true, emailRegistered: true);
        var nafathStore = ACommerce.Templates.Customer.Marketplace.Services.LoginChannels.Offered("nafath", phoneRegistered: true, emailRegistered: true);
        Assert.Equal(new[] { "phone", "email" }, phoneStore);
        Assert.Equal(new[] { "nafath" }, nafathStore);
        Assert.Equal("email", ACommerce.Templates.Customer.Marketplace.Services.LoginChannels.Active("phone", "email", phoneStore));
        Assert.Equal("phone", ACommerce.Templates.Customer.Marketplace.Services.LoginChannels.Active("phone", "nafath", phoneStore));
        Assert.Equal("phone", ACommerce.Templates.Customer.Marketplace.Services.LoginChannels.Active("phone", "<x>", phoneStore));
        Assert.Equal("nafath", ACommerce.Templates.Customer.Marketplace.Services.LoginChannels.Active("nafath", "email", nafathStore));
        Assert.Equal("", ACommerce.Templates.Customer.Marketplace.Services.LoginChannels.ViaSuffix("phone", "phone"));
        Assert.Equal("&via=email", ACommerce.Templates.Customer.Marketplace.Services.LoginChannels.ViaSuffix("phone", "email"));
        Assert.Equal("&via=phone", ACommerce.Templates.Customer.Marketplace.Services.LoginChannels.ViaSuffix("email", "phone"));
    }

    // ─── A2 الجَولَةُ الأُولى (مُراجَعَةُ الموجَة ١١ لِلمَسار F1، ‏2026-10-30): القَناةُ الثانِيَةُ تُسَجِّلُ حِساباً جَديداً ─────────────────────
    //
    // بَعدَ `7869039` يَفتَحُ مَتجَرُ الجَوّالِ بابَي البَريد (ومَتجَرُ البَريدِ بابَي الجَوّال)، وبابُ التَأكيدِ يُنادي المُعالِجَ الَّذي تُناديه قَناةُ المَتجَرِ
    // نَفسُها — `GetOrCreateUserAsync` — فَبَريدٌ لا يَحمِلُه حِسابٌ يُنشِئُ حِساباً ثانِياً بِلا جَوّال: «مَن دَخَلَ مَرَّةً بِجَوّالِه ومَرَّةً بِبَريدِه صارَ
    // حِسابَين» (ADR-042 §١)، وهِيَ المُشكِلَةُ الَّتي قَرَّرَ د27 «ج» عِلاجَها بِالرَبط. **تَوصيفُ «اليَوم»** — يُقلَبُ بِاسمِه في كوميتِ التَبديل.

    /// <summary><b>تَوصيفُ «اليَوم» (القاعِدَة ٣)</b>: مَتجَرُ الجَوّالِ يَعرِضُ البَريدَ ومَتجَرُ البَريدِ الجَوّال، وبابا التَأكيدِ يُناديانِ المُعالِجَ المُسَجِّلَ
    /// ولا يَسأَلانِ أَيُّ القَناتَينِ تُسَجِّل.</summary>
    [Fact]
    public void Today_the_verify_doors_of_the_second_channel_call_the_registering_handler()
    {
        var email = Door("/{slug}/auth/email/verify");
        var phone = Door("/{slug}/auth/phone/verify");
        var registering = new[]
        {
            email.Contains("AuthHandlers.VerifyEmailOtpHandler(", StringComparison.Ordinal),
            phone.Contains("AuthHandlers.VerifyPhoneOtpHandler(", StringComparison.Ordinal),
        };
        var asking = new[] { email, phone }.Count(b => b.Contains("LoginChannels.Registers(", StringComparison.Ordinal));
        output.WriteLine($"اليَوم: مَتجَرُ الجَوّالِ يَعرِضُ البَريد {ACommerce.Templates.Customer.Marketplace.Services.LoginChannels.Offers("phone", "email", true)} · " +
                         $"مَتجَرُ البَريدِ يَعرِضُ الجَوّال {ACommerce.Templates.Customer.Marketplace.Services.LoginChannels.Offers("email", "phone", true)} · " +
                         $"بابا التَأكيدِ يُناديانِ المُعالِجَ المُسَجِّل {registering.Count(r => r)}/2 · يَسأَلانِ أَيُّ القَناتَينِ تُسَجِّل {asking}");
        Assert.True(ACommerce.Templates.Customer.Marketplace.Services.LoginChannels.Offers("phone", "email", true));
        Assert.True(ACommerce.Templates.Customer.Marketplace.Services.LoginChannels.Offers("email", "phone", true));
        Assert.Equal(new[] { true, true }, registering);
        Assert.Equal(0, asking);
    }

    private sealed class CapturingEmail : ACommerce.Kit.Auth.IEmailOtpChannel
    {
        public string? Code;
        public string ChannelName => "capture";
        public string? DevHintCode => null;
        public Task SendOtpAsync(string email, string code, CancellationToken ct) { Code = code; return Task.CompletedTask; }
        public Task SendNoticeAsync(string email, string subject, string text, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>بابُ الرَبطِ يَرُدُّ نَوعاً لا تَعرِضُه صَفحَةُ الدُخولِ قَبلَ أَن يَقرَأ (مُوجَب: مَتجَرُ نَفاذٍ والبَريد — المَخزَنُ البَديلُ لا يُسأَل)، ويَمضي
    /// لِنَوعٍ تَعرِضُه (سالِب: مَتجَرُ جَوّالٍ والبَريد ← رَمزٌ يُرسَل).</summary>
    [Fact]
    public async Task The_link_door_refuses_a_kind_the_store_does_not_sign_in_with()
    {
        var user = new ACommerce.Kit.Auth.User { Id = Guid.NewGuid(), TenantSlug = "zz-lc", Phone = "+966500000299" };
        var mail = new CapturingEmail();
        var nafath = ACommerce.Templates.Customer.Marketplace.Services.LoginChannels.Offered("nafath", true, true);
        var phone = ACommerce.Templates.Customer.Marketplace.Services.LoginChannels.Offered("phone", true, true);

        var refused = await ACommerce.Templates.Customer.Marketplace.Services.AccountContacts.StartAsync(
            AccountStub.Store(null), "zz-lc", user.Id, ACommerce.Kit.Auth.ContactLink.Email, "a@b.co", null, mail, nafath, default);
        var started = await ACommerce.Templates.Customer.Marketplace.Services.AccountContacts.StartAsync(
            AccountStub.Store(user), "zz-lc", user.Id, ACommerce.Kit.Auth.ContactLink.Email, "a.lc@b.co", null, mail, phone, default);
        output.WriteLine($"مَتجَرُ نَفاذ: {refused.Refusal} · مَتجَرُ جَوّال: {(started.AttemptId is null ? started.Refusal : "أُرسِلَ الرَمز")}");
        Assert.Equal(ACommerce.Templates.Customer.Marketplace.Services.AccountContacts.NotOffered, refused.Refusal);
        Assert.Null(refused.AttemptId);
        Assert.NotNull(started.AttemptId);
        Assert.Contains(ACommerce.Templates.Customer.Marketplace.Services.AccountContacts.NotOffered,
            ACommerce.Templates.Customer.Marketplace.Services.AccountContacts.Codes);
    }
}

/// <summary>
/// <para><b>القَناةُ الثانِيَةُ عَلى Postgres</b> (‏<c>WASAYEL_LIVE_PROOF=1</c>، ‏2026-10-30، مُراجَعَةُ الموجَة ١١ لِلمَسار F1 — A2): عُضوٌ بِالجَوّالِ في مَتجَرِ
/// جَوّال، ورَمزٌ صَحيحٌ إلى بَريدٍ لا يَحمِلُه حِساب — بِالمُعالِجِ الَّذي يُناديه بابُ <c>/{slug}/auth/email/verify</c> (الصَفُّ النَقِيُّ أَعلاه يُثبِتُ النِداء).
/// ونَظيرُه في مَتجَرِ البَريد. مُستَأجِرٌ مُؤَقَّتٌ يُمسَحُ بَعدَه، ولا يُطبَعُ بَريدٌ ولا جَوّال.</para>
/// </summary>
[Collection(LiveProofCollection.Name)]
public class LiveSecondChannelProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private const string Code = "424242";

    internal static async Task<ACommerce.Kit.Auth.User> SeedAsync(Marten.IDocumentStore store, string slug, string phone, string email, bool emailVerified = true)
    {
        var user = new ACommerce.Kit.Auth.User
        {
            Id = Guid.NewGuid(), TenantSlug = slug, FullName = "عُضوٌ قائِم",
            Phone = phone, PhoneVerified = phone != "", Email = email, EmailVerified = email != "" && emailVerified,
        };
        await using var s = store.LightweightSession(slug);
        s.Store(user);
        await s.SaveChangesAsync();
        return user;
    }

    internal static async Task<List<ACommerce.Kit.Auth.User>> AccountsAsync(Marten.IDocumentStore store, string slug)
    {
        await using var q = store.QuerySession(slug);
        return (await Marten.QueryableExtensions.ToListAsync(q.Query<ACommerce.Kit.Auth.User>())).ToList();
    }

    /// <summary><b>تَوصيفُ «اليَوم» (القاعِدَة ٣)</b>: في مَتجَرِ الجَوّال، بَريدٌ لا يَحمِلُه حِسابٌ يَدخُلُ حِساباً جَديداً بِلا جَوّالٍ بِجِوارِ العُضو.</summary>
    [Fact]
    public async Task Today_an_email_no_account_holds_creates_a_second_account_in_a_phone_store_on_Postgres()
    {
        if (!Enabled) return;
        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "sc-zz-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var member = await SeedAsync(store, slug, "+966500000293", "");
            ACommerce.Kit.Auth.Server.AuthHandlers.IssueAttempt(slug, "unlinked.sc@example.com", Code, ACommerce.Kit.Auth.Server.AuthHandlers.AuthKind.EmailOtp);
            var r = await ACommerce.Kit.Auth.Server.AuthHandlers.VerifyEmailOtpHandler(new ACommerce.Kit.Auth.VerifyEmailOtp("unlinked.sc@example.com", Code),
                new VendorRegisterBucketTests.ResolvedTenant(slug), store);
            var accounts = await AccountsAsync(store, slug);
            var created = accounts.SingleOrDefault(a => a.Id != member.Id);
            var bare = created is not null && created.Phone == "";
            var opened = r is null ? "رُدّ" : r.UserId == member.Id ? "حِسابُ العُضو" : "حِسابٌ جَديد";
            output.WriteLine($"اليَوم (مَتجَرُ جَوّال، بَريدٌ غَيرُ مَربوط): الدُخول {opened} · الحِسابات {accounts.Count} · الجَديدُ بِلا جَوّال {bare}");
            Assert.NotNull(r);
            Assert.NotEqual(member.Id, r!.UserId);
            Assert.Equal(2, accounts.Count);
            Assert.Equal("", created!.Phone);
        }
        finally { await LiveZeroRoleAdminProofTests.CleanAsync(store, slug); }
    }

    /// <summary><b>تَوصيفُ «اليَوم»</b>: ونَظيرُه في مَتجَرِ البَريد — جَوّالٌ لا يَحمِلُه حِسابٌ يَدخُلُ حِساباً جَديداً بِلا بَريد.</summary>
    [Fact]
    public async Task Today_a_phone_no_account_holds_creates_a_second_account_in_an_email_store_on_Postgres()
    {
        if (!Enabled) return;
        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "sc-zz-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var member = await SeedAsync(store, slug, "", "member.sc@example.com");
            ACommerce.Kit.Auth.Server.AuthHandlers.IssueAttempt(slug, "966500000294", Code, ACommerce.Kit.Auth.Server.AuthHandlers.AuthKind.PhoneOtp);
            var r = await ACommerce.Kit.Auth.Server.AuthHandlers.VerifyPhoneOtpHandler(new ACommerce.Kit.Auth.VerifyPhoneOtp("966500000294", Code),
                new VendorRegisterBucketTests.ResolvedTenant(slug), store);
            var accounts = await AccountsAsync(store, slug);
            var created = accounts.SingleOrDefault(a => a.Id != member.Id);
            var bare = created is not null && created.Email == "";
            var opened = r is null ? "رُدّ" : r.UserId == member.Id ? "حِسابُ العُضو" : "حِسابٌ جَديد";
            output.WriteLine($"اليَوم (مَتجَرُ بَريد، جَوّالٌ غَيرُ مَربوط): الدُخول {opened} · الحِسابات {accounts.Count} · الجَديدُ بِلا بَريد {bare}");
            Assert.NotNull(r);
            Assert.NotEqual(member.Id, r!.UserId);
            Assert.Equal(2, accounts.Count);
            Assert.Equal("", created!.Email);
        }
        finally { await LiveZeroRoleAdminProofTests.CleanAsync(store, slug); }
    }
}
