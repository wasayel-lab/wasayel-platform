using System.Reflection;
using System.Runtime.ExceptionServices;
using ACommerce.Kit.Delivery;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Shipping;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ «Register» نَجَحَ عِندَ OTO وتَعَثَّرَ حِفظُ رَمزِ الحِسابِ الأَوَّل — يَضيعُ الرَمزُ الَّذي لا يُعطى إلّا مَرَّة ═══
//
// مُراجَعَةُ المَوجَة 3b (‏2026-10-13، G5 M3 = (xii) — قَبلَ أَن يُضبَطَ `Shipping__Oto__MarketplaceToken`): `RegisterAsync` تَأخُذُ مِن OTO رَمزَ
// تَحديثٍ دائِماً **مَرَّةً واحِدَة**، وتَضَعُ رابِطَ التَفعيلِ في وِعاءِ العَرض، ثُمَّ `ConnectAsync` يَختِمُ الرَمزَ بِـ`BindVendorAsync` — حِفظٌ واحِدٌ
// بِلا إعادَة. فَإن تَعَثَّرَ ذاكَ الحِفظ (عُطلُ قاعِدَة) صَعِدَ الاستِثناء: الرَمزُ ضاع، والرابِطُ قائِمٌ يُفَعِّلُ الحِسابَ عِندَ OTO — حِسابٌ لا نَملِكُ
// رَمزَه، ولا تَسجيلَ ثانِياً (المُحاوَلَةُ الثانِيَةُ تَفتَحُ حِساباً آخَر أَو تَرُدُّها OTO بِالبَريدِ نَفسِه).
//
// **ما يُقلَبُ بِاسمِه في كوميتِ الإصلاح**: صَفّا «اليَوم» أَدناه (CI وحَيّ).

public class VendorRegisterSealRetryTests(ITestOutputHelper output)
{
    private const string Slug = DealShipTests.Slug;
    private const string Inbound = "https://wasayel.example/theme-demo/shipping/oto/order-status";

    /// <summary>يُفشِلُ حِفظَ الجَلسَةِ رَقمَ <paramref name="failAt"/> وما بَعدَه حَتّى <paramref name="failures"/> مَرّات — بِعُطلٍ كَعُطلِ القاعِدَة.
    /// ويُرجِعُ عَدّادَ ما حاوَلَه.</summary>
    internal static Func<int> FailSaves(DocWorld world, int failAt, int failures)
    {
        var saves = 0;
        world.BeforeMember = name =>
        {
            if (name != "SaveChangesAsync") return;
            saves++;
            if (saves >= failAt && saves < failAt + failures)
                throw new InvalidOperationException($"عُطلُ قاعِدَةٍ مَصنوع — الحِفظُ {saves}");
        };
        return () => saves;
    }

    /// <summary><b>اليَوم</b>: «Register» يَنجَحُ عِندَ OTO (نِداءٌ واحِد)، وحِفظُ الخَتمِ الأَوَّلُ — الحِفظُ الثاني بَعدَ سَطرِ الأَثَر — يَتَعَثَّر:
    /// يَصعَدُ الاستِثناءُ مِن <c>RegisterAsync</c> بِلا إعادَة، ولا يُسَجَّلُ وارِد، ورابِطُ التَفعيلِ قائِمٌ في الوِعاء.</summary>
    [Fact]
    public async Task Today_a_failed_first_seal_after_a_successful_register_escapes_and_the_one_time_token_is_lost()
    {
        var owner = Guid.NewGuid();
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        var surface = VendorRegisterBucketTests.Surface(w);
        var saves = FailSaves(w.Docs, failAt: 2, failures: 1);

        var ex = await Record.ExceptionAsync(() =>
            surface.RegisterAsync(Slug, VendorShippingTests.Registrant(owner), VendorShippingTests.Form(), Inbound));

        output.WriteLine($"الاستِثناء: {ex?.GetType().Name ?? "لا"} · حِفظات: {saves()} · Register: {w.Oto.CallsTo(OtoClient.RegisterPath)} · " +
                         $"webhook: {w.Oto.CallsTo(OtoClient.WebhookPath)} · رابِطُ التَفعيل: {(surface.TakeActivationLink(Slug, owner) is null ? "لا" : "قائِم")}");
        Assert.IsType<InvalidOperationException>(ex);
        Assert.Equal(2, saves());
        Assert.Equal(1, w.Oto.CallsTo(OtoClient.RegisterPath));
        Assert.Equal(0, w.Oto.CallsTo(OtoClient.WebhookPath));
    }
}

/// <summary>مَخزَنٌ يُمَرِّرُ كُلَّ شَيءٍ إلى Postgres ويُفشِلُ أَوَّلَ <c>Failures</c> حِفظاتٍ لِجَلَساتِه — يُعطى لِخِدمَةِ المُزَوِّدينَ وَحدَها
/// فَيَتَعَثَّرُ خَتمُ الرَمزِ ولا يَتَعَثَّرُ سَطرُ الأَثَر.</summary>
internal class FailingSaveStore : DispatchProxy
{
    private object _target = null!;
    private Counter _counter = null!;

    internal sealed class Counter { public int Failures; public int Attempts; }

    public static IDocumentStore Wrap(IDocumentStore store, Counter counter) => Proxy<IDocumentStore>(store, counter);

    private static T Proxy<T>(object target, Counter counter) where T : class
    {
        var proxy = Create<T, FailingSaveStore>();
        var self = (FailingSaveStore)(object)proxy;
        self._target = target;
        self._counter = counter;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        var name = method!.Name;
        if (name == "SaveChangesAsync")
        {
            Interlocked.Increment(ref _counter.Attempts);
            if (Interlocked.Decrement(ref _counter.Failures) >= 0)
                return Task.FromException(new InvalidOperationException("عُطلُ قاعِدَةٍ مَصنوع"));
        }

        object? result;
        try { result = method.Invoke(_target, args); }
        catch (TargetInvocationException e) { ExceptionDispatchInfo.Capture(e.InnerException!).Throw(); throw; }

        return name == "LightweightSession" && result is IDocumentSession session
            ? Proxy<IDocumentSession>(session, _counter)
            : result;
    }
}

// ═══ البُرهانُ الحَيّ — Postgres (‏WASAYEL_LIVE_PROOF=1 وConnectionStrings__Postgres) ═══════════════════════

public class LiveVendorRegisterSealRetryProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    internal static async Task<(Exception? Error, bool EntrySealed, int SaveAttempts, bool LinkStashed, int Webhooks)> RegisterWithFailingSealsAsync(
        IDocumentStore store, string slug, Guid owner, int failures)
    {
        var counter = new FailingSaveStore.Counter { Failures = failures };
        var providers = new TenantProviderService(FailingSaveStore.Wrap(store, counter));
        var vault = DealShipTests.NewVault();
        var oto = new OtoRouteHandler();
        var options = Options.Create(new OtoOptions { Environment = OtoEnvironment.Staging, MarketplaceToken = "MKT-live" });
        var surface = new VendorShippingService(providers, vault,
            new OtoClient(new HttpClient(oto), options, new OtoTokenCache(), NullLogger<OtoClient>.Instance), options,
            new ACommerce.Templates.Customer.Marketplace.Services.Audit.AuditWriter(store),
            new ACommerce.Templates.Customer.Marketplace.Services.Api.ApiKeyRevealStore(
                new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())));

        var error = await Record.ExceptionAsync(() =>
            surface.RegisterAsync(slug, VendorShippingTests.Registrant(owner), VendorShippingTests.Form(), "https://wasayel.example/x"));
        var entry = await new TenantProviderService(store).VendorEntryAsync(slug, ProviderCapabilities.Delivery, owner);
        var sealedToken = entry is not null && entry.Values.TryGetValue(VendorShippingSurface.RefreshField!, out var v)
            && v.Unseal(vault, VendorShippingSurface.AadFor(slug, VendorShippingSurface.RefreshField!, owner)) == "_refreshToken_";
        return (error, sealedToken, counter.Attempts, surface.TakeActivationLink(slug, owner) is not null, oto.CallsTo(OtoClient.WebhookPath));
    }

    internal static async Task CleanAsync(IDocumentStore store, string slug)
    {
        await using var s = store.LightweightSession(slug);
        foreach (var b in await s.Query<TenantProviderBinding>().ToListAsync()) s.Delete(b);
        foreach (var a in await s.Query<ACommerce.Templates.Customer.Marketplace.Services.Audit.AuditEntry>().ToListAsync()) s.Delete(a);
        await s.SaveChangesAsync();
    }

    /// <summary><b>اليَوم</b> عَلى Postgres: حِفظُ الخَتمِ الأَوَّلُ يَتَعَثَّرُ مَرَّةً — يَصعَدُ الاستِثناء، ولا مَدخَلَ في القاعِدَة، ورابِطُ التَفعيلِ قائِم.</summary>
    [Fact]
    public async Task Today_one_failed_seal_after_a_successful_register_leaves_no_entry_and_a_live_activation_link_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "vs-zz-r-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var (error, sealedToken, attempts, link, webhooks) = await RegisterWithFailingSealsAsync(store, slug, Guid.NewGuid(), failures: 1);
            output.WriteLine($"الاستِثناء: {error?.GetType().Name ?? "لا"} · المَدخَل: {(sealedToken ? "مَختوم" : "لا")} · حِفظاتُ الخِدمَة: {attempts} · " +
                             $"رابِطُ التَفعيل: {(link ? "قائِم" : "لا")} · webhook: {webhooks}");
            Assert.IsType<InvalidOperationException>(error);
            Assert.False(sealedToken);
            Assert.Equal(1, attempts);
            Assert.True(link);
        }
        finally { await CleanAsync(store, slug); }
    }
}
