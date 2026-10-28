using System.Text;
using System.Text.RegularExpressions;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
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
