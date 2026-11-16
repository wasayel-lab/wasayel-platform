using System.Reflection;
using ACommerce.Kit.Auth.Server;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ دائِرَتا الوَكيلِ التَفاعُلِيَّتانِ بَعدَ «اخرج من كل الأجهزة» — تَوصيفٌ قَبلَ التَبديل (‏2026-11-05، الموجَة ١٢ G2 · ١؛ A-SESS-W12-2) ═══════════════
//
// `/studio/agent` (‏`StudioAgent.razor`) و`/admin/agent` (‏جَزيرَةُ `AgentChatPanel.razor`) صَفحَتانِ `InteractiveServer`: تَلتَقِطانِ صاحِبَ الجَلسَةِ مَرَّةً
// حينَ يوجَدُ `HttpContext`، ثُمَّ تُنادي مُعالِجاتُ النَقرِ الأَربَعَة (`SendAsync`/`ApplyAsync`/`RejectAsync`/`ResetAsync`) النَموذَجَ والأَدَواتِ عَبرَ دارَةِ
// SignalR بِلا طَلَبٍ يَمُرُّ بِـ`SessionVersionGate` — والبَوّابَةُ لِكُلِّ طَلَبِ HTTP. وتَعليقُ البَوّابَةِ (`SessionVersionGate.cs:25-26`) وADR-041 §٣ يَقولانِ
// «الصَفَحاتُ المَحروسَةُ كُلُّها SSR ثابِت» — وهاتانِ لَيستا.
//
// **كَيفَ يُقاسُ هُنا**: المُضيفُ بِتَركيبِه وصَفحَتُه بِعَينِها (`LiveTemplateDoorHost`)، ومُصَيِّرٌ بِلا مُتَصَفِّحٍ (`CircuitProbe`) يُرَكِّبُ الصَفحَةَ جَذراً بِخِدماتِ
// المُضيف: التَصييرُ الأَوَّلُ بِـ`HttpContext` يَحمِلُ كوكيَّ الاستوديو، ثُمَّ يُنزَعُ `HttpContext` كَما في الدائِرَة وتُنادى المُعالِجاتُ عَلى مُوَزِّعِه. وخَلفِيَّةُ
// النَموذَجِ بَديلٌ يَعُدُّ نِداءاتِه بِلا شَبَكَة (`ProbeAgentBackends`)، والتَنقُّلُ بَديلٌ يُسَجِّلُ وِجهَتَه (`ProbeNavigation`). والأَداةُ المُعَلَّقَةُ حُمولَتُها `{}`
// فَيَرُدُّها المُصادِقُ قَبلَ أَيِّ كِتابَة — فَتَنفيذُها يُقرَأُ مِن حالَتِها («error» بِرِسالَةِ المُصادِق) لا مِن أَثَرٍ في مَتجَر.
//
// **ما يُثَبَّتُ هُنا لِيَبقى** (القاعِدَة ٣): بِجَلسَةٍ مَفتوحَةٍ لِمُشرِفِ مَنَصَّة تُرسَلُ الرِسالَةُ وتُطَبَّقُ الأَداةُ وتُرفَضُ الثانِيَةُ (ثَلاثَةُ نِداءاتِ نَموذَج)
// ويُمحى السِجِلّ، بِلا تَنقُّل. **وما يُطبَعُ ولا يُثَبَّت**: بَعدَ رَفعِ إصدارِ الجَلسَةِ أَو نَزعِ الإشراف — اليَومَ يَجري كُلُّ ذلِكَ كَما هُوَ؛ ويَتَغَيَّرُ في
// كوميتِ التَبديل، والمِلَفُّ يَبقى بِبَصمَتِه.

/// <summary>
/// <para><b>مُصَيِّرٌ بِلا مُتَصَفِّح</b> — مُصَيِّرُ الـHTML الساكِنُ العامُّ نَفسُه (<c>StaticHtmlRenderer</c>، لا صِنفٌ مِن <c>RenderTree</c>)، يُرَكِّبُ الصَفحَةَ جَذراً
/// بِخِدماتِ المُضيف. وحَيثُ يَطلُبُ مُكَوِّنٌ <c>InteractiveServer</c> — الصَفحَةُ بِـ<c>@rendermode</c> أَو الجَزيرَةُ بِسِمَةِ مُضيفِها — يُنشِئُه كَما هُوَ ويَحفَظُه
/// (<see cref="Interactive"/>): المُصَيِّرُ هُنا هُوَ الدائِرَةُ نَفسُها. ثُمَّ تُنادى مُعالِجاتُه عَلى مُوَزِّعِه كَما يُناديها نَقرٌ عَبرَ SignalR.</para>
/// </summary>
internal sealed class CircuitProbe(IServiceProvider services)
    : Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure.StaticHtmlRenderer(services, NullLoggerFactory.Instance)
{
    /// <summary>المُكَوِّنُ التَفاعُلِيُّ الَّذي أَنشَأَه التَصييرُ الأَوَّل.</summary>
    public IComponent? Interactive { get; private set; }

    protected override IComponent ResolveComponentForRenderMode(Type componentType, int? parentComponentId,
        IComponentActivator componentActivator, IComponentRenderMode renderMode)
        => Interactive = componentActivator.CreateInstance(componentType);

    /// <summary>التَصييرُ الأَوَّلُ لِـ<paramref name="type"/> بِـ<paramref name="first"/> في <see cref="IHttpContextAccessor"/> — ثُمَّ يُنزَعُ كَما في الدائِرَة. يُعيدُ
    /// المُكَوِّنَ التَفاعُلِيّ.</summary>
    public Task<IComponent> MountAsync(Type type, IHttpContextAccessor accessor, HttpContext first)
        => Dispatcher.InvokeAsync(async () =>
        {
            accessor.HttpContext = first;
            try
            {
                await BeginRenderingComponent(type, ParameterView.Empty).QuiescenceTask;
                return Interactive ?? throw new InvalidOperationException($"«{type.Name}» لَم يَرسُم مُكَوِّناً تَفاعُلِيّاً — الأَداةُ عَمياء.");
            }
            finally { accessor.HttpContext = null; }
        });

    /// <summary>نِداءُ مُعالِجٍ خاصٍّ بِاسمِه — كَما يُنادي نَقرٌ <c>@onclick</c> عَبرَ الدائِرَة.</summary>
    public Task ClickAsync(IComponent component, string handler, params object[] args)
        => Dispatcher.InvokeAsync(() => (Task)Handler(component.GetType(), handler).Invoke(component, args)!);

    private static MethodInfo Handler(Type type, string name)
        => type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
           ?? throw new InvalidOperationException($"«{type.Name}.{name}» غائِب — الأَداةُ عَمياء.");

    private static FieldInfo FieldOf(IComponent component, string name)
        => component.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
           ?? throw new InvalidOperationException($"«{component.GetType().Name}.{name}» غائِب — الأَداةُ عَمياء.");

    public static T? Field<T>(IComponent component, string name) => (T?)FieldOf(component, name).GetValue(component);

    public static void Set(IComponent component, string name, object? value) => FieldOf(component, name).SetValue(component, value);
}

/// <summary>خَلفِيَّةُ نَموذَجٍ تَعُدُّ نِداءاتِها وتُجيبُ نَصّاً بِلا أَداة — ولا شَبَكَة. ومُزَوِّدُها يُعطيها لِكُلِّ وَكيل.</summary>
internal sealed class ProbeAgentBackends : IAgentBackendProvider, IAgentBackend
{
    /// <summary>اسمُ المُزَوِّدِ في سَطرِ القِياس (<c>ModelCallRecord.Provider</c>) — بِه تُمحى سُطورُ البُرهانِ بَعدَه.</summary>
    public const string Name = "g2-circuit-probe";

    private int _calls;
    public int Calls => Volatile.Read(ref _calls);

    public AgentProfile ProfileFor(string agentName) => new(Name, Name, null, "", Name, null);
    public IAgentBackend For(string agentName) => this;
    public string ModelFor(string agentName) => Name;

    public string ProviderName => Name;
    public string DefaultModel => Name;
    public bool IsConfigured => true;
    public string Endpoint => "probe://circuit/";

    public Task<AgentBackendResponse> CallAsync(AgentRequest req, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(new AgentBackendResponse("رَدُّ المِسبار", null, null));
    }
}

/// <summary>تَنقُّلٌ يُسَجِّلُ وِجهَتَه ولا يَنتَقِل — <c>forceLoad</c> مَعَها.</summary>
internal sealed class ProbeNavigation : NavigationManager
{
    public List<string> Went { get; } = new();

    public ProbeNavigation() => Initialize("http://localhost/", "http://localhost/studio/agent");

    protected override void NavigateToCore(string uri, NavigationOptions options) => Went.Add(options.ForceLoad ? uri + " (forceLoad)" : uri);
}

/// <summary>ما جَرى في الدائِرَة بَعدَ تَصييرِها الأَوَّل: نِداءاتُ النَموذَج، وحالَتا الأَداتَينِ المُعَلَّقَتَين، وهَل مُحِيَ السِجِلّ، والتَنقُّلات، وهَل فُتِحَ البابُ عِندَ
/// التَصييرِ الأَوَّل، وما رَماه المُكَوِّن.</summary>
internal sealed record CircuitOutcome(int ModelCalls, string? Applied, string? Rejected, bool Cleared, IReadOnlyList<string> Went, bool DoorOpen, IReadOnlyList<string> Errors)
{
    public override string ToString()
        => $"نِداءاتُ النَموذَج {ModelCalls} · الأَداةُ المُطَبَّقَة {Applied ?? "—"} · المَرفوضَة {Rejected ?? "—"} · مُحِيَ السِجِلّ {Cleared} · "
           + $"التَنقُّل {(Went.Count == 0 ? "لا شَيء" : string.Join(" ، ", Went))} · البابُ مَفتوحٌ عِندَ التَصييرِ الأَوَّل {DoorOpen} · أَخطاء {Errors.Count}";
}

[Collection(LiveProofCollection.Name)]
public class LiveSessionCircuitProofTests(ITestOutputHelper output)
{
    internal static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>ما يَقَعُ بَينَ التَصييرِ الأَوَّلِ والنَقر: لا شَيء، أَو «اخرج من كل الأجهزة» (رَفعُ الإصدارِ بِدالَّةِ البابِ نَفسِها)، أَو نَزعُ الإشراف.</summary>
    public enum Between { Nothing, SignedOutEverywhere, NoLongerAdmin }

    internal const string StudioPage = "studio", AdminPage = "admin";

    /// <summary>
    /// <para>يُرَكِّبُ الصَفحَةَ (<paramref name="page"/>: <see cref="StudioPage"/> أَو <see cref="AdminPage"/>) لِمُشرِفِ مَنَصَّةٍ مُؤَقَّتٍ في <c>_studio</c>، وفي سِجِلِّها
    /// أَداتانِ مُعَلَّقَتان، ثُمَّ يُوقِعُ <paramref name="between"/>، ثُمَّ يَنقُرُ «أَرسِل» و«طَبِّق» الأُولى و«ارفُض» الثانِيَة و«امسَح» — ويُعيدُ ما جَرى.
    /// والسِجِلُّ المُشتَرَكُ لِمُشرِفِ المَنَصَّة (<c>agent-default</c>) يُعادُ كَما كانَ بَعدَه، وحِسابُ المُشرِفِ وسِجِلُّه وسُطورُ قِياسِ المِسبارِ تُمحى.</para>
    /// </summary>
    internal static async Task<CircuitOutcome> RunAsync(string page, Between between)
    {
        var agents = new ProbeAgentBackends();
        await using var host = LiveTemplateDoorHost.Build(services: s =>
        {
            s.AddSingleton<IAgentBackendProvider>(agents);
            s.AddScoped<NavigationManager, ProbeNavigation>();
        });
        var store = host.Store;
        var admin = new StudioUser { Id = Guid.NewGuid(), Phone = "zz-g2c-" + Guid.NewGuid().ToString("N")[..8], IsPlatformAdmin = true, FullName = "مُشرِفُ المِسبار" };
        var sessionId = page == StudioPage ? "scope:studio-" + admin.Id.ToString("N") : AgentSession.SessionId;
        AgentSession? before;
        await using (var q = store.QuerySession("_admin")) before = await q.LoadAsync<AgentSession>(sessionId);
        static AgentTurn Pending(string id) => new() { Role = "assistant", Text = "أَداةٌ مُعَلَّقَة", Tool = new AgentToolCall { Id = id, Name = "set_branding", InputJson = "{}" } };
        try
        {
            await using (var s = store.LightweightSession(StudioAuth.Tenant)) { s.Store(admin); await s.SaveChangesAsync(); }
            await using (var s = store.LightweightSession("_admin"))
            {
                s.Store(new AgentSession { Id = sessionId, Turns = { Pending("g2-apply"), Pending("g2-reject") } });
                await s.SaveChangesAsync();
            }

            await using var scope = host.Services.CreateAsyncScope();
            var first = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            first.Request.Path = page == StudioPage ? "/studio/agent" : "/admin/agent";
            first.Request.Headers.Cookie = $"{StudioAuth.CookieName}={AuthHandlers.MakeToken(admin.Id, StudioAuth.Tenant, admin.SessionVersion)}";
            await using var renderer = new CircuitProbe(scope.ServiceProvider);
            // ‏`/admin/agent` بِمُضيفِه الثابِت (`AgentChat.razor` داخِلَ `RequirePlatformAdmin`) — هُوَ مَن يَرسُمُ الجَزيرَةَ بِـ`InteractiveServer` ويُمَرِّرُ `AdminUserId`.
            var type = page == StudioPage
                ? typeof(ACommerce.Templates.Customer.Marketplace.Components.Pages.StudioAgent)
                : typeof(ACommerce.Templates.Customer.Marketplace.Components.Pages.Admin.AgentChat);
            var component = await renderer.MountAsync(type, scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>(), first);
            var doorOpen = page == StudioPage
                ? CircuitProbe.Field<Guid>(component, "_userId") == admin.Id && CircuitProbe.Field<bool>(component, "_platformAdmin")
                : component is ACommerce.Templates.Customer.Marketplace.Components.Pages.Admin.AgentChatPanel { AdminUserId: { } id } && id == admin.Id;

            switch (between)
            {
                case Between.SignedOutEverywhere:
                    Assert.True(await SessionVersionGate.RevokeStudioAsync(store, host.Services.GetRequiredService<IMemoryCache>(), admin.Id));
                    break;
                case Between.NoLongerAdmin:
                    Assert.NotNull(await AccountWrites.UpdateAsync<StudioUser>(store, StudioAuth.Tenant, admin.Id, u => { u.IsPlatformAdmin = false; return true; }));
                    break;
            }

            var callsBefore = agents.Calls;
            var errors = new List<string>();
            async Task Click(string handler, params object[] args)
            {
                try { await renderer.ClickAsync(component, handler, args); }
                catch (Exception e) { errors.Add($"{handler}: {e.GetType().Name}: {e.Message}"); }
            }
            CircuitProbe.Set(component, "newMessage", "رِسالَةُ المِسبار");
            await Click("SendAsync");
            await Click("ApplyAsync", "g2-apply");
            await Click("RejectAsync", "g2-reject");
            AgentSession? mid;
            await using (var q = store.QuerySession("_admin")) mid = await q.LoadAsync<AgentSession>(sessionId);
            await Click("ResetAsync");
            AgentSession? after;
            await using (var q = store.QuerySession("_admin")) after = await q.LoadAsync<AgentSession>(sessionId);

            string? Status(string id) => mid?.Turns.LastOrDefault(t => t.Tool?.Id == id)?.Tool?.Status;
            var nav = (ProbeNavigation)scope.ServiceProvider.GetRequiredService<NavigationManager>();
            return new CircuitOutcome(agents.Calls - callsBefore, Status("g2-apply"), Status("g2-reject"), after is null, nav.Went.ToList(), doorOpen, errors);
        }
        finally
        {
            await using (var s = store.LightweightSession("_admin"))
            {
                if (before is null) s.Delete<AgentSession>(sessionId); else s.Store(before);
                await s.SaveChangesAsync();
            }
            await using (var s = store.LightweightSession(StudioAuth.Tenant))
            {
                s.Delete<StudioUser>(admin.Id);
                s.DeleteWhere<ACommerce.Templates.Customer.Marketplace.Services.Metering.ModelCallRecord>(r => r.Provider == ProbeAgentBackends.Name);
                await s.SaveChangesAsync();
            }
        }
    }

    /// <summary><b>ما يَبقى</b>: بِجَلسَةٍ مَفتوحَةٍ لِمُشرِفِ مَنَصَّةٍ تَعمَلُ الصَفحَتانِ — ثَلاثَةُ نِداءاتِ نَموذَج (الرِسالَةُ وما بَعدَ التَطبيقِ وما بَعدَ الرَفض)،
    /// والأَداةُ الأُولى نُفِّذَت (رَدَّها المُصادِقُ بِـ«error») والثانِيَةُ «rejected»، والسِجِلُّ مُحِي، ولا تَنقُّلَ ولا خَطَأ.</summary>
    [Theory]
    [InlineData(StudioPage)]
    [InlineData(AdminPage)]
    public async Task An_open_platform_admin_circuit_sends_applies_rejects_and_clears_on_Postgres(string page)
    {
        if (!Enabled) return;
        var outcome = await RunAsync(page, Between.Nothing);
        output.WriteLine($"{page} · جَلسَةٌ مَفتوحَة ⇒ {outcome}");
        Assert.Empty(outcome.Errors);
        Assert.True(outcome.DoorOpen);
        Assert.Equal(3, outcome.ModelCalls);
        Assert.Equal("error", outcome.Applied);
        Assert.Equal("rejected", outcome.Rejected);
        Assert.True(outcome.Cleared);
        Assert.Empty(outcome.Went);
    }

    /// <summary><b>ما يُطبَعُ ولا يُثَبَّت</b>: الدائِرَةُ المَفتوحَةُ بَعدَ «اخرج من كل الأجهزة» أَو بَعدَ نَزعِ الإشراف — اليَومَ تُرسِلُ وتُطَبِّقُ وتَرفُضُ وتَمحو كَما
    /// لَو لَم يَقَع شَيء. ويُثَبَّتُ ما لا يَتَغَيَّر: البابُ فُتِحَ عِندَ التَصييرِ الأَوَّلِ ولا خَطَأ.</summary>
    [Theory]
    [InlineData(StudioPage, Between.SignedOutEverywhere)]
    [InlineData(StudioPage, Between.NoLongerAdmin)]
    [InlineData(AdminPage, Between.SignedOutEverywhere)]
    [InlineData(AdminPage, Between.NoLongerAdmin)]
    public async Task A_circuit_opened_before_the_session_closed_is_measured_on_Postgres(string page, Between between)
    {
        if (!Enabled) return;
        var outcome = await RunAsync(page, between);
        output.WriteLine($"{page} · {between} ⇒ {outcome}");
        Assert.Empty(outcome.Errors);
        Assert.True(outcome.DoorOpen);
    }
}
