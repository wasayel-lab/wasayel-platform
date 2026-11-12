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
        services.TryAddSingleton(sp => new ProviderCalls(sp.GetService<OutboundRecorder>()));
        var name = typeof(TKit).Name;
        var builder = configure is null ? services.AddHttpClient(name) : services.AddHttpClient(name, configure);
        services.TryAddTransient(sp => new ProviderHttp<TKit>(sp.GetRequiredService<IHttpClientFactory>().CreateClient(name), sp.GetRequiredService<ProviderCalls>()));
        return builder;
    }
}
