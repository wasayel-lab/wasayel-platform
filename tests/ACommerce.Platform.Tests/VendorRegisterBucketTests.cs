using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Delivery;
using ACommerce.Platform.Shared;
using ACommerce.Templates.Customer.Marketplace.Services.Shipping;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ حَدُّ مُحاوَلاتِ «Register» عِندَ OTO وحِصَّةُ إرسالِ رَمزِ الدُخول — مِفتاحٌ واحِدٌ في عَدّادٍ واحِد ═══
//
// مُراجَعَةُ المَوجَة 3b (‏2026-10-13، G5 M1 — قَبلَ أَن يُضبَطَ `Shipping__Oto__MarketplaceToken` في أَيِّ مَكان): حَدُّ مُحاوَلاتِ التَسجيلِ
// (`e5fab62`) يَستَهلِكُ حِصَّةَ الإرسالِ القائِمَة (`AuthHandlers.TryConsumeSendQuota`) بِمِفتاحِ «‏{slug}|oto-register:{vendor}»، وبابُ الدُخولِ
// بِالجَوّالِ — بِلا جَلسَة — يَستَهلِكُ العَدّادَ نَفسَه بِمِفتاحِ «‏{slug}|{phone}»، والجَوّالُ نَصٌّ حُرٌّ يُقَصُّ ولا يُفحَص
// (`/{slug}/auth/phone/login` ← `RequestPhoneOtpHandler`). فَمَن يُرسِلُ `phone=oto-register:{vendorId}` عَشرَ مَرّاتٍ يُقفِلُ «Register» ذاكَ البائِعِ
// ساعَةً، ويُعيدُها كُلَّ ساعَة.
//
// **ما يُقلَبُ بِاسمِه في كوميتِ الإصلاح**: صَفُّ «اليَوم» أَدناه. **وما يَبقى بِحَرفِه**: صَفُّ المِفتاحِ نَفسِه — شَكلُه لا يَتَغَيَّر.

public class VendorRegisterBucketTests(ITestOutputHelper output)
{
    private const string Slug = DealShipTests.Slug;
    private const string Inbound = "https://wasayel.example/theme-demo/shipping/oto/order-status";

    /// <summary>مُستَأجِرٌ مَحلولٌ كَما يَراه بابُ الدُخولِ بَعدَ الوَسيط — المِفتاحُ وَحدَه يَعنينا.</summary>
    internal sealed class ResolvedTenant(string slug) : ITenantContext
    {
        public string Slug => slug;
        public string Name => slug;
        public string BrandColor => "#000000";
        public string AuthChannel => "phone";
        public string TagLine => "";
        public string City => "";
        public bool IsResolved => true;
        public bool HasRoles => false;
    }

    /// <summary>قَناةُ رَسائِلَ تَعُدُّ ولا تُرسِل.</summary>
    internal sealed class CountingSms : IOtpChannel
    {
        public int Sent;
        public string ChannelName => "counting-sms";
        public string? DevHintCode => null;
        public Task SendOtpAsync(string phone, string code, CancellationToken ct) { Interlocked.Increment(ref Sent); return Task.CompletedTask; }
    }

    internal static VendorShippingService Surface(DealShipTests.World w)
    {
        var options = Options.Create(new OtoOptions
        {
            Environment = OtoEnvironment.Staging,
            RefreshToken = DealShipTests.ConfigToken,
            MarketplaceToken = "MKT-token",
        });
        return new VendorShippingService(w.Providers, w.Vault,
            new OtoClient(new HttpClient(w.Oto), options, new OtoTokenCache(), NullLogger<OtoClient>.Instance), options,
            new ACommerce.Templates.Customer.Marketplace.Services.Audit.AuditWriter(w.Docs.Store),
            new ACommerce.Templates.Customer.Marketplace.Services.Api.ApiKeyRevealStore(
                new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())));
    }

    /// <summary>بابُ الدُخولِ بِالجَوّالِ كَما تُناديه النُقطَة: <c>new RequestPhoneOtp(phone)</c> بِالنَصِّ المَقصوصِ نَفسِه — عَشرَ مَرّات.</summary>
    internal static async Task<int> PhoneLoginsAsync(string slug, string phone, int times, CountingSms sms)
    {
        var refused = 0;
        for (var i = 0; i < times; i++)
        {
            try { await AuthHandlers.RequestPhoneOtpHandler(new RequestPhoneOtp(phone), new ResolvedTenant(slug), sms, CancellationToken.None); }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("rate_limited", StringComparison.Ordinal)) { refused++; }
        }
        return refused;
    }

    /// <summary>شَكلُ المِفتاح — يَبقى بِحَرفِه: بائِعٌ في مَتجَر.</summary>
    [Fact]
    public void The_register_attempt_key_is_the_store_and_the_vendor()
    {
        var owner = Guid.NewGuid();
        Assert.Equal($"{Slug}|oto-register:{owner:D}", VendorShippingService.RegisterAttemptKey(Slug, owner));
    }

    /// <summary><b>قُلِبَ</b> (‏2026-10-13، G5 M1) — كانَ <c>Today_ten_phone_logins_with_the_register_subject_lock_that_vendors_register</c> (‏<c>bc35766</c>):
    /// عَشرُ طَلَباتِ دُخولٍ بِالجَوّالِ نَصُّها <c>oto-register:{vendor}</c> تَستَهلِكُ حِصَّةَ الإرسالِ وَحدَها — و«Register» ذاكَ البائِعِ يَمضي إلى OTO.</summary>
    [Fact]
    public async Task Ten_phone_logins_with_the_register_subject_no_longer_touch_that_vendors_register()
    {
        var owner = Guid.NewGuid();
        var sms = new CountingSms();
        var refused = await PhoneLoginsAsync(Slug, $"oto-register:{owner:D}", 10, sms);

        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        var result = await Surface(w).RegisterAsync(Slug, VendorShippingTests.Registrant(owner), VendorShippingTests.Form(), Inbound);

        output.WriteLine($"دُخولٌ مَردود: {refused} · رَسائِل: {sms.Sent} · التَسجيل: {result.Code ?? "ok"} · نِداءاتُ Register: {w.Oto.CallsTo(OtoClient.RegisterPath)}");
        Assert.Equal(0, refused);
        Assert.Equal(10, sms.Sent);
        Assert.True(result.Ok, result.Code);
        Assert.Equal(1, w.Oto.CallsTo(OtoClient.RegisterPath));
    }

    /// <summary><b>والعَدّادانِ مُنفَصِلانِ في الاتِّجاهَين</b> بِالمِفتاحِ نَفسِه: حِصَّةُ إرسالٍ مُستَنفَدَةٌ لا تَمَسُّ حِصَّةَ الأَفعال، وحِصَّةُ أَفعالٍ مُستَنفَدَةٌ
    /// لا تَمَسُّ حِصَّةَ الإرسال — وكُلٌّ مِنهُما يُرَدُّ في حادِيَتِه عَشرَة (الحَدُّ نَفسُه، لا رَقَمَ جَديد).</summary>
    [Fact]
    public void The_action_and_send_counters_are_disjoint_in_both_directions_with_the_same_bound()
    {
        var sendFirst = VendorShippingService.RegisterAttemptKey(Slug, Guid.NewGuid());
        for (var i = 0; i < 10; i++) Assert.True(AuthHandlers.TryConsumeSendQuota(sendFirst));
        Assert.False(AuthHandlers.TryConsumeSendQuota(sendFirst));
        for (var i = 0; i < 10; i++) Assert.True(AuthHandlers.TryConsumeActionQuota(sendFirst));
        Assert.False(AuthHandlers.TryConsumeActionQuota(sendFirst));

        var actionFirst = VendorShippingService.RegisterAttemptKey(Slug, Guid.NewGuid());
        for (var i = 0; i < 10; i++) Assert.True(AuthHandlers.TryConsumeActionQuota(actionFirst));
        Assert.False(AuthHandlers.TryConsumeActionQuota(actionFirst));
        Assert.True(AuthHandlers.TryConsumeSendQuota(actionFirst));
    }

    /// <summary><b>والعَكس</b>: عَشرُ مُحاوَلاتِ تَسجيلٍ تَرُدُّها OTO تُقفِلُ التَسجيلَ وَحدَه — ودُخولٌ بِالجَوّالِ بِذلِكَ النَصِّ لا يُرَدُّ بِها.</summary>
    [Fact]
    public async Task Ten_refused_registers_lock_the_register_only_and_not_a_phone_login_with_that_subject()
    {
        var owner = Guid.NewGuid();
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        w.Oto.Routes[OtoClient.RegisterPath] = _ => OtoRouteHandler.Json(System.Net.HttpStatusCode.BadRequest, "{\"success\": false}");
        var surface = Surface(w);
        var codes = new List<string?>();
        for (var i = 0; i < 11; i++)
            codes.Add((await surface.RegisterAsync(Slug, VendorShippingTests.Registrant(owner), VendorShippingTests.Form(), Inbound)).Code);

        var sms = new CountingSms();
        var refused = await PhoneLoginsAsync(Slug, $"oto-register:{owner:D}", 1, sms);

        output.WriteLine($"{string.Join(" · ", codes)} · دُخولٌ مَردود: {refused}");
        Assert.Equal(VendorShippingService.RegisterRateLimited, codes[^1]);
        Assert.Equal(10, w.Oto.CallsTo(OtoClient.RegisterPath));
        Assert.Equal(0, refused);
        Assert.Equal(1, sms.Sent);
    }
}

// ═══ البُرهانُ الحَيّ — Postgres (‏WASAYEL_LIVE_PROOF=1 وConnectionStrings__Postgres) ═══════════════════════
//
// المُعالِجُ نَفسُه الَّذي تُناديه نُقطَةُ الدُخولِ عَشرَ مَرّاتٍ بِنَصِّ التَسجيل، ثُمَّ خِدمَةُ السَطحِ عَلى Postgres: البائِعُ يُسَجَّلُ ويُختَمُ مَدخَلُه
// ويُكتَبُ سَطرُ أَثَرِه في مُستَأجِرِه. مُستَأجِرٌ مُؤَقَّتٌ يُمسَحُ بَعدَه.

[Collection(LiveProofCollection.Name)]
public class LiveVendorRegisterBucketProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    [Fact]
    public async Task Ten_phone_logins_with_the_register_subject_leave_the_register_open_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "vs-zz-b-" + Guid.NewGuid().ToString("N")[..8];
        var owner = Guid.NewGuid();
        try
        {
            var sms = new VendorRegisterBucketTests.CountingSms();
            var refused = await VendorRegisterBucketTests.PhoneLoginsAsync(slug, $"oto-register:{owner:D}", 10, sms);

            var providers = new ACommerce.Templates.Customer.Marketplace.Services.TenantProviderService(store);
            var vault = DealShipTests.NewVault();
            var oto = new OtoRouteHandler();
            var options = Options.Create(new OtoOptions { Environment = OtoEnvironment.Staging, MarketplaceToken = "MKT-live" });
            var surface = new VendorShippingService(providers, vault,
                new OtoClient(new HttpClient(oto), options, new OtoTokenCache(), NullLogger<OtoClient>.Instance), options,
                new ACommerce.Templates.Customer.Marketplace.Services.Audit.AuditWriter(store),
                new ACommerce.Templates.Customer.Marketplace.Services.Api.ApiKeyRevealStore(
                    new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())));

            var result = await surface.RegisterAsync(slug, VendorShippingTests.Registrant(owner), VendorShippingTests.Form(), "https://wasayel.example/x");
            var entry = (await providers.ReadVendorEntryAsync(slug, ACommerce.Platform.Providers.ProviderCapabilities.Delivery, owner)).Entry;
            await using var q = store.QuerySession(slug);
            var audits = await q.Query<ACommerce.Templates.Customer.Marketplace.Services.Audit.AuditEntry>().ToListAsync();

            output.WriteLine($"دُخولٌ مَردود: {refused} · رَسائِل: {sms.Sent} · التَسجيل: {result.Code ?? "ok"} · " +
                             $"Register: {oto.CallsTo(OtoClient.RegisterPath)} · مَدخَل: {(entry is null ? "لا" : "نَعَم")} · أَثَر: {audits.Count}");
            Assert.Equal(0, refused);
            Assert.True(result.Ok, result.Code);
            Assert.Equal(1, oto.CallsTo(OtoClient.RegisterPath));
            Assert.NotNull(entry);
            Assert.Single(audits, a => a.Action == VendorShippingService.RegisterAuditAction);
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            foreach (var b in await s.Query<ACommerce.Platform.Providers.TenantProviderBinding>().ToListAsync()) s.Delete(b);
            foreach (var a in await s.Query<ACommerce.Templates.Customer.Marketplace.Services.Audit.AuditEntry>().ToListAsync()) s.Delete(a);
            await s.SaveChangesAsync();
        }
    }
}
