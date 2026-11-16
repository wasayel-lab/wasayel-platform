using System.Text.Json;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.LiveStoreBirthOnceCharacterizationTests;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ بَيانِ الـPWA بِشَكلِ المَسارِ لِما يَنجو قَبلَ أَن يَتبَعَ المُضيف (‏2026-11-07، ADR-050 §٩ المَرحَلَةُ الثانِيَة · W13-4·C2b·2 — القاعِدَة ٣) ═══
//
// **ما يَتَغَيَّرُ عَمداً بَعدَ هذا المِلَفّ**: `id` و`start_url` و`scope` ورَوابِطُ المُختَصَرات تَتبَعُ مُضيفَ المَتجَر — عَلى `{slug}.R` تَصيرُ `/` و`/r/{role}/`
// (‏A2 §3.2 وB2 §0: «عَلى المُضيفِ يَصيرُ النِطاقُ خاطِئاً فَيَرفُضُ المُتَصَفِّحُ التَثبيت»). **وما يَنجو ويُثَبَّتُ هُنا**: البَيانُ بِشَكلِ المَسارِ كَما يُجيبُ بِه
// بابُ القالِبِ نَفسُه (`/api/{slug}/manifest.json` و`/api/{slug}/r/{role}/manifest.json` كَما يُسَجِّلُهُما `MapCustomerMarketplaceTemplate`) عَلى Postgres
// بِلا مُضيفِ مَتجَر — المُعَرِّفُ والبِدايَةُ والنِطاقُ ورَوابِطُ المُختَصَراتِ ومَصادِرُ الأَيقونات. والمِلَفُّ يَبقى بِبَصمَتِه بَعدَ التَبديل.
// يَتَخَطّى نَفسَه ما لَم تُضبَط WASAYEL_LIVE_PROOF=1 وConnectionStrings__Postgres — عَقدُ إخوَتِه، وفي مَجموعَتِهِم التَسلسُلِيَّة.

[Collection(LiveProofCollection.Name)]
public class LivePwaManifestCharacterizationTests(ITestOutputHelper output)
{
    private static bool Enabled => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    internal const string StoreManifest = "/api/{slug}/manifest.json";
    internal const string RoleManifest = "/api/{slug}/r/{role}/manifest.json";

    /// <summary>مَتجَرٌ بِدَورَينِ مِن الكَتالوج (بائِعٌ ومُشتَرٍ) يُكتَبُ مُباشَرَة — البابُ المَقيسُ بابُ البَيانِ وَحدَه.</summary>
    internal static async Task PutStoreAsync(LiveTemplateDoorHost host, string slug)
    {
        await using var s = host.Store.LightweightSession();
        s.Store(new Tenant
        {
            Id = slug, Name = "بَيانُ الجَولَة", BrandColor = "#0f766e", TagLine = "سَطر",
            Roles =
            [
                RoleCatalog.InstantiateRole(RoleCatalog.Find("customer")!, 0),
                RoleCatalog.InstantiateRole(RoleCatalog.Find("vendor")!, 1),
            ],
        });
        await s.SaveChangesAsync();
    }

    /// <summary>ما يَتبَعُ المُضيفَ مِن البَيان: المُعَرِّف، والبِدايَة، والنِطاق، ورَوابِطُ المُختَصَرات، ومَصادِرُ الأَيقونات — سَطراً واحِداً.</summary>
    internal static string Shape(byte[] body)
    {
        using var doc = JsonDocument.Parse(body);
        var r = doc.RootElement;
        var shortcuts = r.GetProperty("shortcuts").EnumerateArray().Select(x => x.GetProperty("url").GetString()).ToList();
        var icons = r.GetProperty("icons").EnumerateArray().Select(x => x.GetProperty("src").GetString()).Distinct().ToList();
        return $"id={r.GetProperty("id").GetString()} start={r.GetProperty("start_url").GetString()} scope={r.GetProperty("scope").GetString()} " +
               $"shortcuts=[{string.Join(",", shortcuts)}] icons=[{string.Join(",", icons)}]";
    }

    [Fact]
    public async Task The_store_and_role_manifests_answer_in_the_path_form_off_a_store_host_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var slug = NewSlug("w13m-");
        try
        {
            await PutStoreAsync(host, slug);
            var store = await host.GetAsync(StoreManifest, new Dictionary<string, string> { ["slug"] = slug });
            var vendor = await host.GetAsync(RoleManifest, new Dictionary<string, string> { ["slug"] = slug, ["role"] = "vendor" });
            var customer = await host.GetAsync(RoleManifest, new Dictionary<string, string> { ["slug"] = slug, ["role"] = "customer" });
            var missing = await host.GetAsync(StoreManifest, new Dictionary<string, string> { ["slug"] = slug + "x" });
            output.WriteLine($"[live] المَتجَر {store.Status} {store.ContentType} · {Shape(store.Body)}");
            output.WriteLine($"[live] البائِع {vendor.Status} · {Shape(vendor.Body)}");
            output.WriteLine($"[live] المُشتَري {customer.Status} · {Shape(customer.Body)}");
            output.WriteLine($"[live] مَتجَرٌ لا وُجودَ لَه ⇒ {missing.Status}");

            Assert.Equal((200, "application/manifest+json"), (store.Status, store.ContentType));
            Assert.Equal($"id=/{slug}/ start=/{slug}/ scope=/{slug}/ shortcuts=[/{slug}/explore,/{slug}/me] icons=[/api/{slug}/icon.svg,/api/{slug}/icon.svg?mask=1]",
                Shape(store.Body));
            Assert.Equal(200, vendor.Status);
            Assert.Equal($"id=/{slug}/r/vendor/ start=/{slug}/r/vendor/ scope=/{slug}/r/vendor/ " +
                         $"shortcuts=[/{slug}/r/vendor/create-listing,/{slug}/r/vendor/me/listings,/{slug}/r/vendor/chats] " +
                         $"icons=[/api/{slug}/r/vendor/icon.svg,/api/{slug}/r/vendor/icon.svg?mask=1]", Shape(vendor.Body));
            Assert.Equal(200, customer.Status);
            Assert.StartsWith($"id=/{slug}/r/customer/ start=/{slug}/r/customer/ scope=/{slug}/r/customer/ shortcuts=[/{slug}/r/customer/", Shape(customer.Body));
            Assert.Equal(404, missing.Status);
        }
        finally { await CleanAsync(host, [], slug); }
    }
}
