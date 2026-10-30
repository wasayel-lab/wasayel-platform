using ACommerce.Platform.I18n;
using JasperFx;
using JasperFx.CodeGeneration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.ErrorHandling;
using Wolverine.Runtime;
using Wolverine.Runtime.Handlers;

namespace ACommerce.Templates.Customer.Marketplace.Services.Deals;

// ═══ إلغاءُ الطَلَبِ عِندَ انتِهاءِ مُهلَةِ فاتورَتِه — مُجَدوَلٌ لا كَسول (‏2026-10-29، قَرارُ المالِك د5 (٤) «ب»؛ ‏ADR-046) ═══════════════
//
// **القَرار**: «يُلغى الطلب كله آليًّا، وتُلغى الفاتورة عند ميسر إن بقيت مفتوحة، ويُخبَر المشتري وكل بائع فيه» — وسَبَبُه بِنَصِّه: «ألّا يبقى طلب
// غير مدفوع معلّقًا يحجز البائعين بلا نهاية». وكانَ الإلغاءُ يَقَعُ في `GatewayPayments.VerifyAsync` وَحدَها، ولا يَبلُغُها إلّا زِرُّ المُشتَري —
// فَالحالُ الَّتي كَتَبَت القَرارَ (مُشتَرٍ لا يَعود) هي بِعَينِها الَّتي لا يَبلُغُها (تَوصيفُه `17a9a19`، مُراجَعَةُ تَيّارِ S3 الأولى).
//
// **ولا أُنبوبَ رابِع (القاعِدَة ٨)**: Wolverine مُرَكَّبٌ مُنذُ الأَوَّل (`HostingExtensions` — `IntegrateWithWolverine()` و`UseWolverine`)، وجَداوِلُ صُندوقِه
// المُستَديمِ في القاعِدَةِ أَصلاً (`platform.wolverine_incoming_envelopes`، مَقيسَةً عَلى القاعِدَةِ المَحَلِّيَّة). فَالمُهلَةُ رِسالَةٌ مُجَدوَلَةٌ فيه: يُجَدوِلُها فَتحُ
// الفاتورَةِ لِتاريخِ انتِهائِها المَكتوبِ نَفسِه، وتُحفَظُ في صُندوقِه بِحالِ «مُجَدوَلَة» فَتَعبُرُ إعادَةَ الإقلاع، ويُنَفِّذُها وَكيلُ دَيمومَتِه حينَ يَحين — لا مُؤَقِّتَ
// ولا خِدمَةَ خَلفِيَّةَ نَبنيها. **وهذا أَوَّلُ مُستَهلِكٍ حَيٍّ لِحافِلَةِ الرَسائِل** (`grep IMessageBus` كانَ صِفراً): القاعِدَةُ ١ تَمنَعُ التَجريدَ قَبلَ مُستَهلِكِه،
// والمُستَهلِكُ هُنا هُوَ القَرار.
//
// **والمُعالِجُ لا يَزيدُ عَلى التَحَقُّقِ حَرفاً**: يَسأَلُ `GatewayPayments.ExpireAsync` — الجَلبُ بِمِفتاحِ المَتجَر، ثُمَّ «التَسوِيَةُ» نَفسُها الَّتي يَسلُكُها
// التَحَقُّق (`ExpiryDue` و`ExpiryCanceller` وكاتِبُ «الطَلَبِ كُلِّه») — ثُمَّ يُخبِرُ بِالأُنبوبِ القائِمِ (`DealNotices`) كَما تُخبِرُ نُقطَةُ التَحَقُّق. وزِرُّ «تَحَقَّق»
// باقٍ: مَن عادَ قَبلَ الرِسالَةِ أَلغى بِه، والرِسالَةُ بَعدَه لا تَجِدُ قائِماً فَلا تَكتُب.

/// <summary>
/// <para><b>حانَت مُهلَةُ دَفعِ فاتورَةِ هذِه الصَفقَة</b> — رِسالَةٌ يُجَدوِلُها <see cref="GatewayPayments.OpenInvoiceAsync"/> لِتاريخِ انتِهاءِ الفاتورَةِ الَّذي أُرسِلَ
/// إلى البَوّابَة. <paramref name="DealId"/> الصَفقَةُ المَرجِعُ (أُولى صَفَقاتِ الفاتورَة)، و<paramref name="TenantSlug"/> مَتجَرُها — فَالمُعالِجُ لا يَحتاجُ
/// مُستَأجِراً مِن الغِلاف. <b>واسمُ النَوعِ عَقدٌ مُخَزَّن</b>: الرِسالَةُ تُحفَظُ بِاسمِه الكامِلِ ساعاتٍ، فَإعادَةُ تَسمِيَتِه تُيَتِّمُ ما جُدوِلَ قَبلَها (‏ADR-046 §٤).</para>
/// </summary>
public sealed record GatewayInvoiceExpiryDue(string TenantSlug, Guid DealId);

/// <summary>
/// <para><b>مُعالِجُ المُهلَة</b> — يَكتَشِفُه Wolverine بِالاسمِ لا بِمَسحِ المَكتَبَة (<see cref="GatewayInvoiceExpiry.Configure"/>). يُلغي ما حانَت مُهلَتُه ويُخبِر،
/// ويُسَجِّلُ دَفعاً ثَبَتَ ولَم يُكتَب، ولا يَفعَلُ شَيئاً لِسِواهُما — والقَرارُ كُلُّه في <see cref="GatewayPayments.ExpireAsync"/>.</para>
/// </summary>
public static class GatewayInvoiceExpiryHandler
{
    public static async Task Handle(GatewayInvoiceExpiryDue due, GatewayPayments gateway, DealNotices notices, L l, CancellationToken ct)
    {
        var res = await gateway.ExpireAsync(due.TenantSlug, due.DealId, l, ct);
        await notices.NotifyPaidAsync(due.TenantSlug, res.Confirmed, l, ct);
        // (‏2026-10-30، جَولَةُ الإصلاحِ الأولى لِلمَسار F3؛ تَوصيفُه `8e19697`): وكُلُّ صَفقَةٍ تُوسَمُ بَعدَ إشعاراتِها — فَإشعارٌ يَرمي يَترُكُها لِلإعادَة.
        await notices.NotifyPaymentExpiredAsync(due.TenantSlug, res.Expired, l, ct, told: id => gateway.MarkExpiryToldAsync(due.TenantSlug, id));
        // (‏2026-10-30، جَولَةُ الإصلاحِ الأولى لِلمَسار F3؛ تَوصيفُه `4bc251c` — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه): **وجَلبٌ فَشِلَ يُفشِلُ الرِسالَة** — بَعدَ ما أُخبِرَ بِه،
        // فَتَعمَلُ سِياسَتُها (`GatewayInvoiceExpiryFailures`): إعاداتٌ بِتَأَنٍّ ثُمَّ الرَسائِلُ الميتَةُ وسَطرُ الخَطَإِ وإشعارُ صاحِبِ المَتجَر. كانَ رَمزُ العَميلِ يُرجَعُ فَيَفرُغُ
        // المُعالِجُ بِلا خَطَأ، والطَلَبُ غَيرُ المَدفوعِ قائِمٌ بِلا نِهايَة ولا يَعلَمُ بِه أَحَد.
        if (GatewayInvoiceExpiry.FailsTheMessage(res.Code)) throw new GatewayInvoiceExpiryFetchFailed(due.TenantSlug, due.DealId, res.Code!);
    }
}

/// <summary>
/// <para><b>جَلبُ فاتورَةِ المُهلَةِ فَشِلَ — فَتُعادُ الرِسالَة</b> (‏2026-10-30، جَولَةُ الإصلاحِ الأولى لِلمَسار F3 — CO2 / [S3·١]): يَرميه <see cref="GatewayInvoiceExpiryHandler.Handle"/>
/// حينَ تَصدُقُ <see cref="GatewayInvoiceExpiry.FailsTheMessage"/> عَلى رَمزِ <see cref="GatewayPayments.ExpireAsync"/>، فَيَبلُغُ سِياسَةَ الرِسالَةِ ما كانَ رَمزاً يُبتَلَع. <b>واسمُ
/// النَوعِ يُكتَبُ في صَفِّ الرِسالَةِ الميتَة</b> (<c>exception_type</c>) ورَمزُ العَميلِ في نَصِّه — فَيَقرَأُ المُشَغِّلُ العَطَبَ بِاسمِه.</para>
/// </summary>
public sealed class GatewayInvoiceExpiryFetchFailed(string tenantSlug, Guid dealId, string code)
    : Exception($"[gateway] expiry {tenantSlug}/{dealId:N}: the invoice fetch answered {code}")
{
    /// <summary>رَمزُ العَميلِ كَما أَرجَعَه الجَلب — مِن <see cref="ACommerce.Kit.Payments.MoyasarFailures.All"/>.</summary>
    public string Code { get; } = code;
}

/// <summary>
/// <para><b>تَركيبُ Wolverine لِرِسالَةِ المُهلَة — سَطرانِ في مَوضِعٍ واحِد</b> يُناديهِما <c>AddCustomerMarketplaceTemplate</c> بِـ<c>ConfigureWolverine</c>، ويُناديهِما
/// البُرهانُ الحَيُّ نَفسُهُما (<c>LiveScheduledExpiryProofTests</c>) فَلا يَفتَرِقُ ما يُقاسُ عَمّا يَعمَل:</para>
/// <list type="number">
///   <item><b>اكتِشافُ المُعالِجِ بِنَوعِه</b> — لا مَكتَبَةُ القالَبِ كُلُّها: فيها <c>AcceptTermsHandler</c> بِشَكلِ مُعالِجٍ لا يُريدُه أَحَدٌ مُعالِجَ رِسائِل.</item>
///   <item><b>صُندوقٌ مُستَديمٌ لِطابورِها المَحَلِّيّ</b> — فَالرِسالَةُ المُجَدوَلَةُ تُحفَظُ في القاعِدَةِ لا في الذاكِرَة، وتَعبُرُ إعادَةَ الإقلاع.</item>
///   <item><b>وعَميلُ البَوّابَةِ يُحَلُّ مِن الحاوِيَةِ صَراحَةً</b> — مَقيسٌ حَيّاً: Wolverine ‏6.25.1 سِياسَتُه الافتِراضِيَّةُ <c>ServiceLocationPolicy.NotAllowed</c>، و<c>MoyasarClient</c>
///   عَميلُ HTTP مُسَجَّلٌ بِمَصنَعٍ (<c>AddHttpClient</c>) لا يُبنى في الكودِ المُوَلَّد، فَكانَ بِناءُ المُعالِجِ يَرمي <c>InvalidServiceLocationException</c> ساعَةَ تَحينُ
///   الرِسالَة. الإذنُ لِهذا النَوعِ وَحدَه لا لِلسِياسَةِ كُلِّها.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>(‏2026-10-30، تَدقيقُ الموجَة ١١ CO2 — المُرَحَّلُ [S3·١] بِقَرارِه تَحتَ P1؛ تَوصيفُه <c>1e65236</c> — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق): <b>ورابِعُها سِياسَةُ فَشَلِها</b>
/// (<see cref="GatewayInvoiceExpiryFailures"/>) — كانَ مُعالِجٌ يَرمي نِداءً واحِداً ثُمَّ صَفّاً ميتاً بِلا إعادَةٍ ولا كَلِمَة (‏ADR-046 §٤‑1): إعاداتٌ مَحدودَةٌ
/// بِتَأَنٍّ (<see cref="RetryCooldowns"/>)، ثُمَّ الرِسالَةُ الميتَةُ (<c>platform.wolverine_dead_letters</c>) ومَعَها سَطرُ خَطَأٍ يُسَمّي المَتجَرَ والصَفقَةَ وإشعارٌ لِصاحِبِ
/// المَتجَرِ بِالأُنبوبِ القائِم (<see cref="DealNotices.OwnerAction.ExpiryFailed"/>). في مَوضِعِ التَركيبِ نَفسِه لا في <c>HostingExtensions</c>: لِرِسالَةِ المُهلَةِ وَحدَها.</para>
/// </remarks>
public static class GatewayInvoiceExpiry
{
    /// <summary>
    /// <para><b>التَأَنّي بَينَ الإعادات — ثَلاثٌ بَعدَ الأولى</b> (قَرارٌ تِقنِيٌّ تَحتَ P1، لا رَقَمَ مُنتَج — القاعِدَة ١٦): ثانِيَةٌ ثُمَّ خَمسٌ ثُمَّ خَمسَ عَشرَة، فَعُطلٌ عابِرٌ في
    /// القاعِدَةِ أَو الخِزانَةِ يَمُرُّ في نِصفِ دَقيقَة، وما بَعدَه عَطَبٌ لا ازدِحامٌ يُقالُ لِصاحِبِ المَتجَر. والإعادَةُ آمِنَة: التَسوِيَةُ لا تَكتُبُ مَرَّتَين (<c>IsCaptured</c>،
    /// و<see cref="GatewayPayments.ExpiryToldRef"/>).</para>
    /// </summary>
    public static readonly TimeSpan[] RetryCooldowns = { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15) };

    /// <summary>
    /// <para><b>أَيُفشِلُ رَمزُ المُهلَةِ الرِسالَة؟</b> (‏2026-10-30، جَولَةُ الإصلاحِ الأولى لِلمَسار F3؛ قَرارُ F3-R1·١ تَحتَ P1) — دالَّةٌ نَقِيَّة: <b>نَعَم لِكُلِّ رَمزٍ مِن
    /// مَعجَمِ العَميلِ المُغلَق</b> (<see cref="ACommerce.Kit.Payments.MoyasarFailures.All"/>، سِتَّة) — الجَلبُ لَم يُجِب بِفاتورَة، فَلا يُعرَفُ أَدُفِعَت أَم انتَهَت. العابِرَةُ
    /// مِنها (لا جَواب، ‏429، لا يُقرَأ، مِفتاحٌ لا يُفَكّ — رُموزُ <see cref="GatewayNoticeGate.RetriedConfirmCodes"/> بِلا «لَم تُدفَع») تَبرَأُ بِالإعادَة؛ والدائِمَتانِ
    /// (رَفضٌ ‏401/403/404، طَلَبٌ غَيرُ صالِح) لا تَبرَآن، <b>وتُعادانِ مَعَ ذلك</b>: أَربَعُ نِداءاتٍ في نَحوِ ‏21 ثانِيَةً ثَمَنٌ زَهيدٌ لِسِياسَةٍ واحِدَةٍ تَنتَهي بِإشعارِ صاحِبِ
    /// المَتجَر — والبَديلُ فَرعٌ ثانٍ في السِياسَة، أَو تَركُهُما صامِتَتَينِ كَما كانَتا والطَلَبُ قائِمٌ بِلا نِهايَة.</para>
    /// <para><b>ولا تَصدُقُ لِسِواها</b>: صَفقَةٌ غائِبَةٌ أَو بِلا فاتورَة (لا شَيءَ يُلغى)، وما أَجابَت بِه الفاتورَةُ نَفسُها — «لَم تُدفَع» (<c>on_hold</c>، <c>failed</c>…
    /// لا تُلغي بِاسمِ المُهلَة عَمداً، <see cref="GatewayPayments.ExpiryDue"/>) ومُغلَقَةٌ وما لا يُطابِق — والإلغاءُ والدَفعُ الثابِت.</para>
    /// </summary>
    public static bool FailsTheMessage(string? code)
        => code is not null && ACommerce.Kit.Payments.MoyasarFailures.All.Contains(code, StringComparer.Ordinal);

    public static void Configure(WolverineOptions opts)
    {
        opts.Discovery.IncludeType(typeof(GatewayInvoiceExpiryHandler));
        opts.LocalQueueFor<GatewayInvoiceExpiryDue>().UseDurableInbox();
        opts.CodeGeneration.AlwaysUseServiceLocationFor<ACommerce.Kit.Payments.MoyasarClient>();
        opts.Policies.Add(new GatewayInvoiceExpiryFailures());
    }

    /// <summary>
    /// <para><b>ما يَقَعُ حينَ يَيأَسُ المُعالِج</b> — بَعدَ آخِرِ إعادَة، والرِسالَةُ إلى الرَسائِلِ الميتَة: سَطرُ خَطَأٍ يُسَمّي المَتجَرَ والصَفقَةَ والعَطَب، ثُمَّ إشعارٌ لِصاحِبِ
    /// المَتجَرِ في نِطاقِ خِدماتٍ جَديد. <b>وفَشَلُ الإشعارِ سَطرٌ ثانٍ لا يَرمي</b>: العَطَبُ الَّذي أَيأَسَ المُعالِجَ قَد يَكونُ القاعِدَةَ نَفسَها، والسَطرُ الأَوَّلُ قَد كُتِب.</para>
    /// </summary>
    public static async ValueTask GaveUpAsync(IWolverineRuntime runtime, IEnvelopeLifecycle lifecycle, Exception error)
    {
        if (lifecycle.Envelope?.Message is not GatewayInvoiceExpiryDue due) return;
        var log = runtime.LoggerFactory.CreateLogger(typeof(GatewayInvoiceExpiry));
        // (‏2026-10-30، جَولَةُ الإصلاحِ الأولى لِلمَسار F3 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه): وجَلبٌ فَشِلَ يُسَمّى بِرَمزِ العَميلِ لا بِنَوعِ الاستِثناءِ وَحدَه.
        log.LogError(error, "[gateway] expiry {Slug}/{Deal} gave up after {Attempts} attempt(s) ← {Error} — moved to wolverine_dead_letters; the unpaid order stays until the store owner cancels it",
            due.TenantSlug, due.DealId.ToString("N"), RetryCooldowns.Length + 1,
            error is GatewayInvoiceExpiryFetchFailed failed ? $"{nameof(GatewayInvoiceExpiryFetchFailed)} {failed.Code}" : error.GetType().Name);
        try
        {
            using var scope = runtime.Services.CreateScope();
            var deals = scope.ServiceProvider.GetRequiredService<DealsService>();
            if (await deals.LoadAsync(due.TenantSlug, due.DealId) is { } deal)
                await scope.ServiceProvider.GetRequiredService<DealNotices>().NotifyStoreOwnerAsync(due.TenantSlug, deal, DealNotices.OwnerAction.ExpiryFailed,
                    scope.ServiceProvider.GetRequiredService<L>());
        }
        catch (Exception notice)
        {
            log.LogError("[gateway] expiry {Slug}/{Deal}: the store owner was not told ← {Error}", due.TenantSlug, due.DealId.ToString("N"), notice.GetType().Name);
        }
    }
}

/// <summary>سِياسَةُ فَشَلِ رِسالَةِ المُهلَةِ عَلى سِلسِلَةِ مُعالِجِها وَحدَها (‏2026-10-30، CO2) — <see cref="GatewayInvoiceExpiry.RetryCooldowns"/> ثُمَّ الرَسائِلُ الميتَةُ ومَعَها
/// <see cref="GatewayInvoiceExpiry.GaveUpAsync"/>. وسِلسِلَةٌ غَيرُها لا تُمَسّ.</summary>
public sealed class GatewayInvoiceExpiryFailures : IHandlerPolicy
{
    public void Apply(IReadOnlyList<HandlerChain> chains, GenerationRules rules, IServiceContainer container)
    {
        foreach (var chain in chains.Where(c => c.MessageType == typeof(GatewayInvoiceExpiryDue)))
            chain.OnAnyException()
                .RetryWithCooldown(GatewayInvoiceExpiry.RetryCooldowns)
                .Then.MoveToErrorQueue()
                .And(GatewayInvoiceExpiry.GaveUpAsync, "tell the store owner the expiry gave up");
    }
}
