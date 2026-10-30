using ACommerce.Kit.Delivery;
using ACommerce.Templates.Customer.Marketplace.Services.Shipping;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ رابِطُ التَفعيلِ يَضيعُ حينَ يَتَعَثَّرُ ما بَعدَ الخَتم (‏2026-10-30، تَدقيقُ الموجَة ١١ F2 — د12) ═══════════════════════════════════════════════════
//
// **الحالُ المَقيسَة**: `RegisterAsync` يَحمِلُ الرابِطَ في مُتَغَيِّرٍ مَحَلِّيٍّ ويَضَعُه في وِعاءِ العَرضِ **بَعدَ أَن يَعودَ `ConnectAsync`** — و`ConnectAsync` يَختِمُ
// الرَمزَ (بِإعاداتِه) ثُمَّ يُسَجِّلُ الوارِدَ ثُمَّ `BindVendorAsync` أَخيراً بِلا إعادَة. فَخَتمٌ نَجَحَ في المُحاوَلَةِ الثانِيَةِ ثُمَّ حِفظٌ أَخيرٌ يَرمي ⇒ ‏500، والرابِطُ لا يوضَعُ
// أَبَداً؛ والمَدخَلُ فَعّالٌ بِرَمزٍ مَختوم، فَلا بابَ لَصق، والاستِمارَةُ مَخفِيَّة، والإعادَةُ تُعيدُ تَسجيلَ الوارِدِ وَحدَه. ود12 يَحجُبُ الرابِطَ حينَ يَضيعُ الرَمزُ وَحدَه.

public class VendorRegisterLinkAfterSealTests(ITestOutputHelper output)
{
    private const string Slug = DealShipTests.Slug;
    private const string Inbound = "https://wasayel.example/theme-demo/shipping/oto/order-status";

    /// <summary>يُفشِلُ حِفظاتِ الجَلسَةِ الَّتي أَرقامُها في <paramref name="failing"/> — بِعُطلٍ كَعُطلِ القاعِدَة — ويُرجِعُ عَدّادَها.</summary>
    private static Func<int> FailSavesAt(DocWorld world, params int[] failing)
    {
        var saves = 0;
        world.BeforeMember = name =>
        {
            if (name != "SaveChangesAsync") return;
            saves++;
            if (failing.Contains(saves)) throw new InvalidOperationException($"عُطلُ قاعِدَةٍ مَصنوع — الحِفظُ {saves}");
        };
        return () => saves;
    }

    /// <summary><b>تَوصيفُ «اليَوم»</b> (القاعِدَة ٣): الأَثَر (١) · الخَتمُ يَتَعَثَّر (٢) ثُمَّ يَنجَح (٣) · الحِفظُ الأَخيرُ بَعدَ تَسجيلِ الوارِدِ يَرمي (٤) — التَسجيلُ يَرمي،
    /// والرَمزُ مَختوم، و<b>لا رابِطَ</b> في العَرضِ التالي.</summary>
    [Fact]
    public async Task Today_a_failure_after_the_seal_loses_the_activation_link()
    {
        var owner = Guid.NewGuid();
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        var surface = VendorRegisterBucketTests.Surface(w);
        var saves = FailSavesAt(w.Docs, 2, 4);

        var error = await Record.ExceptionAsync(() =>
            surface.RegisterAsync(Slug, VendorShippingTests.Registrant(owner), VendorShippingTests.Form(), Inbound));
        w.Docs.BeforeMember = null;

        var entry = (await w.Providers.ReadVendorEntryAsync(Slug, ACommerce.Platform.Providers.ProviderCapabilities.Delivery, owner)).Entry;
        var link = surface.TakeActivationLink(Slug, owner);
        output.WriteLine($"الاستِثناء: {error?.GetType().Name ?? "لا"} · حِفظات: {saves()} · webhook: {w.Oto.CallsTo(OtoClient.WebhookPath)} · مَدخَل: {entry?.IsActive} · الرابِط: {link ?? "—"}");
        Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(4, saves());
        Assert.Equal(1, w.Oto.CallsTo(OtoClient.WebhookPath));
        Assert.True(entry is { IsActive: true });
        Assert.Null(link);
    }
}
