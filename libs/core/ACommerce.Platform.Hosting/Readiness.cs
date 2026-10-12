using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ACommerce.Platform.Hosting;

/// <summary>
/// <para><b>«أَتُجيبُ القاعِدَة؟» — <c>/health/ready</c></b>، أُختُ <c>/health</c> لا بَديلُها (‏2026-10-12، موجزُ الفجوات
/// P3-4، وتَنفيذُ ما رَسَمَه ‏ADR-019 §٥ وتَرَكَه «لَم يُنفَّذ»).</para>
///
/// <para><b>العِلَّةُ المَقيسَة</b>: إقلاعٌ بِوَضعِ Production، ثُمَّ القاعِدَةُ تُغلَقُ تَحتَه — <c>/</c> و<c>/ejar</c> ‏500،
/// و<c>/health</c> ‏200 بِبَصمَةٍ صَحيحَة. وهذا بِعَينِه سيناريو و‑٥ (حِصَّةُ Neon نَفِدَت). <c>/health</c> لا يُخطِئُ هُنا:
/// <b>هُوِيَّةٌ وحَياة</b>، يَرُدُّ ‏200 دائِماً بِنَصِّ ‏ADR-019 §٥ — ووَظيفَةُ النَشرِ (‏<c>deploy-hf.yml</c>) تَستَطلِعُه وتَبني عَلى
/// ذلك، فَلا يُمَسّ. والسُؤالُ الآخَرُ يَحتاجُ مَساراً آخَر.</para>
///
/// <para><b>ولِماذا <c>/health/ready</c> لا <c>/ready</c> الَّذي سَمّاهُ ‏ADR-019 — مَقيسٌ لا ذَوق</b>: المَقطَعُ الأَوَّلُ
/// <c>health</c> مَحجوزٌ في <c>ReservedTenantSlugs</c>، فَـ<c>TenantResolverMiddleware.SlugFromPath("/health/ready")</c> =
/// <c>null</c>. أَمّا <c>/ready</c> فَيُعطي <c>"ready"</c> — أَي أَنّ الوَسيطَ <b>يَسأَلُ القاعِدَةَ عَن مُستَأجِرٍ اسمُه
/// ready قَبلَ أَن تَبلُغَ النُقطَة</b>: بِقاعِدَةٍ ساقِطَةٍ يَرمي فَيُرَدُّ ‏500 لا ‏503، وبِقاعِدَةٍ سَليمَةٍ يَستَعلِمُ مَرَّتَين
/// (والغائِبُ لا يُخَزَّن). وتَحتَ نِطاقٍ فَرعِيٍّ مُهَيَّأٍ يُحقَنُ السلاجُ قَبلَه فَيَصيرُ <c>/{slug}/ready</c>. والمَحجوزُ
/// يَسلَمُ مِن الثَلاث.</para>
///
/// <para><b>وما تَفعَلُه — بِحَدِّ سِكّين</b>:</para>
/// <list type="bullet">
///   <item><b><c>select 1</c> عَلى اتِّصالٍ مِن مَخزَنِ Marten نَفسِه</b> — القاعِدَةُ الَّتي يَخدِمُ مِنها التَطبيقُ لا سِلسِلَةٌ
///   تُقرَأُ ثانِيَةً. <b>واستِعلامٌ لا فَتحُ اتِّصالٍ وَحدَه</b>: الاتِّصالُ مِن البِركَةِ يُفتَحُ بِلا ذَهابٍ إلى الخادِم،
///   فَـ«فُتِحَ» تَخضَرُّ عَلى قاعِدَةٍ ساقِطَة.</item>
///   <item><b>لا جَلسَةَ ولا كِتابَة</b>: لا <c>IDocumentSession</c> ولا مُعامَلَةَ ولا صُندوقاً صادِراً ولا مُستَأجِراً —
///   فَالمَخزَنُ يُقرَأُ عِندَ التَركيبِ لا وَسيطاً لِلامدا، وما يَحرُسُه <c>EndpointStoreInjectionTests</c> (جَلسَةٌ تُفتَحُ
///   بِاليَدِ خارِجَ المُعامَلَة) لا يَقَعُ هُنا. واختِبارٌ يَمسَحُ هذا المِلَفَّ ويَشتَرِطُ ذلك.</item>
///   <item><b>‏200 <c>{"ready":true}</c> أَو ‏503 <c>{"ready":false}</c> مَعَ <c>Retry-After: 5</c></b>، و<c>no-store</c> في
///   الحالَين. والجِسمُ لَيسَ تَكراراً لِلرَمز: بِه تُمَيَّزُ النُقطَةُ مِن صَفحَةٍ تَبتَلِعُ المَسار — دَرسُ <c>/health</c>
///   المَقيس.</item>
///   <item><b>والجَوابُ مُخَزَّنٌ خَمسَ ثَوانٍ في الذاكِرَة</b> (<see cref="ReadinessVerdicts"/>)، واستِطلاعٌ واحِدٌ في
///   الطَيَران: نُقطَةٌ عامَّةٌ بِلا تَحديدِ مُعَدَّلٍ لا تَصيرُ باباً عَلى Neon — وهو شَرطُ ‏ADR-019 §٥ نَفسُه. <b>وقيمَتُه
///   الخَمسُ تَقديرٌ مُعلَن</b> لا رَقَمُ مُنتَج: تُساوي ‏<c>Retry-After</c> وسَقفَ الاستِطلاعِ مَعاً.</item>
/// </list>
///
/// <para><b>وشَرطُ الاستِخراج</b> (القاعِدَة ١) كَـ<see cref="BuildIdentity"/>: مُستَهلِكٌ تَشغيليٌّ واحِد.</para>
/// </summary>
public static class Readiness
{
    public const string Path = "/health/ready";

    /// <summary>عُمرُ الجَوابِ المُخَزَّن، وسَقفُ الاستِطلاعِ الواحِد، و<c>Retry-After</c> — رَقَمٌ واحِدٌ لِثَلاثَتِها.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

    /// <summary>
    /// <para>يُسَجِّلُ <c>GET</c> و<c>HEAD</c> عَلى <see cref="Path"/> — <b>HEAD لِلعِلَّةِ نَفسِها في <c>/health</c></b>: أَكثَرُ
    /// أَدَواتِ المُراقَبَةِ تَبعَثُه، و<c>MapGet</c> وَحدَه يُسقِطُه إلى غَيرِ النُقطَة.</para>
    ///
    /// <para>والمَخزَنُ يُحَلُّ <b>هُنا مَرَّةً</b> (‏singleton)، فَمُضيفٌ بِلا Marten يَفشَلُ عِندَ التَركيبِ صَريحاً لا عِندَ
    /// أَوَّلِ استِطلاع.</para>
    /// </summary>
    public static IEndpointRouteBuilder MapReadiness(this IEndpointRouteBuilder endpoints)
    {
        var store = endpoints.ServiceProvider.GetRequiredService<IDocumentStore>();
        var log = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(Readiness).FullName!);
        var verdicts = new ReadinessVerdicts(Window, TimeProvider.System);

        // ‏`RequestDelegate` صَريحاً — والمُعالِجُ يَكتُبُ رَدَّه بِنَفسِه. **مَقيس**: لامدا وَسيطُها `HttpContext` وَحدَه
        // وتُعيدُ `Task<IResult>` تُطابِقُ هذا التَحميلَ فَيُرمى `IResult` صامِتاً ويَخرُجُ ‏200 بِلا جِسم (المُحَلِّلُ ASP0016).
        //
        // **والمَسارُ حَرفِيٌّ هُنا لا `Path`** — مَقيس: ماسِحاتُ النِقاطِ الثَلاثَة (عَدّادُ النَزيف، وحارِسُ الكِتابَة، وحَقنُ
        // المَخزَن) تَقرَأُ `.Map…("…"`، فَتَسجيلٌ بِالثابِتِ مَرَّ بِها أَخضَرَ **وهو غَيرُ مَرئِيٍّ لَها** — الثَقبُ الَّذي
        // سَمّاهُ ‏ADR-019 §٩ في `MapMethods` يَعودُ مِن بابٍ آخَر. واختِبارٌ يَشتَرِطُ تَساوِيَ الحَرفِيِّ والثابِت.
        endpoints.MapMethods("/health/ready", new[] { HttpMethods.Get, HttpMethods.Head },
            ctx => AnswerAsync(ctx, verdicts, store, log));

        return endpoints;
    }

    private static async Task AnswerAsync(
        HttpContext ctx, ReadinessVerdicts verdicts, IDocumentStore store, ILogger log)
    {
        var ready = await verdicts.ReadyAsync(
            ct => PingAsync(store, ct),
            ex => log.LogWarning("[readiness] القاعِدَةُ لا تُجيب: {Type}: {Message}", ex.GetType().Name, ex.Message),
            ctx.RequestAborted);

        ctx.Response.Headers.CacheControl = BuildIdentity.NoStore;
        ctx.Response.Headers.Pragma = "no-cache";
        if (ready)
        {
            await Results.Json(new { ready = true }).ExecuteAsync(ctx);
            return;
        }

        ctx.Response.Headers.RetryAfter = ((int)Window.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await Results.Json(new { ready = false }, statusCode: StatusCodes.Status503ServiceUnavailable).ExecuteAsync(ctx);
    }

    /// <summary>ذَهابٌ وإيابٌ واحِدٌ إلى الخادِم. يَرمي إن لَم يُجِب — والرَميُ حُكمُ «غَيرُ جاهِز» في
    /// <see cref="ReadinessVerdicts.ReadyAsync"/>.</summary>
    public static async Task<bool> PingAsync(IDocumentStore store, CancellationToken ct)
    {
        await using var connection = store.Storage.Database.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("select 1", connection);
        return await command.ExecuteScalarAsync(ct) is 1;
    }
}

/// <summary>
/// <para><b>الحُكمُ المُخَزَّن</b> — يَستَطلِعُ مَرَّةً في كُلِّ نافِذَةٍ عَلى الأَكثَر، وواحِداً في الطَيَرانِ عَلى الأَكثَر،
/// ويُعيدُ ما قاسَه آخِرَ مَرَّةٍ لِكُلِّ مَن سَأَلَ داخِلَ النافِذَة. <b>والسُقوطُ يُخَزَّنُ كَالنَجاح</b> — وإلّا صارَ كُلُّ
/// استِطلاعٍ في انقِطاعٍ مُحاوَلَةَ اتِّصالٍ جَديدَةً، وهي اللَحظَةُ الأَسوَأُ لِذلك.</para>
///
/// <para><b>وثَلاثُ حالاتٍ تُقرَأُ «غَيرُ جاهِز»</b>: استِطلاعٌ أَعادَ <c>false</c>، أَو رَمى، أَو تَجاوَزَ النافِذَة (يُقطَع).
/// <b>وواحِدَةٌ لا تُكتَبُ حُكماً</b>: سائِلٌ انصَرَفَ (<c>RequestAborted</c>) — انصِرافُه لا يَقولُ شَيئاً عَن القاعِدَة.</para>
///
/// <para>عامٌّ لِيُقاسَ بِساعَةٍ يَدَوِيَّةٍ واستِطلاعٍ يَعُدُّ نَفسَه — لا لِيُستَعمَلَ في مَوضِعٍ آخَر.</para>
/// </summary>
public sealed class ReadinessVerdicts
{
    private sealed record Verdict(bool Ready, DateTimeOffset At);

    private readonly TimeSpan _window;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Verdict? _last;

    public ReadinessVerdicts(TimeSpan window, TimeProvider time)
    {
        _window = window;
        _time = time;
    }

    public async Task<bool> ReadyAsync(
        Func<CancellationToken, Task<bool>> probe, Action<Exception>? failed, CancellationToken ct)
    {
        if (Fresh() is { } cached)
            return cached.Ready;

        await _gate.WaitAsync(ct);
        try
        {
            // مَن انتَظَرَ خَلفَ استِطلاعٍ جارٍ يَأخُذُ نَتيجَتَه ولا يَستَطلِعُ ثانِيَة.
            if (Fresh() is { } measuredMeanwhile)
                return measuredMeanwhile.Ready;

            using var cut = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cut.CancelAfter(_window);

            bool ready;
            try
            {
                ready = await probe(cut.Token);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                failed?.Invoke(ex);
                ready = false;
            }

            Volatile.Write(ref _last, new Verdict(ready, _time.GetUtcNow()));
            return ready;
        }
        finally
        {
            _gate.Release();
        }
    }

    private Verdict? Fresh()
    {
        var last = Volatile.Read(ref _last);
        return last is not null && _time.GetUtcNow() - last.At < _window ? last : null;
    }
}
