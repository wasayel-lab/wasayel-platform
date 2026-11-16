using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ACommerce.Kit.Auth.Server;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using ACommerce.Templates.Customer.Marketplace.Services.Queries;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using StudioConsent = ACommerce.Templates.Customer.Marketplace.Components.Pages.StudioConsent;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ «اليَوم» — `/studio/consent` تَطبَعُ `returnUrl` كَما جاءَ في سِكربتِ التَحويلِ وفي الحَقلِ المَخفِيّ (تَدقيقُ الإطلاق lens-security-1، حاجِبَة) ═══
//
// **المَقيس** (‏2026-10-24، عِندَ `def5fd2`): ‏`StudioConsent.razor` تَرسُمُ لِمَن قَبِلَ الشُروطَ سَلَفاً
// `<script>location.replace('@(ReturnUrl ?? "/studio")');</script>`، ولِمَن لَم يَقبَل حَقلاً مَخفِيّاً `returnUrl` بِالقيمَةِ نَفسِها —
// و`ReturnUrl` مَربوطٌ بِسِلسِلَةِ الاستِعلامِ (‏`[SupplyParameterFromQuery]`) ولا يَمُرُّ بِـ`LocalRedirect`. فَرابِطٌ واحِدٌ
// `/studio/consent?returnUrl=javascript:alert(document.domain)` يُرسَلُ إلى مُشرِفِ المَنَصَّةِ (كوكي الاستوديو هُوِيَّتُه —
// `PlatformAdminGuard`) يُنَفِّذُ سِكربتاً في أَصلِنا بِنَقرَة، و`https://evil.example/x` يَقذِفُه إلى مُضيفٍ آخَر. والتَرميزُ لا يَحمي:
// `HtmlEncoder` يُرَمِّزُ `'` و`<` فَلا يُكسَرُ نَصُّ السِكربت، لكِنّ `javascript:` لا يَحتاجُ كَسرَه. ونُقطَةُ القَبولِ
// (`/studio/consent/accept`) تَسأَلُ `LocalRedirect` أَصلاً — فَالحَقلُ المَخفِيُّ لا يُحَوِّلُ إلى الخارِجِ اليَوم، والسِكربتُ يُحَوِّل.
//
// **يُثَبَّتُ قَبلَ العِلاجِ** (القاعِدَة ٣): الصَفّانِ الأَوَّلانِ (مَسارٌ مَحَلِّيٌّ وغِيابُ الوِجهَة) يَبقَيانِ بِلا تَعديلِ حَرفٍ ويَطبَعانِ
// طولَ الصَفحَةِ وبَصمَتَها لِيُقارَنا قَبلَ العِلاجِ وبَعدَه؛ والثالِثُ (`Today_…`) يَنقَلِبُ بِاسمِه في كوميتِ العِلاج.
//
// **والصَفحَةُ مَرسومَةٌ لا مَقروءَة**: بِـ`HtmlRenderer` (نَمَطُ `UpgradePromptPriceTests`) بِلا قاعِدَةٍ ولا خادِم — كوكي استوديو
// مُوَقَّعٌ بِـ`AuthHandlers.MakeToken` يَجعَلُ `StudioAuth.Load()` يَرى مُستَخدِماً، وصِنفٌ مُشتَقٌّ يُبَدِّلُ `OnInitializedAsync` وَحدَه
// فَيَضَعُ «قَبِلَ/لَم يَقبَل» بَدَلَ سُؤالِ `StudioQueries` القاعِدَة. وما يُرسَمُ بَعدَها هُوَ وَسمُ الصَفحَةِ نَفسُه حَرفاً.
//
// **العِلاج** (‏2026-10-24): المَوضِعانِ يَطبَعانِ `Destination` = `LocalRedirect.Resolve(ReturnUrl, "/studio")` — القَرارُ الواحِدُ الَّذي
// تَسأَلُه نُقطَةُ القَبولِ أَصلاً، والسُقوطُ `/studio` كَما كانَ لِلغِياب. الصَفُّ الثالِثُ انقَلَبَ بِاسمِه، والأَوَّلانِ كَما أُودِعا
// وبَصَماتُهُما المَطبوعَةُ لَم تَتَغَيَّر. و`LocalRedirectTests` صارَ يَقرَأُ كُلَّ صَفحَةٍ تَربِطُ `return`/`returnUrl` بِالاستِعلام.
public class StudioConsentReturnUrlTests(ITestOutputHelper output)
{
    /// <summary>الصَفحَةُ نَفسُها بِلا سُؤالِ القاعِدَة: «قَبِلَ سَلَفاً» يُكتَبُ في الحَقلِ الخاصِّ بِالصَفحَة، و<c>ReturnUrl</c> يُضبَطُ كَما
    /// يَضبِطُه مُزَوِّدُ الاستِعلام.</summary>
    private sealed class ConsentProbe : StudioConsent
    {
        [Parameter] public bool Consented { get; set; }
        [Parameter] public string? Next { get; set; }

        protected override Task OnInitializedAsync()
        {
            ReturnUrl = Next;
            var field = typeof(StudioConsent).GetField("alreadyConsented",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.True(field is not null, "لا حَقلَ `alreadyConsented` في الصَفحَة — الأَداةُ عَمياء.");
            field!.SetValue(this, Consented);
            return Task.CompletedTask;
        }
    }

    internal static async Task<string> RenderAsync(bool consented, string? returnUrl)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie =
            $"{StudioAuth.CookieName}={AuthHandlers.MakeToken(Guid.NewGuid(), StudioAuth.Tenant)}";
        var accessor = new HttpContextAccessor { HttpContext = http };

        await using var services = new ServiceCollection()
            .AddSingleton(new L(new HttpContextAccessor()))
            .AddSingleton(new StudioAuth(accessor))
            // لا يُسأَلُ أَبَداً: `ConsentProbe` يُبَدِّلُ السُؤالَ الوَحيدَ الَّذي يَبلُغُه.
            .AddSingleton((StudioQueries)RuntimeHelpers.GetUninitializedObject(typeof(StudioQueries)))
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services,
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<ConsentProbe>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ConsentProbe.Consented)] = consented,
                [nameof(ConsentProbe.Next)] = returnUrl,
            }))).ToHtmlString());
    }

    private static readonly Regex Redirect = new(@"<script>location\.replace\('(?<to>[^']*)'\);</script>");
    private static readonly Regex Hidden = new(@"<input type=""hidden"" name=""returnUrl"" value=""(?<to>[^""]*)""");

    /// <summary>وِجهَةُ سِكربتِ «قَبِلَ سَلَفاً» كَما تُطبَع — وواحِدَةٌ لا غَير.</summary>
    internal static string ScriptTarget(string html)
    {
        var all = Redirect.Matches(html);
        Assert.True(all.Count == 1, $"سِكربتاتُ التَحويلِ في صَفحَةِ «قَبِلَ سَلَفاً»: {all.Count} — الأَداةُ عَمياء أَو الصَفحَةُ تَغَيَّرَت.");
        return all[0].Groups["to"].Value;
    }

    /// <summary>قيمَةُ الحَقلِ المَخفِيِّ في استِمارَةِ القَبولِ كَما تُطبَع — وواحِدٌ لا غَير.</summary>
    internal static string HiddenTarget(string html)
    {
        var all = Hidden.Matches(html);
        Assert.True(all.Count == 1, $"حُقولُ `returnUrl` المَخفِيَّةُ في استِمارَةِ القَبول: {all.Count} — الأَداةُ عَمياء أَو الصَفحَةُ تَغَيَّرَت.");
        Assert.Contains("action=\"/studio/consent/accept\"", html, StringComparison.Ordinal);
        return all[0].Groups["to"].Value;
    }

    private void Print(string label, string html) =>
        output.WriteLine($"{label}: {html.Length} حَرفاً · sha256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(html)))[..16]}");

    // ─── ١. المَسارُ المَحَلِّيُّ يَمُرُّ كَما هُوَ إلى المَوضِعَين — لا يَتَغَيَّرُ بِالعِلاج ─────────────

    [Theory]
    [InlineData("/studio")]
    [InlineData("/studio/s/3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    [InlineData("/studio/apps/theme-demo?tab=deals")]
    public async Task A_local_return_url_reaches_the_redirect_script_and_the_hidden_field_unchanged(string local)
    {
        var consented = await RenderAsync(consented: true, local);
        var pending = await RenderAsync(consented: false, local);
        Print($"consented «{local}»", consented);
        Print($"pending «{local}»", pending);

        Assert.Equal(System.Net.WebUtility.HtmlEncode(local), ScriptTarget(consented));
        Assert.Equal(System.Net.WebUtility.HtmlEncode(local), HiddenTarget(pending));
        Assert.DoesNotContain("<form", consented, StringComparison.Ordinal);
    }

    // ─── ٢. غِيابُ الوِجهَةِ يَسقُطُ إلى `/studio` — لا يَتَغَيَّرُ بِالعِلاج ──────────────────────────

    [Fact]
    public async Task No_return_url_falls_to_studio_in_both_renders()
    {
        var consented = await RenderAsync(consented: true, null);
        var pending = await RenderAsync(consented: false, null);
        Print("consented «null»", consented);
        Print("pending «null»", pending);

        Assert.Equal("/studio", ScriptTarget(consented));
        Assert.Equal("/studio", HiddenTarget(pending));
    }

    // ─── ٣. الوِجهَةُ الخارِجِيَّةُ تَسقُطُ إلى `/studio` في المَوضِعَين ─────────────────────────────────
    //
    // انقَلَبَ بِاسمِه (‏2026-10-24) مِن `Today_a_foreign_return_url_reaches_the_redirect_script_and_the_hidden_field`. والفارِغُ مَعَها:
    // كانَ `?returnUrl=` يُعطي `location.replace('')` — إعادَةُ تَحميلِ الصَفحَةِ نَفسِها، دَورَةٌ لا تَنتَهي لِمَن قَبِلَ سَلَفاً.

    [Theory]
    [InlineData("javascript:alert(document.domain)")]
    [InlineData("https://evil.example/x")]
    [InlineData("//evil.example")]
    [InlineData("")]
    public async Task A_foreign_return_url_falls_to_studio_in_both_renders(string foreign)
    {
        var consented = await RenderAsync(consented: true, foreign);
        var pending = await RenderAsync(consented: false, foreign);

        Assert.Equal("/studio", ScriptTarget(consented));
        Assert.Equal("/studio", HiddenTarget(pending));
        if (foreign.Length > 0)
        {
            Assert.DoesNotContain(System.Net.WebUtility.HtmlEncode(foreign), consented, StringComparison.Ordinal);
            Assert.DoesNotContain(System.Net.WebUtility.HtmlEncode(foreign), pending, StringComparison.Ordinal);
        }
    }
}
