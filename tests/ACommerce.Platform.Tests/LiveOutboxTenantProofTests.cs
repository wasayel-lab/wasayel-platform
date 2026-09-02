using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using ACommerce.Platform.Hosting;
using JasperFx.Events.Projections;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── بُرهان الرَبط — الجَلسَة المَحقونَة: مَحصورَة؟ ومُنخَرِطَة؟ ───────
//
// **الدَعوى المَفحوصَة** — وُسِمَت في وَثيقَة القَرار المِعماريّ بِـ
// ‏«[غَير مُثبَت]» حَرفاً:
//
//   > أَنّ `OutboxedSessionFactory` يُمَرِّر `Envelope.TenantId` إلى
//   > `store.LightweightSession(tenantId)` تِلقائيّاً. الاسمانِ
//   > `ForTenant` و`TenantId` مَوجودانِ في `Wolverine.Marten.dll`، لَكِنّ
//   > التَوثيق XML لا يُصَرِّح بِالرَبط.
//
// وعَلَيها بُنِيَ البَند ١ كُلُّه (‏`opts.TenantId.IsRouteArgumentNamed("slug")`):
// إن لَم يَصِل مُعَرِّف المُستَأجِر مِن وَسيط المَسار إلى الجَلسَة
// المَحقونَة، فَالإعداد زينَةٌ لا عَزل — والتَرحيل عَلَيه **يُصَدِّر
// عَطَباً ويُسَمّيه تَحسيناً**.
//
// **ولِماذا تَجرِبَة حَيَّة لا قِراءَة IL**: التَوثيق لا يُصَرِّح،
// والـIL يُقرَأ فَيُؤَوَّل. القاعِدَة ١٠: القِياس هو الحَكَم. وهذا
// المِلَفّ **يُقلِع مُضيفاً حَقيقيّاً** بِنَفس تَركيب
// `HostingExtensions` — Marten بِإيجار مُقتَرِن، و`IntegrateWithWolverine()`،
// و`AutoApplyTransactions()` — ثُمَّ يَسأَل الجَلسَة نَفسَها عَن
// `TenantId` و`Listeners`.
//
// **ولا يَعمَل في التَشغيل العادِيّ**: يَتَخَطّى نَفسَه ما لَم تُضبَط
// `WASAYEL_LIVE_PROOF=1` و`ConnectionStrings__Postgres` — نَفس عَقد
// `LiveQuotaRaceProofTests` حَرفاً.
//
// **ولا يَكتُب وَثيقَةً واحِدَة**: يَقرَأ خاصِّيَّتَين عَلى جَلسَة
// ويُغلِقُها بِلا `SaveChangesAsync`. الأَثَر عَلى القاعِدَة: صِفر.

// ─── مَوضوعا الفَحص ───────────────────────────────────────────────────

/// <summary>رِسالَة بُرهان — لا تَكتُب شَيئاً، تُبلِغ عَن الجَلسَة
/// الَّتي حُقِنَت فيها.</summary>
public sealed record ProbeSessionTenant;

/// <summary>ما رَأَته الجَلسَة المَحقونَة: مُستَأجِرُها، ومُستَمِعوها
/// (‏وُجودُ مُستَمِعٍ مِن Wolverine هُوَ أَثَر الانخِراط في
/// الصُندوق).</summary>
public sealed record SessionFacts(string TenantId, string Listeners);

/// <summary><b>مُعالِج رِسالَة</b> — يَسلُك مَسار
/// <c>OutboxedSessionFactory.OpenSession(MessageContext)</c>، وهو
/// بِالضَبط المَسار الَّذي تَسلُكُه <c>NotificationHandlers.Send</c>
/// اليَوم.</summary>
public static class ProbeSessionTenantHandler
{
    public static SessionFacts Handle(ProbeSessionTenant _, IDocumentSession session)
        => new(session.TenantId, Describe(session));

    /// <summary><b>الاسم الكامِل لا القَصير</b> — والفَرق كَلَّفَ جَولَة:
    /// أَوَّل صيغَة استَعمَلَت <c>GetType().Name</c> فَأَعطَت
    /// <c>FlushOutgoingMessagesOnCommit</c>، وشَرطُ «مِن Wolverine»
    /// احمَرَّ عَلى **الأَداة** لا عَلى المَفحوص. الاسم الكامِل يَحمِل
    /// النِطاق <c>Wolverine.Marten.Publishing</c> — فَيُقاس الانخِراط
    /// بِمَصدَرِه لا بِلَفظِه.</summary>
    internal static string Describe(IDocumentSession s) =>
        s.Listeners.Count == 0
            ? "—"
            : string.Join(",", s.Listeners.Select(l => l.GetType().FullName));
}

/// <summary><b>نُقطَة HTTP</b> — تَسلُك مَسار Wolverine.Http مَعَ كَشف
/// المُستَأجِر مِن وَسيط المَسار. وهذا هُوَ المَسار الَّذي يَعتَمِد
/// عَلَيه البَند ١.</summary>
public static class ProbeTenantEndpoint
{
    /// <summary>ما رَأَتهُ آخِر نُقطَة — الرَدّ نَصّاً أَبسَط، لَكِنّ
    /// التَقاطَه هُنا يُجَنِّب الاعتِماد عَلى تَسَلسُل JSON.</summary>
    public static readonly ConcurrentQueue<SessionFacts> Seen = new();

    [WolverinePost("/{slug}/zz-tenant-proof")]
    public static string Probe(IDocumentSession session)
    {
        var facts = new SessionFacts(session.TenantId, ProbeSessionTenantHandler.Describe(session));
        Seen.Enqueue(facts);
        return facts.TenantId;
    }
}

// ─── البُرهان ─────────────────────────────────────────────────────────

public class LiveOutboxTenantProofTests
{
    private const string RouteTenant = "zz-proof-route";
    private const string BusTenant   = "zz-proof-bus";

    private static bool Enabled =>
        Environment.GetEnvironmentVariable("WASAYEL_LIVE_PROOF") == "1";

    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");

    /// <summary>نَفس عِلَّة <c>LiveQuotaRaceProofTests</c>: حاسوب Neon
    /// يَنام، فَأَوَّل اتِّصال بَعدَ نَومٍ يَتَجاوَز المُهلَة
    /// الافتِراضِيَّة — فَتَفشَل الأَداة لا المَفحوص.</summary>
    private static string ResilientConnection =>
        ConnectionString!.TrimEnd(';') + ";Timeout=60;Command Timeout=120";

    private static int FreePort()
    {
        using var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    /// <summary><b>نَفس تَركيب <c>HostingExtensions</c> في ما يَخُصّ
    /// الدَعوى</b>: إيجار مُقتَرِن، جَلسات خَفيفَة،
    /// <c>IntegrateWithWolverine()</c>، <c>AutoApplyTransactions()</c>.
    /// البُرهان لا يَصِحّ عَلى تَركيب آخَر.</summary>
    private static WebApplication BuildApp(int port, bool detectTenantFromRoute)
    {
        var builder = WebApplication.CreateSlimBuilder();

        builder.Services.AddMarten(o =>
            {
                o.Connection(ResilientConnection);
                o.DatabaseSchemaName = "platform";
                o.Policies.AllDocumentsAreMultiTenanted();
                o.Events.TenancyStyle = global::JasperFx.MultiTenancy.TenancyStyle.Conjoined;
                o.Schema.For<ACommerce.Kit.Tenants.Tenant>().SingleTenanted().Identity(x => x.Id);
                o.Projections.Snapshot<ACommerce.Kit.Listings.Listing>(SnapshotLifecycle.Inline);
                o.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
            })
            .UseLightweightSessions()
            .IntegrateWithWolverine();

        builder.Host.UseWolverine(opts =>
        {
            opts.UseRuntimeCompilation();
            opts.Discovery.IncludeAssembly(typeof(LiveOutboxTenantProofTests).Assembly);
            opts.Policies.AutoApplyTransactions();
        });

        builder.Services.AddWolverineHttp();

        var app = builder.Build();
        app.Urls.Add($"http://127.0.0.1:{port}");
        app.MapWolverineEndpoints(opts =>
        {
            // ← البَند ١ بِعَينِه. والمُتَغَيِّر يَجعَل الفَحص
            //   يَرى الفَرق بَينَ الحالَتَين بَدَل أَن يُصَدِّق إحداهُما.
            if (detectTenantFromRoute) opts.TenantId.IsRouteArgumentNamed("slug");
        });
        return app;
    }

    /// <summary>
    /// <para><b>الدَعوى الأولى — مَسار الرِسالَة</b>:
    /// <c>InvokeForTenantAsync(t, msg)</c> يَجعَل
    /// <c>IDocumentSession</c> المَحقونَة مَحصورَةً بِـ<c>t</c>.</para>
    ///
    /// <para><b>والضِدّ في نَفس الاختِبار</b>: نِداءٌ بِلا مُستَأجِر
    /// يُعطي <c>*DEFAULT*</c>. بِدونِ هذا الطَرَف، اختِبارٌ يُعطي
    /// «مَحصورَة» لا يُمَيَّز عَن اختِبارٍ يَقرَأ ثابِتاً.</para>
    /// </summary>
    [Fact]
    public async Task Injected_session_carries_the_message_tenant_and_is_enrolled_in_the_outbox()
    {
        if (!Enabled || string.IsNullOrEmpty(ConnectionString)) return;

        await using var app = BuildApp(FreePort(), detectTenantFromRoute: true);
        await app.StartAsync();

        // ‏`IMessageBus` مُسَجَّلَة scoped — وحَلُّها مِن الجَذر يَرمي.
        // النِطاق هُنا لَيسَ تَجميلاً: هُوَ ما يَفعَلُه الطَلَب نَفسُه.
        using var scope = app.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var scoped = await bus.InvokeForTenantAsync<SessionFacts>(BusTenant, new ProbeSessionTenant());
        Console.WriteLine($"[bus/tenanted] TenantId={scoped.TenantId} Listeners={scoped.Listeners}");

        var plain = await bus.InvokeAsync<SessionFacts>(new ProbeSessionTenant());
        Console.WriteLine($"[bus/plain]    TenantId={plain.TenantId} Listeners={plain.Listeners}");

        await app.StopAsync();

        Assert.Equal(BusTenant, scoped.TenantId);

        // الضِدّ: بِلا مُستَأجِر تَقَع الكِتابَة في *DEFAULT* — وهذا
        // بِعَينِه العَطَب الَّذي وَصَفَته الوَثيقَة في المُعالِجات.
        Assert.NotEqual(BusTenant, plain.TenantId);

        // والانخِراط في الصُندوق: مُستَمِعٌ مِن Wolverine عَلى الجَلسَة.
        Assert.Contains("Wolverine", scoped.Listeners, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <para><b>الدَعوى الثانِيَة — وهي مِفصَل البَند ١</b>: مَعَ
    /// <c>opts.TenantId.IsRouteArgumentNamed("slug")</c> تَصِل قيمَة
    /// وَسيط المَسار إلى <c>IDocumentSession</c> المَحقونَة في نُقطَة
    /// HTTP.</para>
    ///
    /// <para><b>وتُقاس بِطَرَفَيها</b>: نَفس النُقطَة، نَفس الطَلَب،
    /// مَرَّةً بِالكَشف ومَرَّةً بِدونِه. فَإن تَساوى الطَرَفانِ
    /// فَالإعداد لا يَفعَل شَيئاً — وذاكَ كَسرٌ لِلدَعوى لا
    /// إثباتٌ لَها.</para>
    /// </summary>
    [Fact]
    public async Task Route_argument_tenant_reaches_the_injected_session_in_an_http_endpoint()
    {
        if (!Enabled || string.IsNullOrEmpty(ConnectionString)) return;

        var withDetection    = await ProbeHttpAsync(detect: true);
        var withoutDetection = await ProbeHttpAsync(detect: false);

        Console.WriteLine($"[http/detect=on ] TenantId={withDetection.TenantId} Listeners={withDetection.Listeners}");
        Console.WriteLine($"[http/detect=off] TenantId={withoutDetection.TenantId} Listeners={withoutDetection.Listeners}");

        Assert.Equal(RouteTenant, withDetection.TenantId);
        Assert.NotEqual(RouteTenant, withoutDetection.TenantId);
        Assert.Contains("Wolverine", withDetection.Listeners, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<SessionFacts> ProbeHttpAsync(bool detect)
    {
        var port = FreePort();
        await using var app = BuildApp(port, detect);
        await app.StartAsync();

        while (ProbeTenantEndpoint.Seen.TryDequeue(out _)) { }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var res = await http.PostAsync(
            $"http://127.0.0.1:{port}/{RouteTenant}/zz-tenant-proof",
            new StringContent(""));
        res.EnsureSuccessStatusCode();

        await app.StopAsync();

        Assert.True(ProbeTenantEndpoint.Seen.TryDequeue(out var facts),
            "أَداة عَمياء: النُقطَة لَم تُستَدعَ إطلاقاً.");
        return facts!;
    }

    /// <summary>
    /// <para><b>والطَرَفُ الثالِث — وهُوَ الَّذي كانَ تَعليقاً فَصارَ
    /// قِياساً</b>: نُقطَةُ <b>Minimal API</b> تَحقِنُ الجَلسَةَ نَفسَها،
    /// على مُضيفٍ كَشفُ المُستَأجِرِ فيه <b>مُفَعَّل</b> — وتَحمِلُ
    /// <c>*DEFAULT*</c> لا وَسيطَ المَسار.</para>
    ///
    /// <para><b>ولِماذا يُكتَبُ هذا الآن</b>: الجُملَةُ كانَت مَكتوبَةً في
    /// <c>PinnedRoutes</c> و<c>HostingExtensions</c> نَصّاً — ولَم تَمنَع
    /// أَحَداً، لِأَنّ رِسالَةَ فَحصٍ مُجاوِرَةً كانَت تَقولُ عَكسَها.
    /// وحينَ التَقى النَصّانِ في رَأسِ كاتِبٍ واحِدٍ خَرَجَت
    /// <c>/{slug}/me/delete/confirm</c> تَحقِنُ الجَلسَةَ، فَصارَت شاشَةُ
    /// حَذفِ الحِسابِ <b>تَنقُرُ ولا تَحذِف</b>. <b>والجُملَةُ الَّتي لا
    /// يُنتِجُها أَمرٌ تُقرَأُ ولا تُصَدَّق</b> — فَهذا هُوَ الأَمر.</para>
    /// </summary>
    [Fact]
    public async Task Minimal_api_session_does_not_carry_the_route_tenant_even_with_detection_on()
    {
        if (!Enabled || string.IsNullOrEmpty(ConnectionString)) return;

        var port = FreePort();
        await using var app = BuildApp(port, detectTenantFromRoute: true);

        // نَفسُ المُضيفِ الَّذي تَعمَلُ عَلَيه نُقطَةُ Wolverine أَعلاه —
        // فَالفَرقُ الوَحيدُ بَينَ الطَرَفَين هُوَ الأُنبوب.
        app.MapPost("/{slug}/zz-minimal-tenant-proof",
            (string slug, IDocumentSession session) => session.TenantId);

        await app.StartAsync();

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var res = await http.PostAsync(
            $"http://127.0.0.1:{port}/{RouteTenant}/zz-minimal-tenant-proof",
            new StringContent(""));
        res.EnsureSuccessStatusCode();
        var tenantId = (await res.Content.ReadAsStringAsync()).Trim();

        await app.StopAsync();

        Console.WriteLine($"[minimal/detect=on] TenantId={tenantId}");

        Assert.False(string.IsNullOrWhiteSpace(tenantId),
            "أَداة عَمياء: النُقطَة رَدَّت فارِغاً — لا حُكمَ على مُستَأجِرٍ لَم يُقرَأ.");

        // الحُكم: كَشفُ المُستَأجِرِ مُفَعَّلٌ، ومَع ذلك **لا يَبلُغُ**
        // جَلسَةَ Minimal API. وهذا بِعَينِه سَبَبُ سَطرِ
        // ‏`/{slug}/me/delete/confirm` في PinnedRoutes.StoreTakers.
        Assert.NotEqual(RouteTenant, tenantId);
    }
}
