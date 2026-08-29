using ACommerce.Kit.Payments.Providers.Paddle;
using ACommerce.Kit.Payments.Providers.PayPal;
using ACommerce.Kit.Subscriptions;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.Subscriptions;

/// <summary>
/// <para><b>أَثَرُ رِسالَةِ Paddle على الوَثائِق</b> — تَأخُذ الجَلسَةَ
/// ولا تَفتَحُها ولا تُودِع، كَجارَتِها
/// <see cref="PayPalBillingService"/> حَرفاً.</para>
///
/// <para><b>وباعِثُ التَمديدِ واحِدٌ لا اثنان</b> (القاعِدَة ٨) —
/// وهذا هُوَ القَرارُ الحاكِمُ في هذا المِلَفّ: تَحريكُ
/// <c>TenantPlan.ExpiresAt</c> وإدراجُ <c>PayPalWebhookRecord</c>
/// بِـ<c>Insert</c> في <b>نَفسِ المُعامَلَة</b> يَقَعانِ في
/// <see cref="PayPalBillingService.Apply"/> نَفسِها الَّتي يُنادِيها
/// مَسارا PayPal (الاشتِراكاتُ والطَلَبات). وما يُضيفُه هذا المَسارُ
/// <b>وَثيقَةُ المُعامَلَةِ وَحدَها</b>.</para>
///
/// <para><b>ولِماذا لا يُنشَأُ باعِثٌ ثانٍ ولَو كانَ أَنظَفَ
/// اسماً</b>: الباعِثُ هُوَ المَوضِعُ الَّذي يُضمَنُ فيه أَنّ
/// «تَحَرَّكَ التاريخُ» و«سُجِّلَ الحَدَثُ مَرَّةً واحِدَة» يَقَعانِ
/// مَعاً أَو لا يَقَعان. وباعِثانِ يَعنِيانِ **نافِذَةً يَقَعُ فيها
/// أَحَدُهُما دونَ الآخَر** — أَي تَمديدَينِ لِدَفعَةٍ واحِدَة، أَو
/// سِجِلّاً يَحجُب تَمديداً لَم يَقَع.</para>
///
/// <para><b>وسِجِلُّ مَرَّة-واحِدَةٍ مُشتَرَكٌ ولا تَصادُمَ فيه</b>:
/// مِفتاحُه مُعَرِّفُ الحَدَثِ كَما جاءَ مِن مُزَوِّدِه
/// (<c>WH-…</c> عِندَ PayPal، <c>evt_…</c> عِندَ Paddle)، ومَجالانِ
/// لا يَتَقاطَعان. <b>ولَو تَقاطَعا لَكانَ الأَثَرُ رَفضَ إدراجٍ مِن
/// Postgres — أَي حَجباً لِتَمديد، لا تَمديداً مُزدَوَجاً</b>:
/// واتِّجاهُ الفَشَلِ هذا هُوَ الصَحيح.</para>
/// </summary>
public static class PaddleBillingService
{
    /// <summary><b>اسمُ المُزَوِّدِ في <c>TenantPlan.SetBy</c></b> —
    /// يُمَرَّرُ إلى الباعِثِ المُشتَرَك، فَلا يَنسِبُ سَطرُ
    /// التَدقيقِ دَفعَةَ بِطاقَةٍ إلى PayPal.</summary>
    public const string ProviderName = "paddle";

    // ─── أَسماءُ أَفعالِ التَدقيق — تَسكُن مَعَ المَنطِق فَلا
    //     يَختَرِعُها سَطحٌ ولا تَنجَرِف ─────────────────────────────
    public const string ExtendAuditAction      = "platform.tenant_plan_paddle_extend";
    public const string WithdrawAuditAction    = "platform.tenant_plan_paddle_withdraw";
    public const string StoppedAuditAction     = "platform.tenant_plan_paddle_renewal_stopped";
    public const string TransactionAuditAction = "platform.tenant_plan_paddle_transaction";

    /// <summary>فِعلُ التَدقيقِ المُقابِلُ لِلقَرار — فَسَطرُ السِجِلّ
    /// يَقول ماذا وَقَع لا «‏paddle».</summary>
    public static string AuditActionFor(PaddleAction action) => action switch
    {
        PaddleAction.Extend      => ExtendAuditAction,
        PaddleAction.Withdraw    => WithdrawAuditAction,
        PaddleAction.StopRenewal => StoppedAuditAction,
        _                        => TransactionAuditAction
    };

    /// <summary>
    /// <para><b>يُخَزِّنُ وَثيقَةَ المُعامَلَةِ المُعَلَّقَة، ويُرجِعُ
    /// هَل كُتِبَ شَيءٌ فِعلاً.</b> <c>false</c> تَعني <b>صِفرَ
    /// وَثيقَةٍ مُخَزَّنَة</b> — وهُوَ ما يَفحَصُه اختِبارُ «رابِطٌ لَم
    /// تُعِدهُ Paddle لا يُخَزَّن».</para>
    ///
    /// <para><b>و<c>Store</c> لا <c>Insert</c></b>: المِفتاحُ مَرجِعُنا
    /// الحَتميّ، والمَقصودُ أَنّ <b>لِمُدخَلاتٍ واحِدَةٍ وَثيقَةً
    /// واحِدَة</b> لا أَنّ الوَثيقَةَ تُكتَبُ مَرَّةً في العُمر.
    /// ونَقرَتانِ تَكتُبانِ فَوقَ الوَثيقَةِ نَفسِها — <b>ما دامَت
    /// تَنتَظِرُ دَفعاً</b>، وحارِسُ
    /// <c>PaddleTransactionPolicy.IsOverwritable</c> يَمنَع ما
    /// عَداه.</para>
    /// </summary>
    public static bool SaveTransaction(IDocumentSession session, PaddleTransactionRecord? record)
    {
        if (record is null
            || string.IsNullOrWhiteSpace(record.Id)
            || string.IsNullOrWhiteSpace(record.TransactionId)
            || string.IsNullOrWhiteSpace(record.CheckoutUrl)) return false;

        session.Store(record);
        return true;
    }

    /// <summary>
    /// <para><b>يُطَبِّقُ قَرارَ حَدَثِ Paddle، ويُرجِعُ هَل كُتِبَ
    /// شَيءٌ فِعلاً.</b> <c>false</c> تَعني <b>صِفرَ وَثيقَةٍ
    /// مُخَزَّنَة</b> — وهذا بِعَينِه ما تَفحَصُه حُرّاسُ «تَوقيعٌ
    /// فاشِلٌ ⇒ صِفرُ كِتابَة» و«مَرجِعٌ مَجهولٌ ⇒ لا كِتابَة».</para>
    ///
    /// <para><b>ولا سِجِلَّ مَرَّة-واحِدَةٍ لِما لا يُحَرِّكُ
    /// تاريخاً</b>: تَعليمُ مُعامَلَةٍ لا يُدرِج صَفّاً، فَإعادَةُ
    /// الإرسالِ تُعيدُ التَعليمَ نَفسَه — <b>وذاكَ عَمَلٌ لا ضَرَرَ
    /// في تَكرارِه</b>، بِخِلافِ تَمديدٍ يُشتَرى بِمالٍ واحِدٍ
    /// مَرَّتَين.</para>
    /// </summary>
    public static bool ApplyTransaction(
        IDocumentSession session, TenantPlan? plan, PaddleTransactionRecord? record,
        PaddleEvent e, PaddleDecision decision, DateTime at)
    {
        if (!decision.Writes || record is null) return false;

        var wrote = false;

        if (decision.TouchesPlan && plan is not null)
        {
            // ‏`SubscriptionId` و`NextBillingTime` غائِبانِ عَمداً:
            // المُدَّةُ تُقرَأُ مِن وَثيقَتِنا، و`NewExpiresAt` مَحسوبَةٌ
            // سَلَفاً في القَرار — فَلا حَقلَ يُكتَب لا مَعنى لَه هُنا.
            var billing = new PayPalWebhookEvent(
                e.EventId, e.EventType, record.TenantSlug, null, null, ProviderName);

            wrote = PayPalBillingService.Apply(
                session, plan, billing, BillingDecisionFor(decision), at);
        }

        if (decision.TouchesTransaction)
        {
            PaddleBillingPolicy.Apply(record, e, decision, at);
            session.Store(record);
            wrote = true;
        }

        return wrote;
    }

    /// <summary>
    /// <para><b>تَرجَمَةُ قَرارِ Paddle إلى قَرارِ الباعِثِ
    /// المُشتَرَك</b> — ثَلاثَةُ أَفعالٍ لا أَكثَر، وما عَداها لا
    /// يَبلُغُ هُنا أَصلاً (<c>TouchesPlan</c> يَحرُسُه).</para>
    ///
    /// <para><b>والسَبَبُ يُمَرَّرُ كَما هُوَ</b>: هُوَ ما يُكتَبُ في
    /// سِجِلِّ التَدقيق، وإعادَةُ صِياغَتِه هُنا تَجعَل الشاشَةَ
    /// تَقول غَيرَ ما يَقولُه اللوغ.</para>
    /// </summary>
    public static PayPalBillingDecision BillingDecisionFor(PaddleDecision decision)
        => new(
            decision.Action switch
            {
                PaddleAction.Extend      => PayPalBillingAction.Extend,
                PaddleAction.Withdraw    => PayPalBillingAction.Withdraw,
                _                        => PayPalBillingAction.StopRenewal
            },
            decision.NewExpiresAt,
            decision.ReasonAr);
}
