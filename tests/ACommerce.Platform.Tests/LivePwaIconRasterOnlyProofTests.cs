using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.TenantConfig;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.LivePwaIconCharacterizationTests;
using static ACommerce.Platform.Tests.LiveStoreBirthOnceCharacterizationTests;

namespace ACommerce.Platform.Tests;

// ═══ لا SVG في أَيقونَةِ الـPWA: الأَبوابُ الثَلاثَةُ تَرُدُّها، والمُخزَّنُ مِنها لا يُعادُ أَبَداً (‏2026-11-07، مُراجَعَةُ C2a الجَولَةُ الأُولى · SEC-C2a-1) ═══
//
// **المَقيسُ قَبل** (عِندَ `14c6f29`): ‏`POST /studio/apps/{slug}/pwa/save` بِـ`image/svg+xml` وسكربت ← `302 ?saved=1`، ثُمَّ أَيقونَةُ الدَور ← 200 `image/svg+xml`
// والسكربتُ في الجِسمِ بِلا CSP ولا `nosniff`. **والآن** — بِأَبوابِ القالِبِ كَما يُسَجِّلُها `MapCustomerMarketplaceTemplate` عَلى Postgres: لِكُلِّ بابٍ سالِبٌ
// (SVG ← رَمزُ النَوع ولا يُكتَبُ شَيء) وموجَبُه في `LivePwaIconCharacterizationTests` (PNG يُحفَظُ ويُجاب)؛ وSVG مُخزَّنَةٌ قَبلَ الإصلاحِ تُجابُ بِالأَيقونَةِ
// المُوَلَّدَةِ لا بِبايتاتِها؛ وكُلُّ جَوابٍ لِلأَيقونَةِ يَحمِلُ `Content-Security-Policy: default-src 'none'; sandbox` و`X-Content-Type-Options: nosniff`.

[Collection(LiveProofCollection.Name)]
public class LivePwaIconRasterOnlyProofTests(ITestOutputHelper output)
{
    private static bool Enabled => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private static readonly byte[] Svg = PwaIconRasterOnlyTests.Svg;

    private static void AssertSandboxed(IReadOnlyDictionary<string, string> headers)
    {
        Assert.Equal("default-src 'none'; sandbox", headers.GetValueOrDefault("Content-Security-Policy"));
        Assert.Equal("nosniff", headers.GetValueOrDefault("X-Content-Type-Options"));
    }

    /// <summary>بابُ الاستوديو: SVG بِنَوعِها ← <c>icon_bad_type</c>، وSVG بِرَأسِ <c>image/png</c> ← <c>icon_bad_type</c> — ولا يُكتَبُ شَيء.</summary>
    [Fact]
    public async Task The_studio_door_refuses_svg_and_svg_bytes_under_a_png_header_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var owner = await UserAsync(host);
        var slug = NewSlug("w13s-");
        try
        {
            await PutStoreAsync(host, slug, owner);
            var svg = await host.PostStudioMultipartAsync(StudioSave, Route(slug), owner.Token, [("name_customer", "")], ("icon_customer", "x.svg", "image/svg+xml", Svg));
            var disguised = await host.PostStudioMultipartAsync(StudioSave, Route(slug), owner.Token, [("name_customer", "")], ("icon_customer", "x.png", "image/png", Svg));
            var stored = await StoredIconAsync(host, slug);
            output.WriteLine($"[live] svg ⇒ {svg} · svg بِرَأسِ png ⇒ {disguised} · المُخزَّن {stored ?? "(لا شَيء)"}");
            Assert.Equal((302, $"/studio/apps/{slug}/pwa?err={TenantConfigCodes.IconBadType}"), svg);
            Assert.Equal((302, $"/studio/apps/{slug}/pwa?err={TenantConfigCodes.IconBadType}"), disguised);
            Assert.Null(stored);
        }
        finally { await CleanAsync(host, [owner], slug); }
    }

    /// <summary>بابُ الإشراف: SVG ← <c>icon_bad_type</c> ولا يُكتَبُ شَيء.</summary>
    [Fact]
    public async Task The_admin_door_refuses_svg_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var owner = await UserAsync(host);
        var slug = NewSlug("w13s-");
        try
        {
            await PutStoreAsync(host, slug, owner);
            var svg = await host.PostStudioMultipartAsync(AdminSave, Route(slug), owner.Token, [("name_customer", "")], ("icon_customer", "x.svg", "image/svg+xml", Svg));
            output.WriteLine($"[live] svg ⇒ {svg}");
            Assert.Equal((302, $"/admin/tenants/{slug}/pwa?err={TenantConfigCodes.IconBadType}"), svg);
            Assert.Null(await StoredIconAsync(host, slug));
        }
        finally { await CleanAsync(host, [owner], slug); }
    }

    /// <summary>أَداةُ الوَكيل: <c>data:image/svg+xml</c> و<c>data:image/png</c> بِبايتاتِ SVG ← نَصُّ رَمزِ النَوعِ مِن القاموس، ولا يُكتَبُ شَيء.</summary>
    [Fact]
    public async Task The_agent_tool_refuses_svg_data_urls_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var owner = await UserAsync(host);
        var slug = NewSlug("w13s-");
        var typeText = LocaleCatalog.Text(LocaleCatalog.Arabic, "studio.app_pwa.err_icon_type");
        try
        {
            await PutStoreAsync(host, slug, owner);
            var exec = new AgentToolExecutor(host.Store, new TenantRoleService(host.Store));
            var svg = await exec.ExecuteAsync("set_pwa", SetPwa(slug, "data:image/svg+xml;base64," + Convert.ToBase64String(Svg)), owner.User.Id);
            var disguised = await exec.ExecuteAsync("set_pwa", SetPwa(slug, "data:image/png;base64," + Convert.ToBase64String(Svg)), owner.User.Id);
            output.WriteLine($"[live] svg ⇒ {svg} · svg بِرَأسِ png ⇒ {disguised}");
            Assert.Matches("[؀-ۿ]", typeText);
            Assert.Equal((false, typeText), svg);
            Assert.Equal((false, typeText), disguised);
            Assert.Null(await StoredIconAsync(host, slug));
        }
        finally { await CleanAsync(host, [owner], slug); }
    }

    /// <summary><b>المُخزَّنُ قَبلَ الإصلاح</b>: SVG بِسكربتٍ مَكتوبَةٌ في الوَثيقَة (كَما كَتَبَها البابُ القَديم) ← الأَيقونَةُ المُوَلَّدَة، لا بايتاتُها ولا سكربتُها؛
    /// وPNG مُخزَّنٌ يُجابُ بِبايتاتِه — وكُلُّ جَوابٍ في عُلبَتِه (CSP sandbox وnosniff)، والمُوَلَّدَةُ كَذلك.</summary>
    [Fact]
    public async Task A_stored_svg_is_never_replayed_and_every_icon_answer_is_sandboxed_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var owner = await UserAsync(host);
        var (bad, good) = (NewSlug("w13s-"), NewSlug("w13s-"));
        try
        {
            await PutStoreAsync(host, bad, owner, customerIcon: "data:image/svg+xml;base64," + Convert.ToBase64String(Svg));
            await PutStoreAsync(host, good, owner, customerIcon: "data:image/png;base64," + Convert.ToBase64String(Png));
            var replay = await host.GetAsync(RoleIcon, Route(bad, "customer"));
            var raster = await host.GetAsync(RoleIcon, Route(good, "customer"));
            var generated = await host.GetAsync(RoleIcon, Route(good, "vendor"));
            var body = System.Text.Encoding.UTF8.GetString(replay.Body);
            output.WriteLine($"[live] svg مُخزَّنَة ⇒ {replay.Status} {replay.ContentType} سكربت={body.Contains("<script", StringComparison.OrdinalIgnoreCase)} · png ⇒ {raster.ContentType} · مُوَلَّدَة ⇒ {generated.ContentType}");
            Assert.Equal((200, "image/svg+xml; charset=utf-8"), (replay.Status, replay.ContentType));
            Assert.DoesNotContain("<script", body, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(Svg, replay.Body);
            Assert.Contains($"fill=\"{Color}\"", body, StringComparison.Ordinal);
            Assert.Equal((200, "image/png"), (raster.Status, raster.ContentType));
            Assert.Equal(Png, raster.Body);
            AssertSandboxed(replay.Headers);
            AssertSandboxed(raster.Headers);
            AssertSandboxed(generated.Headers);
            Assert.Equal("data:image/svg+xml;base64," + Convert.ToBase64String(Svg), await StoredIconAsync(host, bad));   // لا هِجرَةَ ولا كِتابَة
        }
        finally { await CleanAsync(host, [owner], bad, good); }
    }
}
