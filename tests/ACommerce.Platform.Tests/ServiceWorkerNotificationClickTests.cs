using System.Text.RegularExpressions;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.MultiTenancy;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ النَقرُ عَلى الإشعارِ يَجِدُ تَبويبَ المُضيفِ بِعُنوانِه النَظيف (‏2026-11-12، مُراجَعَةُ الإغلاقِ الأَخيرِ لِـW13-4 الجَولَةُ الثالِثَة · W13-4·C2f-R3·1 —
//     C2f-PATHS-R3-01) ═══
//
// رابِطُ الإشعارِ بِشَكلِ المَسار (G15)، والمُضيفُ يُحَوِّلُ شَكلَ المَسارِ إلى العُنوانِ النَظيف (C2f·6) — فَكانَ `c.url.includes(url)` لا يَجِدُ النافِذَةَ الَّتي
// فَتَحَها الإشعارُ الأَوَّل، ويَفتَحُ نافِذَةً مُكَرَّرَةً كُلَّ مَرَّة (مَقيسٌ حَيّاً عِندَ `82b015b`: ‏`open` عَلى المُضيفَينِ بِنِطاقِ `/` و`/r/vendor/`، و`focus` عَلى
// المَسار). **والآن** بَعدَ حُكمِ المَسارِ بِحَرفِه حُكمٌ ثانٍ: عُنوانُ النافِذَةِ كَما يَكتُبُه شَكلُ المَسار (`/{store}` + مَسارُها) يُسأَلُ السُؤالَ نَفسَه — عَلى نِطاقٍ
// بَناه أَساسٌ فارِغ (`/` أَو `/r/{role}/`، أَي صَفحَةٌ نَظيفَةٌ عَلى مُضيف) ومُضيفٍ مِلصَقُه المَتجَرُ أَو يَبدَأُ بِه. فَيَعمَلُ المُضيفُ كَما يَعمَلُ المَسار: حَيّاً بَعدَ
// التَبديلِ (Playwright عَلى Chrome 153) كُلُّ صَفٍّ عَلى المُضيفاتِ يُساوي صَفَّه عَلى المَسار. وما لا يَتَغَيَّرُ يُثَبِّتُه
// `ServiceWorkerNotificationClickCharacterizationTests` بِبايتاتِه.
//
// **ولِماذا نَصٌّ مُثَبَّتٌ لا تَنفيذ**: لا مُحَرِّكَ JS في الاختِبارات. فَما يُنَفَّذُ هُنا ما يُنَفَّذُ بِالمَعنى نَفسِه في .NET — التَعبيرانِ النَمَطِيّانِ بِنَصِّهِما
// المُستَخرَجِ مِن المِلَفّ — والسَطرُ الَّذي يَبني شَكلَ المَسارِ يُقابَلُ بِتَحويلِ الخادِمِ الحَقيقيِّ لِكُلِّ صَفحَةٍ عَلى كُلِّ مُضيف؛ وما سِواه بِنَصِّه.

public class ServiceWorkerNotificationClickTests(ITestOutputHelper output)
{
    private const string Apex = "example.test";
    private const string Id = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";

    internal const string Listener = """
        self.addEventListener('notificationclick', (event) => {
            event.notification.close();
            const url = (event.notification.data && event.notification.data.url) || '/';
            event.waitUntil((async () => {
                const all = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
                for (const c of all) {
                    if (c.url.includes(url)) return c.focus();
                }
                for (const c of all) {
                    const asPath = pathFormOnStoreHost(c.url, url);
                    if (asPath && asPath.includes(url)) return c.focus();
                }
                return self.clients.openWindow(url);
            })());
        });

        """;

    internal const string PathForm = """
        function pathFormOnStoreHost(href, url) {
            if (!/^\/(r\/[^/]+\/)?$/.test(new URL(self.registration.scope).pathname)) return null;
            const store = /^\/([^/?#]+)/.exec(url);
            if (!store) return null;
            const at = new URL(href);
            const label = at.hostname.split('.')[0];
            if (label !== store[1] && !label.startsWith(store[1] + '-')) return null;
            return at.origin + '/' + store[1] + (at.pathname === '/' ? '' : at.pathname) + at.search + at.hash;
        }

        """;

    /// <summary>حُكمُ المَسارِ أَوَّلاً بِحَرفِه، ثُمَّ شَكلُ المَسارِ لِعُنوانِ النافِذَة، ثُمَّ نافِذَةٌ جَديدَة — والدالَّةُ مَرَّةً بَعدَ المُستَمِع.</summary>
    [Fact]
    public void The_listener_asks_the_path_test_then_the_path_form_of_each_window_then_opens()
    {
        Assert.Equal(Listener, ServiceWorkerNotificationClickCharacterizationTests.ClickBlock());
        var s = ServiceWorkerNotificationClickCharacterizationTests.Worker();
        Assert.Single(Regex.Matches(s, Regex.Escape("function pathFormOnStoreHost(")));
        Assert.Equal(2, Regex.Matches(s, Regex.Escape("pathFormOnStoreHost(")).Count);
        Assert.True(s.IndexOf("function pathFormOnStoreHost(", StringComparison.Ordinal) > s.IndexOf(ServiceWorkerNotificationClickCharacterizationTests.ClickListener, StringComparison.Ordinal));
    }

    [Fact]
    public void The_path_form_is_built_only_under_a_clean_host_scope_for_this_hosts_store()
    {
        var s = ServiceWorkerNotificationClickCharacterizationTests.Worker();
        var at = s.IndexOf("function pathFormOnStoreHost(", StringComparison.Ordinal);
        Assert.Equal(PathForm, s[at..(s.IndexOf("\n}\n", at, StringComparison.Ordinal) + 3)]);
    }

    private static Regex JsRegex(string before, string after)
    {
        var s = ServiceWorkerNotificationClickCharacterizationTests.Worker();
        var from = s.IndexOf(before, StringComparison.Ordinal);
        Assert.True(from > 0, before);
        from += before.Length;
        return new Regex(s[from..s.IndexOf(after, from, StringComparison.Ordinal)], RegexOptions.CultureInvariant);
    }

    private static Regex ScopeGate => JsRegex("if (!/", "/.test(new URL(self.registration.scope).pathname)) return null;");
    private static Regex StoreOfUrl => JsRegex("const store = /", "/.exec(url);");

    /// <summary>النِطاقُ كَما تَبنيه الصَدَفَة: <c>t.base + (t.role ? '/r/' + t.role : '') + '/'</c> (<c>App.razor</c> — يُثَبِّتُه التَوصيف).</summary>
    private static string Scope(string @base, string role) => @base + (role.Length > 0 ? "/r/" + role : "") + "/";

    private static HttpContext Request(string path, string? hostSlug = null, string? visible = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        if (hostSlug is not null) { ctx.Items[StoreHostItems.Slug] = hostSlug; ctx.Items[StoreHostItems.OriginalPath] = visible; }
        return ctx;
    }

    /// <summary>
    /// <para><b>شَرطُ النِطاقِ بِنَصِّه المُستَخرَجِ مِن المِلَفّ</b>: لا يَصدُقُ عَلى نِطاقٍ في وَضعِ المَسارِ أَبَداً — لِكُلِّ مَتجَرٍ (ومِنه <c>r</c> و<c>rr</c> و<c>r-a</c>) ولِكُلِّ دَورٍ
    /// في الكَتالوجِ ومُؤَلَّفٍ وبِلا دَور — ويَصدُقُ عَلى كُلِّ نِطاقٍ يَبنيه أَساسٌ فارِغٌ عَلى مُضيف (لِكُلِّ مَتجَرٍ يَصلُحُ اسمُه مُضيفاً)، ولا يَصدُقُ عَلى صَفحَةٍ بَقِيَت بِشَكلِ المَسارِ عَلى مُضيف (الشُروط).
    /// يَطبَعُ ما فَحَصَه ويَفشَلُ صِفراً (القاعِدَة ١٠).</para>
    /// </summary>
    [Fact]
    public void The_scope_gate_never_holds_in_path_mode_and_holds_for_every_clean_host_scope()
    {
        var gate = ScopeGate;
        var slugs = new[] { "ashare", "ejar", "theme-demo", "r", "rr", "r-a", "a", "store-r" };
        var roles = new[] { "" }.Concat(RoleHostNames.ByCatalogSlug.Keys).Concat(new[] { "staff", "r", "best-vendor" }).ToArray();
        int path = 0, host = 0, kept = 0;
        foreach (var slug in slugs)
        foreach (var role in roles)
        {
            var pathBase = StoreHostPage.VisibleBase(Request($"/{slug}" + (role.Length > 0 ? $"/r/{role}" : "") + "/explore"), slug);
            Assert.False(gate.IsMatch(Scope(pathBase, role)), $"وَضعُ المَسار: {Scope(pathBase, role)}");
            path++;

            if (!StoreHostName.IsValid(slug)) continue;   // `r` لا مُضيفَ لَه (C2b-P2-01) — وَضعُ المَسارِ وَحدَه
            var cleanBase = StoreHostPage.VisibleBase(Request($"/{slug}/explore", slug, (role.Length > 0 ? $"/r/{role}" : "") + "/explore"), slug);
            Assert.Equal("", cleanBase);
            Assert.True(gate.IsMatch(Scope(cleanBase, role)), $"عَلى المُضيف: {Scope(cleanBase, role)}");
            host++;

            var keptBase = StoreHostPage.VisibleBase(Request($"/{slug}/terms", slug, $"/{slug}/terms"), slug);
            Assert.False(gate.IsMatch(Scope(keptBase, role)), $"بِشَكلِ المَسارِ عَلى المُضيف: {Scope(keptBase, role)}");
            kept++;
        }
        output.WriteLine($"نِطاقاتُ وَضعِ المَسار: {path} · يَصدُقُ عَلَيها: 0 · نِطاقاتُ مُضيفٍ نَظيفَة: {host} · يَصدُقُ: {host} · بِشَكلِ المَسارِ عَلى مُضيف: {kept} · يَصدُقُ: 0");
        Assert.True(path > 0 && host > 0 && kept > 0);
    }

    [Theory]
    [InlineData("/ashare/deals/" + Id, "ashare")]
    [InlineData("/ashare/me", "ashare")]
    [InlineData("/ashare", "ashare")]
    [InlineData("/ashare?x=1", "ashare")]
    [InlineData("/theme-demo/listings/" + Id, "theme-demo")]
    [InlineData("/admin/tenants/ashare/users", "admin")]
    [InlineData("/studio/apps/ashare/deals/" + Id, "studio")]
    [InlineData("/", null)]
    [InlineData("https://ashare.example.test/explore", null)]
    public void The_store_is_the_first_segment_of_the_push_url(string url, string? store)
    {
        var m = StoreOfUrl.Match(url);
        Assert.Equal(store, m.Success ? m.Groups[1].Value : null);
    }

    /// <summary>
    /// <para><b>شَكلُ المَسارِ لِعُنوانِ النافِذَةِ هُوَ الرابِطُ الَّذي يَفتَحُها</b>: لِكُلِّ قالِبِ صَفحَةِ مَتجَرٍ عَلى كُلِّ مُضيفٍ يُحَوِّلُه الخادِمُ إلى عُنوانٍ نَظيف
    /// <c>L</c>، فَـ<c>/{store}</c> + مَسارُ <c>L</c> (و<c>/</c> لا يُضاف) بِاستِعلامِه — ما يَبنيه السَطرُ الأَخيرُ في الدالَّة — يُحَوِّلُه الخادِمُ الحَقيقيُّ إلى <c>L</c> نَفسِه.
    /// فَالنافِذَةُ الَّتي فَتَحَها رابِطٌ هِيَ النافِذَةُ الَّتي يَجِدُها الرابِطُ نَفسُه. ومِلصَقُ كُلِّ مُضيفٍ المَتجَرُ أَو يَبدَأُ بِه. يَطبَعُ العَدَّ ويَفشَلُ صِفراً.</para>
    /// </summary>
    [Fact]
    public void The_path_form_of_every_clean_page_on_every_host_is_the_link_that_lands_on_it()
    {
        var hosts = new (string Host, string Role)[]
        {
            ("ashare.example.test", "customer"), ("ashare.example.test", "vendor"),
            ("ashare-vendor.example.test", "vendor"), ("ashare-customer.example.test", "customer"), ("ashare-admin.example.test", "tenant_admin"),
        };
        var templates = RoleHostPages.Templates.StoreTemplates;
        int landed = 0;
        foreach (var (host, role) in hosts)
        {
            var label = host.Split('.')[0];
            Assert.True(label == "ashare" || label.StartsWith("ashare-", StringComparison.Ordinal), host);
            foreach (var template in templates)
            {
                var path = template.Replace("{slug}", "ashare").Replace("{role}", role).Replace("{id:guid}", Id).Replace("{conversationId:guid}", Id)
                    .Replace("{vendorId:guid}", Id).Replace("{section}", "privacy").Replace("{planId}", "p1");
                var get = TenantResolverMiddleware.AnswerStoreHost(host, Apex, "GET", path, "?x=1", "https", RoleHostPages.Templates);
                if (get.Verdict != StoreHostVerdict.Redirect || get.Location is not { } location || !location.StartsWith('/')) continue;
                var q = location.IndexOf('?');
                var clean = location[..q];
                var pathForm = "/ashare" + (clean == "/" ? "" : clean);
                var again = TenantResolverMiddleware.AnswerStoreHost(host, Apex, "GET", pathForm, location[q..], "https", RoleHostPages.Templates);
                Assert.True((again.Verdict, again.Location) == (StoreHostVerdict.Redirect, location), $"{host}: {pathForm}{location[q..]} ⇒ {again.Verdict} {again.Location} لا {location}");
                landed++;
            }
        }
        output.WriteLine($"مُضيفات: {hosts.Length} · قَوالِب: {templates.Count} · عَناوينُ نَظيفَةٌ يَعودُ إلَيها شَكلُ مَسارِها: {landed}");
        Assert.True(landed > 0);
    }
}
