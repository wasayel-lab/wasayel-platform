using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.TenantConfig;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.LiveStoreBirthOnceCharacterizationTests;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ أَبوابِ أَيقونَةِ الـPWA الثَلاثَةِ وجَوابِ الأَيقونَةِ لِما يَنجو (‏2026-11-07، مُراجَعَةُ تَيّارِ C2a الجَولَةُ الأُولى · SEC-C2a-1 — القاعِدَة ٣) ═══
//
// **المَقيسُ قَبل** (عِندَ `14c6f29`، خادِمٌ حَيٌّ بِـ`MultiTenancy__BaseDomain=localhost` وقاعِدَةٌ مَحَلِّيَّةٌ جَديدَة): صاحِبُ `rewalk-111201` أَرسَلَ إلى
// `POST /studio/apps/rewalk-111201/pwa/save` جُزءَ `icon_customer` بِنَوعِ `image/svg+xml` وجِسمُه `<svg …><script>alert(document.domain)</script></svg>`
// فَأُجيبَ `302 …/pwa?saved=1`؛ ثُمَّ `GET /api/rewalk-111201/r/customer/icon.svg` ← 200 بِـ`Content-Type: image/svg+xml` والسكربتُ في الجِسم، بِلا CSP ولا
// `nosniff` — عَلى أَصلِ الجَذرِ نَفسِه الَّذي يَسكُنُه `/studio` و`/admin`، وعَلى مُضيفِ المَتجَرِ كَذلك. والعِلَّة: `PwaSaveService.AllowedContentTypes` فيها
// `image/svg+xml` والنَوعُ مِن رَأسِ الجُزء والبايتاتُ تُخزَنُ كَما وَصَلَت؛ و`BuildIconAsync` يُعيدُ المُخزَّنَ بِنَوعِه المُخزَّن؛ وأَداةُ الوَكيلِ `set_pwa` تَقبَلُ
// أَيَّ `data:image/…`. **وما يُثَبِّتُه هذا المِلَفُّ — وهُوَ ما يَجِبُ أَن يَنجوَ مِن الإصلاح، ولا يُعادُ حَرفٌ مِنه بَعدَه**: في الأَبوابِ الثَلاثَة (الاستوديو
// والإشرافُ وأَداةُ الوَكيل) صورَةُ PNG أَو WebP حَقيقِيَّةٌ تُحفَظُ وتُجابُ بِنَوعِها وبايتاتِها، وGIF والحَجمُ فَوقَ السَقفِ يُرَدّانِ بِرَمزَيهِما، والمَسحُ يُعيدُ
// المُوَلَّدَة، والأَيقونَةُ المُوَلَّدَةُ SVG بِلَونِ المَتجَر — بِأَبوابِ القالِبِ كَما يُسَجِّلُها `MapCustomerMarketplaceTemplate` عَلى Postgres. **وما يَتَغَيَّرُ
// عَمداً لا يَرِد**: SVG مَرفوعَةٌ أَو مُخزَّنَة، ونَوعٌ مُعلَنٌ يُخالِفُ بايتاتِه، ورُؤوسُ الجَواب.
// يَتَخَطّى نَفسَه ما لَم تُضبَط WASAYEL_LIVE_PROOF=1 وConnectionStrings__Postgres — عَقدُ إخوَتِه، وفي مَجموعَتِهِم التَسلسُلِيَّة.

[Collection(LiveProofCollection.Name)]
public class LivePwaIconCharacterizationTests(ITestOutputHelper output)
{
    private static bool Enabled => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    internal const string Color = "#1d4ed8";
    internal const string StudioSave = "/studio/apps/{slug}/pwa/save";
    internal const string AdminSave = "/admin/tenants/{slug}/pwa/save";
    internal const string RoleIcon = "/api/{slug}/r/{role}/icon.svg";

    /// <summary>‏PNG بِكسِلٌ واحِد — بَصمَتُه الثَمانِيَةُ الأُولى <c>89 50 4E 47 0D 0A 1A 0A</c>.</summary>
    internal static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    /// <summary>‏WebP بِكسِلٌ واحِد — <c>RIFF</c>…<c>WEBP</c>.</summary>
    internal static readonly byte[] Webp = Convert.FromBase64String("UklGRiQAAABXRUJQVlA4IBgAAAAwAQCdASoBAAEAAwA0JaQAA3AA/vuUAAA=");

    /// <summary>‏GIF — <c>GIF89a</c>، نَوعٌ خارِجَ القائِمَةِ قَبلُ وبَعد.</summary>
    internal static readonly byte[] Gif = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");

    /// <summary>مَتجَرٌ بِدَورَين، صاحِبُه <paramref name="owner"/> — يُكتَبُ مُباشَرَةً لا بِبابِ الإنشاء: الأَبوابُ المَقيسَةُ هُنا أَبوابُ الأَيقونَةِ وَحدَها.</summary>
    internal static async Task PutStoreAsync(LiveTemplateDoorHost host, string slug, Probe owner, string? customerIcon = null)
    {
        await using var s = host.Store.LightweightSession();
        s.Store(new Tenant
        {
            Id = slug, Name = "أَيقوناتُ الجَولَة", BrandColor = Color, OwnerUserId = owner.User.Id,
            Roles =
            [
                new Role { Slug = "customer", CatalogSlug = "customer", Label = "عَميل", PwaIconDataUrl = customerIcon },
                new Role { Slug = "vendor", CatalogSlug = "vendor", Label = "تاجِر" },
            ],
        });
        await s.SaveChangesAsync();
    }

    internal static async Task<string?> StoredIconAsync(LiveTemplateDoorHost host, string slug, string role = "customer")
        => (await TenantAsync(host, slug))!.Roles.Single(r => r.Slug == role).PwaIconDataUrl;

    internal static Dictionary<string, string> Route(string slug, string? role = null)
        => role is null ? new() { ["slug"] = slug } : new() { ["slug"] = slug, ["role"] = role };

    internal static string SetPwa(string slug, string iconUrl) => $$"""{"slug":"{{slug}}","role":"customer","pwa_icon_url":"{{iconUrl}}"}""";

    /// <summary>بابُ الاستوديو: PNG وWebP يُحفَظانِ ويُجابانِ بِنَوعِهِما وبايتاتِهِما؛ وGIF والحَجمُ فَوقَ السَقفِ يُرَدّانِ ولا يُكتَبُ شَيء؛ والمَسحُ يُعيدُ المُوَلَّدَة.</summary>
    [Fact]
    public async Task The_studio_door_keeps_a_real_png_or_webp_and_refuses_gif_and_oversize_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var owner = await UserAsync(host);
        var slug = NewSlug("w13i-");
        try
        {
            await PutStoreAsync(host, slug, owner);
            var gif = await host.PostStudioMultipartAsync(StudioSave, Route(slug), owner.Token, [("name_customer", "")], ("icon_customer", "a.gif", "image/gif", Gif));
            var gifStored = await StoredIconAsync(host, slug);
            var big = Png.Concat(new byte[PwaSaveService.MaxIconBytes + 1 - Png.Length]).ToArray();
            var large = await host.PostStudioMultipartAsync(StudioSave, Route(slug), owner.Token, [("name_customer", "")], ("icon_customer", "big.png", "image/png", big));
            var largeStored = await StoredIconAsync(host, slug);
            var png = await host.PostStudioMultipartAsync(StudioSave, Route(slug), owner.Token, [("name_customer", "")], ("icon_customer", "i.png", "image/png", Png));
            var pngStored = await StoredIconAsync(host, slug);
            var pngIcon = await host.GetAsync(RoleIcon, Route(slug, "customer"));
            var webp = await host.PostStudioMultipartAsync(StudioSave, Route(slug), owner.Token, [("name_customer", "")], ("icon_customer", "i.webp", "image/webp", Webp));
            var webpIcon = await host.GetAsync(RoleIcon, Route(slug, "customer"));
            var clear = await host.PostStudioMultipartAsync(StudioSave, Route(slug), owner.Token, [("name_customer", ""), ("clear_customer", "1")]);
            var cleared = await host.GetAsync(RoleIcon, Route(slug, "customer"));
            output.WriteLine($"[live] gif ⇒ {gif} · كَبيرَة ⇒ {large} · png ⇒ {png} {pngIcon.Status} {pngIcon.ContentType} · webp ⇒ {webp} {webpIcon.ContentType} · مَسح ⇒ {clear} {cleared.ContentType}");

            Assert.Equal((302, $"/studio/apps/{slug}/pwa?err={TenantConfigCodes.IconBadType}"), gif);
            Assert.Null(gifStored);
            Assert.Equal((302, $"/studio/apps/{slug}/pwa?err={TenantConfigCodes.IconTooLarge}"), large);
            Assert.Null(largeStored);
            Assert.Equal((302, $"/studio/apps/{slug}/pwa?saved=1"), png);
            Assert.Equal("data:image/png;base64," + Convert.ToBase64String(Png), pngStored);
            Assert.Equal((200, "image/png"), (pngIcon.Status, pngIcon.ContentType));
            Assert.Equal(Png, pngIcon.Body);
            Assert.Equal((302, $"/studio/apps/{slug}/pwa?saved=1"), webp);
            Assert.Equal((200, "image/webp"), (webpIcon.Status, webpIcon.ContentType));
            Assert.Equal(Webp, webpIcon.Body);
            Assert.Equal((302, $"/studio/apps/{slug}/pwa?saved=1"), clear);
            Assert.Null(await StoredIconAsync(host, slug));
            Assert.Equal((200, "image/svg+xml; charset=utf-8"), (cleared.Status, cleared.ContentType));
        }
        finally { await CleanAsync(host, [owner], slug); }
    }

    /// <summary>بابُ الإشراف (صاحِبُ المَتجَرِ مِن الاستوديو يَجتازُ حارِسَه): PNG يُحفَظُ ويُجابُ بِنَوعِه وبايتاتِه؛ وGIF يُرَدّ.</summary>
    [Fact]
    public async Task The_admin_door_keeps_a_real_png_and_refuses_gif_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var owner = await UserAsync(host);
        var slug = NewSlug("w13i-");
        try
        {
            await PutStoreAsync(host, slug, owner);
            var gif = await host.PostStudioMultipartAsync(AdminSave, Route(slug), owner.Token, [("name_customer", "")], ("icon_customer", "a.gif", "image/gif", Gif));
            var png = await host.PostStudioMultipartAsync(AdminSave, Route(slug), owner.Token, [("name_customer", "")], ("icon_customer", "i.png", "image/png", Png));
            var icon = await host.GetAsync(RoleIcon, Route(slug, "customer"));
            output.WriteLine($"[live] gif ⇒ {gif} · png ⇒ {png} {icon.Status} {icon.ContentType} {icon.Body.Length}");
            Assert.Equal((302, $"/admin/tenants/{slug}/pwa?err={TenantConfigCodes.IconBadType}"), gif);
            Assert.Equal((302, $"/admin/tenants/{slug}/pwa?saved=1"), png);
            Assert.Equal("data:image/png;base64," + Convert.ToBase64String(Png), await StoredIconAsync(host, slug));
            Assert.Equal((200, "image/png"), (icon.Status, icon.ContentType));
            Assert.Equal(Png, icon.Body);
        }
        finally { await CleanAsync(host, [owner], slug); }
    }

    /// <summary>أَداةُ الوَكيل <c>set_pwa</c>: ‏<c>data:image/png;base64,…</c> حَقيقِيٌّ يُحفَظُ ويُجابُ بِنَوعِه وبايتاتِه؛ وما لَيسَ <c>data:image/…</c> يُرَدُّ بِنَصِّه؛
    /// والفارِغُ يَمسَح.</summary>
    [Fact]
    public async Task The_agent_tool_keeps_a_real_png_data_url_and_refuses_a_non_data_url_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var owner = await UserAsync(host);
        var slug = NewSlug("w13i-");
        try
        {
            await PutStoreAsync(host, slug, owner);
            var exec = new AgentToolExecutor(host.Store, new TenantRoleService(host.Store));
            var remote = await exec.ExecuteAsync("set_pwa", SetPwa(slug, "https://example.test/i.png"), owner.User.Id);
            var remoteStored = await StoredIconAsync(host, slug);
            var dataUrl = "data:image/png;base64," + Convert.ToBase64String(Png);
            var png = await exec.ExecuteAsync("set_pwa", SetPwa(slug, dataUrl), owner.User.Id);
            var pngStored = await StoredIconAsync(host, slug);
            var icon = await host.GetAsync(RoleIcon, Route(slug, "customer"));
            var clear = await exec.ExecuteAsync("set_pwa", SetPwa(slug, ""), owner.User.Id);
            output.WriteLine($"[live] رابِط ⇒ {remote} · png ⇒ {png} {icon.Status} {icon.ContentType} · فارِغ ⇒ {clear}");
            Assert.Equal((false, "pwa_icon_url يَجِب أَن يَكون data:image/…"), remote);
            Assert.Null(remoteStored);
            Assert.Equal((true, "تَمّ تَحديث PWA «customer»: الأَيقونَة."), png);
            Assert.Equal(dataUrl, pngStored);
            Assert.Equal((200, "image/png"), (icon.Status, icon.ContentType));
            Assert.Equal(Png, icon.Body);
            Assert.Equal((true, "تَمّ تَحديث PWA «customer»: الأَيقونَة (حُذِفَت)."), clear);
            Assert.Null(await StoredIconAsync(host, slug));
        }
        finally { await CleanAsync(host, [owner], slug); }
    }

    /// <summary>الأَيقونَةُ المُوَلَّدَة: دَورٌ بِلا أَيقونَةٍ مُخَصَّصَةٍ والمَتجَرُ بِلا دَور ← 200 SVG بِلَونِ المَتجَرِ وحَرفِ الدَور؛ ومَتجَرٌ لا وُجودَ لَه ← 404.</summary>
    [Fact]
    public async Task The_generated_icon_is_an_svg_in_the_store_colour_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var owner = await UserAsync(host);
        var slug = NewSlug("w13i-");
        try
        {
            await PutStoreAsync(host, slug, owner);
            var vendor = await host.GetAsync(RoleIcon, Route(slug, "vendor"));
            var store = await host.GetAsync("/api/{slug}/icon.svg", Route(slug));
            var missing = await host.GetAsync(RoleIcon, Route(slug + "-none", "vendor"));
            var body = System.Text.Encoding.UTF8.GetString(vendor.Body);
            output.WriteLine($"[live] تاجِر ⇒ {vendor.Status} {vendor.ContentType} · المَتجَر ⇒ {store.Status} {store.ContentType} · مَجهول ⇒ {missing.Status}");
            Assert.Equal((200, "image/svg+xml; charset=utf-8"), (vendor.Status, vendor.ContentType));
            Assert.Contains($"fill=\"{Color}\"", body, StringComparison.Ordinal);
            Assert.Contains(">ت</text>", body, StringComparison.Ordinal);
            Assert.Equal((200, "image/svg+xml; charset=utf-8"), (store.Status, store.ContentType));
            Assert.Equal(404, missing.Status);
        }
        finally { await CleanAsync(host, [owner], slug); }
    }
}
