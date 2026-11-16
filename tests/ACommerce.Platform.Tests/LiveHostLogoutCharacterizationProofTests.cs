using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Tenants;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ بابِ الخُروجِ عَلى المُضيفاتِ لِما يَنجو، قَبلَ أَن يَمُرَّ الخُروجُ بِمُضيفاتِ المَتجَرِ كُلِّها (‏2026-11-07، مُراجَعَةُ C2b الجَولَةُ الثانِيَة ·
//     SEC-C2b-R2-2 — القاعِدَة ٣) ═══
//
// **ما يَتَغَيَّرُ عَمداً بَعدَ هذا المِلَفّ**: الكَعكاتُ مَقصورَةٌ عَلى مُضيفِها (ADR-050 §٥)، فَعُضوٌ دَخَلَ عَلى `{slug}.R` وعَلى `{slug}-vendor.R` ثُمَّ ضَغَطَ «خُروج»
// عَلى أَحَدِهِما يَبقى داخِلاً عَلى الآخَرِ ثَلاثينَ يَوماً (مَقيسٌ حَيّاً عِندَ f8e02c1: بَعدَ الخُروجِ عَلى مُضيفِ المَتجَرِ ما زالَ `/me` عَلى مُضيفِ البائِعِ يَرسُمُ
// رَقمَه). فَيَصيرُ الخُروجُ عَلى مُضيفٍ لِلمَتجَرِ — والطَلَبُ يَحمِلُ جَلسَةً، والمَتجَرُ يَملِكُ مُضيفاً آخَر — سِلسِلَةً تَمُرُّ بِكُلِّ مُضيفٍ لَه وتَنتَهي حَيثُ بَدَأَت.
// **وما يَنجو ويُثَبَّتُ هُنا** — والبابُ بِجِسمِه المُسَجَّلِ (`LiveTemplateDoorHost`)، والطَلَبُ كَما يَترُكُه الحاقِنُ اليَوم (الرَأسُ `Host` وعَناصِرُ `StoreHostItems`
// الثَلاثَة، والمَسارُ `/{slug}/auth/logout`): في وَضعِ المَسارِ 302 إلى `/{slug}`؛ وعَلى مُضيفِ المَتجَرِ ومُضيفِ الدَورِ — بِجَلسَةٍ وبِلا جَلسَةٍ وبِلا عُنصُرٍ يُسَمّي
// مُضيفاً آخَرَ لِلمَتجَر — 302 إلى `/` عَلى المُضيفِ نَفسِه؛ ويُمحى في كُلٍّ ما يَحمِلُه هذا المُتَصَفِّحُ لِلمَتجَر (الجَلسَةُ واسمُها ودَورُه واسمُه) لا غَيرُه.
// ولا يَقرَأُ البابُ القاعِدَةَ هُنا (لا عُنوانَ اشتِراكٍ في الاستِمارَة). والمِلَفُّ يَبقى بِبَصمَتِه بَعدَ التَبديل.

[Collection(LiveProofCollection.Name)]
public class LiveHostLogoutCharacterizationProofTests(ITestOutputHelper output)
{
    private const string Apex = "example.test";

    /// <summary>جَوابُ الباب: الحالَة، والوِجهَة، وأَسماءُ ما يَمحوه (المُعَرِّفُ ← <c>{slug}</c>).</summary>
    internal sealed record Answer(int Status, string Location, IReadOnlyList<string> Cleared);

    /// <summary>
    /// <para>يُرسِلُ <c>POST /{slug}/auth/logout</c> إلى البابِ المُسَجَّل. <paramref name="where"/>: <c>path</c> (بِلا مُضيف)، <c>store</c> (<c>{slug}.example.test</c>)،
    /// <c>vendor</c> (<c>{slug}-vendor.example.test</c>). <paramref name="signedIn"/>: الطَلَبُ يَحمِلُ جَلسَةَ عُضوٍ في المَتجَرِ وكوكيَّ دَورِ البائِع.
    /// و<paramref name="items"/> ما يَضَعُه الطَلَبُ في <c>HttpContext.Items</c> فَوقَ عَناصِرِ الحاقِن.</para>
    /// </summary>
    internal static async Task<Answer> LogoutAsync(LiveTemplateDoorHost host, string slug, string where, bool signedIn,
        IReadOnlyDictionary<string, object>? items = null, Guid? userId = null)
    {
        var door = host.Door("/{slug}/auth/logout");
        await using var scope = host.Services.CreateAsyncScope();
        var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        if (scope.ServiceProvider.GetService<IHttpContextAccessor>() is { } accessor) accessor.HttpContext = ctx;
        ctx.Request.Method = "POST";
        ctx.Request.Scheme = "http";
        ctx.Request.Host = new HostString(where switch { "store" => $"{slug}.{Apex}", "vendor" => $"{slug}-vendor.{Apex}", _ => "localhost" });
        ctx.Request.Path = $"/{slug}/auth/logout";
        ctx.Request.RouteValues["slug"] = slug;
        if (where != "path")
        {
            ctx.Items[StoreHostItems.Apex] = Apex;
            ctx.Items[StoreHostItems.Slug] = slug;
            if (where == "vendor") ctx.Items[StoreHostItems.Role] = "vendor";
        }
        foreach (var (k, v) in items ?? new Dictionary<string, object>()) ctx.Items[k] = v;
        if (signedIn)
        {
            var token = AuthHandlers.MakeToken(userId ?? Guid.NewGuid(), slug, 1);
            var generic = ACommerce.Templates.Customer.Marketplace.AuthSession.CookieName(slug);
            var role = ACommerce.Templates.Customer.Marketplace.AuthSession.CookieName(slug, "vendor");
            ctx.Request.Headers.Cookie = $"{generic}={token}; {generic}.name=x; {role}={token}; {role}.name=x";
        }
        ctx.Request.ContentType = "application/x-www-form-urlencoded";
        ctx.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>());
        ctx.Response.Body = new MemoryStream();
        ctx.SetEndpoint(door);
        await door.RequestDelegate!(ctx);
        var cleared = ctx.Response.Headers.SetCookie
            .Where(c => c is not null && c.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase))
            .Select(c => c!.Split('=', 2)[0].Replace(slug, "{slug}")).Distinct().Order(StringComparer.Ordinal).ToList();
        return new Answer(ctx.Response.StatusCode, ctx.Response.Headers.Location.ToString().Replace(slug, "{slug}"), cleared);
    }

    internal static readonly string[] AllFour =
        [".acommerce.auth.{slug}", ".acommerce.auth.{slug}.name", ".acommerce.auth.{slug}.vendor", ".acommerce.auth.{slug}.vendor.name"];

    [Theory]
    [InlineData("path", true, "/{slug}", 4)]
    [InlineData("store", true, "/", 4)]
    [InlineData("vendor", true, "/", 4)]
    [InlineData("store", false, "/", 2)]
    [InlineData("vendor", false, "/", 2)]
    [InlineData("path", false, "/{slug}", 2)]
    public async Task The_logout_door_answers_that_survive_on_Postgres(string where, bool signedIn, string location, int cleared)
    {
        if (!LiveSessionLogoutProofTests.Enabled) return;
        var slug = "zz-hlo-" + Guid.NewGuid().ToString("N")[..8];
        await using var host = LiveTemplateDoorHost.Build();
        var answer = await LogoutAsync(host, slug, where, signedIn);
        output.WriteLine($"{where} · {(signedIn ? "بِجَلسَة" : "بِلا جَلسَة")} ⇒ {answer.Status} → {answer.Location} · يَمحو {string.Join(" ، ", answer.Cleared)}");
        Assert.Equal(302, answer.Status);
        Assert.Equal(location, answer.Location);
        Assert.Equal(AllFour.Take(cleared).Order(StringComparer.Ordinal), answer.Cleared);
    }
}
