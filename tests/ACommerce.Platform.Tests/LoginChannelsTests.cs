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

    // ─── A-SESS-1 (الموجَة ١٢ F1 البَند ٥، ‏2026-10-30): بابا نَفاذٍ لا يَسأَلانِ قَناةَ المَتجَر ─────────────────────────────────────────
    //
    // ‏«أ» F1·٤ و‏ADR-042 §٥: «قَناةُ المَتجَرِ وَحدَها تُسَجِّل»، وأَبوابُ الجَوّالِ والبَريدِ تَرُدُّ غَيرَها بِـ`channel_not_offered` (`7869039`، `8f2a604`). وبابا
    // نَفاذٍ (`/{slug}/auth/nafath/login` و`/verify`) يَسأَلانِ `tenant.IsResolved` و`channel is null` وَحدَهُما ثُمَّ يُناديانِ `RequestNafathHandler` و
    // `VerifyNafathHandler` — والثاني يُنشِئُ حِساباً (`GetOrCreateUserAsync(phone: "NID-…")`) لِهُوِيَّةٍ لا يَحمِلُها حِساب، ومِثلُه مَسارُ `[WolverinePost]`
    // بِجِسمِ JSON. وفي التَطويرِ يُسَجِّلُ `Program.cs` ‏MockNafath لِكُلِّ مَتجَر، وفي الإنتاجِ يُسَجِّلُه `Auth__Nafath__Provider=nafath` لِأَيِّ مَتجَرِ نَفاذ —
    // فَمَتجَرُ جَوّالٍ أَو بَريدٍ يُنشِئُ لِلشَخصِ حِساباً ثانِياً بِقَناةٍ لا يَعرِضُها (ضَرَرُ د27) ويَتَجاوَزُ اختِيارَ صاحِبِه (P2). **تَوصيفُ «اليَوم»** — يُقلَبُ
    // بِاسمِه في كوميتِ التَبديل.

    internal static readonly string[] NafathDoors = ["/{slug}/auth/nafath/login", "/{slug}/auth/nafath/verify"];

    [Fact]
    public void Today_the_nafath_sign_in_doors_never_ask_the_store_channel()
    {
        var asking = NafathDoors.Count(r => Door(r).Contains("Services.LoginChannels.Offers(", StringComparison.Ordinal));
        var creates = Door("/{slug}/auth/nafath/verify").Contains("AuthHandlers.VerifyNafathHandler(", StringComparison.Ordinal);
        output.WriteLine($"اليَوم: بابا نَفاذٍ يَسأَلانِ قَناةَ المَتجَر {asking}/{NafathDoors.Length} · بابُ التَأكيدِ يُنادي المُعالِجَ الَّذي يُنشِئ {creates}");
        Assert.Equal(2, NafathDoors.Length);
        Assert.Equal(0, asking);
        Assert.True(creates);
    }

    /// <summary>نَفاذٌ يُوافِقُ فَوراً — بَديلُ MockNafath (تَسجيلُ <c>Program.cs</c> في التَطوير) بِلا انتِظارِ الثَواني الخَمس.</summary>
    internal sealed class ApprovingNafath : ACommerce.Kit.Auth.INafathChannel
    {
        public string ChannelName => "approving";
        public Task<ACommerce.Kit.Auth.NafathStartResult> StartAsync(string nationalId, CancellationToken ct)
            => Task.FromResult(new ACommerce.Kit.Auth.NafathStartResult(Guid.NewGuid().ToString("N"), "00", 0));
        public Task<bool> IsApprovedAsync(string attemptId, CancellationToken ct) => Task.FromResult(true);
    }

    /// <summary>مُستَأجِرٌ مَحلولٌ بِقَناةٍ تُعطى — <c>VendorRegisterBucketTests.ResolvedTenant</c> قَناتُه الجَوّالُ وَحدَه.</summary>
    internal sealed class ChannelTenant(string slug, string authChannel) : ACommerce.Platform.Shared.ITenantContext
    {
        public string Slug => slug;
        public string Name => slug;
        public string BrandColor => "#000000";
        public string AuthChannel => authChannel;
        public string TagLine => "";
        public string City => "";
        public bool IsResolved => true;
        public bool HasRoles => false;
    }

    /// <summary><b>تَوصيفُ «اليَوم» بِلا قاعِدَة</b>: <c>VerifyNafathHandler</c> بِمُحاوَلَةٍ مُوافَقٍ عَلَيها في مَتجَرِ جَوّالٍ يَمضي إلى المَخزَن (لِيَجِدَ
    /// الحِسابَ أَو يُنشِئَه) — والمَخزَنُ البَديلُ يَرمي عِندَ أَوَّلِ جَلسَة، فَالرَميُ هُوَ القِياس: لا سُؤالَ عَن قَناةِ المَتجَرِ قَبلَه.</summary>
    [Fact]
    public async Task Today_the_nafath_verify_handler_reaches_the_store_in_a_phone_store()
    {
        var slug = "zz-nf-" + Guid.NewGuid().ToString("N")[..6];
        var nid = "1012345678";
        var tenant = new ChannelTenant(slug, ACommerce.Kit.Auth.AuthChannels.Phone);
        var channel = new ApprovingNafath();
        var pending = await ACommerce.Kit.Auth.Server.AuthHandlers.RequestNafathHandler(new ACommerce.Kit.Auth.RequestNafath(nid), tenant, channel, default);
        var ex = await Record.ExceptionAsync(() => ACommerce.Kit.Auth.Server.AuthHandlers.VerifyNafathHandler(
            new ACommerce.Kit.Auth.VerifyNafath(pending.AttemptId, nid), tenant, channel, AccountStub.Store(null), default));
        output.WriteLine($"اليَوم: مَتجَرُ {tenant.AuthChannel} ⇒ المُعالِجُ {(ex is NotSupportedException ? "بَلَغَ المَخزَن" : ex?.GetType().Name ?? "رَدَّ بِلا مَخزَن")}");
        Assert.IsType<NotSupportedException>(ex);
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

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-30، كوميتُ التَبديل)</b> — كانَ <c>Today_the_verify_doors_of_the_second_channel_call_the_registering_handler</c>
    /// (‏<c>c3aa888</c>): «بابا التَأكيدِ يُناديانِ المُعالِجَ المُسَجِّل 2/2 · يَسأَلانِ أَيُّ القَناتَينِ تُسَجِّل 0». والآن يَسأَلانِ <c>LoginChannels.Registers</c>
    /// ويُناديانِ <c>Verify*OtpAsync</c> بِه، ويَرُدّانِ رَمزَ الرَدِّ قَبلَ أَن يَكتُبا كوكيّاً.</summary>
    [Fact]
    public void The_verify_doors_of_the_second_channel_ask_whether_it_registers_before_they_sign_in()
    {
        var asking = 0;
        foreach (var d in SecondChannelVerifyDoors)
        {
            var breach = VerifyDoorBreach(Door(d.Route), d);
            Assert.True(breach is null, $"«{d.Route}»: {breach}");
            asking++;
        }
        output.WriteLine($"بَعدَ التَبديل: بابا التَأكيدِ يَسأَلانِ أَيُّ القَناتَينِ تُسَجِّل {asking}/{SecondChannelVerifyDoors.Length} · يُناديانِ المُعالِجَ المُسَجِّلَ بِلا سُؤال 0");
        Assert.True(asking > 0, "صِفرُ بابٍ مَفحوص — الأَداةُ عَمياء.");
        Assert.Equal(2, asking);
    }

    // ─── (‏2026-10-30، الموجَة ١٢ F1 البَند ٣ — مُراجَعَةُ F1 الأَخيرَة R1-3) الفاحِصُ يَقبَلُ جِسماً مُعطى فَيُقاسُ أَنَّه يَحمَرّ ─────────────
    //
    // كانَ القَرارُ داخِلَ حَلقَةِ الصَفِّ أَعلاه عَلى البابَينِ الحَقيقِيَّينِ وَحدَهُما، فَلا صَفَّ يُثبِتُ أَنَّه يَرُدُّ جِسماً بِالشَكلِ القَديم (القاعِدَة ١٠ — وصَفّا
    // R1-1 وR1-2 لِكُلٍّ مِنهُما عَيِّنَةٌ مَحقونَة). فَصارَ دالَّةً تُسأَلُ عَلى جِسمٍ مُعطى، والصَفُّ أَعلاه يُناديها عَلى البابَين، والنَظَريَّةُ أَدناه تَحقِنُ
    // الأَشكالَ الَّتي يَجِبُ أَن تَرُدَّها: المُعالِجُ القَديم، والرَدُّ بَعدَ الكوكيّ، والنَوعانِ مُتَبادَلانِ في سُؤالِ «أَتُسَجِّل».

    /// <summary>بابُ تَأكيدٍ لِلقَناةِ الثانِيَة: مَسارُه، والنِداءُ الَّذي يُناديه، والمُعالِجُ القَديمُ الَّذي لا يُناديه، وسُؤالُ «أَتُسَجِّل» بِنَوعِه.</summary>
    internal sealed record VerifyDoor(string Route, string Call, string Old, string Ask);

    internal static readonly VerifyDoor[] SecondChannelVerifyDoors =
    [
        new("/{slug}/auth/email/verify", "AuthHandlers.VerifyEmailOtpAsync(", "AuthHandlers.VerifyEmailOtpHandler(",
            "Services.LoginChannels.Registers(tenant.AuthChannel, AuthChannels.Email)"),
        new("/{slug}/auth/phone/verify", "AuthHandlers.VerifyPhoneOtpAsync(", "AuthHandlers.VerifyPhoneOtpHandler(",
            "Services.LoginChannels.Registers(tenant.AuthChannel, AuthChannels.Phone)"),
    ];

    private const string RefusalLine = "if (refusal is not null) return Results.Redirect(Link(req, slug, $\"login?err={refusal}\"));";

    /// <summary><b>القَرارُ عَلى جِسمٍ مُعطى</b> — <c>null</c> إن كانَ البابُ يُنادي <see cref="VerifyDoor.Call"/> بِسُؤالِ «أَتُسَجِّل» بِنَوعِه هُوَ في وُسَطائِه، ولا
    /// يُنادي المُعالِجَ القَديم، ولا يَسأَلُ بِنَوعِ غَيرِه، ويَرُدُّ رَمزَ الرَدِّ بَعدَ النِداءِ وقَبلَ أَن يَكتُبَ كوكيّاً؛ وإلّا سَبَبُ الرَدّ.</summary>
    internal static string? VerifyDoorBreach(string body, VerifyDoor d)
    {
        var call = body.IndexOf(d.Call, StringComparison.Ordinal);
        if (call < 0) return $"لا يُنادي {d.Call}";
        if (body.Contains(d.Old, StringComparison.Ordinal)) return $"يُنادي المُعالِجَ القَديمَ {d.Old}";
        var close = body.IndexOf(';', call);
        var args = close < 0 ? body[call..] : body[call..close];
        if (!args.Contains(d.Ask, StringComparison.Ordinal)) return $"لا يُمَرِّرُ {d.Ask} إلى {d.Call}";
        var kind = d.Ask[(d.Ask.LastIndexOf('.') + 1)..^1];
        foreach (var other in new[] { "Phone", "Email", "Nafath" }.Where(k => k != kind))
            if (body.Contains($"Services.LoginChannels.Registers(tenant.AuthChannel, AuthChannels.{other})", StringComparison.Ordinal))
                return $"يَسأَلُ «أَتُسَجِّل» بِنَوعِ {other}";
        var refusal = body.IndexOf(RefusalLine, StringComparison.Ordinal);
        var cookie = body.IndexOf("AuthSession.WriteCookie(", StringComparison.Ordinal);
        if (!(refusal > call && cookie > refusal)) return $"الرَدُّ لا يَسبِقُ الكوكيّ ({call}/{refusal}/{cookie})";
        return null;
    }

    /// <summary>حَقنُ عَيب (القاعِدَة ١٠): بابُ تَأكيدِ البَريدِ بِأَشكالِه القَديمَة — المُعالِجُ القَديم، والرَدُّ بَعدَ الكوكيّ، والنَوعانِ مُتَبادَلان — يَرُدُّه الفاحِصُ كُلَّه،
    /// وجِسمُ البابِ الحَقيقيِّ يَمُرّ.</summary>
    [Theory]
    [InlineData("old-handler",
        "var result = await AuthHandlers.VerifyEmailOtpHandler(new VerifyEmailOtp(email, code), tenant, store);\nAuthSession.WriteCookie(res, slug, result, role: null);")]
    [InlineData("old-handler-beside-new",
        "var (result, refusal) = await AuthHandlers.VerifyEmailOtpAsync(new VerifyEmailOtp(email, code), tenant, store, Services.LoginChannels.Registers(tenant.AuthChannel, AuthChannels.Email));\n" +
        "if (refusal is not null) return Results.Redirect(Link(req, slug, $\"login?err={refusal}\"));\nresult ??= await AuthHandlers.VerifyEmailOtpHandler(new VerifyEmailOtp(email, code), tenant, store);\nAuthSession.WriteCookie(res, slug, result, role: null);")]
    [InlineData("refusal-after-cookie",
        "var (result, refusal) = await AuthHandlers.VerifyEmailOtpAsync(new VerifyEmailOtp(email, code), tenant, store, Services.LoginChannels.Registers(tenant.AuthChannel, AuthChannels.Email));\n" +
        "AuthSession.WriteCookie(res, slug, result, role: null);\nif (refusal is not null) return Results.Redirect(Link(req, slug, $\"login?err={refusal}\"));")]
    [InlineData("swapped-kinds",
        "var (result, refusal) = await AuthHandlers.VerifyEmailOtpAsync(new VerifyEmailOtp(email, code), tenant, store, Services.LoginChannels.Registers(tenant.AuthChannel, AuthChannels.Phone));\n" +
        "if (refusal is not null) return Results.Redirect(Link(req, slug, $\"login?err={refusal}\"));\nAuthSession.WriteCookie(res, slug, result, role: null);")]
    [InlineData("no-question",
        "var (result, refusal) = await AuthHandlers.VerifyEmailOtpAsync(new VerifyEmailOtp(email, code), tenant, store, true);\n" +
        "if (refusal is not null) return Results.Redirect(Link(req, slug, $\"login?err={refusal}\"));\nAuthSession.WriteCookie(res, slug, result, role: null);")]
    public void The_verify_door_check_refuses_a_door_body_in_an_old_shape(string shape, string body)
    {
        var breach = VerifyDoorBreach(body, SecondChannelVerifyDoors[0]);
        output.WriteLine($"{shape}: {breach ?? "يَمُرّ"}");
        Assert.NotNull(breach);
        Assert.Null(VerifyDoorBreach(Door(SecondChannelVerifyDoors[0].Route), SecondChannelVerifyDoors[0]));
    }

    /// <summary>القَرارُ النَقِيّ: قَناةُ المَتجَرِ وَحدَها تُسَجِّل (مُوجَب)، والثانِيَةُ لا (سالِب) — وقيمَةٌ خارِجَ المَعجَمِ قَناتُها الجَوّالُ كَما تُرسَم.</summary>
    [Theory]
    [InlineData("phone", "phone", true)]
    [InlineData("phone", "email", false)]
    [InlineData("email", "email", true)]
    [InlineData("email", "phone", false)]
    [InlineData("nafath", "nafath", true)]
    [InlineData("nafath", "email", false)]
    [InlineData("nafath", "phone", false)]
    [InlineData("sms", "phone", true)]
    [InlineData(null, "phone", true)]
    [InlineData(null, "email", false)]
    public void Only_the_store_channel_registers_a_new_account(string? store, string kind, bool expected)
        => Assert.Equal(expected, ACommerce.Templates.Customer.Marketplace.Services.LoginChannels.Registers(store, kind));

    /// <summary>رَمزُ الرَدِّ واحِدٌ مِن العُدَّة، وتُسَمّيه صَفحَةُ الدُخولِ بِه نَفسِه (لا حَرفِيَّةٍ مَنسوخَة) ونَصُّه في القامُوس، وسَطرُ «لِمَن رَبَطَها» تَحتَ
    /// الاستِمارَةِ الثانِيَةِ وَحدَها بِالقَرارِ نَفسِه.</summary>
    [Fact]
    public void The_not_linked_refusal_is_one_code_the_login_page_names_and_the_second_form_says_it_first()
    {
        var page = Read($"{Pages}/Login.razor");
        Assert.Equal("contact_not_linked", ACommerce.Kit.Auth.Server.AuthHandlers.NotLinked);
        Assert.Contains("ACommerce.Kit.Auth.Server.AuthHandlers.NotLinked => L[\"auth.login.err_contact_not_linked\"]", page, StringComparison.Ordinal);
        Assert.Contains("@if (!Services.LoginChannels.Registers(Tenant.AuthChannel, Active))", page, StringComparison.Ordinal);
        Assert.Contains("L.Markup(\"auth.login.second_channel_hint\")", page, StringComparison.Ordinal);
        foreach (var key in new[] { "auth.login.err_contact_not_linked", "auth.login.second_channel_hint" })
            Assert.False(string.IsNullOrWhiteSpace(ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, key)), key);
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

    internal static async Task<ACommerce.Kit.Auth.User> SeedAsync(Marten.IDocumentStore store, string slug, string phone, string email, bool emailVerified = true,
        int sessionVersion = 0)
    {
        var user = new ACommerce.Kit.Auth.User
        {
            Id = Guid.NewGuid(), TenantSlug = slug, FullName = "عُضوٌ قائِم",
            Phone = phone, PhoneVerified = phone != "", Email = email, EmailVerified = email != "" && emailVerified, SessionVersion = sessionVersion,
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

    private static ACommerce.Kit.Auth.Server.AuthHandlers.VerifyOutcome Wrong => ACommerce.Kit.Auth.Server.AuthHandlers.VerifyOutcome.Wrong;

    /// <summary>بابُ <c>/{slug}/auth/email/verify</c> بَعدَ التَبديل: المُعالِجُ بِقَرارِ القَناةِ لِمَتجَرٍ قَناتُه <paramref name="storeChannel"/>.</summary>
    private static Task<ACommerce.Kit.Auth.Server.AuthHandlers.VerifyOutcome> EmailDoorAsync(Marten.IDocumentStore store, string slug, string storeChannel, string email)
    {
        ACommerce.Kit.Auth.Server.AuthHandlers.IssueAttempt(slug, ACommerce.Kit.Auth.EmailAddress.Normalize(email), Code, ACommerce.Kit.Auth.Server.AuthHandlers.AuthKind.EmailOtp);
        return ACommerce.Kit.Auth.Server.AuthHandlers.VerifyEmailOtpAsync(new ACommerce.Kit.Auth.VerifyEmailOtp(email, Code),
            new VendorRegisterBucketTests.ResolvedTenant(slug), store,
            ACommerce.Templates.Customer.Marketplace.Services.LoginChannels.Registers(storeChannel, ACommerce.Kit.Auth.AuthChannels.Email));
    }

    /// <summary>ونَظيرُه لِبابِ <c>/{slug}/auth/phone/verify</c>.</summary>
    private static Task<ACommerce.Kit.Auth.Server.AuthHandlers.VerifyOutcome> PhoneDoorAsync(Marten.IDocumentStore store, string slug, string storeChannel, string phone)
    {
        ACommerce.Kit.Auth.Server.AuthHandlers.IssueAttempt(slug, phone, Code, ACommerce.Kit.Auth.Server.AuthHandlers.AuthKind.PhoneOtp);
        return ACommerce.Kit.Auth.Server.AuthHandlers.VerifyPhoneOtpAsync(new ACommerce.Kit.Auth.VerifyPhoneOtp(phone, Code),
            new VendorRegisterBucketTests.ResolvedTenant(slug), store,
            ACommerce.Templates.Customer.Marketplace.Services.LoginChannels.Registers(storeChannel, ACommerce.Kit.Auth.AuthChannels.Phone));
    }

    /// <summary><b>تَوصيفُ «اليَوم» (الموجَة ١٢ F1 البَند ٥، A-SESS-1)</b>: بابا نَفاذٍ في مَتجَرِ جَوّالٍ بِحَرفِهِما اليَوم — لا سُؤالَ عَن قَناةِ المَتجَر
    /// (الصَفُّ النَقِيُّ <c>Today_the_nafath_sign_in_doors_never_ask_the_store_channel</c> يُثبِتُه) ثُمَّ المُعالِجانِ اللَّذانِ يُناديانِهِما — فَهُوِيَّةٌ لا يَحمِلُها
    /// حِسابٌ تُنشِئُ حِساباً ثانِياً جَوّالُه <c>NID-…</c>.</summary>
    [Fact]
    public async Task Today_a_national_id_signs_up_in_a_phone_store_through_the_nafath_doors_on_Postgres()
    {
        if (!Enabled) return;
        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "sc-zz-" + Guid.NewGuid().ToString("N")[..8];
        var nid = "10" + Random.Shared.Next(10_000_000, 99_999_999);
        try
        {
            var member = await SeedAsync(store, slug, "+966500000297", "");
            var tenant = new LoginChannelsTests.ChannelTenant(slug, ACommerce.Kit.Auth.AuthChannels.Phone);
            var channel = new LoginChannelsTests.ApprovingNafath();
            var pending = await ACommerce.Kit.Auth.Server.AuthHandlers.RequestNafathHandler(new ACommerce.Kit.Auth.RequestNafath(nid), tenant, channel, default);
            var result = await ACommerce.Kit.Auth.Server.AuthHandlers.VerifyNafathHandler(new ACommerce.Kit.Auth.VerifyNafath(pending.AttemptId, nid), tenant, channel, store, default);
            var accounts = await AccountsAsync(store, slug);
            var created = accounts.Any(a => a.Phone == $"NID-{nid}");
            output.WriteLine($"اليَوم: مَتجَرُ {tenant.AuthChannel}، هُوِيَّةٌ لا يَحمِلُها حِساب ⇒ {(result is null ? "لا جَلسَة" : "جَلسَة")} · الحِسابات {accounts.Count} · جَوّالُ الجَديد NID-… {created}");
            Assert.NotNull(result);
            Assert.Equal(2, accounts.Count);
            Assert.Contains(accounts, a => a.Id != member.Id && a.Phone == $"NID-{nid}" && a.NationalId == nid);
        }
        finally { await LiveZeroRoleAdminProofTests.CleanAsync(store, slug); }
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-30، كوميتُ التَبديل)</b> — كانَ <c>Today_an_email_no_account_holds_creates_a_second_account_in_a_phone_store_on_Postgres</c>
    /// (‏<c>c3aa888</c>): «الدُخول حِسابٌ جَديد · الحِسابات 2 · الجَديدُ بِلا جَوّال True». والآن يُرَدُّ بِـ<c>contact_not_linked</c> بَعدَ الرَمزِ ولا يُكتَبُ حِساب،
    /// والرَمزُ نَفسُه لا يُعادُ استِعمالُه (مُستَهلَك).</summary>
    [Fact]
    public async Task An_email_no_account_holds_is_refused_in_a_phone_store_and_no_account_is_created_on_Postgres()
    {
        if (!Enabled) return;
        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "sc-zz-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var member = await SeedAsync(store, slug, "+966500000293", "");
            var outcome = await EmailDoorAsync(store, slug, ACommerce.Kit.Auth.AuthChannels.Phone, "unlinked.sc@example.com");
            var again = await ACommerce.Kit.Auth.Server.AuthHandlers.VerifyEmailOtpAsync(new ACommerce.Kit.Auth.VerifyEmailOtp("unlinked.sc@example.com", Code),
                new VendorRegisterBucketTests.ResolvedTenant(slug), store, registers: false);
            var accounts = await AccountsAsync(store, slug);
            var refusal = outcome.Refusal ?? "دَخَل";
            output.WriteLine($"مَتجَرُ جَوّال، بَريدٌ غَيرُ مَربوط: {refusal} · الحِسابات {accounts.Count} · الرَمزُ نَفسُه مَرَّةً ثانِيَة {(again == Wrong ? "مُستَهلَك" : "يَفتَح")}");
            Assert.Null(outcome.Result);
            Assert.Equal(ACommerce.Kit.Auth.Server.AuthHandlers.NotLinked, outcome.Refusal);
            Assert.Equal(Wrong, again);
            Assert.Equal(member.Id, Assert.Single(accounts).Id);
        }
        finally { await LiveZeroRoleAdminProofTests.CleanAsync(store, slug); }
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه</b> — كانَ <c>Today_a_phone_no_account_holds_creates_a_second_account_in_an_email_store_on_Postgres</c> (‏<c>c3aa888</c>):
    /// «حِسابٌ جَديد · 2 · بِلا بَريد True». والآن يُرَدُّ ولا يُكتَبُ حِساب.</summary>
    [Fact]
    public async Task A_phone_no_account_holds_is_refused_in_an_email_store_and_no_account_is_created_on_Postgres()
    {
        if (!Enabled) return;
        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "sc-zz-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var member = await SeedAsync(store, slug, "", "member.sc@example.com");
            var outcome = await PhoneDoorAsync(store, slug, ACommerce.Kit.Auth.AuthChannels.Email, "966500000294");
            var accounts = await AccountsAsync(store, slug);
            var refusal = outcome.Refusal ?? "دَخَل";
            output.WriteLine($"مَتجَرُ بَريد، جَوّالٌ غَيرُ مَربوط: {refusal} · الحِسابات {accounts.Count}");
            Assert.Null(outcome.Result);
            Assert.Equal(ACommerce.Kit.Auth.Server.AuthHandlers.NotLinked, outcome.Refusal);
            Assert.Equal(member.Id, Assert.Single(accounts).Id);
        }
        finally { await LiveZeroRoleAdminProofTests.CleanAsync(store, slug); }
    }

    /// <summary><b>السالِب</b>: وَسيلَةٌ رَبَطَها العُضوُ مِن «حِسابي» (مُتَحَقَّقاً مِنها) تُدخِلُه حِسابَه نَفسَه بِالقَناةِ الثانِيَة — بَريدٌ في مَتجَرِ جَوّالٍ (وبِحالَةِ
    /// أَحرُفٍ أُخرى: تَطبيعُ الدُخول)، وجَوّالٌ في مَتجَرِ بَريدٍ بِصيغَةٍ مَحَلِّيَّة — والرَمزُ بِإصدارِ جَلسَتِه، ولا حِسابَ جَديد.</summary>
    [Fact]
    public async Task A_linked_contact_signs_in_to_its_own_account_through_the_second_channel_on_Postgres()
    {
        if (!Enabled) return;
        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "sc-zz-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var byPhone = await SeedAsync(store, slug, "+966500000295", "linked.sc@example.com");
            var byEmail = await SeedAsync(store, slug, "+966500000296", "other.sc@example.com", sessionVersion: 3);
            var email = await EmailDoorAsync(store, slug, ACommerce.Kit.Auth.AuthChannels.Phone, "Linked.SC@Example.com");
            var phone = await PhoneDoorAsync(store, slug, ACommerce.Kit.Auth.AuthChannels.Email, "0500000296");
            var accounts = await AccountsAsync(store, slug);
            var emailOwn = email.Result?.UserId == byPhone.Id;
            var phoneOwn = phone.Result?.UserId == byEmail.Id;
            var version = phone.Result is null ? null : ACommerce.Kit.Auth.Server.AuthHandlers.SessionVersionOf(phone.Result.Token);
            output.WriteLine($"بَريدٌ مَربوطٌ في مَتجَرِ جَوّال ← حِسابُه {emailOwn} · جَوّالٌ مَربوطٌ في مَتجَرِ بَريد ← حِسابُه {phoneOwn} بِإصدارِ الجَلسَة {version} · الحِسابات {accounts.Count}");
            Assert.Null(email.Refusal);
            Assert.Null(phone.Refusal);
            Assert.True(emailOwn && phoneOwn);
            Assert.Equal(3, version);
            Assert.Equal(2, accounts.Count);
        }
        finally { await LiveZeroRoleAdminProofTests.CleanAsync(store, slug); }
    }

    /// <summary><b>السالِب</b>: قَناةُ المَتجَرِ ما زالَت تُسَجِّل — بَريدٌ جَديدٌ في مَتجَرِ بَريد، وجَوّالٌ جَديدٌ في مَتجَرِ جَوّال، كُلٌّ حِسابٌ جَديد.</summary>
    [Fact]
    public async Task The_store_channel_still_registers_a_new_member_on_Postgres()
    {
        if (!Enabled) return;
        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "sc-zz-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var email = await EmailDoorAsync(store, slug, ACommerce.Kit.Auth.AuthChannels.Email, "new.sc@example.com");
            var phone = await PhoneDoorAsync(store, slug, ACommerce.Kit.Auth.AuthChannels.Phone, "966500000297");
            var accounts = await AccountsAsync(store, slug);
            output.WriteLine($"مَتجَرُ بَريد ← بَريدٌ جَديد {(email.Result is null ? "رُدّ" : "حِسابٌ جَديد")} · مَتجَرُ جَوّال ← جَوّالٌ جَديد {(phone.Result is null ? "رُدّ" : "حِسابٌ جَديد")} · الحِسابات {accounts.Count}");
            Assert.Null(email.Refusal);
            Assert.Null(phone.Refusal);
            Assert.NotNull(email.Result);
            Assert.NotNull(phone.Result);
            Assert.Equal(2, accounts.Count);
        }
        finally { await LiveZeroRoleAdminProofTests.CleanAsync(store, slug); }
    }

    /// <summary><b>المُوجَب</b> (‏P1): وَسيلَةٌ في حِسابٍ لَم يُتَحَقَّق مِنها (مُستَورَدَةٌ أَو مَكتوبَة) لا تَفتَحُه بِالقَناةِ الثانِيَة — رَمزٌ إلَيها يُثبِتُ مِلكِيَّةَ
    /// الوَسيلَةِ لا الحِساب — ولا تُرفَعُ مُتَحَقَّقاً مِنها.</summary>
    [Fact]
    public async Task An_unverified_contact_on_an_account_does_not_open_it_through_the_second_channel_on_Postgres()
    {
        if (!Enabled) return;
        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "sc-zz-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var member = await SeedAsync(store, slug, "+966500000298", "typed.sc@example.com", emailVerified: false);
            var outcome = await EmailDoorAsync(store, slug, ACommerce.Kit.Auth.AuthChannels.Phone, "typed.sc@example.com");
            var after = Assert.Single(await AccountsAsync(store, slug));
            var refusal = outcome.Refusal ?? "دَخَل";
            var raised = after.EmailVerified;
            output.WriteLine($"بَريدٌ غَيرُ مُتَحَقَّقٍ مِنه في حِسابِ العُضو: {refusal} · رُفِعَ مُتَحَقَّقاً {raised}");
            Assert.Equal(ACommerce.Kit.Auth.Server.AuthHandlers.NotLinked, outcome.Refusal);
            Assert.Equal(member.Id, after.Id);
            Assert.False(raised);
        }
        finally { await LiveZeroRoleAdminProofTests.CleanAsync(store, slug); }
    }
}
