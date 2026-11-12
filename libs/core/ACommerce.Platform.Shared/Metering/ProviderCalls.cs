namespace ACommerce.Platform.Shared.Metering;

/// <summary>
/// <para><b>كاتِبُ الصادِر</b> — يُنَفِّذُه القالِبُ (<c>UsageLedgerWriter</c>) لِأَنّه يَملِكُ القاعِدَة، وهذه المَكتَبَةُ بِلا Marten. <b>مُراقِبٌ لا حارِس</b>
/// (‏ADR-031، القاعِدَة ٧): لا يَرُدُّ نِداءً ولا يُغَيِّرُ جَوابَه، ولا يَرمي أَبَداً — ما يَتَعَذَّرُ عَلَيه يَطبَعُه سَطراً بِاسمِه (<c>[meter] outbound-…</c>).</para>
/// <list type="number">
/// <item><see cref="OpenAsync"/> <b>قَبلَ</b> النِداء: نِيَّةٌ <c>pending</c> في مُعامَلَتِها القَصيرَة؛ وفَشَلُها لا يَمنَعُ النِداء.</item>
/// <item><see cref="DecideAsync"/> بَعدَه: المَقبولُ سَطرٌ ونَتيجَةٌ في مُعامَلَةٍ واحِدَة، والمَرفوضُ والمَجهولُ نَتيجَةٌ عَلى النِيَّةِ وَحدَها.</item>
/// </list>
/// </summary>
public abstract class OutboundRecorder
{
    /// <summary>النِيَّةُ قَبلَ النِداء — لا تَرمي.</summary>
    public abstract Task OpenAsync(ProviderCall call, CancellationToken ct);

    /// <summary>النَتيجَةُ بَعدَ النِداء — لا تَرمي، وبِلا رَمزِ إلغاءٍ مِن المُنادي (السابِقَة <c>DealShipping.cs</c>: الأَثَرُ يُكتَبُ وإن انصَرَفَ الطالِب).</summary>
    public abstract Task DecideAsync(ProviderCall call, ProviderAcceptance acceptance);
}

/// <summary>
/// <para><b>المُستَدعي المَعدودُ الواحِد</b> (التَصميم §6.1): كُلُّ نِداءٍ يُغادِرُ العَمَلِيَّةَ يَمُرُّ بِـ<see cref="InvokeAsync"/> — مُباشَرَةً لِعُدَّةٍ بِـSDK،
/// أَو عَبرَ <see cref="ProviderHttp{TKit}"/> لِعُدَّةِ HTTP. نِيَّةٌ قَبلَ الإرسال، ثُمَّ النِداءُ كَما هُوَ، ثُمَّ نَتيجَتُه: <b>قُبِلَ</b> ⇐ سَطر، و<b>رُفِضَ</b> و<b>مَجهولٌ</b>
/// ⇐ عَلى النِيَّة. والاستِثناءُ يُعادُ رَميُه كَما هُوَ بَعدَ أَن يُعَدَّ مَجهولاً — فَتَحويلُ العُدَّةِ لِأَخطائِها لا يَتَغَيَّر.</para>
/// <para><b>ولا يُفشِلُ العَدُّ نِداءً أَبَداً</b> (ADR-051 §٤): الكاتِبُ لا يَرمي، وما يَفلِتُ مِنه رَغمَ ذلكَ يُطبَعُ سَطراً ويُبتَلَعُ هُنا — لا في النِداء.</para>
/// <para><b>ومَن لا كاتِبَ لَه لا يُسَجِّل</b> (<see cref="IsRecording"/> = <c>false</c>): تَركيبٌ لا يُسَجِّلُ القالِبَ (اختِبارٌ يُرَكِّبُ العُدَّةَ وَحدَها). والمُضيفُ
/// المُرَكَّبُ كَما يُرَكِّبُه <c>Program.cs</c> يُسَجِّلُ كُلَّ عَميلٍ صادِرٍ فيه — يَحرُسُه <c>MeterCompositionTests</c> (G6d) ويَحمَرُّ عَلى عَميلٍ واحِدٍ لا يُسَجِّل.</para>
/// </summary>
public sealed class ProviderCalls(OutboundRecorder? recorder)
{
    /// <summary>هَل يُكتَبُ ما يَمُرُّ هُنا؟</summary>
    public bool IsRecording => recorder is not null;

    public async Task<T> InvokeAsync<T>(ProviderCall call, Func<CancellationToken, Task<T>> send, Func<T, ProviderAcceptance> accepted, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (recorder is null) return await send(ct).ConfigureAwait(false);

        await Quietly(call, () => recorder.OpenAsync(call, ct)).ConfigureAwait(false);
        T result;
        try
        {
            result = await send(ct).ConfigureAwait(false);
        }
        catch
        {
            await Quietly(call, () => recorder.DecideAsync(call, new ProviderAcceptance(ProviderOutcome.Unknown))).ConfigureAwait(false);
            throw;
        }
        ProviderAcceptance acceptance;
        try { acceptance = accepted(result); }
        catch (Exception ex)
        {
            // تَصنيفُ الجَوابِ نَفسُه لا يُفشِلُ النِداءَ الَّذي نَجَح — يُعَدُّ مَجهولاً ويُطبَع.
            Console.Error.WriteLine($"[meter] outbound-unclassified unit={call.Unit} app={call.App} request={call.RequestId}: {ex.GetType().Name}");
            acceptance = new ProviderAcceptance(ProviderOutcome.Unknown);
        }
        await Quietly(call, () => recorder.DecideAsync(call, acceptance)).ConfigureAwait(false);
        return result;
    }

    /// <summary>ما يَفلِتُ مِن كاتِبٍ وَعَدَ أَلّا يَرمي: سَطرٌ بِاسمِه، ولا يَبلُغُ النِداء.</summary>
    private static async Task Quietly(ProviderCall call, Func<Task> write)
    {
        try { await write().ConfigureAwait(false); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[meter] outbound-recorder-threw unit={call.Unit} app={call.App} request={call.RequestId}: {ex.GetType().Name}");
        }
    }
}
