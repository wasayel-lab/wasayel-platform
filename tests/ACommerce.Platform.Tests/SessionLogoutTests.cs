using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Tenants;
using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ الخُروجُ مِن هذا الجِهازِ حينَ تَتَعَثَّرُ كِتابَتُه — تَوصيفٌ قَبلَ التَبديل (‏2026-11-05، الموجَة ١٢ G2 · ٢؛ مُراجَعَةُ F1 عَلى `bcd83f7`، known-leftovers:149) ═══
//
// بابُ `/{slug}/auth/logout` صارَ في `bcd83f7` يَسحَبُ اشتِراكاتِ هذا المُتَصَفِّحِ مِن الحِسابِ (`SessionVersionGate.ForgetDeviceAsync`، بَعدَ `ReadFormAsync`) **قَبلَ**
// أَن يَقرَأَ أَدوارَ المَتجَرِ ويَمسَحَ الكوكيّات — بِلا حِراسَة. فَعَطَبُ قاعِدَةٍ أَو تَعارُضٌ أَو استِمارَةٌ مَكسورَةٌ يُسقِطُ الخُروجَ نَفسَه: بابُ أَمانٍ لا يَفعَلُ ما يَقولُه
// (القاعِدَة ١٢).
//
// **كَيفَ يُقاسُ هُنا**: البابُ بِجِسمِه المُسَجَّلِ في المُضيفِ المُرَكَّب (`LiveTemplateDoorHost`)؛ والمَخزَنُ إمّا Postgres (الحالُ السَليمَة)، وإمّا عالَمُ وَثائِقَ في
// الذاكِرَةِ (`DocWorld`) يُعطى لِلبابِ بَدَلَ مَخزَنِ المُضيفِ: كِتابَتُه تَرمي، أَو كُلُّ نِداءٍ عَلَيه يَرمي (القاعِدَةُ غائِبَة). والطَلَبُ يَحمِلُ كوكيَّ الجَلسَةِ وكوكيَّ
// اسمِه وكوكيَّ دَورٍ، وعُنوانَ اشتِراكِ هذا المُتَصَفِّح. ويُقرَأُ ما يُجيبُ بِه البابُ (رَمزُ الحالَةِ، أَو ما رَماه — وذاكَ ‏500 عِندَ الخادِم) وأَسماءُ ما يَمحوه
// `Set-Cookie`، وما بَقِيَ مِن اشتِراكاتِ الحِساب.
//
// **ما يُثَبَّتُ هُنا لِيَبقى** (القاعِدَة ٣): بِمَخزَنٍ سَليمٍ ‏302 إلى `/{slug}`، وتُمحى كوكيّاتُ الجَلسَةِ والاسمِ والدَور، ويُمحى عُنوانُ هذا المُتَصَفِّحِ وَحدَه ويَبقى الآخَر.
// **وما يُطبَعُ ولا يُثَبَّت**: بِكِتابَةٍ تَرمي، وبِقاعِدَةٍ غائِبَة، وبِاستِمارَةٍ مَكسورَة — اليَومَ يَرمي البابُ ولا يَمحو كوكيّاً؛ ويَتَغَيَّرُ في كوميتِ التَبديل،
// والمِلَفُّ يَبقى بِبَصمَتِه.

[Collection(LiveProofCollection.Name)]
public class LiveSessionLogoutProofTests(ITestOutputHelper output)
{
    internal static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>مَخزَنُ الباب: Postgres سَليم، أَو كِتابَةٌ تَرمي (عَطَبٌ أَو تَعارُضٌ عِندَ الحِفظ)، أَو قاعِدَةٌ لا تُبلَغ (كُلُّ نِداءٍ يَرمي).</summary>
    public enum Store { Healthy, WriteThrows, Unreachable }

    internal const string PushA = "https://fcm.googleapis.com/fcm/send/g2-logout-a";
    internal const string PushB = "https://fcm.googleapis.com/fcm/send/g2-logout-b";
    internal const string Role = "buyer";

    /// <summary>جَوابُ البابِ (الحالَة، أَو «يَرمي X ⇒ 500»)، ووِجهَتُه، وأَسماءُ الكوكيّاتِ الَّتي يَمحوها، واشتِراكاتُ الحِسابِ بَعدَه.</summary>
    internal sealed record LogoutOutcome(string Answer, string Location, IReadOnlyList<string> Cleared, IReadOnlyList<string> PushLeft)
    {
        public override string ToString()
            => $"الجَواب {Answer} → {(Location == "" ? "—" : Location)} · يَمحو {(Cleared.Count == 0 ? "لا كوكي" : string.Join(" ، ", Cleared))} · الاشتِراكاتُ الباقِيَة {PushLeft.Count}";
    }

    /// <summary>الكوكيّاتُ الَّتي يَحمِلُها الطَلَبُ لِـ<paramref name="slug"/>: الجَلسَة، واسمُها، والدَور.</summary>
    internal static string[] SessionCookies(string slug)
        => [ACommerce.Templates.Customer.Marketplace.AuthSession.CookieName(slug), ACommerce.Templates.Customer.Marketplace.AuthSession.CookieName(slug) + ".name",
            ACommerce.Templates.Customer.Marketplace.AuthSession.CookieName(slug, Role)];

    /// <summary>
    /// <para>يُرسِلُ إلى بابِ الخُروجِ طَلَبَ جِهازٍ يَحمِلُ جَلسَةَ عُضوٍ في مَتجَرٍ مُؤَقَّتٍ بِدَورٍ واحِد، وعُنوانَ اشتِراكِه <see cref="PushA"/> (ولِلحِسابِ اشتِراكٌ آخَرُ <see cref="PushB"/>).
    /// <paramref name="malformedForm"/>: الجِسمُ <c>multipart/form-data</c> بِلا حَدٍّ فَيَرمي <c>ReadFormAsync</c>. والمَتجَرُ والحِسابُ يُمسَحانِ بَعدَه.</para>
    /// </summary>
    internal static async Task<LogoutOutcome> RunAsync(Store store, bool malformedForm)
    {
        var slug = "zz-g2lo-" + Guid.NewGuid().ToString("N")[..8];
        var tenant = new Tenant { Id = slug, Name = "مَتجَرُ الخُروج", Roles = [new ACommerce.Kit.Roles.Role { Slug = Role, CatalogSlug = Role }] };
        var user = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = "عُضوٌ يَخرُج", Phone = "+966500000771", PhoneVerified = true, SessionVersion = 1 };
        foreach (var e in new[] { PushA, PushB }) user.PushSubscriptions.Add(new PushSubscription { Endpoint = e, P256dh = "p", Auth = "a" });

        DocWorld? world = store == Store.Healthy ? null : new DocWorld().Put(tenant).Put(user);
        await using var host = LiveTemplateDoorHost.Build(services: world is null ? null : s => s.AddSingleton<IDocumentStore>(world.Store));
        if (world is null)
        {
            await using (var g = host.Store.LightweightSession()) { g.Store(tenant); await g.SaveChangesAsync(); }
            await using (var s = host.Store.LightweightSession(slug)) { s.Store(user); await s.SaveChangesAsync(); }
        }
        else world.BeforeMember = store == Store.WriteThrows
            ? m => { if (m == "SaveChangesAsync") throw new InvalidOperationException("عَطَبٌ عِندَ الحِفظ (مِسبار)"); }
            : _ => throw new InvalidOperationException("القاعِدَةُ لا تُبلَغ (مِسبار)");
        try
        {
            var door = host.Door("/{slug}/auth/logout");
            await using var scope = host.Services.CreateAsyncScope();
            var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            if (scope.ServiceProvider.GetService<IHttpContextAccessor>() is { } accessor) accessor.HttpContext = ctx;
            ctx.Request.Method = "POST";
            ctx.Request.Scheme = "http";
            ctx.Request.Host = new HostString("localhost");
            ctx.Request.Path = $"/{slug}/auth/logout";
            ctx.Request.RouteValues["slug"] = slug;
            var token = AuthHandlers.MakeToken(user.Id, slug, user.SessionVersion);
            var names = SessionCookies(slug);
            ctx.Request.Headers.Cookie = $"{names[0]}={token}; {names[1]}={Uri.EscapeDataString(user.FullName)}; {names[2]}={token}";
            if (malformedForm)
            {
                ctx.Request.ContentType = "multipart/form-data";
                ctx.Request.Body = new MemoryStream("--x\r\nبِلا حَدّ\r\n"u8.ToArray());
            }
            else
            {
                ctx.Request.ContentType = "application/x-www-form-urlencoded";
                ctx.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { ["push_endpoint"] = PushA });
            }
            ctx.Response.Body = new MemoryStream();
            ctx.SetEndpoint(door);
            string answer;
            try
            {
                await door.RequestDelegate!(ctx);
                answer = ctx.Response.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception e) { answer = $"يَرمي {e.GetType().Name} ⇒ 500"; }
            var cleared = ctx.Response.Headers.SetCookie
                .Where(c => c is not null && c.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase))
                .Select(c => c!.Split('=', 2)[0].Replace(slug, "{slug}")).Distinct().ToList();
            User? after;
            if (world is not null) after = world.Read<User>(user.Id.ToString());
            else { await using var q = host.Store.QuerySession(slug); after = await q.LoadAsync<User>(user.Id); }
            return new LogoutOutcome(answer, ctx.Response.Headers.Location.ToString().Replace(slug, "{slug}"), cleared,
                after?.PushSubscriptions.Select(p => p.Endpoint).ToList() ?? []);
        }
        finally
        {
            if (world is null) await LiveZeroRoleAdminProofTests.CleanAsync(host.Store, slug);
        }
    }

    /// <summary><b>ما يَبقى</b>: بِمَخزَنٍ سَليمٍ يَخرُجُ الجِهازُ — ‏302 إلى <c>/{slug}</c>، وتُمحى كوكيّاتُ الجَلسَةِ والاسمِ والدَور، ويُمحى عُنوانُ هذا المُتَصَفِّحِ وَحدَه
    /// ويَبقى عُنوانُ الجِهازِ الآخَر.</summary>
    [Fact]
    public async Task A_healthy_store_signs_this_device_out_and_forgets_only_its_browser_on_Postgres()
    {
        if (!Enabled) return;
        var outcome = await RunAsync(Store.Healthy, malformedForm: false);
        output.WriteLine($"Healthy ⇒ {outcome}");
        Assert.Equal("302", outcome.Answer);
        Assert.Equal("/{slug}", outcome.Location);
        Assert.Equal(new[] { ".acommerce.auth.{slug}", ".acommerce.auth.{slug}.name", ".acommerce.auth.{slug}.buyer", ".acommerce.auth.{slug}.buyer.name" }.Order(), outcome.Cleared.Order());
        Assert.Equal(new[] { PushB }, outcome.PushLeft);
    }

    /// <summary><b>ما يُطبَعُ ولا يُثَبَّت</b>: كِتابَةٌ تَرمي، وقاعِدَةٌ لا تُبلَغ، واستِمارَةٌ مَكسورَة — اليَومَ يَرمي البابُ ولا يَمحو كوكيّاً.</summary>
    [Theory]
    [InlineData(Store.WriteThrows, false)]
    [InlineData(Store.Unreachable, false)]
    [InlineData(Store.Healthy, true)]
    public async Task A_logout_whose_device_step_stumbles_is_measured_on_Postgres(Store store, bool malformedForm)
    {
        if (!Enabled) return;
        var outcome = await RunAsync(store, malformedForm);
        output.WriteLine($"{store}{(malformedForm ? " · استِمارَةٌ مَكسورَة" : "")} ⇒ {outcome}");
        Assert.NotEqual("", outcome.Answer);
    }
}
