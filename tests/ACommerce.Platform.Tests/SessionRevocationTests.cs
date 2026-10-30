using System.Text;
using System.Text.RegularExpressions;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>«اخرج من كل الأجهزة» — الجَلسَةُ قَبلَ رَقمِ الإصدار</b> (‏2026-10-28، قَرارُ المالِك د28 «ب»: رَقمٌ في كُلِّ حِسابٍ اسمُه «إصدارُ الجَلسَة»،
/// يَرفَعُه الزِرُّ وحَذفُ الحِساب، فَتَسقُطُ كُلُّ رُموزِ الدُخولِ القَديمَة؛ ويَشمَلُ حِساباتِ الاستوديو ومِنها مُشرِفُ المَنَصَّة). <b>تَوصيفُ «اليَوم»
/// (القاعِدَة ٣)</b> قَبلَ أَيِّ سَطرِ كود: الرَمزُ HMAC بِأَربَعَةِ حُقولٍ صالِحٌ ثَلاثينَ يَوماً و<c>ParseToken</c> يَفحَصُ التَوقيعَ وَحدَه، فَلا
/// شَيءَ في الخادِمِ يُسقِطُ رَمزاً صَدَرَ — ولا حَذفُ الحِسابِ نَفسُه؛ والخُروجُ يَمسَحُ كوكيَّ الجِهازِ وَحدَه. وصُفوفُ <c>Today_</c> تُقلَبُ بِأَسمائِها
/// في كوميتِ التَبديل.</para>
/// </summary>
public class SessionRevocationTests(ITestOutputHelper output)
{
    private const string TemplateRoot = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    private static string Read(string relative)
        => File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string[] Fields(string token) => Encoding.UTF8.GetString(Convert.FromBase64String(token)).Split('|');

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-28، د28 — كوميتُ التَبديل)</b> — كانَ <c>Today_a_session_token_carries_no_version_and_lives_thirty_days</c>
    /// (‏<c>f22108d</c>): «حُقولُ الرَمز: 4». والآن الإصدارُ 0 يَكتُبُ الشَكلَ القَديمَ بِحَرفِه (فَلا يَخرُجُ أَحَدٌ بِالنَشر)، والإصدارُ المُوجَبُ خَمسَةَ حُقولٍ
    /// يُغَطّيها التَوقيع — فَإصدارٌ مُبَدَّلٌ يُرَدّ — والعُمرُ ثَلاثونَ يَوماً كَما كان.</summary>
    [Fact]
    public void A_session_token_carries_its_account_version_and_version_zero_keeps_the_old_shape()
    {
        var id = Guid.NewGuid();
        var v0 = AuthHandlers.MakeToken(id, "zz-session");
        var v3 = AuthHandlers.MakeToken(id, "zz-session", 3);
        output.WriteLine($"حُقولُ الرَمز: الإصدارُ 0 ← {Fields(v0).Length} · الإصدارُ 3 ← {Fields(v3).Length}");
        Assert.Equal(4, Fields(v0).Length);
        Assert.Equal(5, Fields(v3).Length);
        Assert.Equal(0, AuthHandlers.SessionVersionOf(v0));
        Assert.Equal(3, AuthHandlers.SessionVersionOf(v3));
        Assert.Equal(id, AuthHandlers.ParseToken(v3)!.Value.UserId);
        Assert.InRange((AuthHandlers.ParseToken(v3)!.Value.ExpiresAt - DateTime.UtcNow).TotalDays, 29.9, 30.1);

        var f = Fields(v3);
        var forged = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{f[0]}|{f[1]}|{f[2]}|9|{f[4]}"));
        Assert.Null(AuthHandlers.ParseToken(forged));
        Assert.Null(AuthHandlers.SessionVersionOf(forged));
        Assert.Null(AuthHandlers.SessionVersionOf("not-a-token"));
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-28، د28)</b> — كانَ <c>Today_account_deletion_leaves_a_token_issued_before_it_valid</c> (‏<c>f22108d</c>):
    /// الرَمزُ صالِحٌ بَعدَ الحَذفِ ولا حَقلَ إصدار. والآن <c>Erase</c> يَرفَعُ الإصدار، فَالرَمزُ السابِقُ ساقِطٌ عِندَ البَوّابَة — والتَوقيعُ وَحدَه ما زالَ يَقبَلُه،
    /// ولِذلكَ البَوّابَةُ لا <c>ParseToken</c>.</summary>
    [Fact]
    public void Account_deletion_raises_the_session_version_so_a_token_issued_before_it_is_revoked()
    {
        var user = new User { Id = Guid.NewGuid(), TenantSlug = "zz-session", Phone = "966500000028", FullName = "عُضو" };
        var token = AuthHandlers.MakeToken(user.Id, user.TenantSlug, user.SessionVersion);
        AccountDeletion.Erase(user, DateTime.UtcNow);
        var revoked = ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.IsRevoked(AuthHandlers.SessionVersionOf(token)!.Value, user.SessionVersion);
        output.WriteLine($"بَعدَ الحَذف: إصدارُ الحِساب {user.SessionVersion} · الرَمزُ عِندَ البَوّابَة {(revoked ? "ساقِط" : "صالِح")}");
        Assert.Equal(1, user.SessionVersion);
        Assert.True(revoked);
        Assert.NotNull(AuthHandlers.ParseToken(token));
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-28، د28)</b> — كانَ <c>Today_no_door_signs_an_account_out_of_every_device</c> (‏<c>f22108d</c>):
    /// «أَبوابٌ 0». والآن بابانِ (المَتجَرُ والاستوديو)، وبابا الخُروجِ مِن الجِهازِ اثنانِ كَما كانا.</summary>
    [Fact]
    public void Two_doors_sign_an_account_out_of_every_device_and_logout_stays_per_device()
    {
        var code = WriteEndpointGuardTests.StripComments(Read($"{TemplateRoot}/MarketplaceTemplateExtensions.cs"));
        var doors = Regex.Matches(code, @"MapPost\(""(?<r>[^""]*sessions/revoke[^""]*)""").Select(m => m.Groups["r"].Value).ToList();
        var logouts = Regex.Matches(code, @"MapPost\(""(?<r>/(?:\{slug\}/auth|studio)/logout)""").Count;
        output.WriteLine($"أَبوابُ «اخرج من كل الأجهزة»: {doors.Count} ({string.Join(" · ", doors)}) · أَبوابُ الخُروجِ مِن الجِهاز: {logouts}");
        Assert.Equal(2, logouts);
        Assert.Equal(new[] { "/studio/me/sessions/revoke", "/{slug}/me/sessions/revoke" }, doors.OrderBy(d => d, StringComparer.Ordinal));
    }

    // ─── البَوّابَة: دالّاتٌ نَقِيَّة، ومَوضِعُها في الأُنبوب ─────────────────────────────────────

    [Theory]
    [InlineData(0, 1, true)]
    [InlineData(2, 3, true)]
    [InlineData(1, 1, false)]
    [InlineData(3, 1, false)]
    [InlineData(0, 0, false)]
    public void A_token_below_the_account_version_is_revoked_and_one_at_or_above_is_not(int token, int account, bool expected)
        => Assert.Equal(expected, ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.IsRevoked(token, account));

    [Fact]
    public void An_account_the_gate_cannot_read_revokes_nothing()
        => Assert.False(ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.IsRevoked(0, null));

    [Fact]
    public void The_gate_reads_every_session_cookie_and_no_name_or_foreign_cookie()
    {
        var names = ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.SessionCookieNames(new[]
        {
            ".acommerce.studio", ".acommerce.studio.name", ".acommerce.auth.ejar", ".acommerce.auth.ejar.name",
            ".acommerce.auth.order.driver", ".acommerce.auth.order.driver.name", ".AspNetCore.Antiforgery.x", "ac_lang",
        });
        Assert.Equal(new[] { ".acommerce.studio", ".acommerce.auth.ejar", ".acommerce.auth.order.driver" }, names);
    }

    [Fact]
    public void The_gate_strips_a_revoked_cookie_and_its_name_from_the_request_header_and_keeps_the_rest()
    {
        const string header = ".acommerce.auth.ejar=AAA; .acommerce.auth.ejar.name=x; ac_lang=ar; .acommerce.studio=BBB";
        Assert.Equal("ac_lang=ar; .acommerce.studio=BBB",
            ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.WithoutCookies(header, new[] { ".acommerce.auth.ejar" }));
        Assert.Equal(header.Replace("; ", "; "), ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.WithoutCookies(header, Array.Empty<string>()));
    }

    [Fact]
    public void The_version_is_held_in_memory_one_minute_and_the_gate_runs_before_the_template()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.CacheFor);
        var program = WriteEndpointGuardTests.StripComments(Read("apps/V1.App/Program.cs"));
        var gate = program.IndexOf("app.UseSessionVersionGate();", StringComparison.Ordinal);
        var template = program.IndexOf("app.MapCustomerMarketplaceTemplate();", StringComparison.Ordinal);
        var pages = program.IndexOf("app.MapRazorComponents<App>()", StringComparison.Ordinal);
        output.WriteLine($"Program.cs: البَوّابَة @{gate} · القالِب @{template} · الصَفَحات @{pages}");
        Assert.True(gate > 0 && gate < template && gate < pages, "بَوّابَةُ الجَلسَةِ غائِبَةٌ أَو بَعدَ نِقاطِ القالِبِ وصَفَحاتِه.");
    }

    /// <summary>البابانِ يُرسَمانِ حَيثُ يَجِدُهُما صاحِبُهُما (القاعِدَة ١٢): «حِسابي» في المَتجَرِ وصَفحَةُ الاشتِراكِ في الاستوديو، والنَصُّ مِن القامُوس.</summary>
    [Fact]
    public void Each_door_is_reached_by_a_form_and_says_its_text_from_the_dictionary()
    {
        var me = Read($"{TemplateRoot}/Components/Pages/Me.razor");
        var billing = Read($"{TemplateRoot}/Components/Pages/StudioBilling.razor");
        Assert.Contains("action=\"@($\"/{Slug}/me/sessions/revoke\")\"", me, StringComparison.Ordinal);
        Assert.Contains("action=\"/studio/me/sessions/revoke\"", billing, StringComparison.Ordinal);
        foreach (var page in new[] { me, billing })
            Assert.Contains("L.Markup(\"auth.action.logout_everywhere\")", page, StringComparison.Ordinal);
        using var ar = System.Text.Json.JsonDocument.Parse(Read("libs/core/ACommerce.Platform.I18n/Locales/ar.json"));
        Assert.Equal("اخرُج مِن كُلِّ الأَجهِزَة", ar.RootElement.GetProperty("auth.action.logout_everywhere").GetString());
    }

    /// <summary>شَرطُ نَقلِ حُدودِ الطَلَباتِ مَكتوبٌ بِجِوارِها (د28 (٢)): تَبقى في الذاكِرَةِ ما دامَ الموقِعُ نُسخَةً واحِدَة.</summary>
    [Fact]
    public void The_rate_limits_say_when_they_move_out_of_memory()
    {
        var auth = Read("libs/kits/Auth/ACommerce.Kit.Auth.Server/AuthHandlers.cs");
        Assert.Contains("شَرطُ النَقلِ — مَكتوبٌ هُنا بِقَرارِ المالِك د28", auth, StringComparison.Ordinal);
        Assert.Contains("يَومَ يَعمَلُ بِأَكثَرَ مِن نُسخَةٍ تُنقَلُ إلى قاعِدَةِ البَيانات", auth, StringComparison.Ordinal);
    }

    // ─── I-SEC-2 + A5 (تَدقيقُ الموجَة ١١، ‏2026-10-29): «اخرج من كل الأجهزة» يَمحو اشتِراكاتِ الإشعار ─────────────────────────────

    private static ACommerce.Kit.Auth.PushSubscription Sub(string device)
        => new() { Endpoint = $"https://fcm.googleapis.com/fcm/send/{device}", P256dh = "p", Auth = "a", CreatedAt = DateTime.UtcNow };

    /// <summary>كانَ الرَفعُ يَرفَعُ الإصدارَ وَحدَه، والمُرسِلُ يَطرُقُ كُلَّ اشتِراكٍ مَحفوظٍ بِلا جَلسَة — فَالهاتِفُ المَسروقُ يَتَلَقّى نَصَّ المُحادَثات.
    /// والآن الأَثَرُ الواحِدُ (<c>Revoke</c>) يَمحوها كُلَّها مَعَ الإصدار.</summary>
    [Fact]
    public void Signing_out_everywhere_clears_every_push_subscription_with_the_version()
    {
        var user = new User { Id = Guid.NewGuid(), TenantSlug = NameSlug, SessionVersion = 3 };
        user.PushSubscriptions.AddRange(new[] { Sub("phone"), Sub("laptop") });
        var at = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
        ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.Revoke(user, at);
        output.WriteLine($"بَعدَ الرَفع: الإصدارُ {user.SessionVersion} · الاشتِراكات {user.PushSubscriptions.Count}");
        Assert.Equal(4, user.SessionVersion);
        Assert.Empty(user.PushSubscriptions);
        Assert.Equal(at, user.UpdatedAt);
    }

    /// <summary>بابُ «اخرج من كل الأجهزة» يُطَبِّقُ الأَثَرَ نَفسَه — لا رَفعاً بِيَدِه يَنجَرِف.</summary>
    [Fact]
    public void The_tenant_revoke_applies_the_one_effect()
    {
        var gate = WriteEndpointGuardTests.StripComments(Read($"{TemplateRoot}/Services/SessionVersionGate.cs"));
        var body = gate[gate.IndexOf("RevokeTenantAsync(", StringComparison.Ordinal)..];
        body = body[..body.IndexOf("public static", StringComparison.Ordinal)];
        Assert.Contains("Revoke(", body, StringComparison.Ordinal);
        Assert.DoesNotContain("SessionVersion++", body, StringComparison.Ordinal);
    }

    // ─── G-1 (تَدقيقُ الموجَة ١١، ‏2026-10-29): كُتّابُ الحِسابِ المُسَمَّونَ يَحفَظونَ بِالكِتابَةِ الواحِدَةِ الَّتي تُعيدُ مَرَّة ──────────

    /// <summary>جِسمٌ يَحفَظُ الحِسابَ بِـ<c>AccountWrites.UpdateAsync</c> ولا يَفتَحُ جَلسَةً خَفيفَةً يُحَمِّلُ فيها الحِسابَ بِيَدِه — نَقِيّ.</summary>
    internal static bool SavesThroughAccountWrites(string body)
        => body.Contains("AccountWrites.UpdateAsync<", StringComparison.Ordinal)
           && !Regex.IsMatch(body, @"LightweightSession\([^)]*\)[\s\S]*LoadAsync<(?:ACommerce\.Kit\.Auth\.)?(?:User|(?:Services\.Incubator\.)?StudioUser)>");

    private static string MethodBody(string code, string signature)
    {
        var i = code.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(i >= 0, $"«{signature}» غائِب — الأَداةُ عَمياء.");
        var j = code.IndexOf("\n    }\n", i, StringComparison.Ordinal);
        return code[i..j];
    }

    [Fact]
    public void Every_named_account_writer_saves_through_the_one_write_that_retries_once()
    {
        var gate = WriteEndpointGuardTests.StripComments(Read($"{TemplateRoot}/Services/SessionVersionGate.cs"));
        var push = WriteEndpointGuardTests.StripComments(Read($"{TemplateRoot}/Services/WebPushService.cs"));
        var contacts = WriteEndpointGuardTests.StripComments(Read($"{TemplateRoot}/Services/AccountContacts.cs"));
        var mte = WriteEndpointGuardTests.StripComments(Read($"{TemplateRoot}/MarketplaceTemplateExtensions.cs"));
        var auth = WriteEndpointGuardTests.StripComments(Read("libs/kits/Auth/ACommerce.Kit.Auth.Server/AuthHandlers.cs"));
        var bodies = new Dictionary<string, string>
        {
            ["RevokeTenantAsync"] = MethodBody(gate, "RevokeTenantAsync("),
            ["RevokeStudioAsync"] = MethodBody(gate, "RevokeStudioAsync("),
            ["WebPushService.SendAsync"] = MethodBody(push, "public async Task SendAsync("),
            ["AccountContacts.VerifyAsync"] = MethodBody(contacts, "public static async Task<string?> VerifyAsync("),
            ["AccountContacts.VerifyRemoveAsync"] = MethodBody(contacts, "public static async Task<string?> VerifyRemoveAsync("),
            ["AuthHandlers.GetOrCreateUserAsync"] = MethodBody(auth, "private static async Task<AuthResult> GetOrCreateUserAsync("),
            ["AssignRoleAsync"] = MethodBody(mte, "private static async Task AssignRoleAsync("),
            ["PostLoginRouteAsync"] = MethodBody(mte, "private static async Task<string> PostLoginRouteAsync("),
            // ‏(‏2026-10-30، الموجَة ١٢ F1 البَند ٦) كانَ يَحفَظُ بِيَدِه — وكانَ في قائِمَةِ «ما بَقِيَ بِلا إعادَة» تَحتَ ADR-041 G-1.
            ["AcceptTermsHandler.HandleAsync"] = MethodBody(WriteEndpointGuardTests.StripComments(Read($"{TemplateRoot}/Commands/AcceptTermsCommand.cs")), "public async Task<bool> HandleAsync("),
        };
        foreach (var route in new[] { "/api/{slug}/push/subscribe", "/{slug}/me/save", "/{slug}/me/role/save", "/{slug}/me/role/onboarding/save", "/{slug}/me/area/save" })
        {
            var doors = WriteEndpointGuardTests.AllMinimalApiEndpoints().Where(e => e.Route == route).ToList();
            Assert.True(doors.Count == 1, $"«{route}»: {doors.Count} باباً — الأَداةُ عَمياء.");
            bodies[route] = doors[0].Body;
        }
        var off = bodies.Where(b => !SavesThroughAccountWrites(b.Value)).Select(b => b.Key).ToList();
        output.WriteLine($"كُتّابُ الحِسابِ المُسَمَّون: {bodies.Count} · بِالكِتابَةِ الواحِدَة: {bodies.Count - off.Count}");
        Assert.Equal(14, bodies.Count);
        Assert.True(off.Count == 0, "كاتِبٌ يَحفَظُ الحِسابَ بِيَدِه: " + string.Join(" · ", off));
    }

    /// <summary>حَقنُ عَيب (القاعِدَة ١٠): شَكلُ اليَومِ بِحَرفِه يُحمِرُّ الفاحِص، وشَكلُ اليَومِ لِلاستوديو أَيضاً.</summary>
    [Theory]
    [InlineData("await using var s = store.LightweightSession(slug);\nvar user = await s.LoadAsync<User>(userId);\ns.Store(user);")]
    [InlineData("var x = await AccountWrites.UpdateAsync<User>(store, slug, id, u => true);\nawait using var s = store.LightweightSession(slug);\nvar user = await s.LoadAsync<User>(userId);")]
    [InlineData("await using var s = store.LightweightSession(StudioAuth.Tenant);\nvar user = await s.LoadAsync<StudioUser>(userId);")]
    public void The_writer_scan_refuses_a_hand_written_load_and_store(string body)
        => Assert.False(SavesThroughAccountWrites(body));

    // ─── ذاكِرَةُ الإصدار (الموجَة ١٢ F1 البَند ٧، ‏2026-10-30): قِراءَةٌ فاتَتها الذاكِرَةُ قَبلَ رَفعٍ تُعيدُ حِفظَ ما قَبلَه ──────────────────

    /// <summary>
    /// <para><b>تَوصيفُ «اليَوم»</b>: <c>AccountVersionAsync</c> تَقرَأُ الإصدارَ ثُمَّ تَحفَظُه دَقيقَة، و<c>Forget</c> يَمحو المَحفوظَ فَورَ الرَفع. فَقِراءَةٌ بَدَأَت
    /// قَبلَ الرَفعِ (فَأَعادَت الإصدارَ 0) ووَقَعَ الرَفعُ و<c>Forget</c> بَينَها وبَينَ الحِفظ تَحفَظُ 0 بَعدَ المَحو — فَيَعبُرُ رَمزُ ما قَبلَ الرَفعِ البَوّابَةَ دَقيقَةً
    /// كامِلَة. الصَفُّ نَقِيٌّ بِالمَخزَنِ المُرَتَّب: القِراءَةُ الأُولى 0 ثُمَّ <c>Forget</c> قَبلَ أَن تَعودَ، والثانِيَةُ (إن وَقَعَت) 1. يُقلَبُ بِاسمِه في كوميتِ التَبديل.</para>
    /// </summary>
    [Fact]
    public async Task Today_a_read_that_misses_the_cache_before_a_revoke_caches_the_old_version_after_the_forget()
    {
        var id = Guid.NewGuid();
        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var (store, script) = ScriptedAccountStore.Of(
            read => { if (read == 1) ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.Forget(cache, NameSlug, id); },
            new User { Id = id, TenantSlug = NameSlug, SessionVersion = 0 }, new User { Id = id, TenantSlug = NameSlug, SessionVersion = 1 });
        var first = await ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.AccountVersionAsync(store, cache, NameSlug, id);
        var next = await ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.AccountVersionAsync(store, cache, NameSlug, id);
        var refused = ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.IsRevoked(0, next);
        output.WriteLine($"اليَوم: القِراءَةُ المُتَقاطِعَة ⇒ {first} · الطَلَبُ التالي ⇒ {next} مِن الذاكِرَة · قِراءاتُ المَخزَن {script.Reads} · رَمزُ ما قَبلَ الرَفعِ يُرَدّ {refused}");
        Assert.Equal(0, first);
        Assert.Equal(0, next);
        Assert.Equal(1, script.Reads);
        Assert.False(refused);
    }

    // ─── الكاتِبُ بِاسمِ جَلسَةٍ يَسأَلُها عَلى القِراءَةِ الجَديدَة (مُراجَعَةُ الموجَة ١١ لِلمَسار F1، ‏2026-10-30) ───────────────────────

    /// <summary><c>SessionVersionGate.StillOpen</c> — نَقِيَّة: رَمزٌ حَيٌّ لِهذا الحِسابِ مَفتوح (مُوجَب)؛ ورَمزُ ما قَبلَ الرَفعِ، ورَمزُ المَحذوف، ورَمزُ حِسابٍ آخَر،
    /// وما لَيسَ رَمزاً — مُغلَقَةٌ (سالِب).</summary>
    [Theory]
    [InlineData("live", true)]
    [InlineData("above", true)]
    [InlineData("revoked", false)]
    [InlineData("deleted", false)]
    [InlineData("other", false)]
    [InlineData("garbage", false)]
    public void A_session_is_still_open_only_for_its_own_live_account(string row, bool open)
    {
        var account = new User { Id = Guid.NewGuid(), TenantSlug = NameSlug, SessionVersion = 2, DeletedAt = row == "deleted" ? DateTime.UtcNow : null };
        var token = row switch
        {
            "live" or "deleted" => AuthHandlers.MakeToken(account.Id, NameSlug, 2),
            "above" => AuthHandlers.MakeToken(account.Id, NameSlug, 3),
            "revoked" => AuthHandlers.MakeToken(account.Id, NameSlug, 1),
            "other" => AuthHandlers.MakeToken(Guid.NewGuid(), NameSlug, 2),
            _ => "not-a-token",
        };
        Assert.Equal(open, ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.StillOpen(account, token));
    }

    /// <summary>قَبولُ الشُروطِ بِالبابِ الحَقيقيّ: <c>admit</c> قَرارُ <c>StillOpen</c> عَلى الرَمزِ الَّذي عَبَرَ البَوّابَة.</summary>
    private static async Task<bool> AcceptTermsAsync(DocWorld world, Guid userId, string token, int version)
        => await new ACommerce.Templates.Customer.Marketplace.Commands.AcceptTermsHandler(world.Store).HandleAsync(
            new ACommerce.Templates.Customer.Marketplace.Commands.AcceptTermsCommand(userId, NameSlug, version),
            admit: u => ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.StillOpen(u, token));

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-30، كوميتُ التَبديل)</b> — كانَ <c>Today_the_terms_acceptance_is_written_for_a_session_revoked_before_the_write</c>
    /// (‏<c>c5d124c</c>): «كُتِبَ القَبولُ 1 مَرَّة · إصدارُ الشُروط 3 · الجَلسَةُ مَفتوحَة False». والآن جَلسَةٌ أَسقَطَها «اخرج من كل الأجهزة» بَينَ البَوّابَةِ
    /// والقِراءَةِ يُرَدُّ قَبولُها عَلى الوَثيقَةِ الجَديدَة بِلا كِتابَة.</summary>
    [Fact]
    public async Task A_session_revoked_before_the_terms_write_is_refused_and_nothing_is_written()
    {
        var user = new User { Id = Guid.NewGuid(), TenantSlug = NameSlug, SessionVersion = 0 };
        var token = AuthHandlers.MakeToken(user.Id, NameSlug, 0);
        var world = new DocWorld().Put(user);
        world.BeforeMember = m => { if (m == "LoadAsync") user.SessionVersion = 1; };
        var accepted = await AcceptTermsAsync(world, user.Id, token, 3);
        var stored = world.Read<User>(user.Id.ToString())!;
        output.WriteLine($"جَلسَةٌ أُسقِطَت بَينَ البَوّابَةِ والكِتابَة ⇒ {(accepted ? "قُبِل" : "رُدّ")} · كُتِبَ {world.Wrote<User>()} مَرَّة · إصدارُ الشُروط {stored.AcceptedTermsVersion}");
        Assert.False(accepted);
        Assert.Equal(0, world.Wrote<User>());
        Assert.Equal(0, world.SaveCalls);
        Assert.Equal(0, stored.AcceptedTermsVersion);
        Assert.Null(stored.AcceptedTermsAt);
    }

    /// <summary>السالِبان: جَلسَةٌ مَفتوحَةٌ تَقبَلُ الشُروطَ ويُكتَبُ الإصدار؛ وحِسابٌ غائِبٌ يُرَدُّ بِـ<c>false</c> (كانَ يَرمي «user not found» ← ‏500).</summary>
    [Fact]
    public async Task An_open_session_accepts_the_terms_and_a_missing_account_is_refused_without_a_throw()
    {
        var user = new User { Id = Guid.NewGuid(), TenantSlug = NameSlug, SessionVersion = 2 };
        var world = new DocWorld().Put(user);
        var accepted = await AcceptTermsAsync(world, user.Id, AuthHandlers.MakeToken(user.Id, NameSlug, 2), 3);
        var stored = world.Read<User>(user.Id.ToString())!;
        var missing = await AcceptTermsAsync(new DocWorld(), Guid.NewGuid(), AuthHandlers.MakeToken(Guid.NewGuid(), NameSlug, 0), 3);
        output.WriteLine($"جَلسَةٌ مَفتوحَة ⇒ {(accepted ? "قُبِل" : "رُدّ")} · إصدارُ الشُروط {stored.AcceptedTermsVersion} · حِسابٌ غائِب ⇒ {(missing ? "قُبِل" : "رُدّ")}");
        Assert.True(accepted);
        Assert.Equal(1, world.Wrote<User>());
        Assert.Equal(3, stored.AcceptedTermsVersion);
        Assert.NotNull(stored.AcceptedTermsAt);
        Assert.False(missing);
    }

    /// <summary>بابُ <c>/{slug}/terms/accept</c> يُعطي كاتِبَه <c>admit</c> عَلى الرَمزِ الَّذي يَحُلُّه، ويَرُدُّ المُغلَقَ إلى الدُخولِ بِرَمزِ بَوّابَةِ التَوثيقِ نَفسِه.</summary>
    [Fact]
    public void The_terms_door_hands_its_writer_the_session_and_sends_a_closed_one_to_sign_in()
    {
        var doors = WriteEndpointGuardTests.AllMinimalApiEndpoints().Where(e => e.Route == "/{slug}/terms/accept").ToList();
        Assert.True(doors.Count == 1, $"«/{{slug}}/terms/accept»: {doors.Count} باباً — الأَداةُ عَمياء.");
        var body = doors[0].Body;
        Assert.Contains("var token = AuthSession.ResolveToken(req, slug);", body, StringComparison.Ordinal);
        Assert.Contains("handler.HandleAsync(cmd, admit: u => Services.SessionVersionGate.StillOpen(u, token))", body, StringComparison.Ordinal);
        Assert.Contains("if (!accepted) return Results.Redirect(AuthSession.LinkFor(slug, role, \"login?err=auth\"));", body, StringComparison.Ordinal);
        Assert.True(AsksTheSession(body));
    }

    /// <summary>
    /// <para>المُعلَنُ في النِداء: كُلُّ كاتِبٍ بِاسمِ جَلسَةٍ يُعطي <c>admit</c> قَرارَ <c>StillOpen</c>، والخِدماتُ الَّتي تَأخُذُ <c>admit</c> تُمَرِّرُه — عَيِّنَةٌ تَطبَعُ عَدَدَها.</para>
    /// <para><b>(‏2026-10-30، الموجَة ١٢ F1 البَند ٢ — مُراجَعَةُ F1 الأَخيرَة R1-2؛ يُلحَقُ ولا يُعادُ حَرفٌ فَوقَ ما سَبَق) يَجِدُ الكُتّابَ ولا يُسَمّيهِم</b>: كانَ
    /// يُسَمّي سَبعَةَ مَساراتٍ ودالَّتَين (<c>SessionWriterRoutes</c>)، فَبابُ جَلسَةٍ جَديدٌ يُنادي <c>AccountWrites.UpdateAsync&lt;User&gt;</c> بِلا <c>admit</c>، أَو
    /// مُنادٍ جَديدٌ لِـ<c>AccountContacts.VerifyAsync</c> (و<c>admit</c> فيها اختِيارِيٌّ افتِراضُه <c>null</c>) بِلا <c>admit</c>، يَترُكُ كُلَّ صَفٍّ أَخضَر —
    /// العَمى نَفسُه الَّذي تُسَمّيه القاعِدَة ١٠ («قَواعِدُ احتِواءٍ تَفحَصُ أَزواجاً مُسَمّاة»). والآن: كُلُّ جِسمِ نُقطَةٍ يَقرَأُ جَلسَةً (<see cref="ReadsTheSession"/>)
    /// ويَكتُبُ الحِسابَ (<see cref="WritesTheAccount"/>: نِداءٌ مُباشِرٌ أَو دالَّةٌ كاتِبَةٌ يَجِدُها <see cref="AccountWriterMethods"/> في المَصدَر) يُسأَلُ
    /// <see cref="AsksTheSession"/>، إلّا ما في <see cref="SessionlessWriters"/> بِسَبَبِه؛ وكُلُّ دالَّةٍ كاتِبَةٍ تَأخُذُ <c>admit</c> تُسأَلُ أَن تُمَرِّرَه.</para>
    /// </summary>
    [Fact]
    public void Every_session_writer_asks_the_session_on_the_fresh_read()
    {
        var all = AccountWriterMethods();
        var notForSession = SessionlessWriterMethods.Select(m => m.Writer).ToHashSet(StringComparer.Ordinal);
        var writers = all.Where(w => !notForSession.Contains($"{w.Type}.{w.Method}")).ToList();
        var staleMethods = notForSession.Where(n => all.All(w => $"{w.Type}.{w.Method}" != n)).ToList();
        Assert.True(staleMethods.Count == 0, "استِثناءٌ لِدالَّةٍ لا يَجِدُها الفاحِصُ كاتِبَة — يُرفَع: " + string.Join(" · ", staleMethods));
        var found = WriteEndpointGuardTests.AllMinimalApiEndpoints()
            .Where(e => ReadsTheSession.IsMatch(e.Body) && WritesTheAccount(e.Body, writers)).ToList();
        var exempt = SessionlessWriters.ToDictionary(x => x.Route, x => x.WhyAr, StringComparer.Ordinal);
        var asked = found.Where(e => !exempt.ContainsKey(e.Route)).ToList();
        var off = asked.Where(e => !AsksTheSession(e.Body)).Select(e => e.Route).ToList();
        var takers = writers.Where(w => w.TakesAdmit).ToList();
        var dropped = takers.Where(w => !PassesAdmit(w.Body)).Select(w => $"{w.Type}.{w.Method}").ToList();
        output.WriteLine($"دَوالُّ تَكتُبُ الحِساب: {all.Count} ({string.Join(" · ", all.Select(w => $"{w.Type}.{w.Method}"))}) · لا تَكتُبُ لِجَلسَةٍ بِسَبَبِها: {all.Count - writers.Count} · تَأخُذُ admit: {takers.Count}");
        output.WriteLine($"أَبوابٌ تَقرَأُ جَلسَةً وتَكتُبُ الحِساب: {found.Count} · مُستَثناةٌ بِسَبَبِها: {found.Count - asked.Count} · تَسأَلُ الجَلسَةَ عَلى القِراءَةِ الجَديدَة: {asked.Count - off.Count}/{asked.Count}");
        foreach (var e in found) output.WriteLine($"  {e.Route}{(exempt.ContainsKey(e.Route) ? "  (مُستَثنى)" : "")}");
        Assert.True(writers.Count > 0 && found.Count > 0 && takers.Count > 0, "صِفرٌ مَفحوص — الأَداةُ عَمياء.");
        Assert.True(off.Count == 0, "كاتِبٌ بِاسمِ جَلسَةٍ لا يَسأَلُها: " + string.Join(" · ", off));
        Assert.True(dropped.Count == 0, "دالَّةٌ تَأخُذُ admit ولا تُمَرِّرُه: " + string.Join(" · ", dropped));
        var stale = exempt.Keys.Where(r => found.All(e => e.Route != r)).ToList();
        Assert.True(stale.Count == 0, "استِثناءٌ لِبابٍ لا يَجِدُه الفاحِص — يُرفَع: " + string.Join(" · ", stale));
    }

    /// <summary>يَقرَأُ جِسمُ النُقطَةِ جَلسَةَ مَتجَر — الشَكلانِ اللَّذانِ يَحُلُّ بِهِما كُلُّ بابٍ رَمزَه.</summary>
    internal static readonly Regex ReadsTheSession = new(@"AuthSession\.ResolveToken\s*\(|AuthHandlers\.ParseToken\s*\(", RegexOptions.Compiled);

    /// <summary>الكِتابَةُ الواحِدَةُ لِوَثيقَةِ الحِساب (‏<c>AccountWrites</c>).</summary>
    private static readonly Regex AccountWrite = new(@"AccountWrites\s*\.\s*UpdateAsync\s*<\s*(?:[\w.]+\.)?User\s*>\s*\(", RegexOptions.Compiled);

    private static readonly Regex TypeDecl = new(@"\b(?:class|record|struct)\s+(?<name>\w+)", RegexOptions.Compiled);
    private static readonly Regex MethodDecl = new(
        @"(?:public|internal|private|protected)\s+(?:(?:static|async|virtual|override|sealed|new|readonly|partial)\s+)*[\w<>\[\]?,.() ]+?\s+(?<name>\w+)\s*(?:<[^<>()]*>)?\s*\(",
        RegexOptions.Compiled);

    /// <summary>دالَّةٌ في المَصدَرِ يُنادي جِسمُها <c>AccountWrites.UpdateAsync&lt;User&gt;</c> — بِنَوعِها واسمِها وجِسمِها، و<c>TakesAdmit</c> إن كانَ لَها وَسيطٌ اسمُه <c>admit</c>.</summary>
    internal sealed record AccountWriter(string Type, string Method, string Body, bool TakesAdmit);

    /// <summary>
    /// <para><b>يَجِدُ الدَوالَّ الكاتِبَةَ لِلحِسابِ في <c>libs/</c> و<c>apps/</c></b>: لِكُلِّ نِداءٍ لِـ<c>AccountWrites.UpdateAsync&lt;User&gt;</c> الدالَّةُ الَّتي تَحويه
    /// (أَقرَبُ تَصريحٍ قَبلَه يَحتَوي جِسمُه النِداءَ) ونَوعُها (أَقرَبُ تَصريحِ نَوعٍ قَبلَها). ودالَّةُ تَسجيلِ النِقاطِ نَفسُها (<c>Map…</c>) لا تُعَدُّ كاتِبَة: النِداءاتُ
    /// فيها أَجسامُ نِقاطٍ يَسأَلُها الصَفُّ بِأَعيانِها. <b>وحَدُّه</b>: النَصُّ لا الدَلالَة — كاتِبٌ يُنادى عَبرَ واجِهَةٍ أَو مُفَوَّضٍ لا يُرى.</para>
    /// </summary>
    internal static IReadOnlyList<AccountWriter> AccountWriterMethods()
    {
        var found = new List<AccountWriter>();
        foreach (var (file, text) in EntitlementContractTests.SourceFiles())
        {
            if (!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
            var code = WriteEndpointGuardTests.StripComments(text);
            if (!AccountWrite.IsMatch(code)) continue;
            var methods = MethodDecl.Matches(code).Cast<Match>().Select(m => (Match: m, Span: MethodSpan(code, m))).Where(x => x.Span.Start >= 0).ToList();
            var types = TypeDecl.Matches(code).Cast<Match>().Select(t =>
            {
                var open = code.IndexOf('{', t.Index + t.Length);
                var semi = code.IndexOf(';', t.Index + t.Length);
                return open < 0 || (semi >= 0 && semi < open)
                    ? (Name: t.Groups["name"].Value, Start: -1, End: -1)
                    : (Name: t.Groups["name"].Value, Start: open, End: Closing(code, open, '{', '}'));
            }).Where(t => t.Start >= 0).ToList();
            foreach (Match call in AccountWrite.Matches(code))
            {
                var owner = methods.LastOrDefault(x => x.Match.Index < call.Index && x.Span.End > call.Index);
                if (owner.Match is null) continue;
                var name = owner.Match.Groups["name"].Value;
                var body = code[owner.Span.Start..owner.Span.End];
                if (name.StartsWith("Map", StringComparison.Ordinal) && body.Contains(".MapPost(", StringComparison.Ordinal)) continue;
                var type = types.Where(t => t.Start < owner.Match.Index && t.End > owner.Match.Index).OrderByDescending(t => t.Start).Select(t => t.Name).FirstOrDefault() ?? "?";
                var head = code[owner.Match.Index..owner.Span.Start];
                if (found.Any(w => w.Type == type && w.Method == name)) continue;
                found.Add(new(type, name, body, Regex.IsMatch(head, @"\badmit\b")));
            }
        }
        return found;
    }

    /// <summary>مَدى جِسمِ الدالَّةِ الَّتي يَبدَأُ تَصريحُها عِندَ <paramref name="decl"/>: مِن بَعدِ قَوسِ وُسَطائِها إلى قَوسِها المُغلِق (أَو فاصِلَةِ السَهمِ المُنهِيَة).</summary>
    private static (int Start, int End) MethodSpan(string code, Match decl)
    {
        var after = Closing(code, decl.Index + decl.Length - 1, '(', ')') + 1;
        var head = Regex.Match(code[after..Math.Min(code.Length, after + 400)], @"^\s*(?:where[^{=;]*)?(?<kind>\{|=>)");
        if (!head.Success) return (-1, -1);
        var at = after + head.Groups["kind"].Index;
        if (head.Groups["kind"].Value == "{") return (at, Closing(code, at, '{', '}') + 1);
        var semi = at;
        for (int depth = 0; semi < code.Length; semi++)
        {
            if (code[semi] is '"' or '\'') { semi = WriteEndpointGuardTests.SkipLiteral(code, semi); continue; }
            if (code[semi] is '(' or '{' or '[') depth++;
            else if (code[semi] is ')' or '}' or ']') depth--;
            else if (code[semi] == ';' && depth == 0) break;
        }
        return (at, Math.Min(semi + 1, code.Length));
    }

    private static int Closing(string code, int open, char o, char c)
    {
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            if (code[i] is '"' or '\'') { i = WriteEndpointGuardTests.SkipLiteral(code, i); continue; }
            if (code[i] == o) depth++;
            else if (code[i] == c && --depth == 0) return i;
        }
        return code.Length - 1;
    }

    /// <summary>يَكتُبُ جِسمُ النُقطَةِ الحِساب: يُنادي الكِتابَةَ الواحِدَةَ أَو <c>AccountContacts.Verify…Async</c> مُباشَرَةً، أَو دالَّةً كاتِبَةً — بِنَوعِها
    /// (<c>Type.Method(</c>، ولِلساكِنَةِ في مِلَفِّ النُقطَةِ بِلا نَوع) أَو عَلى وَسيطٍ مِن نَوعِها في لامدَتِها.</summary>
    internal static bool WritesTheAccount(string body, IReadOnlyList<AccountWriter> writers)
    {
        if (AccountWrite.IsMatch(body) || Regex.IsMatch(body, @"AccountContacts\s*\.\s*Verify\w*Async\s*\(")) return true;
        var receivers = WriteEndpointGuardTests.HandlerParameters(body);
        foreach (var w in writers)
        {
            if (Regex.IsMatch(body, $@"(?<![\w.]){Regex.Escape(w.Type)}\s*\.\s*{Regex.Escape(w.Method)}\s*\(")) return true;
            if (Regex.IsMatch(body, $@"\.\s*{Regex.Escape(w.Type)}\s*\.\s*{Regex.Escape(w.Method)}\s*\(")) return true;
            if (w.Type == "MarketplaceTemplateExtensions" && Regex.IsMatch(body, $@"(?<![\w.]){Regex.Escape(w.Method)}\s*\(")) return true;
            foreach (var (name, type) in receivers)
                if (type == w.Type && Regex.IsMatch(body, $@"(?<![\w.]){Regex.Escape(name)}\s*\.\s*{Regex.Escape(w.Method)}\s*\(")) return true;
        }
        return false;
    }

    /// <summary>دالَّةٌ تَأخُذُ <c>admit</c> تُمَرِّرُه إلى الكِتابَةِ الواحِدَة.</summary>
    internal static bool PassesAdmit(string body)
        => Regex.IsMatch(body, @"AccountWrites\.UpdateAsync<(?:[\w.]+\.)?User>\([\s\S]*\},\s*ct,\s*admit\)");

    /// <summary><b>دَوالُّ تَكتُبُ الحِسابَ ولا تَكتُبُ لِلجَلسَةِ الَّتي يَقرَؤُها البابُ</b> — بِسَبَبِ كُلٍّ؛ وإدخالَةٌ لا يَجِدُها المَسحُ كاتِبَةً تَحمَرّ.</summary>
    internal static readonly (string Writer, string WhyAr)[] SessionlessWriterMethods =
    {
        ("WebPushService.SendAsync",
            "مُرسِلُ الإشعار: يَكتُبُ حِسابَ **المُستَلِم** لا حِسابَ الجَلسَة — يَمحو عَناوينَ قالَت خِدمَةُ الإشعارِ إنَّها انتَهَت؛ ولا جَلسَةَ لِلمُستَلِمِ في الطَلَب (`/offers/{id}/accept` و`/trips/{id}/arrived` يُخبِرانِ غَيرَ الناقِر)."),
        ("AuthHandlers.GetOrCreateUserAsync",
            "كاتِبُ الدُخول: يُصدِرُ الجَلسَةَ ولا يَبني عَلى جَلسَةٍ قائِمَة — رَمزُه الجَديدُ بِإصدارِ الحِسابِ نَفسِه."),
        ("MarketplaceTemplateExtensions.AssignRoleAsync",
            "كاتِبُ الدُخول: يَكتُبُ الدَورَ لِلجَلسَةِ الَّتي صَدَرَت في الطَلَبِ نَفسِه (بَعدَ `WriteCookie`) — لا جَلسَةَ أَقدَمَ مِنها تُسأَل."),
        ("MarketplaceTemplateExtensions.PostLoginRouteAsync",
            "كاتِبُ الدُخول: يُتِمُّ الدَورَ الوَحيدَ لِلجَلسَةِ الَّتي صَدَرَت في الطَلَبِ نَفسِه — نَفسُ حُجَّةِ `AssignRoleAsync`."),
    };

    /// <summary><b>الكُتّابُ بِاسمِ جَلسَةٍ المُستَثنَونَ بِسَبَبِهِم</b> — ونُموُّ القائِمَةِ قَرارٌ مَرئيٌّ في مُراجَعَة، وإدخالَةٌ لا يَجِدُ الفاحِصُ بابَها تَحمَرّ.</summary>
    internal static readonly (string Route, string WhyAr)[] SessionlessWriters =
    {
        ("/{slug}/me/sessions/revoke",
            "«اخرج من كل الأجهزة» — يُنهي الجَلسَةَ ولا يَبني عَلَيها: رَفعُ الإصدارِ ومَحوُ الاشتِراكاتِ يُزيلانِ وُصولاً ولا يَمنَحانِه، و`RevokeTenantAsync` مُتَساوي الأَثَر؛ فَرَدُّ رَفعٍ مِن جَلسَةٍ أُغلِقَت قَبلَه بِلَحظَةٍ لا يُغَيِّرُ شَيئاً (الموجَة ١٢ F1 البَند ٦ يُسَمّيه مُستَثنىً بِالتَصميم)."),
    };

    internal static bool AsksTheSession(string body)
        => body.Contains("admit: u => Services.SessionVersionGate.StillOpen(u, ", StringComparison.Ordinal)
           || Regex.IsMatch(body, @"AccountWrites\.UpdateAsync<User>\([\s\S]*\},\s*ct,\s*admit\);");

    /// <summary>حَقنُ عَيب (القاعِدَة ١٠): نِداءُ اليَومِ بِلا <c>admit</c> — في البابِ وفي الخِدمَة — يُحمِرُّ الفاحِص.</summary>
    [Theory]
    [InlineData("var saved = await AccountWrites.UpdateAsync<User>(store, slug, userId, user =>\n{\n    Keep(user.PushSubscriptions, sub);\n    return true;\n});")]
    [InlineData("var user = await AccountWrites.UpdateAsync<User>(store, slug, userId, u =>\n{\n    ContactLink.Link(u, kind, p.Value, now);\n    return true;\n}, ct);")]
    public void The_session_writer_scan_refuses_a_write_without_admission(string body)
        => Assert.False(AsksTheSession(body));

    /// <summary>حَقنُ عَيب (القاعِدَة ١٠، ‏2026-10-30، الموجَة ١٢ F1 البَند ٢): الحالَتانِ اللَّتانِ كانَتا تَترُكانِ كُلَّ صَفٍّ أَخضَر — بابُ جَلسَةٍ جَديدٌ يُنادي الكِتابَةَ
    /// الواحِدَةَ بِلا <c>admit</c>، ومُنادٍ جَديدٌ لِـ<c>AccountContacts.VerifyAsync</c> بِلا <c>admit</c> — وبابٌ يُنادي دالَّةً كاتِبَةً وَجَدَها المَسح (لا اسمَ لَها
    /// في الصَفّ): الفاحِصُ يَجِدُ كُلّاً كاتِباً بِاسمِ جَلسَةٍ ويَرُدُّه.</summary>
    [Theory]
    [InlineData("var token = AuthSession.ResolveToken(req, slug);\nvar user = await AccountWrites.UpdateAsync<User>(store, slug, userId, u =>\n{\n    u.FullName = name;\n    return true;\n});")]
    [InlineData("var parsed = AuthHandlers.ParseToken(AuthSession.ResolveToken(req, slug));\nvar refusal = await Services.AccountContacts.VerifyAsync(store, slug, parsed.Value.UserId, attempt,\n    req.Form[\"code\"].ToString(), DateTime.UtcNow, phoneChannel, emailChannel, req.HttpContext.RequestAborted);")]
    [InlineData("var parsed = AuthHandlers.ParseToken(AuthSession.ResolveToken(req, slug));\nawait Services.SessionVersionGate.RevokeTenantAsync(store, cache, slug, parsed.Value.UserId);")]
    public void The_session_writer_finder_finds_and_refuses_an_unnamed_writer_without_admission(string body)
    {
        var writers = AccountWriterMethods();
        Assert.Matches(ReadsTheSession, body);
        Assert.True(WritesTheAccount(body, writers), "الفاحِصُ لا يَرى الكاتِب.");
        Assert.False(AsksTheSession(body));
    }

    // ─── I-SEC-1 (تَدقيقُ الموجَة ١١، ‏2026-10-29): كوكيُّ «الاسم» يَحمِلُ رَمزاً عَلى مَسارِ دَورٍ اسمُه name ─────────────────────
    //
    // البَوّابَةُ تُسقِطُ مِن فَحصِها كُلَّ اسمٍ يَنتَهي بِـ«.name» (كوكيُّ اسمِ العَرض)، و`ResolveCookieBase` يَقرَأُ لِمَسارِ
    // `/{slug}/r/name/…` الكوكيَّ `.acommerce.auth.{slug}.name` نَفسَه ويَقبَلُه بِالتَوقيعِ والمَتجَرِ وَحدَهُما — فَرَمزٌ أَسقَطَه
    // «اخرج من كل الأجهزة» يَفتَحُ الصَفحَةَ مِن ذلِكَ الاسم. والمَخزَنُ بَديلٌ يُعيدُ الحِسابَ بِإصدارِه (لا قاعِدَة).

    internal const string NameSlug = "zz-name";

    /// <summary>طَلَبٌ عَلى <paramref name="path"/> بِكوكيّاتِه، ومَخزَنٌ بَديلٌ يُعيدُ <paramref name="account"/> لِلبَوّابَة.</summary>
    internal static DefaultHttpContext GateContext(string path, object? account, params (string Name, string Value)[] cookies)
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton(AccountStub.Store(account));
        services.AddSingleton<Microsoft.Extensions.Caching.Memory.IMemoryCache>(
            new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Request.Path = path;
        if (cookies.Length > 0)
            ctx.Request.Headers.Cookie = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
        return ctx;
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-29، I-SEC-1 — كوميتُ التَبديل)</b> — كانَ
    /// <c>Today_a_revoked_token_under_the_name_cookie_opens_a_role_path_named_name</c> (‏<c>cf0f9bc</c>): «الكوكيُّ المَحلول
    /// ‹.acommerce.auth.zz-name.name› · الجَلسَة تُفتَح». والآن لا يُحَلُّ كوكيُّ «الاسم» جَلسَةً عَلى مَسارٍ، والبَوّابَةُ تَفحَصُ الرَمزَ فيه فَتَنزِعُه.</summary>
    [Fact]
    public async Task A_revoked_token_under_the_name_cookie_opens_no_role_path_and_the_gate_strips_it()
    {
        var user = new User { Id = Guid.NewGuid(), TenantSlug = NameSlug, SessionVersion = 1 };
        var revoked = AuthHandlers.MakeToken(user.Id, NameSlug, 0);
        var ctx = GateContext($"/{NameSlug}/r/name/me/data", user, ($".acommerce.auth.{NameSlug}.name", revoked));

        await ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.ApplyAsync(ctx);
        var resolved = ACommerce.Templates.Customer.Marketplace.AuthSession.ResolveCookieBase(ctx.Request, NameSlug);
        var token = ACommerce.Templates.Customer.Marketplace.AuthSession.ResolveToken(ctx.Request, NameSlug);
        output.WriteLine($"بَعدَ الإصلاح: الكوكيُّ المَحلول «{resolved ?? "—"}» · الجَلسَة {(token is null ? "لا" : "تُفتَح")} · رَأسُ Cookie «{ctx.Request.Headers.Cookie}» · Set-Cookie {ctx.Response.Headers.SetCookie.Count}");
        Assert.Null(resolved);
        Assert.Null(token);
        Assert.DoesNotContain($".acommerce.auth.{NameSlug}.name=", ctx.Request.Headers.Cookie.ToString(), StringComparison.Ordinal);
        Assert.Contains(ctx.Response.Headers.SetCookie, c => c!.StartsWith($".acommerce.auth.{NameSlug}.name=;", StringComparison.Ordinal));
    }

    /// <summary>(أ) وَحدَه: حَتّى رَمزٌ <b>صالِحٌ</b> في كوكيِّ «الاسم» لا يُحَلُّ جَلسَةً عَلى مَسارِ دَورٍ اسمُه <c>name</c> أَو يَنتَهي بِـ<c>.name</c> —
    /// والبَوّابَةُ لا تَنزِعُه (لَم يَسقُط).</summary>
    [Theory]
    [InlineData("name")]
    [InlineData("driver.name")]
    [InlineData("NAME")]
    public async Task A_role_segment_naming_the_name_cookie_resolves_no_session_even_for_a_live_token(string segment)
    {
        var user = new User { Id = Guid.NewGuid(), TenantSlug = NameSlug, SessionVersion = 0 };
        var live = AuthHandlers.MakeToken(user.Id, NameSlug, 0);
        var cookie = ACommerce.Templates.Customer.Marketplace.AuthSession.CookieName(NameSlug, segment.ToLowerInvariant());
        var ctx = GateContext($"/{NameSlug}/r/{segment}/me", user, (cookie, live));
        await ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.ApplyAsync(ctx);
        Assert.Contains($"{cookie}=", ctx.Request.Headers.Cookie.ToString(), StringComparison.Ordinal);
        Assert.Null(ACommerce.Templates.Customer.Marketplace.AuthSession.ResolveCookieBase(ctx.Request, NameSlug));
    }

    /// <summary>(ب) وَحدَه: رَمزٌ ساقِطٌ في كوكيِّ «اسمٍ» لِدَورٍ عادِيٍّ (<c>.driver.name</c>) يُنزَعُ وإن لَم يَقرَأه حَلٌّ — وكوكيُّ الدَورِ نَفسِه الحَيُّ
    /// بِجِوارِه يَبقى.</summary>
    [Fact]
    public async Task The_gate_checks_a_name_cookie_that_holds_a_token_and_leaves_its_live_neighbour()
    {
        var user = new User { Id = Guid.NewGuid(), TenantSlug = NameSlug, SessionVersion = 2 };
        var revoked = AuthHandlers.MakeToken(user.Id, NameSlug, 1);
        var live = AuthHandlers.MakeToken(user.Id, NameSlug, 2);
        var ctx = GateContext($"/{NameSlug}/me", user,
            ($".acommerce.auth.{NameSlug}.driver", live), ($".acommerce.auth.{NameSlug}.driver.name", revoked));
        Assert.Equal(new[] { $".acommerce.auth.{NameSlug}.driver", $".acommerce.auth.{NameSlug}.driver.name" },
            ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.CheckedCookieNames(ctx.Request.Cookies));
        await ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.ApplyAsync(ctx);
        Assert.Equal($".acommerce.auth.{NameSlug}.driver={live}", ctx.Request.Headers.Cookie.ToString());
        Assert.Equal(user.Id, AuthHandlers.ParseToken(
            ACommerce.Templates.Customer.Marketplace.AuthSession.ResolveToken(ctx.Request, NameSlug))!.Value.UserId);
    }

    /// <summary>القَرارُ الواحِد: ما تَعُدُّه البَوّابَةُ جَلسَةً بِاسمِه هُوَ بِعَينِه ما لا يَراه الحَلُّ «اسماً» — عَيِّنَةٌ تَطبَعُ عَدَدَها (القاعِدَة ١٠).</summary>
    [Fact]
    public void The_gate_and_the_resolver_agree_on_which_names_are_name_cookies()
    {
        var sample = new[]
        {
            ".acommerce.auth.ejar", ".acommerce.auth.ejar.name", ".acommerce.auth.ejar.driver", ".acommerce.auth.ejar.driver.name",
            ".acommerce.auth.ejar.name.name", ".acommerce.auth.ejar.names", ".acommerce.studio", ".acommerce.studio.name",
        };
        var sessions = ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.SessionCookieNames(sample);
        output.WriteLine($"عَيِّنَةٌ {sample.Length} · جَلَساتٌ {sessions.Count} · أَسماءُ عَرضٍ {sample.Count(ACommerce.Templates.Customer.Marketplace.AuthSession.IsNameCookie)}");
        Assert.True(sample.Length > 0);
        foreach (var n in sample)
            Assert.Equal(!ACommerce.Templates.Customer.Marketplace.AuthSession.IsNameCookie(n), sessions.Contains(n));
        Assert.True(ACommerce.Templates.Customer.Marketplace.AuthSession.IsNameCookie(".acommerce.auth.ejar.name"));
        Assert.False(ACommerce.Templates.Customer.Marketplace.AuthSession.IsNameCookie(".acommerce.auth.ejar.names"));
    }

    /// <summary>ما لا يَتَغَيَّرُ بِالإصلاح: كوكيُّ اسمِ العَرضِ — قيمَتُه اسمٌ لا رَمز — لا تَمَسُّه البَوّابَة، والجَلسَةُ بِجِوارِه تُحَلُّ
    /// عَلى مَسارِ دَورٍ حَقيقيٍّ وبِلا دَور.</summary>
    [Fact]
    public async Task A_display_name_cookie_is_left_alone_and_a_real_role_path_still_resolves()
    {
        var user = new User { Id = Guid.NewGuid(), TenantSlug = NameSlug, SessionVersion = 0 };
        var live = AuthHandlers.MakeToken(user.Id, NameSlug, 0);
        var header = new[]
        {
            ($".acommerce.auth.{NameSlug}", live), ($".acommerce.auth.{NameSlug}.name", "%D8%B9%D8%B6%D9%88"),
            ($".acommerce.auth.{NameSlug}.driver", live), ($".acommerce.auth.{NameSlug}.driver.name", "%D8%B9%D8%B6%D9%88"),
        };
        foreach (var path in new[] { $"/{NameSlug}/r/driver/me", $"/{NameSlug}/me" })
        {
            var ctx = GateContext(path, user, header);
            var before = ctx.Request.Headers.Cookie.ToString();
            await ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.ApplyAsync(ctx);
            Assert.Equal(before, ctx.Request.Headers.Cookie.ToString());
            Assert.Equal(0, ctx.Response.Headers.SetCookie.Count);
            Assert.Equal(user.Id, AuthHandlers.ParseToken(
                ACommerce.Templates.Customer.Marketplace.AuthSession.ResolveToken(ctx.Request, NameSlug))!.Value.UserId);
        }
    }

    // ─── I-SEC-1 الجَولَةُ الأُولى (مُراجَعَةُ الموجَة ١١، ‏2026-10-30): حالَةُ الأَحرُفِ في اسمِ الكوكيّ ─────────────────────────────
    //
    // `Request.Cookies` يَبحَثُ بِالاسمِ بِلا حالَةِ أَحرُف (مَقيس: `.ACOMMERCE.AUTH.x` يُجيبُ `[".acommerce.auth.x"]`، والمُكَرَّرُ الأَخيرُ يَربَح)،
    // والبَوّابَةُ تَختارُ ما تَفحَصُه وما تَنزِعُه بِمُقارَنَةٍ حَسّاسَة (`Ordinal`). فَرَمزٌ ساقِطٌ تَحتَ اسمٍ بِحالَةٍ أُخرى لا يُفحَص، ويَقرَؤُه كُلُّ قارِئ.

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-30، كوميتُ التَبديل)</b> — كانَ
    /// <c>Today_a_revoked_token_under_an_upper_or_mixed_case_cookie_name_opens_a_session</c> (‏<c>4ef835a</c>): «تَفحَصُ البَوّابَةُ 0 · الجَلسَة تُفتَح ·
    /// Set-Cookie 0». والآن يُفحَصُ ويُنزَعُ ويُمحى بِحالَتِه.</summary>
    [Theory]
    [InlineData(".ACOMMERCE.AUTH.{0}", "/{0}/me")]
    [InlineData(".Acommerce.auth.{0}.driver", "/{0}/r/driver/me")]
    public async Task A_revoked_token_under_an_upper_or_mixed_case_cookie_name_is_checked_stripped_and_deleted(string cookieFormat, string pathFormat)
    {
        var user = new User { Id = Guid.NewGuid(), TenantSlug = NameSlug, SessionVersion = 1 };
        var revoked = AuthHandlers.MakeToken(user.Id, NameSlug, 0);
        var cookie = string.Format(System.Globalization.CultureInfo.InvariantCulture, cookieFormat, NameSlug);
        var ctx = GateContext(string.Format(System.Globalization.CultureInfo.InvariantCulture, pathFormat, NameSlug), user, (cookie, revoked));
        var checkedNames = ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.CheckedCookieNames(ctx.Request.Cookies);
        await ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.ApplyAsync(ctx);
        var token = ACommerce.Templates.Customer.Marketplace.AuthSession.ResolveToken(ctx.Request, NameSlug);
        output.WriteLine($"بَعدَ التَبديل: «{cookie}» · تَفحَصُ البَوّابَةُ {checkedNames.Count} · الجَلسَة {(token is null ? "لا" : "تُفتَح")} · Set-Cookie {ctx.Response.Headers.SetCookie.Count}");
        Assert.Equal(new[] { cookie }, checkedNames);
        Assert.Null(token);
        Assert.Contains(ctx.Response.Headers.SetCookie, c => c!.StartsWith($"{cookie}=;", StringComparison.Ordinal));
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه</b> — كانَ <c>Today_a_shadowed_case_duplicate_of_a_revoked_cookie_appears_after_the_gate_strips_the_first</c>
    /// (‏<c>4ef835a</c>): «الجَلسَة تُفتَح بِالإصدار 1». والآن تُنزَعُ النُسخَتانِ مَعاً مِن الطَلَب، ويُمحى الاسمُ مِن المُتَصَفِّح.</summary>
    /// <remarks>مَقيس: <c>Response.Cookies.Delete</c> يُبدِلُ <c>Set-Cookie</c> سابِقاً لِلاسمِ نَفسِه بِلا حالَةِ أَحرُف، فَرَدٌّ واحِدٌ يَمحو إحدى الصورَتَين؛
    /// والأُخرى تُنزَعُ مِن كُلِّ طَلَبٍ وتُمحى في الرَدِّ التالي — والمُتَصَفِّحُ الَّذي يَحمِلُهُما مُتَصَفِّحُ حامِلِ الرَمزِ نَفسِه.</remarks>
    [Fact]
    public async Task A_shadowed_case_duplicate_of_a_revoked_cookie_is_stripped_with_it()
    {
        var user = new User { Id = Guid.NewGuid(), TenantSlug = NameSlug, SessionVersion = 2 };
        var hidden = AuthHandlers.MakeToken(user.Id, NameSlug, 1);
        var seen = AuthHandlers.MakeToken(user.Id, NameSlug, 0);
        var ctx = GateContext($"/{NameSlug}/me", user, ($".ACOMMERCE.AUTH.{NameSlug}", hidden), ($".acommerce.auth.{NameSlug}", seen));
        await ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.ApplyAsync(ctx);
        var token = ACommerce.Templates.Customer.Marketplace.AuthSession.ResolveToken(ctx.Request, NameSlug);
        output.WriteLine($"بَعدَ التَبديل: رَأسُ Cookie «{ctx.Request.Headers.Cookie}» · الجَلسَة {(token is null ? "لا" : "تُفتَح")} · Set-Cookie {ctx.Response.Headers.SetCookie.Count}");
        Assert.Null(token);
        Assert.Equal("", ctx.Request.Headers.Cookie.ToString());
        Assert.Contains(ctx.Response.Headers.SetCookie, c => c!.StartsWith($".acommerce.auth.{NameSlug}=;", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه</b> — كانَ <c>Today_a_revoked_studio_token_under_an_upper_case_cookie_name_opens_the_studio</c> (‏<c>4ef835a</c>):
    /// «‹.ACOMMERCE.STUDIO› تُفتَح». والآن لا يَفتَحُ الاستوديو بِأَيِّ حالَة.</summary>
    [Fact]
    public async Task A_revoked_studio_token_opens_the_studio_under_no_case_of_its_name()
    {
        var studio = new ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioUser { Id = Guid.NewGuid(), SessionVersion = 1 };
        var revoked = AuthHandlers.MakeToken(studio.Id, ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioAuth.Tenant, 0);
        var opened = new Dictionary<string, bool>();
        foreach (var name in new[] { ".acommerce.studio", ".ACOMMERCE.STUDIO", ".Acommerce.Studio" })
        {
            var ctx = GateContext("/studio", studio, (name, revoked));
            await ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.ApplyAsync(ctx);
            var auth = new ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioAuth(new HttpContextAccessor { HttpContext = ctx });
            auth.Load();
            opened[name] = auth.IsAuthenticated;
        }
        output.WriteLine($"بَعدَ التَبديل: {string.Join(" · ", opened.Select(o => $"«{o.Key}» {(o.Value ? "تُفتَح" : "لا")}"))}");
        Assert.Equal(3, opened.Count);
        Assert.All(opened.Values, Assert.False);
    }

    /// <summary>المُوجَب «أَيّاً كانَ اسمُه»: رَمزٌ ساقِطٌ في كوكيٍّ خارِجَ العائِلَة، وفي كوكيِّ «اسمٍ» بِحالَةٍ مُختَلِطَة — يُنزَعانِ، والجَلسَةُ الحَيَّةُ بِجِوارِهِما
    /// تَبقى بِرَمزِها.</summary>
    [Fact]
    public async Task A_revoked_token_under_any_other_name_is_stripped_and_the_live_session_beside_it_stays()
    {
        var user = new User { Id = Guid.NewGuid(), TenantSlug = NameSlug, SessionVersion = 2 };
        var revoked = AuthHandlers.MakeToken(user.Id, NameSlug, 1);
        var live = AuthHandlers.MakeToken(user.Id, NameSlug, 2);
        var ctx = GateContext($"/{NameSlug}/me", user,
            ("zz_other", revoked), ($".acommerce.auth.{NameSlug}.driver.NAME", revoked), ($".acommerce.auth.{NameSlug}", live));
        await ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.ApplyAsync(ctx);
        output.WriteLine($"رَأسُ Cookie بَعدَ البَوّابَة: «{ctx.Request.Headers.Cookie.ToString().Split('=')[0]}=…» · Set-Cookie {ctx.Response.Headers.SetCookie.Count}");
        Assert.Equal($".acommerce.auth.{NameSlug}={live}", ctx.Request.Headers.Cookie.ToString());
        Assert.Equal(live, ACommerce.Templates.Customer.Marketplace.AuthSession.ResolveToken(ctx.Request, NameSlug));
    }

    /// <summary>السالِبان: كوكيُّ اسمِ عَرضٍ بِحالَةٍ مُختَلِطَةٍ قيمَتُه اسمٌ لا رَمز، ورَمزٌ <b>حَيٌّ</b> تَحتَ اسمٍ بِحُروفٍ كَبيرَة — لا يَمَسُّهُما شَيء.</summary>
    [Fact]
    public async Task A_mixed_case_display_name_cookie_and_a_live_upper_case_token_are_left_alone()
    {
        var user = new User { Id = Guid.NewGuid(), TenantSlug = NameSlug, SessionVersion = 0 };
        var live = AuthHandlers.MakeToken(user.Id, NameSlug, 0);
        var ctx = GateContext($"/{NameSlug}/me", user,
            ($".ACOMMERCE.AUTH.{NameSlug}", live), ($".Acommerce.Auth.{NameSlug}.NAME", "%D8%B9%D8%B6%D9%88"));
        var before = ctx.Request.Headers.Cookie.ToString();
        await ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.ApplyAsync(ctx);
        Assert.Equal(before, ctx.Request.Headers.Cookie.ToString());
        Assert.Equal(0, ctx.Response.Headers.SetCookie.Count);
        Assert.Equal(live, ACommerce.Templates.Customer.Marketplace.AuthSession.ResolveToken(ctx.Request, NameSlug));
        Assert.True(ACommerce.Templates.Customer.Marketplace.AuthSession.IsNameCookie($".Acommerce.Auth.{NameSlug}.NAME"));
    }

    /// <summary>الاحتِياطُ المُغلَق: رَأسا <c>Cookie</c> (يَجمَعُهُما <c>ToString</c> بِفاصِلَة) والرَمزُ الساقِطُ في الثاني — التَقسيمُ بِـ«;» لا يَجِدُه فَلا يُغَيِّرُ
    /// النَزعُ شَيئاً، فَيُمحى الرَأسُ كُلُّه ولا تُفتَحُ جَلسَة.</summary>
    /// <remarks>
    /// <para><b>(‏2026-10-30، الموجَة ١٢ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) هذا الصَفُّ تَغطِيَةُ كودٍ دِفاعيٍّ، لا قِياسٌ لِلمَحو</b>: أَخضَرُ عَلى البَوّابَةِ قَبلَ
    /// <c>5d858d0</c> وعَلى نُسخَةٍ فَرعُ المَحوِ فيها «يَضَعُ المَنزوعَ ويَخرُج» — لِأَنّ مُحَلِّلَ القارِئِ نَفسَه يَرُدُّ الرَأسَ المَجموعَ بِفاصِلَة (قيمَةُ الكوكيِّ لا تَحمِلُ
    /// <c>,</c> فَيُترَكُ الباقي إلى «;» لا تَأتي)، فَيَختَفي الرَمزُ أَمُحِيَ الرَأسُ أَم لا. والسَطرُ الأَخيرُ يَقيسُ تِلكَ الآلِيَّةَ بِعَينِها. ولِلمَحوِ <b>لا حالَةَ
    /// مَقيسَةً تَبلُغُه</b>: بَحثٌ مَحدودٌ بِثَلاثَةٍ وثَلاثينَ رَأساً (القائِمَةُ في جِسمِ الكوميت) خَرَجَ مِنه كُلُّها بِلا رَمزٍ ساقِطٍ عَلى النُسخَتَين.</para>
    /// </remarks>
    [Fact]
    public async Task A_cookie_header_the_strip_cannot_change_is_dropped_whole()
    {
        var user = new User { Id = Guid.NewGuid(), TenantSlug = NameSlug, SessionVersion = 1 };
        var revoked = AuthHandlers.MakeToken(user.Id, NameSlug, 0);
        var ctx = GateContext($"/{NameSlug}/me", user);
        ctx.Request.Headers.Cookie = new Microsoft.Extensions.Primitives.StringValues(new[] { "ac_lang=ar", $".acommerce.auth.{NameSlug}={revoked}" });
        Assert.Equal(revoked, ctx.Request.Cookies[$".acommerce.auth.{NameSlug}"]);
        await ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.ApplyAsync(ctx);
        output.WriteLine($"رَأسانِ: بَعدَ البَوّابَة كوكيّات {ctx.Request.Cookies.Count} · Set-Cookie {ctx.Response.Headers.SetCookie.Count}");
        Assert.Equal(0, ctx.Request.Cookies.Count);
        Assert.Null(ACommerce.Templates.Customer.Marketplace.AuthSession.ResolveToken(ctx.Request, NameSlug));
        Assert.Contains(ctx.Response.Headers.SetCookie, c => c!.StartsWith($".acommerce.auth.{NameSlug}=;", StringComparison.Ordinal));

        var joined = GateContext($"/{NameSlug}/me", user);
        joined.Request.Headers.Cookie = $"ac_lang=ar,.acommerce.auth.{NameSlug}={revoked}";
        output.WriteLine($"الرَأسُ مَجموعاً بِفاصِلَةٍ عِندَ القارِئِ بِلا بَوّابَة: كوكيّات {joined.Request.Cookies.Count} — فَالرَمزُ يَختَفي بِلا المَحو، والصَفُّ تَغطِيَةٌ دِفاعِيَّة");
        Assert.Equal(0, joined.Request.Cookies.Count);
    }

    /// <summary>
    /// <para><b>الشَرطُ بِمُحَلِّلِ القارِئِ نَفسِه (القاعِدَة ١٠)</b>: لِكُلِّ رَأسٍ مَحقونٍ يَحمِلُ رَمزاً ساقِطاً بِشَكلٍ ما — حالاتُ الأَحرُف، والمُكَرَّرُ بِتَرتيبَيه،
    /// وكوكيُّ «الاسم»، والاستوديو، والفَراغاتُ والفَواصِلُ المَنقوطَةُ الزائِدَة — لا يَبقى بَعدَ البَوّابَةِ في <c>Request.Cookies</c> كوكيٌّ رَمزُه ساقِط.
    /// والعَيِّنَةُ تَطبَعُ عَدَدَها وتَفشَلُ إن كانَت صِفراً، وكُلُّ رَأسٍ يَفتَحُ الجَلسَةَ قَبلَ البَوّابَةِ (فَلا تُقاسُ عَيِّنَةٌ لا تُحرِجُها).</para>
    /// </summary>
    [Fact]
    public async Task After_the_gate_no_reader_sees_a_revoked_token_in_any_injected_header()
    {
        var user = new User { Id = Guid.NewGuid(), TenantSlug = NameSlug, SessionVersion = 3 };
        var a = AuthHandlers.MakeToken(user.Id, NameSlug, 1);
        var b = AuthHandlers.MakeToken(user.Id, NameSlug, 2);
        var g = $".acommerce.auth.{NameSlug}";
        var headers = new[]
        {
            $"{g}={a}", $"{g.ToUpperInvariant()}={a}", $".Acommerce.Auth.{NameSlug}={a}",
            $"{g.ToUpperInvariant()}={a}; {g}={b}", $"{g}={a}; {g.ToUpperInvariant()}={b}",
            $"{g}.driver={a}; {g}.DRIVER={b}", $"{g}.name={a}", $"{g}.NAME={a}; {g}.name={b}",
            $"ac_lang=ar;;; \t{g}={a}", $"zz={a}; {g}={b}",
        };
        var measured = 0;
        foreach (var header in headers)
        {
            var ctx = GateContext($"/{NameSlug}/me", user);
            ctx.Request.Headers.Cookie = header;
            Assert.Contains(ctx.Request.Cookies, c => AuthHandlers.SessionVersionOf(c.Value) is int v && v < user.SessionVersion);
            await ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.ApplyAsync(ctx);
            var left = ctx.Request.Cookies.Where(c => AuthHandlers.SessionVersionOf(c.Value) is int v && v < user.SessionVersion).Select(c => c.Key).ToList();
            Assert.True(left.Count == 0, $"«{header.Split('=')[0]}…» تَرَكَ رَمزاً ساقِطاً تَحتَ {string.Join(", ", left)}.");
            Assert.Null(ACommerce.Templates.Customer.Marketplace.AuthSession.ResolveToken(ctx.Request, NameSlug));
            measured++;
        }
        output.WriteLine($"رُؤوسٌ مَحقونَةٌ مَقيسَة: {measured} · كُلُّها بِلا رَمزٍ ساقِطٍ بَعدَ البَوّابَة");
        Assert.True(measured > 0);
    }

    /// <summary>القَرارُ الواحِدُ بِحالاتِ الأَحرُف (القاعِدَة ١٠، عَيِّنَةٌ مَحقونَة): ما يَراه <c>Request.Cookies</c> تَحتَ الاسمِ القانونيِّ لِجَلسَةٍ أَو لِاسمِ عَرض،
    /// تَعُدُّه البَوّابَةُ كَذلِك — والعَيِّنَةُ تَطبَعُ عَدَدَها.</summary>
    [Fact]
    public void The_gate_and_the_reader_agree_on_session_and_name_cookies_whatever_the_case()
    {
        var sample = new (string Name, string Canonical, bool Session)[]
        {
            (".ACOMMERCE.AUTH.ejar", ".acommerce.auth.ejar", true), (".Acommerce.auth.ejar.driver", ".acommerce.auth.ejar.driver", true),
            (".ACOMMERCE.STUDIO", ".acommerce.studio", true), (".Acommerce.Studio", ".acommerce.studio", true),
            (".acommerce.auth.ejar.NAME", ".acommerce.auth.ejar.name", false), (".ACOMMERCE.AUTH.EJAR.DRIVER.Name", ".acommerce.auth.ejar.driver.name", false),
            (".ACOMMERCE.STUDIO.NAME", ".acommerce.studio.name", false),
        };
        foreach (var (name, canonical, session) in sample)
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Headers.Cookie = $"{name}=v";
            Assert.Equal("v", ctx.Request.Cookies[canonical]);
            Assert.Equal(!session, ACommerce.Templates.Customer.Marketplace.AuthSession.IsNameCookie(name));
            Assert.Equal(session, ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.SessionCookieNames(new[] { name }).Count == 1);
        }
        output.WriteLine($"عَيِّنَةُ الحالات: {sample.Length} · جَلَساتٌ {sample.Count(x => x.Session)} · أَسماءُ عَرضٍ {sample.Count(x => !x.Session)}");
        Assert.True(sample.Length > 0);
    }

    // ─── A4 (تَدقيقُ الموجَة ١١، ‏2026-10-29): حِسابٌ حُذِفَ قَبلَ إصدارِ الجَلسَةِ يَبقى رَمزُه ‏0 صالِحاً ───────────────────────────
    //
    // الحَذفُ يَرفَعُ الإصدارَ مُنذُ `2e65882` (د28)، والبَوّابَةُ تُقارِنُ إصدارَ الرَمزِ بِإصدارِ الحِسابِ وَحدَه — فَحِسابٌ حُذِفَ قَبلَها إصدارُه
    // المُخَزَّنُ ‏0، ورَمزُه القَديمُ (بِلا إصدار = ‏0) لَيسَ أَصغَرَ مِنه: يَعبُرُ البَوّابَةَ ثَلاثينَ يَوماً مِن إصدارِه.

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-29، A4 — كوميتُ التَبديل)</b> — كانَ
    /// <c>Today_a_version_zero_token_of_an_account_deleted_before_session_versions_passes_the_gate</c> (‏<c>ca9e8b3</c>): «حِسابٌ مَحذوفٌ بِإصدارِ 0 ·
    /// رَمزُه 0 يَعبُر · Set-Cookie 0». والآن المَحذوفُ إصدارُه في البَوّابَةِ <see cref="int.MaxValue"/>، فَيُنزَعُ رَمزُه ويُمحى مِن المُتَصَفِّح —
    /// بِأَيِّ إصدارٍ كانَ الرَمزُ أَو الحِساب.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(0, 5)]
    public async Task A_deleted_account_token_is_stripped_whatever_its_version(int accountVersion, int tokenVersion)
    {
        var user = new User { Id = Guid.NewGuid(), TenantSlug = NameSlug, SessionVersion = accountVersion, DeletedAt = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc) };
        var token = AuthHandlers.MakeToken(user.Id, NameSlug, tokenVersion);
        var ctx = GateContext($"/{NameSlug}/me", user, ($".acommerce.auth.{NameSlug}", token));
        await ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.ApplyAsync(ctx);
        var resolved = ACommerce.Templates.Customer.Marketplace.AuthSession.ResolveToken(ctx.Request, NameSlug);
        output.WriteLine($"بَعدَ التَبديل: حِسابٌ مَحذوفٌ بِإصدارِ {accountVersion} · رَمزُه {tokenVersion} {(resolved is null ? "يُنزَع" : "يَعبُر")} · Set-Cookie {ctx.Response.Headers.SetCookie.Count}");
        Assert.Null(resolved);
        Assert.Contains(ctx.Response.Headers.SetCookie, c => c!.StartsWith($".acommerce.auth.{NameSlug}=;", StringComparison.Ordinal));
    }

    /// <summary>السالِب: حِسابٌ حَيٌّ بِإصدارِ ‏0 ورَمزُه ‏0 — يَعبُرُ كَما كان، ولا يُمحى شَيء.</summary>
    [Fact]
    public async Task A_live_version_zero_account_token_still_passes()
    {
        var user = new User { Id = Guid.NewGuid(), TenantSlug = NameSlug, SessionVersion = 0 };
        var token = AuthHandlers.MakeToken(user.Id, NameSlug, 0);
        var ctx = GateContext($"/{NameSlug}/me", user, ($".acommerce.auth.{NameSlug}", token));
        await ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.ApplyAsync(ctx);
        Assert.Equal(user.Id, AuthHandlers.ParseToken(ACommerce.Templates.Customer.Marketplace.AuthSession.ResolveToken(ctx.Request, NameSlug))!.Value.UserId);
        Assert.Equal(0, ctx.Response.Headers.SetCookie.Count);
    }

    /// <summary>الدالَّةُ النَقِيَّة: لا وَثيقَة ← <c>null</c> (لا يُسقِطُ شَيئاً)؛ مَحذوف ← <see cref="int.MaxValue"/>؛ حَيّ ← إصدارُه.</summary>
    [Fact]
    public void The_gate_version_is_null_without_an_account_max_when_deleted_and_the_stored_one_otherwise()
    {
        Assert.Null(ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.GateVersion(null));
        Assert.Equal(int.MaxValue, ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.GateVersion(new User { SessionVersion = 2, DeletedAt = DateTime.UtcNow }));
        Assert.Equal(2, ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.GateVersion(new User { SessionVersion = 2 }));
        Assert.True(ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.IsRevoked(int.MaxValue - 1, int.MaxValue));
    }
}

/// <summary>
/// <para><b>مَخزَنٌ بَديلٌ لِلبَوّابَة</b> — <c>QuerySession(tenant).LoadAsync&lt;T&gt;(id)</c> يُعيدُ الحِسابَ المُعطى إن كانَ مِن نَوعِه ومُعَرِّفُه
/// المَطلوب، وإلّا <c>null</c>؛ وكُلُّ نِداءٍ آخَرَ يَرمي، فَلا يَمُرُّ شَيءٌ بِلا قَصد. صُفوفُ البَوّابَةِ تُقاسُ بِه بِلا قاعِدَة.</para>
/// </summary>
/// <summary>
/// <para><b>مَخزَنُ حِسابٍ بِقِراءاتٍ مُرَتَّبَة</b> (‏2026-10-30، الموجَة ١٢ F1 البَند ٧): كُلُّ <c>LoadAsync&lt;User&gt;</c> يُعيدُ الحِسابَ التالي مِن القائِمَة —
/// القِراءَةُ الأُولى «قَبلَ الرَفع» والثانِيَةُ «بَعدَه» — و<c>AfterRead</c> يَجري بَعدَ أَن تَثبُتَ قيمَةُ القِراءَةِ وقَبلَ أَن تَعودَ إلى المُنادي: فَـ«الرَفعُ ثُمَّ
/// <c>Forget</c>» يَقَعُ بَينَ القِراءَةِ والحِفظِ في الذاكِرَةِ تَرتيباً حَتمِيّاً لا حَظّاً.</para>
/// </summary>
public class ScriptedAccountStore : System.Reflection.DispatchProxy
{
    private ScriptedAccountStore _root = null!;
    private Queue<User> _reads = new();
    private Action<int>? _afterRead;

    /// <summary>عَدَدُ القِراءاتِ الَّتي بَلَغَت المَخزَن.</summary>
    public int Reads { get; private set; }

    public static (Marten.IDocumentStore Store, ScriptedAccountStore Script) Of(Action<int>? afterRead, params User[] reads)
    {
        var proxy = Create<Marten.IDocumentStore, ScriptedAccountStore>();
        var script = (ScriptedAccountStore)(object)proxy;
        script._root = script;
        script._reads = new Queue<User>(reads);
        script._afterRead = afterRead;
        return (proxy, script);
    }

    protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args)
    {
        switch (method!.Name)
        {
            case "QuerySession":
                var session = Create<Marten.IQuerySession, ScriptedAccountStore>();
                ((ScriptedAccountStore)(object)session)._root = _root;
                return session;
            case "DisposeAsync": return ValueTask.CompletedTask;
            case "Dispose": return null;
            case "LoadAsync" when method.IsGenericMethod && method.GetGenericArguments()[0] == typeof(User):
                var read = _root._reads.Dequeue();
                _root.Reads++;
                _root._afterRead?.Invoke(_root.Reads);
                return Task.FromResult<User?>(read);
            default: throw new NotSupportedException($"المَخزَنُ المُرَتَّبُ لا يُجيبُ {method.Name}.");
        }
    }
}

public class AccountStub : System.Reflection.DispatchProxy
{
    private object? _account;

    public static Marten.IDocumentStore Store(object? account)
    {
        var proxy = Create<Marten.IDocumentStore, AccountStub>();
        ((AccountStub)(object)proxy)._account = account;
        return proxy;
    }

    private static Marten.IQuerySession Session(object? account)
    {
        var proxy = Create<Marten.IQuerySession, AccountStub>();
        ((AccountStub)(object)proxy)._account = account;
        return proxy;
    }

    protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args)
    {
        switch (method!.Name)
        {
            case "QuerySession": return Session(_account);
            case "DisposeAsync": return ValueTask.CompletedTask;
            case "Dispose": return null;
            case "LoadAsync" when method.IsGenericMethod && args is { Length: > 0 }:
                var type = method.GetGenericArguments()[0];
                var id = args[0];
                var hit = _account is not null && type.IsInstanceOfType(_account)
                          && Equals(type.GetProperty("Id")?.GetValue(_account), id) ? _account : null;
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(type).Invoke(null, new[] { hit });
            default: throw new NotSupportedException($"المَخزَنُ البَديلُ لا يُجيبُ {method.Name}.");
        }
    }
}

// ═══ البُرهانُ الحَيّ — «اخرج من كل الأجهزة» عَلى Postgres (‏WASAYEL_LIVE_PROOF=1، ‏2026-10-28، د28) ═══
//
// حِسابٌ يَدخُلُ بِالجَوّالِ مِن «جِهازَين» (رَمزانِ مِن المُعالِجِ نَفسِه)، ثُمَّ يَرفَعُ أَحَدُهُما الإصدار — فَيَسقُطُ الرَمزانِ عِندَ البَوّابَة، ودُخولٌ جَديدٌ
// بَعدَه يَصدُرُ بِالإصدارِ الجَديدِ فَيَمُرّ. ونَظيرُه لِحِسابِ الاستوديو. مُستَأجِرٌ مُؤَقَّتٌ يُمسَحُ بَعدَه.

[Collection(LiveProofCollection.Name)]
public class LiveSessionRevocationProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private static async Task<AuthResult> SignInAsync(Marten.IDocumentStore store, string slug, string phone)
    {
        const string code = "424242";
        AuthHandlers.IssueAttempt(slug, phone, code, AuthHandlers.AuthKind.PhoneOtp);
        return (await AuthHandlers.VerifyPhoneOtpHandler(new VerifyPhoneOtp(phone, code), new VendorRegisterBucketTests.ResolvedTenant(slug), store))!;
    }

    [Fact]
    public async Task Signing_out_everywhere_revokes_both_devices_and_a_fresh_sign_in_passes_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var slug = "sv-zz-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var phoneA = await SignInAsync(store, slug, "966500000280");
            var phoneB = await SignInAsync(store, slug, "966500000280");
            Assert.Equal(phoneA.UserId, phoneB.UserId);
            var before = await ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.AccountVersionAsync(store, cache, slug, phoneA.UserId);

            Assert.True(await ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.RevokeTenantAsync(store, cache, slug, phoneA.UserId));
            var after = await ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.AccountVersionAsync(store, cache, slug, phoneA.UserId);
            var fresh = await SignInAsync(store, slug, "966500000280");

            bool Revoked(AuthResult r) => ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.IsRevoked(AuthHandlers.SessionVersionOf(r.Token)!.Value, after);
            output.WriteLine($"الإصدار: {before} ← {after} · الجِهازُ أ: {(Revoked(phoneA) ? "ساقِط" : "صالِح")} · الجِهازُ ب: {(Revoked(phoneB) ? "ساقِط" : "صالِح")} · دُخولٌ جَديد: {(Revoked(fresh) ? "ساقِط" : "صالِح")}");
            Assert.Equal((0, 1), (before, after));
            Assert.True(Revoked(phoneA) && Revoked(phoneB));
            Assert.False(Revoked(fresh));
        }
        finally { await LiveZeroRoleAdminProofTests.CleanAsync(store, slug); }
    }

    /// <summary>(‏2026-10-29، I-SEC-2) الرَفعُ يَمحو اشتِراكاتِ صاحِبِه في الحِفظِ نَفسِه (مُوجَب) ويَترُكُ اشتِراكاتِ حِسابٍ آخَرَ في المَتجَرِ نَفسِه (سالِب).</summary>
    [Fact]
    public async Task Signing_out_everywhere_clears_its_own_push_subscriptions_and_no_one_elses_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var slug = "sv-zz-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var a = new User { Id = Guid.NewGuid(), TenantSlug = slug, Phone = "+966500000281" };
            var b = new User { Id = Guid.NewGuid(), TenantSlug = slug, Phone = "+966500000282" };
            a.PushSubscriptions.Add(new ACommerce.Kit.Auth.PushSubscription { Endpoint = "https://fcm.googleapis.com/fcm/send/a", P256dh = "p", Auth = "x" });
            b.PushSubscriptions.Add(new ACommerce.Kit.Auth.PushSubscription { Endpoint = "https://fcm.googleapis.com/fcm/send/b", P256dh = "p", Auth = "x" });
            await using (var s = store.LightweightSession(slug)) { s.Store(a); s.Store(b); await s.SaveChangesAsync(); }

            Assert.True(await ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.RevokeTenantAsync(store, cache, slug, a.Id));
            await using var q = store.QuerySession(slug);
            var a2 = await q.LoadAsync<User>(a.Id);
            var b2 = await q.LoadAsync<User>(b.Id);
            output.WriteLine($"بَعدَ رَفعِ أ: اشتِراكاتُ أ {a2!.PushSubscriptions.Count} وإصدارُه {a2.SessionVersion} · اشتِراكاتُ ب {b2!.PushSubscriptions.Count} وإصدارُه {b2.SessionVersion}");
            Assert.Equal((0, 1), (a2.PushSubscriptions.Count, a2.SessionVersion));
            Assert.Equal((1, 0), (b2.PushSubscriptions.Count, b2.SessionVersion));
        }
        finally { await LiveZeroRoleAdminProofTests.CleanAsync(store, slug); }
    }

    [Fact]
    public async Task A_studio_account_signing_out_everywhere_revokes_its_earlier_token_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var user = new ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioUser { Id = Guid.NewGuid(), Phone = "zz-" + Guid.NewGuid().ToString("N")[..8] };
        await using (var s = store.LightweightSession(ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioAuth.Tenant)) { s.Store(user); await s.SaveChangesAsync(); }
        try
        {
            var token = AuthHandlers.MakeToken(user.Id, ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioAuth.Tenant, user.SessionVersion);
            Assert.True(await ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.RevokeStudioAsync(store, cache, user.Id));
            var after = await ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.AccountVersionAsync(store, cache, ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioAuth.Tenant, user.Id);
            var revoked = ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.IsRevoked(AuthHandlers.SessionVersionOf(token)!.Value, after);
            output.WriteLine($"حِسابُ الاستوديو: الإصدارُ ← {after} · رَمزُه السابِق {(revoked ? "ساقِط" : "صالِح")}");
            Assert.Equal(1, after);
            Assert.True(revoked);
        }
        finally
        {
            await using var s = store.LightweightSession(ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioAuth.Tenant);
            s.Delete<ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioUser>(user.Id);
            await s.SaveChangesAsync();
        }
    }
}

// ═══ G-1 (تَدقيقُ الموجَة ١١، ‏2026-10-29) — الكُتّابُ المُتَزامِنونَ لِوَثيقَةِ الحِساب عَلى Postgres (‏WASAYEL_LIVE_PROOF=1) ═══
//
// `User` وَثيقَةٌ واحِدَةٌ يَكتُبُها أَبوابٌ كَثيرَةٌ كامِلَةً (تَحميلٌ ← تَعديلٌ ← `Store`) بِلا فَحصِ نُسخَة — والأَخيرُ يَربَح. فَطَلَبُ اشتِراكٍ
// قَرَأَ الحِسابَ قَبلَ «اخرج من كل الأجهزة» ويَحفَظُ بَعدَه يُعيدُ الإصدارَ إلى ما قَرَأ، ونُسخَةٌ قُرِئَت قَبلَ الحَذفِ تُعيدُ الحِسابَ المَمحُوّ.
// **والمَخزَنُ مَخزَنُ المُضيفِ بِعَينِه** (‏`AddPlatformHost` + `AddCustomerMarketplaceTemplate`، تَركيبُ Production) لا نُسخَةٌ مِنه — فَما يُقاسُ هُنا
// هُوَ ما يُشَغَّل (القاعِدَة ٢). مُستَأجِرٌ مُؤَقَّتٌ يُمسَحُ بَعدَه.

[Collection(LiveProofCollection.Name)]
public class LiveAccountConcurrencyProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>مَخزَنُ المُضيف: النِداءانِ اللَذانِ يُناديهِما <c>apps/V1.App/Program.cs</c>، عَلى القاعِدَةِ الحَيَّة.</summary>
    internal static Microsoft.AspNetCore.Builder.WebApplication HostApp()
    {
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(new Microsoft.AspNetCore.Builder.WebApplicationOptions
        {
            EnvironmentName = Microsoft.Extensions.Hosting.Environments.Production,
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Configuration["ConnectionStrings:Postgres"] = LiveDealConcurrencyProofTests.ConnectionString!.TrimEnd(';') + ";Timeout=60;Command Timeout=120";
        builder.Host.UseDefaultServiceProvider(o => { o.ValidateOnBuild = false; o.ValidateScopes = false; });
        ACommerce.Platform.Hosting.HostingExtensions.AddPlatformHost(builder);
        ACommerce.Templates.Customer.Marketplace.MarketplaceTemplateExtensions.AddCustomerMarketplaceTemplate(builder.Services);
        return builder.Build();
    }

    private static Microsoft.Extensions.Caching.Memory.MemoryCache Cache()
        => new(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());

    private static async Task<User> SeedAsync(Marten.IDocumentStore store, string slug)
    {
        var user = new User { Id = Guid.NewGuid(), TenantSlug = slug, Phone = "+966500000290", FullName = "صاحِبُ الحِساب", PhoneVerified = true };
        user.PushSubscriptions.Add(new ACommerce.Kit.Auth.PushSubscription { Endpoint = "https://fcm.googleapis.com/fcm/send/old", P256dh = "p", Auth = "a" });
        await using var s = store.LightweightSession(slug);
        s.Store(user);
        await s.SaveChangesAsync();
        return user;
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-29، G-1 — كوميتُ التَبديل)</b> — كانَ
    /// <c>Today_a_subscribe_that_loaded_before_the_revoke_writes_the_old_version_back_on_Postgres</c> (‏<c>9f887fa</c>): «الحِفظُ القَديمُ حُفِظ ·
    /// الإصدارُ بَعدَه 0 · رَمزُ ما قَبلَ الرَفع صالِحٌ مِن جَديد». والآن شَكلُ <c>/api/{slug}/push/subscribe</c> القَديمُ (‏<c>Load → Keep → Store</c>)
    /// يُرَدُّ بِـ<c>ConcurrencyException</c> والإصدارُ قائِم.</summary>
    [Fact]
    public async Task A_subscribe_that_loaded_before_the_revoke_is_refused_and_the_version_stands_on_Postgres()
    {
        if (!Enabled) return;
        await using var app = HostApp();
        var store = app.Services.GetRequiredService<Marten.IDocumentStore>();
        var slug = "cc-zz-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var user = await SeedAsync(store, slug);
            var token = AuthHandlers.MakeToken(user.Id, slug, user.SessionVersion);

            await using var stale = store.LightweightSession(slug);
            var copy = await stale.LoadAsync<User>(user.Id);
            Assert.True(await ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.RevokeTenantAsync(store, Cache(), slug, user.Id));
            ACommerce.Templates.Customer.Marketplace.Services.PushSubscriptionDoor.Keep(copy!.PushSubscriptions,
                new ACommerce.Kit.Auth.PushSubscription { Endpoint = "https://fcm.googleapis.com/fcm/send/thief", P256dh = "p", Auth = "a" });
            stale.Store(copy);
            string outcome;
            try { await stale.SaveChangesAsync(); outcome = "حُفِظ"; }
            catch (JasperFx.ConcurrencyException) { outcome = "رُدَّ (ConcurrencyException)"; }

            await using var q = store.QuerySession(slug);
            var after = (await q.LoadAsync<User>(user.Id))!;
            var revoked = ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.IsRevoked(AuthHandlers.SessionVersionOf(token)!.Value, after.SessionVersion);
            output.WriteLine($"بَعدَ التَبديل: الحِفظُ القَديمُ {outcome} · الإصدارُ بَعدَه {after.SessionVersion} · رَمزُ ما قَبلَ الرَفع {(revoked ? "ساقِط" : "صالِحٌ مِن جَديد")} · الاشتِراكات {after.PushSubscriptions.Count}");
            Assert.Equal("رُدَّ (ConcurrencyException)", outcome);
            Assert.Equal(1, after.SessionVersion);
            Assert.True(revoked);
            Assert.Empty(after.PushSubscriptions);
        }
        finally { await LiveZeroRoleAdminProofTests.CleanAsync(store, slug); }
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-29، G-1)</b> — كانَ <c>Today_a_stale_copy_saved_after_erase_writes_the_erased_account_back_on_Postgres</c>
    /// (‏<c>9f887fa</c>): «حُفِظ · DeletedAt فارِغ · الاسم عادَ · الجَوّال عادَ · الإصدار 0». والآن شَكلُ <c>WebPushService.SendAsync</c> القَديمُ (قِراءَةٌ،
    /// ثُمَّ نِداءاتُ شَبَكَة، ثُمَّ حِفظُ النُسخَةِ نَفسِها) بَعدَ الحَذفِ يُرَدّ، والحِسابُ يَبقى مَمحُوّاً — وبابُ الحَذفِ نَفسُه (F2) يَلتَقِطُه بِإعادَتِه.</summary>
    [Fact]
    public async Task A_stale_copy_saved_after_erase_is_refused_and_the_account_stays_erased_on_Postgres()
    {
        if (!Enabled) return;
        await using var app = HostApp();
        var store = app.Services.GetRequiredService<Marten.IDocumentStore>();
        var slug = "cc-zz-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var user = await SeedAsync(store, slug);

            await using var stale = store.LightweightSession(slug);
            var copy = await stale.LoadAsync<User>(user.Id);
            await using (var erase = store.LightweightSession(slug))
            {
                var fresh = await erase.LoadAsync<User>(user.Id);
                erase.Store(AccountDeletion.Erase(fresh!, DateTime.UtcNow));
                await erase.SaveChangesAsync();
            }
            copy!.PushSubscriptions.Clear();
            stale.Store(copy);
            string outcome;
            try { await stale.SaveChangesAsync(); outcome = "حُفِظ"; }
            catch (JasperFx.ConcurrencyException) { outcome = "رُدَّ (ConcurrencyException)"; }

            await using var q = store.QuerySession(slug);
            var after = (await q.LoadAsync<User>(user.Id))!;
            var nameBack = after.FullName != AccountDeletion.ErasedName;
            var contactBack = after.Phone.Length > 0;
            output.WriteLine($"بَعدَ التَبديل: الحِفظُ القَديمُ بَعدَ الحَذفِ {outcome} · DeletedAt {(after.DeletedAt is null ? "فارِغ" : "قائِم")} · الاسم {(nameBack ? "عادَ" : "مَمحُوّ")} · الجَوّال {(contactBack ? "عادَ" : "مَمحُوّ")} · الإصدار {after.SessionVersion}");
            Assert.Equal("رُدَّ (ConcurrencyException)", outcome);
            Assert.NotNull(after.DeletedAt);
            Assert.False(nameBack || contactBack);
            Assert.Equal(1, after.SessionVersion);
        }
        finally { await LiveZeroRoleAdminProofTests.CleanAsync(store, slug); }
    }

    /// <summary><b>القِياسُ قَبلَ الوُثوق</b> (القاعِدَة ١٠): الجَلسَةُ الخَفيفَةُ تَحفَظُ نُسخَةَ القِراءَةِ — بِالمُعَرِّفِ أَو بِالاستِعلامِ (شَكلُ دُخولِ
    /// الاستوديو ودُخولِ المَتجَر) — بِلا تَعارُضٍ حينَ لا كاتِبَ بَينَهُما، لِلحِسابَين؛ وتَرُدُّ وَثيقَةً لَم تُقرَأ في جَلسَتِها.</summary>
    [Fact]
    public async Task A_copy_read_by_id_or_by_query_saves_without_a_writer_between_and_an_unread_one_is_refused_on_Postgres()
    {
        if (!Enabled) return;
        await using var app = HostApp();
        var store = app.Services.GetRequiredService<Marten.IDocumentStore>();
        var slug = "cc-zz-" + Guid.NewGuid().ToString("N")[..8];
        var studio = new ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioUser { Id = Guid.NewGuid(), Phone = "cc-" + Guid.NewGuid().ToString("N")[..10] };
        try
        {
            var user = await SeedAsync(store, slug);
            await using (var s = store.LightweightSession(ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioAuth.Tenant)) { s.Store(studio); await s.SaveChangesAsync(); }

            async Task<string> Save(Func<Marten.IDocumentSession, Task> body, string tenant)
            {
                await using var s = store.LightweightSession(tenant);
                await body(s);
                try { await s.SaveChangesAsync(); return "حُفِظ"; }
                catch (JasperFx.ConcurrencyException) { return "رُدّ"; }
            }
            var byId = await Save(async s => { var u = await s.LoadAsync<User>(user.Id); u!.FullName = "بِالمُعَرِّف"; s.Store(u); }, slug);
            var byQuery = await Save(async s =>
            {
                var u = (await Marten.QueryableExtensions.ToListAsync(s.Query<User>().Where(x => x.Phone == user.Phone))).Single();
                u.FullName = "بِالاستِعلام"; s.Store(u);
            }, slug);
            var studioByQuery = await Save(async s =>
            {
                var u = (await Marten.QueryableExtensions.ToListAsync(s.Query<ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioUser>().Where(x => x.Phone == studio.Phone))).Single();
                u.LastLoginAt = DateTime.UtcNow; s.Store(u);
            }, ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioAuth.Tenant);
            var unread = await Save(s =>
            {
                s.Store(new User { Id = user.Id, TenantSlug = slug, Phone = user.Phone, FullName = "لَم تُقرَأ" });
                return Task.CompletedTask;
            }, slug);
            output.WriteLine($"بِالمُعَرِّف: {byId} · بِالاستِعلام: {byQuery} · الاستوديو بِالاستِعلام: {studioByQuery} · وَثيقَةٌ لَم تُقرَأ: {unread}");
            Assert.Equal(("حُفِظ", "حُفِظ", "حُفِظ", "رُدّ"), (byId, byQuery, studioByQuery, unread));
        }
        finally
        {
            await LiveZeroRoleAdminProofTests.CleanAsync(store, slug);
            await using var s = store.LightweightSession(ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioAuth.Tenant);
            s.Delete<ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioUser>(studio.Id);
            await s.SaveChangesAsync();
        }
    }

    /// <summary><b>الإعادَةُ تُطَبِّقُ تَغييرَ الكاتِبِ وَحدَه مَرَّةً عَلى ما كُتِب</b> (‏<c>AccountWrites.UpdateAsync</c>): كاتِبٌ يَرفَعُ الإصدارَ بَينَ قِراءَةِ
    /// الاشتِراكِ وحِفظِه — فَيُرَدُّ الحِفظُ الأَوَّل، ويُعادُ التَغييرُ عَلى الوَثيقَةِ الجَديدَة: الإصدارُ المَرفوعُ والاشتِراكُ مَعاً (مُوجَب). وكاتِبٌ يَسبِقُ
    /// المُحاوَلَتَينِ كِلتَيهِما يُفشِلُ الطَلَبَ مُغلَقاً ولا يَكتُبُ فَوقَه (سالِب).</summary>
    /// <remarks>‏(‏2026-10-30، مُراجَعَةُ الموجَة ١١ لِلمَسار F1 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) <b>هذا الصَفُّ يَقيسُ العُدَّةَ بِلا شَرطِ جَلسَة</b> — كاتِبٌ لا جَلسَةَ لَه
    /// (‏<c>admit</c> غائِب)، فَـ«الاشتِراكُ مَعَ الإصدارِ المَرفوع» أَعلاه سُلوكُ العُدَّةِ لا سُلوكُ البابِ. وبابُ <c>/api/{slug}/push/subscribe</c> يُعلِنُ
    /// <c>SessionVersionGate.StillOpen</c> مُنذُ <c>ec856fd</c>، فَاشتِراكٌ يُسابِقُ الرَفعَ يُرَدُّ في إعادَتِه والاشتِراكاتُ صِفر —
    /// <c>LiveSessionWriteProofTests.A_subscribe_racing_the_revoke_is_refused_on_its_retry_on_Postgres</c> (مُوجَب) و
    /// <c>A_live_session_racing_an_unrelated_writer_still_subscribes_on_Postgres</c> (سالِب).</remarks>
    [Fact]
    public async Task The_writer_reapplies_only_its_own_change_once_and_fails_closed_on_a_second_conflict_on_Postgres()
    {
        if (!Enabled) return;
        await using var app = HostApp();
        var store = app.Services.GetRequiredService<Marten.IDocumentStore>();
        var slug = "cc-zz-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var user = await SeedAsync(store, slug);
            var calls = 0;
            var saved = await AccountWrites.UpdateAsync<User>(store, slug, user.Id, u =>
            {
                if (++calls == 1)
                    ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.RevokeTenantAsync(store, Cache(), slug, user.Id).GetAwaiter().GetResult();
                ACommerce.Templates.Customer.Marketplace.Services.PushSubscriptionDoor.Keep(u.PushSubscriptions,
                    new ACommerce.Kit.Auth.PushSubscription { Endpoint = "https://fcm.googleapis.com/fcm/send/new", P256dh = "p", Auth = "a" });
                return true;
            });
            await using (var q = store.QuerySession(slug))
            {
                var after = (await q.LoadAsync<User>(user.Id))!;
                output.WriteLine($"مُحاوَلات: {calls} · الإصدار {after.SessionVersion} · الاشتِراكات {after.PushSubscriptions.Count}");
                Assert.Equal(2, calls);
                Assert.Equal((1, 1), (after.SessionVersion, after.PushSubscriptions.Count));
                Assert.Equal("https://fcm.googleapis.com/fcm/send/new", after.PushSubscriptions[0].Endpoint);
            }

            var tries = 0;
            var failed = await Assert.ThrowsAsync<JasperFx.ConcurrencyException>(() => AccountWrites.UpdateAsync<User>(store, slug, user.Id, u =>
            {
                tries++;
                ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.RevokeTenantAsync(store, Cache(), slug, user.Id).GetAwaiter().GetResult();
                u.FullName = "لا يُكتَب";
                return true;
            }));
            await using (var q = store.QuerySession(slug))
            {
                var after = (await q.LoadAsync<User>(user.Id))!;
                var unwritten = after.FullName != "لا يُكتَب";
                output.WriteLine($"تَعارُضانِ مُتَتالِيان: مُحاوَلات {tries} · رُمِيَ {failed.GetType().Name} · الإصدار {after.SessionVersion} · التَغييرُ لَم يُكتَب {unwritten}");
                Assert.Equal(AccountWrites.Attempts, tries);
                Assert.Equal(3, after.SessionVersion);
                Assert.True(unwritten);
            }
        }
        finally { await LiveZeroRoleAdminProofTests.CleanAsync(store, slug); }
    }
}

// ═══ كاتِبٌ بِاسمِ جَلسَةٍ عَلى قِراءَةٍ جَديدَة (مُراجَعَةُ الموجَة ١١ لِلمَسار F1، ‏2026-10-30؛ I-SEC-2 مَعَ G-1) ═══
//
// الجَلسَةُ تُفحَصُ مَرَّةً عِندَ البَوّابَةِ أَوَّلَ الطَلَب، و`AccountWrites.UpdateAsync` يُطَبِّقُ تَغييرَ البابِ عَلى الوَثيقَةِ الجَديدَةِ ولا يَسأَلُ: أَما زالَ
// رَمزُ هذا الطَلَبِ صالِحاً عَلَيها؟ فَطَلَبٌ عَبَرَ البَوّابَةَ قَبلَ «اخرج من كل الأجهزة» وقَرَأَ بَعدَه يَحفَظُ بِلا تَعارُض، والاشتِراكُ لا يَنتَهي.

[Collection(LiveProofCollection.Name)]
public class LiveSessionWriteProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private static Microsoft.Extensions.Caching.Memory.MemoryCache Cache()
        => new(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());

    internal static ACommerce.Kit.Auth.PushSubscription Thief()
        => new() { Endpoint = "https://fcm.googleapis.com/fcm/send/thief", P256dh = "p", Auth = "a" };

    private static async Task<User> SeedAsync(Marten.IDocumentStore store, string slug)
    {
        var user = new User { Id = Guid.NewGuid(), TenantSlug = slug, Phone = "+966500000291", FullName = "صاحِبُ الحِساب", PhoneVerified = true };
        await using var s = store.LightweightSession(slug);
        s.Store(user);
        await s.SaveChangesAsync();
        return user;
    }

    /// <summary>شَكلُ <c>/api/{slug}/push/subscribe</c> بَعدَ التَبديل: الاشتِراكُ بِشَرطِ الجَلسَةِ عَلى القِراءَةِ الجَديدَة.</summary>
    internal static Task<User?> SubscribeAsync(Marten.IDocumentStore store, string slug, Guid userId, string token, Func<User, bool>? before = null)
        => AccountWrites.UpdateAsync<User>(store, slug, userId, u =>
        {
            before?.Invoke(u);
            ACommerce.Templates.Customer.Marketplace.Services.PushSubscriptionDoor.Keep(u.PushSubscriptions, Thief());
            return true;
        }, admit: u => ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.StillOpen(u, token));

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-30، كوميتُ التَبديل)</b> — كانَ
    /// <c>Today_a_subscribe_for_a_session_revoked_before_its_read_lands_on_the_revoked_account_on_Postgres</c> (‏<c>6e17ba9</c>): «رَمزُ الطَلَب ساقِط ·
    /// الحِفظ حُفِظ · الإصدار 1 · الاشتِراكات 1». والآن الشَرطُ يَرُدُّ القِراءَةَ الجَديدَة فَلا يُكتَبُ شَيء.</summary>
    [Fact]
    public async Task A_subscribe_for_a_session_revoked_before_its_read_writes_nothing_on_Postgres()
    {
        if (!Enabled) return;
        await using var app = LiveAccountConcurrencyProofTests.HostApp();
        var store = app.Services.GetRequiredService<Marten.IDocumentStore>();
        var slug = "sw-zz-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var user = await SeedAsync(store, slug);
            var token = AuthHandlers.MakeToken(user.Id, slug, user.SessionVersion);
            Assert.True(await ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.RevokeTenantAsync(store, Cache(), slug, user.Id));
            var saved = await SubscribeAsync(store, slug, user.Id, token);
            await using var q = store.QuerySession(slug);
            var after = (await q.LoadAsync<User>(user.Id))!;
            output.WriteLine($"بَعدَ التَبديل: الحِفظ {(saved is null ? "رُدّ" : "حُفِظ")} · الإصدار {after.SessionVersion} · الاشتِراكات {after.PushSubscriptions.Count}");
            Assert.Null(saved);
            Assert.Equal((1, 0), (after.SessionVersion, after.PushSubscriptions.Count));
        }
        finally { await LiveZeroRoleAdminProofTests.CleanAsync(store, slug); }
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه</b> — كانَ <c>Today_a_subscribe_for_an_account_erased_before_its_read_lands_on_the_erased_account_on_Postgres</c>
    /// (‏<c>6e17ba9</c>): «DeletedAt قائِم · الحِفظ حُفِظ · الاشتِراكات 1». والآن المَحذوفُ إصدارُه <see cref="int.MaxValue"/> فَيُرَدّ.</summary>
    [Fact]
    public async Task A_subscribe_for_an_account_erased_before_its_read_writes_nothing_on_Postgres()
    {
        if (!Enabled) return;
        await using var app = LiveAccountConcurrencyProofTests.HostApp();
        var store = app.Services.GetRequiredService<Marten.IDocumentStore>();
        var slug = "sw-zz-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var user = await SeedAsync(store, slug);
            var token = AuthHandlers.MakeToken(user.Id, slug, user.SessionVersion);
            await AccountWrites.UpdateAsync<User>(store, slug, user.Id, u => { AccountDeletion.Erase(u, DateTime.UtcNow); return true; });
            var saved = await SubscribeAsync(store, slug, user.Id, token);
            await using var q = store.QuerySession(slug);
            var after = (await q.LoadAsync<User>(user.Id))!;
            output.WriteLine($"بَعدَ التَبديل: DeletedAt {(after.DeletedAt is null ? "فارِغ" : "قائِم")} · الحِفظ {(saved is null ? "رُدّ" : "حُفِظ")} · الاشتِراكات {after.PushSubscriptions.Count}");
            Assert.NotNull(after.DeletedAt);
            Assert.Null(saved);
            Assert.Empty(after.PushSubscriptions);
        }
        finally { await LiveZeroRoleAdminProofTests.CleanAsync(store, slug); }
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه</b> — كانَ <c>Today_a_contact_link_for_a_session_revoked_before_its_verify_lands_on_Postgres</c> (‏<c>6e17ba9</c>):
    /// «الرَبط رُبِط · البَريدُ المَزروعُ في الحِساب True». والآن يُرَدُّ بِـ<c>no_account</c> ولا يُربَط.</summary>
    [Fact]
    public async Task A_contact_link_for_a_session_revoked_before_its_verify_links_nothing_on_Postgres()
    {
        if (!Enabled) return;
        await using var app = LiveAccountConcurrencyProofTests.HostApp();
        var store = app.Services.GetRequiredService<Marten.IDocumentStore>();
        var slug = "sw-zz-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            const string code = "424243";
            AuthHandlers.IssueAttempt(slug, "966500000302", code, AuthHandlers.AuthKind.PhoneOtp);
            var victim = (await AuthHandlers.VerifyPhoneOtpHandler(new VerifyPhoneOtp("966500000302", code), new VendorRegisterBucketTests.ResolvedTenant(slug), store))!;
            var mail = new LiveContactNoticeProofTests.Capture();
            var sms = new LiveContactNoticeProofTests.CaptureSms();
            var offered = ACommerce.Templates.Customer.Marketplace.Services.LoginChannels.Offered(AuthChannels.Phone, phoneRegistered: true, emailRegistered: true);
            var start = await ACommerce.Templates.Customer.Marketplace.Services.AccountContacts.StartAsync(store, slug, victim.UserId, ContactLink.Email, "planted.r1@example.com", sms, mail, offered, default);
            Assert.True(await ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.RevokeTenantAsync(store, Cache(), slug, victim.UserId));
            var linked = await ACommerce.Templates.Customer.Marketplace.Services.AccountContacts.VerifyAsync(store, slug, victim.UserId, start.AttemptId, mail.Code, DateTime.UtcNow, sms, mail, default,
                admit: u => ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.StillOpen(u, victim.Token));
            await using var q = store.QuerySession(slug);
            var after = (await q.LoadAsync<User>(victim.UserId))!;
            var planted = after.Email == "planted.r1@example.com";
            output.WriteLine($"بَعدَ التَبديل: الرَبط {(linked is null ? "رُبِط" : linked)} · البَريدُ المَزروعُ في الحِساب {planted} · رِسالَةٌ إلى الجَوّال {sms.Notices.Count}");
            Assert.Equal(ContactLink.NoAccount, linked);
            Assert.False(planted);
            Assert.Empty(sms.Notices);
        }
        finally { await LiveZeroRoleAdminProofTests.CleanAsync(store, slug); }
    }

    /// <summary>طَريقُ التَعارُض (المُوجَب): رَفعٌ بَينَ قِراءَةِ الاشتِراكِ وحِفظِه — يُرَدُّ الحِفظُ الأَوَّل، والإعادَةُ عَلى الوَثيقَةِ الجَديدَةِ يَرُدُّها الشَرط:
    /// الإصدارُ مَرفوعٌ والاشتِراكاتُ صِفر. (صَفُّ <c>LiveAccountConcurrencyProofTests</c> القائِمُ يَقيسُ الإعادَةَ نَفسَها لِكاتِبٍ بِلا جَلسَة.)</summary>
    [Fact]
    public async Task A_subscribe_racing_the_revoke_is_refused_on_its_retry_on_Postgres()
    {
        if (!Enabled) return;
        await using var app = LiveAccountConcurrencyProofTests.HostApp();
        var store = app.Services.GetRequiredService<Marten.IDocumentStore>();
        var slug = "sw-zz-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var user = await SeedAsync(store, slug);
            var token = AuthHandlers.MakeToken(user.Id, slug, user.SessionVersion);
            var calls = 0;
            var saved = await SubscribeAsync(store, slug, user.Id, token, _ =>
            {
                if (++calls == 1)
                    ACommerce.Templates.Customer.Marketplace.Services.SessionVersionGate.RevokeTenantAsync(store, Cache(), slug, user.Id).GetAwaiter().GetResult();
                return true;
            });
            await using var q = store.QuerySession(slug);
            var after = (await q.LoadAsync<User>(user.Id))!;
            output.WriteLine($"تَعارُضٌ مَعَ الرَفع: تَطبيقات {calls} · الحِفظ {(saved is null ? "رُدّ" : "حُفِظ")} · الإصدار {after.SessionVersion} · الاشتِراكات {after.PushSubscriptions.Count}");
            Assert.Equal(1, calls);
            Assert.Null(saved);
            Assert.Equal((1, 0), (after.SessionVersion, after.PushSubscriptions.Count));
        }
        finally { await LiveZeroRoleAdminProofTests.CleanAsync(store, slug); }
    }

    /// <summary>السالِب: كاتِبٌ آخَرُ لا يَمَسُّ الجَلسَةَ (تَغييرُ الاسمِ) بَينَ القِراءَةِ والحِفظ — الإعادَةُ يَقبَلُها الشَرطُ فَيُحفَظُ الاشتِراكُ والاسمُ مَعاً.</summary>
    [Fact]
    public async Task A_live_session_racing_an_unrelated_writer_still_subscribes_on_Postgres()
    {
        if (!Enabled) return;
        await using var app = LiveAccountConcurrencyProofTests.HostApp();
        var store = app.Services.GetRequiredService<Marten.IDocumentStore>();
        var slug = "sw-zz-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var user = await SeedAsync(store, slug);
            var token = AuthHandlers.MakeToken(user.Id, slug, user.SessionVersion);
            var calls = 0;
            var saved = await SubscribeAsync(store, slug, user.Id, token, _ =>
            {
                if (++calls == 1)
                    AccountWrites.UpdateAsync<User>(store, slug, user.Id, u => { u.FullName = "اسمٌ جَديد"; return true; }).GetAwaiter().GetResult();
                return true;
            });
            await using var q = store.QuerySession(slug);
            var after = (await q.LoadAsync<User>(user.Id))!;
            var renamed = after.FullName == "اسمٌ جَديد";
            output.WriteLine($"تَعارُضٌ مَعَ كاتِبٍ آخَر: تَطبيقات {calls} · الحِفظ {(saved is null ? "رُدّ" : "حُفِظ")} · الإصدار {after.SessionVersion} · الاشتِراكات {after.PushSubscriptions.Count} · الاسمُ الجَديدُ باقٍ {renamed}");
            Assert.Equal(2, calls);
            Assert.NotNull(saved);
            Assert.Equal((0, 1), (after.SessionVersion, after.PushSubscriptions.Count));
            Assert.True(renamed);
        }
        finally { await LiveZeroRoleAdminProofTests.CleanAsync(store, slug); }
    }
}
