using ACommerce.Kit.Auth;
using ACommerce.Kit.Subscriptions;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Gates;
using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ تَحويلاتِ حُرّاسِ الكِتابَةِ لِما يَنجو، قَبلَ أَن تُبنى عَلى المُضيفِ الَّذي يَخدِمُ الطَلَب (‏2026-11-07، مُراجَعَةُ C2b الجَولَةُ الأُولى · C2b-P-01 — القاعِدَة ٣) ═══
//
// **ما يَتَغَيَّرُ عَمداً بَعدَ هذا المِلَفّ**: `AuthFilter` و`TermsFilter` و`EntitlementFilter` و`ListingOwnerFilter` وبابُ `/{slug}/terms/accept` تَبني تَحويلاتِها بِـ
// `AuthSession.LinkFor` (`StoreUrl.Link` — رابِطُ تَنَقُّلٍ قَد يُسَمّي تَطبيقاً آخَرَ مِن المَتجَرِ مُطلَقاً) فَتَصيرُ `StoreUrl.For` (لا يُغادِرُ المُضيفَ أَبَداً، ADR-050 §١٤).
// عِندَ a9bc2cf كانَ `TermsFilter` عَلى مُضيفِ الدَورِ يُحَوِّلُ إلى `{slug}.R/{slug}/terms?…` — مُضيفٌ بِلا جَلسَة؛ وأَسقَطَ 1821a69 ذلكَ الفَرعَ مِن `Link` لِلدَورِ الفارِغ،
// ويَبقى لِدَورٍ مُسَمّىً غَيرِ دَورِ المُضيفِ في سِياقِ الحارِس. **وما يَنجو ويُثَبَّتُ هُنا**: كُلُّ حارِسٍ في وَضعِ المَسارِ بِحَرفِه، وعَلى مُضيفِ المَتجَر، وعَلى مُضيفِ الدَورِ
// والدَورُ فارِغٌ (وهُوَ ما يَحمِلُه كُلُّ طَلَبِ كِتابَة: لا مَسارَ كِتابَةٍ بِـ`/r/`)، ومُرورُ المَقبولِ إلى ما بَعدَه. والمِلَفُّ يَبقى بِبَصمَتِه بَعدَ التَبديل.

public class WriteGateRedirectCharacterizationTests(ITestOutputHelper output)
{
    internal const string Slug = "ashare";
    internal const string Apex = "example.test";

    /// <summary>طَلَبُ كِتابَةٍ كَما يَترُكُه الحاقِن: <c>none</c> بِلا نِطاق، و<c>store</c> عَلى <c>ashare.example.test</c>، و<c>vendor</c> عَلى <c>ashare-vendor.example.test</c>.</summary>
    internal static DefaultHttpContext Post(string where, string path, IServiceProvider? services = null)
    {
        var ctx = new DefaultHttpContext { RequestServices = services ?? new ServiceCollection().BuildServiceProvider() };
        ctx.Request.Method = "POST";
        ctx.Request.Scheme = "https";
        ctx.Request.Path = path;
        ctx.Request.RouteValues["slug"] = Slug;
        ctx.Request.Host = new HostString(where switch { "store" => $"{Slug}.{Apex}", "vendor" => $"{Slug}-vendor.{Apex}", _ => "localhost" });
        if (where != "none")
        {
            ctx.Items[StoreHostItems.Apex] = Apex;
            ctx.Items[StoreHostItems.Slug] = Slug;
            if (where == "vendor") ctx.Items[StoreHostItems.Role] = "vendor";
        }
        return ctx;
    }

    /// <summary>جَوابُ الحارِس: وِجهَةُ التَحويل، أَو <c>next</c> إن مَرَّ، أَو رَمزُ الحالَة. والوَصّالُ الساكِنُ كَما يَضَعُه <c>Program.cs</c>
    /// (<c>AuthSession.HttpAccessor</c>) — وبِدونِه يُجيبُ <c>LinkFor</c> بِشَكلِ المَسارِ عَلى كُلِّ مُضيفٍ فَلا يَقيسُ ما يَراه المُتَصَفِّح (مَقيس).
    /// والقيمَةُ <c>AsyncLocal</c> فَلا تَبلُغُ اختِباراً آخَر.</summary>
    internal static async Task<string> AnswerAsync(IEndpointFilter filter, HttpContext ctx)
    {
        var previous = ACommerce.Templates.Customer.Marketplace.AuthSession.HttpAccessor;
        ACommerce.Templates.Customer.Marketplace.AuthSession.HttpAccessor = new HttpContextAccessor { HttpContext = ctx };
        object? result;
        try { result = await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(ctx), _ => ValueTask.FromResult<object?>("next")); }
        finally { ACommerce.Templates.Customer.Marketplace.AuthSession.HttpAccessor = previous; }
        return result switch
        {
            RedirectHttpResult r => r.Url,
            string s => s,
            IStatusCodeHttpResult c => c.StatusCode?.ToString() ?? "?",
            _ => result?.GetType().Name ?? "null",
        };
    }

    internal sealed class StubEntitlements(bool allowed) : IEntitlements
    {
        public IReadOnlyCollection<string> Handles { get; } = [CapabilityCatalog.ListingCreate];

        public Task<EntitlementResult> PeekAsync(string tenantSlug, Guid userId, string capability, CancellationToken ct = default)
            => Task.FromResult(new EntitlementResult(allowed, capability, 0, allowed ? null : "نَفِدَ الرَصيد."));

        public Task<EntitlementResult> ConsumeAsync(IDocumentSession session, string tenantSlug, Guid userId, string capability, int amount = 1, CancellationToken ct = default)
            => throw new NotSupportedException("المُرَشِّحُ لا يَستَهلِك.");
    }

    [Theory]
    [InlineData("none", "/ashare/login?returnUrl=%2Fashare%2Flistings%2Fcreate")]
    [InlineData("store", "/login?returnUrl=%2Fashare%2Flistings%2Fcreate")]
    [InlineData("vendor", "/login?returnUrl=%2Fashare%2Flistings%2Fcreate")]
    public async Task The_sign_in_gate_answers_as_today(string where, string expected)
    {
        var got = await AnswerAsync(new AuthFilter(), Post(where, "/ashare/listings/create"));
        var api = await AnswerAsync(new AuthFilter(redirectOnFailure: false), Post(where, "/ashare/listings/create"));
        output.WriteLine($"{where}: ⇒ {got} · بِلا تَحويل ⇒ {api}");
        Assert.Equal(expected, got);
        Assert.Equal("401", api);
    }

    [Theory]
    [InlineData("none", "unaccepted", "/ashare/terms?returnUrl=%2Fashare%2Flistings%2Fcreate")]
    [InlineData("store", "unaccepted", "/ashare/terms?returnUrl=%2Fashare%2Flistings%2Fcreate")]
    [InlineData("vendor", "unaccepted", "/ashare/terms?returnUrl=%2Fashare%2Flistings%2Fcreate")]
    [InlineData("none", "missing", "/ashare/login")]
    [InlineData("store", "missing", "/login")]
    [InlineData("vendor", "missing", "/login")]
    [InlineData("vendor", "accepted", "next")]
    [InlineData("none", "accepted", "next")]
    public async Task The_terms_gate_answers_as_today(string where, string who, string expected)
    {
        var world = new DocWorld();
        var id = Guid.NewGuid();
        if (who != "missing")
            world.Put(new User
            {
                Id = id, TenantSlug = Slug, Phone = "+966500000001",
                AcceptedTermsAt = who == "accepted" ? DateTime.UtcNow : null,
                AcceptedTermsVersion = who == "accepted" ? TermsPolicy.CurrentVersion : 0,
            });
        var ctx = Post(where, "/ashare/listings/create", new ServiceCollection().AddSingleton(world.Store).BuildServiceProvider());
        ctx.Items[GateKeys.UserId] = id;
        ctx.Items[GateKeys.SlugItem] = Slug;
        var got = await AnswerAsync(new TermsFilter(), ctx);
        output.WriteLine($"{where} · {who}: ⇒ {got}");
        Assert.Equal(expected, got);
    }

    [Theory]
    [InlineData("none", false, "/ashare/create-listing?err=quota")]
    [InlineData("store", false, "/create-listing?err=quota")]
    [InlineData("vendor", false, "/create-listing?err=quota")]
    [InlineData("vendor", true, "next")]
    public async Task The_entitlement_gate_answers_as_today(string where, bool allowed, string expected)
    {
        var ctx = Post(where, "/ashare/listings/create",
            new ServiceCollection().AddSingleton<IEntitlements>(new StubEntitlements(allowed)).BuildServiceProvider());
        ctx.Items[GateKeys.UserId] = Guid.NewGuid();
        ctx.Items[GateKeys.SlugItem] = Slug;
        var got = await AnswerAsync(new EntitlementFilter(CapabilityCatalog.ListingCreate, "create-listing", "quota"), ctx);
        output.WriteLine($"{where} · {(allowed ? "مَسموح" : "نَفِد")}: ⇒ {got}");
        Assert.Equal(expected, got);
    }

    [Theory]
    [InlineData("none", "/ashare/me/listings?err=not_owner")]
    [InlineData("store", "/me/listings?err=not_owner")]
    [InlineData("vendor", "/me/listings?err=not_owner")]
    public async Task The_listing_owner_gate_answers_as_today(string where, string expected)
    {
        var ctx = Post(where, "/ashare/listings/not-a-guid/edit");
        ctx.Request.RouteValues[ListingOwnerFilter.RouteArgument] = "not-a-guid";
        ctx.Items[GateKeys.UserId] = Guid.NewGuid();
        ctx.Items[GateKeys.SlugItem] = Slug;
        var got = await AnswerAsync(new ListingOwnerFilter(), ctx);
        output.WriteLine($"{where}: ⇒ {got}");
        Assert.Equal(expected, got);
    }
}
