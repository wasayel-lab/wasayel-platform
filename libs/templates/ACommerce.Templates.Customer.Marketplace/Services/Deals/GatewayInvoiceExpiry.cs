using ACommerce.Platform.I18n;
using Wolverine;

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
        await notices.NotifyPaymentExpiredAsync(due.TenantSlug, res.Expired, l, ct);
    }
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
public static class GatewayInvoiceExpiry
{
    public static void Configure(WolverineOptions opts)
    {
        opts.Discovery.IncludeType(typeof(GatewayInvoiceExpiryHandler));
        opts.LocalQueueFor<GatewayInvoiceExpiryDue>().UseDurableInbox();
        opts.CodeGeneration.AlwaysUseServiceLocationFor<ACommerce.Kit.Payments.MoyasarClient>();
    }
}
