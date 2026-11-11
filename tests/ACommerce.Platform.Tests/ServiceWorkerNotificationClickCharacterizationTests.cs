using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ ما يَنجو قَبلَ أَن يُطابِقَ النَقرُ عَلى الإشعارِ تَبويبَ المُضيفِ بِعُنوانِه النَظيف (‏2026-11-11، مُراجَعَةُ الإغلاقِ الأَخيرِ لِـW13-4 الجَولَةُ الثالِثَة ·
//     W13-4·C2f-R3·1 — C2f-PATHS-R3-01) ═══
//
// `sw.js` يَفتَحُ رابِطَ الإشعارِ (`url` بِشَكلِ المَسارِ دائِماً — G15: ‏`/{slug}/deals/{id}`، `/{slug}/me`، والافتِراضُ `/{slug}`) ويُرَكِّزُ أَوَّلَ نافِذَةٍ يَحمِلُ
// عُنوانُها ذلك النَصَّ، وإلّا يَفتَحُ نافِذَةً. و`7a6980e` (C2f·6) جَعَلَ شَكلَ المَسارِ عَلى المُضيفِ 302 إلى العُنوانِ النَظيف — فَالنافِذَةُ الَّتي فَتَحَها الإشعارُ
// الأَوَّلُ عُنوانُها `/explore` لا `/ashare/explore`، والنَقرُ التالي لا يَجِدُها. **مَقيسٌ حَيّاً عِندَ `82b015b`** (Playwright عَلى Chrome 153، `BaseDomain=localhost`،
// نَقرٌ يُرسَلُ إلى العامِلِ الحَقيقيِّ بَعدَ تَنَقُّلٍ حَقيقيّ): عَلى `ashare.localhost` و`ashare-vendor.localhost` تَبويبا `/explore` و`/listings/{id}` (‏302 مِن
// شَكلِ المَسار) والإشعاران `/ashare/explore` و`/ashare/listings/{id}` ← **`openWindow`** (نافِذَةٌ مُكَرَّرَةٌ كُلَّ مَرَّة)؛ وعَلى الجَذرِ بِشَكلِ المَسارِ
// ← **`focus`**.
//
// **وما لا يَتَغَيَّرُ حينَ يُضافُ حُكمُ المُضيف — ويَبقى هذا المِلَفُّ بِبَصمَتِه**: (١) ما قَبلَ مُستَمِعِ `notificationclick` بِبايتاتِه (التَثبيتُ والتَفعيلُ والجَلبُ
// ومُستَمِعُ `push` الَّذي يَضَعُ `url` في الإشعار)؛ (٢) رَأسُ المُستَمِعِ إلى آخِرِ الحَلقَةِ الأُولى بِحَرفِه — حُكمُ وَضعِ المَسارِ (`c.url.includes(url)`) أَوَّلُ
// حُكمٍ وبِلا شَرط — وذَيلُه `openWindow(url)` آخِرَ شَيء؛ (٣) **مُقَدِّمَةُ أَنّ حُكمَ المُضيفِ لا يَبلُغُ وَضعَ المَسار**: نِطاقُ العامِلِ في وَضعِ المَسارِ
// `/{slug}/…` دائِماً (`VisibleBase` = `/{slug}` بِلا مُضيف، والصَدَفَةُ تَبنيه مِنها ولا تُسَجِّلُ بِلا مَتجَر) — فَشَرطُ «النِطاقُ `/`» لا يَصدُقُ هُناكَ أَبَداً؛
// (٤) والمِلَفُّ المَخدومُ هُوَ هذا: لا `sw.js` آخَرَ في المُستَودَعِ يَعلوه، و`url` الدَفعِ بِشَكلِ المَسار.

public class ServiceWorkerNotificationClickCharacterizationTests
{
    internal const string WorkerPath = "libs/templates/ACommerce.Templates.Customer.Marketplace/wwwroot/sw.js";
    internal const string ClickListener = "self.addEventListener('notificationclick'";

    /// <summary>النَصُّ بِأَسطُرِ LF — نُسخَةُ العَمَلِ عَلى Windows بِـ<c>core.autocrlf=true</c> تَأخُذُ CRLF، والبَصمَةُ لِلبايتاتِ المُودَعَة.</summary>
    internal static string Worker()
        => File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, WorkerPath)).Replace("\r\n", "\n");

    internal static string ClickBlock()
    {
        var s = Worker();
        var at = s.IndexOf(ClickListener, StringComparison.Ordinal);
        Assert.True(at > 0, "مُستَمِعُ notificationclick غائِب");
        var end = s.IndexOf("\n});\n", at, StringComparison.Ordinal);
        Assert.True(end > at, "نِهايَةُ المُستَمِعِ غائِبَة");
        return s[at..(end + 5)];
    }

    [Fact]
    public void Everything_before_the_click_listener_keeps_its_bytes()
    {
        var s = Worker();
        var head = s[..s.IndexOf(ClickListener, StringComparison.Ordinal)];
        Assert.Equal(3203, Encoding.UTF8.GetByteCount(head));
        Assert.Equal("613ab8bb7cdd7239b0095d635cc54f113a92e0c5f5f000531318d74aefc234b3",
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(head))));
        Assert.Contains("        data:  { url: data.url || '/' },\n", head, StringComparison.Ordinal);
        Assert.Equal(1, Regex.Matches(s, Regex.Escape(ClickListener)).Count);
    }

    /// <summary>حُكمُ وَضعِ المَسارِ أَوَّلُ حُكمٍ في المُستَمِعِ وبِلا شَرط، و<c>openWindow(url)</c> آخِرُه.</summary>
    [Fact]
    public void The_path_test_comes_first_unconditioned_and_opening_a_window_comes_last()
    {
        const string head =
            "self.addEventListener('notificationclick', (event) => {\n" +
            "    event.notification.close();\n" +
            "    const url = (event.notification.data && event.notification.data.url) || '/';\n" +
            "    event.waitUntil((async () => {\n" +
            "        const all = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });\n" +
            "        for (const c of all) {\n" +
            "            if (c.url.includes(url)) return c.focus();\n" +
            "        }\n";
        const string tail =
            "        return self.clients.openWindow(url);\n" +
            "    })());\n" +
            "});\n";
        var block = ClickBlock();
        Assert.StartsWith(head, block, StringComparison.Ordinal);
        Assert.EndsWith(tail, block, StringComparison.Ordinal);
        Assert.Equal(1, Regex.Matches(block, Regex.Escape("openWindow(")).Count);
    }

    private static HttpContext PathModeRequest(string path)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        return ctx;
    }

    /// <summary>المُقَدِّمَة: بِلا مُضيفِ مَتجَرٍ بادِئَةُ العُنوانِ <c>/{slug}</c> لِكُلِّ صَفحَة — فَنِطاقُ العامِلِ في وَضعِ المَسارِ لا يَكونُ <c>/</c>.</summary>
    [Theory]
    [InlineData("/ashare", "ashare")]
    [InlineData("/ashare/explore", "ashare")]
    [InlineData("/ashare/deals/3f2504e0-4f89-11d3-9a0c-0305e82c3301", "ashare")]
    [InlineData("/ashare/r/vendor/me", "ashare")]
    [InlineData("/ejar/me", "ejar")]
    [InlineData("/theme-demo/r/customer/explore", "theme-demo")]
    public void In_path_mode_the_worker_scope_is_under_the_store_never_the_root(string path, string slug)
    {
        Assert.Null(StoreHostPage.HostSlug(PathModeRequest(path)));
        Assert.Equal("/" + slug, StoreHostPage.VisibleBase(PathModeRequest(path), slug));
    }

    [Fact]
    public void The_shell_registers_the_worker_under_the_base_and_only_for_a_store()
    {
        var app = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs/templates/ACommerce.Templates.Customer.Marketplace/Components/App.razor")).Replace("\r\n", "\n");
        var guard = app.IndexOf("            if (!t.slug) return;   // صَفَحاتُ المَنَصَّة — لا PWA\n", StringComparison.Ordinal);
        var scope = app.IndexOf("            var scope = t.base + (t.role ? '/r/' + t.role : '') + '/';\n", StringComparison.Ordinal);
        var register = app.IndexOf("            navigator.serviceWorker.register('/sw.js', { scope: scope })\n", StringComparison.Ordinal);
        Assert.True(guard > 0 && scope > guard && register > scope, $"guard {guard} · scope {scope} · register {register}");
    }

    /// <summary>المِلَفُّ المَخدومُ هُوَ هذا (لا نُسخَةَ في <c>wwwroot</c> تَطبيقٍ تَعلوه — <c>MapGet("/sw.js")</c> يَبحَثُ فيها أَوَّلاً)، وعُنوانُ الدَفعِ بِشَكلِ المَسار.</summary>
    [Fact]
    public void The_worker_served_is_this_file_and_the_push_url_is_path_shaped()
    {
        var root = ThemeZeroEquivalenceTests.RepoRoot;
        var workers = new[] { "apps", "libs" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(root, d), "sw.js", SearchOption.AllDirectories))
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(f => !f.Contains("/bin/", StringComparison.Ordinal) && !f.Contains("/obj/", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(new[] { WorkerPath }, workers);

        var push = File.ReadAllText(Path.Combine(root, "libs/templates/ACommerce.Templates.Customer.Marketplace/Services/WebPushService.cs"));
        Assert.Contains("title, body, url = url ?? $\"/{tenantSlug}\",", push, StringComparison.Ordinal);
    }
}
