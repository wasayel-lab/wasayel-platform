using ACommerce.Kit.Subscriptions;
using ACommerce.Templates.Customer.Marketplace.Gates;
using ACommerce.Templates.Customer.Marketplace.Services.Api;
using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>الحارِسُ الوَحيد تَحتَ <c>/api/v1</c> — مَقيساً
/// بِنِداءٍ حَقيقيّ لا مَوصوفاً في تَعليق.</b> أَربَعُ حالاتِ رَفضٍ
/// وحالَةُ مُرور، كُلُّها بِلا قاعِدَةِ بَيانات: الاعتِمادُ
/// مُستَبدَلٌ بِمَعبَرٍ يُعيد ما نُريد، والاستِحقاقُ واجِهَةٌ
/// أَصلاً.</para>
///
/// <para><b>ولِماذا يُقاس التَرتيب</b> (القاعِدَة ٦: «التَخويلُ
/// يَسبِق تَحَقُّقَ الحُقول»): مِفتاحٌ مُزَوَّرٌ بِنِطاقٍ ناقِص
/// يَجِب أَن يُرَدّ <c>auth_invalid</c> لا <c>scope_missing</c> —
/// وإلّا صارَ جَوابُ الرَفضِ نَفسُه <b>كاشِفاً</b>: يُخبِر
/// المُهاجِمَ أَنّ المِفتاحَ صَحيحٌ وأَنّ ما يَنقُصُه نِطاق.</para>
/// </summary>
public class ApiKeyFilterTests
{
    // ─── المَعابِر ────────────────────────────────────────────────────

    /// <summary>مَعبَرُ الاعتِماد: يُعيد نَتيجَةً مُعَدَّةً سَلَفاً،
    /// و<b>يَعُدُّ نِداءاتِه</b> — فَنُثبِت أَنّ الطَلَبَ بِلا رَأسٍ
    /// لا يَلمِس قاعِدَةَ البَيانات إطلاقاً.</summary>
    private sealed class StubKeys : ApiKeyService
    {
        private readonly ApiKeyAuthResult _result;
        public int Calls { get; private set; }

        public StubKeys(ApiKeyAuthResult result) : base(null!) => _result = result;

        public override Task<ApiKeyAuthResult> AuthenticateAsync(
            string? presented, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(_result);
        }
    }

    /// <summary>مَعبَرُ الاستِحقاق: <c>PeekAsync</c> يُجيب بِما
    /// نُعِدّ، و<c>ConsumeAsync</c> يَرمي — فَلَو نادَتهُ نُقطَةُ
    /// قِراءَةٍ يَوماً لَاحمَرَّ بَدَلَ أَن يَمُرَّ صامِتاً.</summary>
    private sealed class StubEntitlements : IEntitlements
    {
        private readonly bool _allowed;
        public StubEntitlements(bool allowed) => _allowed = allowed;

        public IReadOnlyCollection<string> Handles { get; } =
            new[] { CapabilityCatalog.ApiCall };

        public Task<EntitlementResult> PeekAsync(
            string tenantSlug, Guid userId, string capability, CancellationToken ct = default)
            => Task.FromResult(new EntitlementResult(
                _allowed, capability, Entitlements.Unlimited,
                _allowed ? null : "الباقَةُ لا تَشمَل الـAPI."));

        public Task<EntitlementResult> ConsumeAsync(
            IDocumentSession session, string tenantSlug, Guid userId,
            string capability, int amount = 1, CancellationToken ct = default)
            => throw new NotSupportedException("رايَةٌ لا تُستَهلَك — ولا نُقطَةَ تَستَهلِكُها.");
    }

    private static ApiKeyPrincipal Principal(params string[] scopes) =>
        new("abcdef0123456789", "ashare",
            Guid.Parse("11111111-1111-1111-1111-111111111111"), "شَرِكَةُ النَقل", scopes);

    // ─── الحالاتُ الخَمس ──────────────────────────────────────────────

    /// <summary>بِلا رَأسٍ ⇒ ‏401 <c>auth_missing</c> —
    /// <b>وبِلا نِداءِ اعتِماد</b>.</summary>
    [Fact]
    public async Task No_authorization_header_is_401_and_never_reaches_the_store()
    {
        var keys = new StubKeys(new ApiKeyAuthResult(null, ApiKeyRejection.Missing));
        var (status, body, passed) = await RunAsync(keys, new StubEntitlements(true),
            ApiScopeCatalog.DealsRead, header: null);

        Assert.Equal(401, status);
        Assert.Contains("auth_missing", body);
        Assert.False(passed);
        Assert.Equal(0, keys.Calls);
    }

    /// <summary>مِفتاحٌ مُزَوَّرٌ/مَجهولٌ/مُبطَلٌ/مُنتَهٍ ⇒ ‏401
    /// <c>auth_invalid</c> — <b>رَمزٌ واحِدٌ لِلأَربَع</b>، فَلا
    /// يُفشي الجَوابُ حالَةَ المِفتاح.</summary>
    [Theory]
    [InlineData(ApiKeyRejection.Malformed)]
    [InlineData(ApiKeyRejection.Unknown)]
    [InlineData(ApiKeyRejection.SecretMismatch)]
    [InlineData(ApiKeyRejection.Revoked)]
    [InlineData(ApiKeyRejection.Expired)]
    [InlineData(ApiKeyRejection.TenantGone)]
    public async Task Any_failed_credential_is_401_with_one_code(ApiKeyRejection rejection)
    {
        var (status, body, passed) = await RunAsync(
            new StubKeys(new ApiKeyAuthResult(null, rejection)), new StubEntitlements(true),
            ApiScopeCatalog.DealsRead, header: "Bearer wsl_x_y");

        Assert.Equal(401, status);
        Assert.Contains("auth_invalid", body);
        Assert.DoesNotContain(rejection.ToString().ToLowerInvariant(), body);
        Assert.False(passed);
    }

    /// <summary>نِطاقٌ ناقِص ⇒ ‏403 <c>scope_missing</c>، والجِسمُ
    /// يَقولُ ما يَنقُص — فَصاحِبُ المِفتاحِ يُصلِحُ بِلا تَخمين.</summary>
    [Fact]
    public async Task A_key_without_the_required_scope_is_403()
    {
        var (status, body, passed) = await RunAsync(
            new StubKeys(new ApiKeyAuthResult(Principal(ApiScopeCatalog.DealsRead), ApiKeyRejection.None)),
            new StubEntitlements(true), ApiScopeCatalog.DealsWrite, header: "Bearer wsl_x_y");

        Assert.Equal(403, status);
        Assert.Contains("scope_missing", body);
        Assert.Contains("deals:write", body);
        Assert.False(passed);
    }

    /// <summary>
    /// <para><b>واستِحقاقُ <c>api.call</c> مَفروضٌ فِعلاً — هذا هُوَ
    /// الاختِبارُ السالِب الَّذي يُفَرِّق «قُدرَةً تُفحَص» عَن
    /// «قُدرَةٍ تُوصَف».</b> اليَومَ تُجيبُ الطَبَقَةُ بِنَعَم
    /// دائِماً (تَكافُؤٌ صِفريّ — لا رَقمَ حِصَّةٍ يُخترَع)؛ ويَومَ
    /// تَقولُ لا، يَرُدُّ الحارِسُ ‏403 <b>بِلا سَطرٍ جَديد</b>.
    /// وهذا مَقيسٌ هُنا لا مَوعود.</para>
    /// </summary>
    [Fact]
    public async Task A_key_whose_tenant_is_not_entitled_is_403()
    {
        var (status, body, passed) = await RunAsync(
            new StubKeys(new ApiKeyAuthResult(Principal(ApiScopeCatalog.DealsRead), ApiKeyRejection.None)),
            new StubEntitlements(false), ApiScopeCatalog.DealsRead, header: "Bearer wsl_x_y");

        Assert.Equal(403, status);
        Assert.Contains("entitlement_denied", body);
        Assert.Contains("api.call", body);
        Assert.False(passed);
    }

    /// <summary>ومِفتاحٌ سَليمٌ يَمُرّ — <b>ويَتُرك خَلفَه نَفسَ
    /// <c>GateKeys</c></b> الَّتي يَملَؤُها <c>AuthFilter</c>،
    /// فَيَرِثُه كُلُّ حارِسٍ يَقرَأُ <c>HttpContext.Items</c>.</summary>
    [Fact]
    public async Task A_valid_key_passes_and_fills_the_same_gate_keys()
    {
        var principal = Principal(ApiScopeCatalog.DealsRead, ApiScopeCatalog.DealsWrite);
        var ctx = Context("Bearer wsl_x_y",
            new StubKeys(new ApiKeyAuthResult(principal, ApiKeyRejection.None)),
            new StubEntitlements(true));

        var passed = false;
        var result = await new ApiKeyFilter(ApiScopeCatalog.DealsWrite).InvokeAsync(
            EndpointFilterInvocationContext.Create(ctx),
            _ => { passed = true; return ValueTask.FromResult<object?>(Results.Ok()); });

        Assert.True(passed);
        Assert.NotNull(result);
        Assert.Equal(principal.ActorUserId, ctx.Items[GateKeys.UserId]);
        Assert.Equal(principal.TenantSlug, ctx.Items[GateKeys.SlugItem]);
        Assert.Same(principal, ctx.ApiPrincipal());
    }

    /// <summary><b>والمُستَأجِرُ مِن الوَثيقَةِ لا مِن الطَلَب</b>
    /// (‏§٣٫٦): المَسارُ يَذكُر مُستَأجِراً آخَر، والمَملوءُ هُوَ
    /// مُستَأجِرُ المِفتاح.</summary>
    [Fact]
    public async Task The_tenant_comes_from_the_key_not_from_the_path()
    {
        var ctx = Context("Bearer wsl_x_y",
            new StubKeys(new ApiKeyAuthResult(Principal(ApiScopeCatalog.DealsRead), ApiKeyRejection.None)),
            new StubEntitlements(true));
        ctx.Request.Path = "/api/v1/deals";
        ctx.Request.RouteValues["slug"] = "some-other-tenant";

        await new ApiKeyFilter(ApiScopeCatalog.DealsRead).InvokeAsync(
            EndpointFilterInvocationContext.Create(ctx),
            _ => ValueTask.FromResult<object?>(Results.Ok()));

        Assert.Equal("ashare", ctx.Items[GateKeys.SlugItem]);
    }

    // ─── البَوّابَةُ عِندَ التَركيب ────────────────────────────────────

    /// <summary>نِطاقٌ خارِجَ المَعجَم يَرمي <b>عِندَ بِناءِ
    /// المَسار</b> — فَيُفشِل الإقلاعَ لا طَلَباً واحِداً في
    /// اللَيل.</summary>
    [Theory]
    [InlineData("deals")]
    [InlineData("")]
    [InlineData("listings:write")]
    public void An_unknown_scope_fails_at_composition_time(string scope)
        => Assert.Throws<ArgumentException>(() => new ApiKeyFilter(scope));

    // ─── الأَدَوات ────────────────────────────────────────────────────

    private static DefaultHttpContext Context(string? header, ApiKeyService keys, IEntitlements ents)
    {
        var sp = new ServiceCollection()
            .AddLogging()
            .AddSingleton(keys)
            .AddSingleton(ents)
            .BuildServiceProvider();

        var ctx = new DefaultHttpContext
        {
            RequestServices = sp,
            Response = { Body = new MemoryStream() },
        };
        if (header is not null) ctx.Request.Headers.Authorization = header;
        return ctx;
    }

    private static async Task<(int Status, string Body, bool Passed)> RunAsync(
        ApiKeyService keys, IEntitlements ents, string requiredScope, string? header)
    {
        var ctx = Context(header, keys, ents);
        var passed = false;

        var result = await new ApiKeyFilter(requiredScope).InvokeAsync(
            EndpointFilterInvocationContext.Create(ctx),
            _ => { passed = true; return ValueTask.FromResult<object?>(Results.Ok()); });

        if (result is IResult r) await r.ExecuteAsync(ctx);

        ctx.Response.Body.Position = 0;
        using var reader = new StreamReader(ctx.Response.Body);
        return (ctx.Response.StatusCode, await reader.ReadToEndAsync(), passed);
    }
}
