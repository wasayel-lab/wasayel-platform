using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ACommerce.Platform.Shared.Metering;

/// <summary>
/// <para><b>مالِكُ <c>HttpClient</c> الوَحيدُ لِعُدَدِ HTTP</b> (التَصميم §6.1): العُدَّةُ تَحمِلُ <c>ProviderHttp&lt;TKit&gt;</c> ولا تَحمِلُ <c>HttpClient</c>، فَكُلُّ طَلَبٍ
/// يُغادِرُها يَمُرُّ بِـ<see cref="SendAsync"/> وفي تَوقيعِه <see cref="ProviderCall"/> — ونِسيانُه خَطَأُ بِناء (القاعِدَة ٦). والطَبَقَةُ ٨-ج
/// (<c>scripts/verify-architecture.sh</c>) تُحَمِّرُ أَيَّ مِلَفٍّ في <c>libs/</c> يُسَمّي نَوعَ عَميلٍ خامٍّ خارِجَ قائِمَتِها، وهذا المِلَفُّ فيها.</para>
/// <para><b>النَتيجَةُ مِن الحالَة</b> (§6.2): ‏2xx قُبِل، و4xx (ومِنه 429) رُفِض، وما سِواهُما — 5xx، أَو مُهلَتُنا، أَو إلغاءُ المُنادي، أَو استِثناءٌ بَعدَ الإرسال —
/// مَجهول. والاستِثناءُ يُعادُ رَميُه كَما هُوَ، والجَوابُ يُعادُ كَما هُوَ: العُدَّةُ تُحَوِّلُ أَخطاءَها كَما كانَت.</para>
/// </summary>
public sealed class ProviderHttp<TKit>(HttpClient http, ProviderCalls calls)
{
    /// <summary>هَل يُكتَبُ ما يَمُرُّ هُنا؟ — يَقرَؤُه <c>MeterCompositionTests</c> (G6d) عَلى المُضيفِ المُرَكَّب.</summary>
    public bool IsRecording => calls.IsRecording;

    public Task<HttpResponseMessage> SendAsync(ProviderCall call, HttpRequestMessage request, CancellationToken ct)
        => calls.InvokeAsync(call, t => http.SendAsync(request, t), Classify, ct);

    /// <summary>‏(‏2026-11-12، W13-6·١٣) <b>طَلَبٌ مُساعِدٌ لا يُعَدّ</b> (رَمزُ OAuth، استِطلاعُ الحالَة) — بِالعَميلِ نَفسِه، والجَوابُ كَما هُوَ.</summary>
    public Task<HttpResponseMessage> SendHelperAsync(ProviderHelper helper, HttpRequestMessage request, CancellationToken ct)
        => calls.HelperAsync(helper, t => http.SendAsync(request, t), ct);

    /// <summary>
    /// <para>‏(‏2026-11-12، W13-6·١٢ — ADR-051 §١٨؛ التَصميم §6.3) <b>مَقعَدُ اختِبار</b>: <c>HttpClient</c> يَصيرُ مُستَدعِياً لا يُسَجِّل
    /// (<see cref="ProviderCalls.Unrecorded"/>) — فَيَبقى بِناءُ العُدَّةِ في اختِباراتِها بِعَميلٍ خامٍّ كَما كانَ (<c>new BrevoEmailChannel(…, new HttpClient(handler), …)</c>،
    /// أَوَّلُ مُستَهلِك). <b>ولا يَبلُغُه كودُ الإنتاج</b>: لا يُسَمّي <c>HttpClient</c> في <c>libs/</c> إلّا هذا المِلَفُّ (الطَبَقَة ٨-ج)، و<c>MeterTestSeamTests</c>
    /// (G6c) يَحمَرُّ عَلى مُنادٍ لَه في <c>libs/</c> أَو <c>apps/</c>.</para>
    /// </summary>
    public static implicit operator ProviderHttp<TKit>(HttpClient http) => new(http, ProviderCalls.Unrecorded);

    /// <summary>النَتيجَةُ مِن حالَةِ الجَواب.</summary>
    public static ProviderAcceptance Classify(HttpResponseMessage response) => (int)response.StatusCode switch
    {
        >= 200 and <= 299 => new ProviderAcceptance(ProviderOutcome.Accepted),
        >= 400 and <= 499 => new ProviderAcceptance(ProviderOutcome.Refused),
        _ => new ProviderAcceptance(ProviderOutcome.Unknown),
    };
}

/// <summary>تَسجيلُ عَميلِ العُدَّة — مَكانَ <c>AddHttpClient&lt;…&gt;</c>.</summary>
public static class ProviderHttpExtensions
{
    /// <summary>
    /// <para>عَميلٌ مُسَمّىً بِاسمِ العُدَّةِ يَبنيه مَصنَعُ الإطار (فَتَسري عَلَيه إعداداتُه الافتِراضِيَّة ومُعالِجاتُه، ويُعادُ ما يُعيدُه <c>AddHttpClient</c> لِيُضبَطَ
    /// بِه ما كانَ يُضبَطُ)، و<see cref="ProviderHttp{TKit}"/> عابِرٌ — حَياةُ العَميلِ المُطَبَّعِ الَّذي يَحِلُّ مَحَلَّه — و<see cref="ProviderCalls"/> مُفرَدٌ بِكاتِبِ
    /// المُضيفِ إن سُجِّل (<see cref="OutboundRecorder"/>، يُسَجِّلُه القالِب).</para>
    /// </summary>
    public static IHttpClientBuilder AddProviderHttp<TKit>(this IServiceCollection services, Action<IServiceProvider, HttpClient>? configure = null)
        where TKit : class
    {
        services.AddProviderCalls();
        var name = typeof(TKit).Name;
        var builder = configure is null ? services.AddHttpClient(name) : services.AddHttpClient(name, configure);
        services.TryAddTransient(sp => new ProviderHttp<TKit>(sp.GetRequiredService<IHttpClientFactory>().CreateClient(name), sp.GetRequiredService<ProviderCalls>()));
        return builder;
    }
}
