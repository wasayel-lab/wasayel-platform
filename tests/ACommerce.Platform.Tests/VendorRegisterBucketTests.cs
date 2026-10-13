using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Delivery;
using ACommerce.Platform.Shared;
using ACommerce.Templates.Customer.Marketplace.Services.Shipping;
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

    /// <summary><b>اليَوم</b>: عَشرُ طَلَباتِ دُخولٍ بِالجَوّالِ بِلا جَلسَةٍ نَصُّها <c>oto-register:{vendor}</c> تُقفِلُ «Register» ذاكَ البائِع — يُرَدُّ بِـ
    /// <c>vendor_register_rate_limited</c> قَبلَ OTO، ولَم يُحاوِل مَرَّةً واحِدَة.</summary>
    [Fact]
    public async Task Today_ten_phone_logins_with_the_register_subject_lock_that_vendors_register()
    {
        var owner = Guid.NewGuid();
        var sms = new CountingSms();
        var refused = await PhoneLoginsAsync(Slug, $"oto-register:{owner:D}", 10, sms);

        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        var result = await Surface(w).RegisterAsync(Slug, VendorShippingTests.Registrant(owner), VendorShippingTests.Form(), Inbound);

        output.WriteLine($"دُخولٌ مَردود: {refused} · رَسائِل: {sms.Sent} · التَسجيل: {result.Code ?? "ok"} · نِداءاتُ OTO: {w.Oto.Calls.Count}");
        Assert.Equal(0, refused);
        Assert.Equal(10, sms.Sent);
        Assert.Equal(VendorShippingService.RegisterRateLimited, result.Code);
        Assert.Empty(w.Oto.Calls);
    }
}
