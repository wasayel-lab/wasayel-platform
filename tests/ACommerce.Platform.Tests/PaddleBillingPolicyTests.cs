using System.Globalization;
using ACommerce.Kit.Payments.Providers.Paddle;
using ACommerce.Kit.Subscriptions;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ قَرارُ فَوتَرَةِ Paddle — بِلا قاعِدَةِ بَياناتٍ وبِلا شَبَكَة ═════
//
// **ولِماذا كُلُّه دَوالُّ نَقِيَّة**: «صِفرُ كِتابَةٍ عِندَ تَوقيعٍ
// فاشِل» و«تَكرارُ الحَدَثِ لا يُمَدِّد» جُملَتانِ لا تُبرهَنانِ
// بِفَحصِ قاعِدَةِ بَياناتٍ بَعدَ الحَدَث — تُبرهَنانِ بِأَنّ القَرارَ
// نَفسَه **لا يُنتِج وَثيقَةً**. ونَفسُ عادَةِ
// `PayPalBillingPolicyTests` حَرفاً.
//
// **وحُرّاسُ النُقطَةِ نَفسِها** — تَوقيعٌ فاشِلٌ ⇒ صِفرُ كِتابَة، وغَيرُ
// المُشرِفِ ⇒ رَفضٌ قَبلَ أَوَّلِ كِتابَة — **في مِلَفٍّ آخَر يُشَغِّل
// النُقطَةَ فِعلاً** (`PaddleEndpointBehaviourTests`)، لِأَنّ قِراءَةَ
// نَصِّ المَصدَرِ حارِسٌ لا يُحمِرُّ عِندَ نَزعِ `!` (القاعِدَة ٢).

public class PaddleBillingPolicyTests
{
    private static readonly DateTime Now = new(2026, 08, 29, 12, 00, 00, DateTimeKind.Utc);
    private static readonly DateTimeOffset NowOffset = new(Now, TimeSpan.Zero);

    private const string Secret    = "pdl_ntfset_01j0000000000000000000000";
    private const string Reference = "wsl-pd-ejar-abc123def456";

    private static PaddleOptions Ready(
        string secret = Secret, string apiKey = "pdl_apikey_1",
        string env = PaddleEnvironment.Live,
        string token = "live_token", string link = "https://wasayel.example/billing/paddle/checkout.html")
        => new()
        {
            Environment = env, ApiKey = apiKey, WebhookSecret = secret,
            ClientToken = token, DefaultPaymentLink = link,
        };

    private static TenantPlan Plan(string slug = "ejar") => new()
    {
        Id = slug, PlanId = "manual", Status = PlatformPlanStatuses.Active,
        StartsAt = Now.AddDays(-20), ExpiresAt = Now.AddDays(10), GraceDays = 14,
    };

    private static PaddleTransactionRecord Record(
        string status = PaddleTransactionStatuses.Created,
        string minor = "4900", string currency = "USD", int days = 30)
        => new()
        {
            Id = Reference, TenantSlug = "ejar", PlanId = "manual",
            Amount = 49m, AmountMinor = minor, Currency = currency, Days = days,
            TransactionId = "txn_01j", Status = status,
            CheckoutUrl = "https://wasayel.example/billing/paddle/checkout.html?_ptxn=txn_01j",
            CreatedAt = Now, At = Now,
        };

    private static string CompletedBody(
        string eventId = "evt_1", string status = "completed",
        string grandTotal = "4900", string currency = "USD", string? reference = Reference,
        string txn = "txn_01j")
        => $$$"""
        {
          "event_id": "{{{eventId}}}",
          "event_type": "transaction.completed",
          "data": {
            "id": "{{{txn}}}",
            "status": "{{{status}}}",
            "currency_code": "{{{currency}}}",
            "custom_data": { "wasayel_ref": "{{{reference}}}" },
            "details": { "totals": { "subtotal": "{{{grandTotal}}}", "grand_total": "{{{grandTotal}}}" } }
          }
        }
        """;

    // ═══ ١. البَوّابَة — يُتَحَقَّقُ قَبلَ أَن يُقرَأ ═══════════════════

    /// <summary>الطَريقُ السَعيد: سِرٌّ مَضبوطٌ ورَأسٌ مَقروءٌ وزَمَنٌ
    /// داخِلَ التَسامُحِ وبَصمَةٌ مُطابِقَة.</summary>
    [Fact]
    public void Gate_Accepts_AProperlySignedBody()
    {
        var body = CompletedBody();
        var ts   = NowOffset.ToUnixTimeSeconds();
        var header = $"ts={ts};h1={PaddleWebhookGuard.SignHex(Secret, ts, body)}";

        Assert.Equal(PaddleWebhookGate.Accepted,
            PaddleWebhookGuard.Gate(Ready(), header, body, NowOffset));
    }

    /// <summary><b>غِيابُ السِرِّ يُغلِق</b> — ولا يُفتَحُ على أَمَل.</summary>
    [Fact]
    public void Gate_Refuses_WhenTheWebhookSecretIsMissing()
    {
        var body = CompletedBody();
        var ts   = NowOffset.ToUnixTimeSeconds();
        var header = $"ts={ts};h1={PaddleWebhookGuard.SignHex(Secret, ts, body)}";

        Assert.Equal(PaddleWebhookGate.NotConfigured,
            PaddleWebhookGuard.Gate(Ready(secret: ""), header, body, NowOffset));
    }

    /// <summary>بيئَةٌ خارِجَ المَعجَمِ تُغلِقُ البابَ كَذلك — <b>لا
    /// يُخمَّنُ مُضيف</b>.</summary>
    [Fact]
    public void Gate_Refuses_WhenTheEnvironmentIsUnknown()
    {
        var body = CompletedBody();
        var ts   = NowOffset.ToUnixTimeSeconds();
        var header = $"ts={ts};h1={PaddleWebhookGuard.SignHex(Secret, ts, body)}";

        Assert.Equal(PaddleWebhookGate.NotConfigured,
            PaddleWebhookGuard.Gate(Ready(env: "staging"), header, body, NowOffset));
    }

    [Fact]
    public void Gate_Refuses_WhenTheHeaderIsAbsent()
        => Assert.Equal(PaddleWebhookGate.HeaderMissing,
            PaddleWebhookGuard.Gate(Ready(), null, CompletedBody(), NowOffset));

    [Theory]
    [InlineData("garbage")]
    [InlineData("ts=1756468800")]                  // بِلا بَصمَة
    [InlineData("h1=deadbeef")]                    // بِلا زَمَن
    [InlineData("ts=notanumber;h1=deadbeef")]      // زَمَنٌ غَيرُ عَدَد
    public void Gate_Refuses_AMalformedHeader(string header)
        => Assert.Equal(PaddleWebhookGate.HeaderMalformed,
            PaddleWebhookGuard.Gate(Ready(), header, CompletedBody(), NowOffset));

    /// <summary>
    /// <para><b>التَسامُحُ خَمسُ ثَوانٍ، ويُقاسُ بِطَرَفَيه.</b>
    /// ورِسالَةٌ صَحيحَةُ التَوقيعِ عُمرُها دَقيقَةٌ تُرفَض — وهذا
    /// بِعَينِه ما يَمنَع إعادَةَ اللَعِب.</para>
    /// </summary>
    [Theory]
    [InlineData(0,   PaddleWebhookGate.Accepted)]
    [InlineData(5,   PaddleWebhookGate.Accepted)]
    [InlineData(-5,  PaddleWebhookGate.Accepted)]
    [InlineData(6,   PaddleWebhookGate.TimestampOutOfTolerance)]
    [InlineData(-6,  PaddleWebhookGate.TimestampOutOfTolerance)]
    [InlineData(60,  PaddleWebhookGate.TimestampOutOfTolerance)]
    public void Gate_MeasuresTheClockDrift_OnBothSides(int driftSeconds, PaddleWebhookGate expected)
    {
        var body = CompletedBody();
        var ts   = NowOffset.ToUnixTimeSeconds() - driftSeconds;
        var header = $"ts={ts};h1={PaddleWebhookGuard.SignHex(Secret, ts, body)}";

        Assert.Equal(expected, PaddleWebhookGuard.Gate(Ready(), header, body, NowOffset));
    }

    /// <summary><b>سِرٌّ آخَرُ ⇒ رَفض</b> — وهذا هُوَ العَطَبُ
    /// الأَوَّلُ المُتَوَقَّع: مِفتاحُ الـAPI مَكانَ سِرِّ
    /// الوِجهَة.</summary>
    [Fact]
    public void Gate_Refuses_WhenSignedWithTheApiKeyInsteadOfTheNotificationSecret()
    {
        var body = CompletedBody();
        var ts   = NowOffset.ToUnixTimeSeconds();
        var header = $"ts={ts};h1={PaddleWebhookGuard.SignHex("pdl_apikey_1", ts, body)}";

        Assert.Equal(PaddleWebhookGate.SignatureInvalid,
            PaddleWebhookGuard.Gate(Ready(), header, body, NowOffset));
    }

    /// <summary>
    /// <para><b>الجِسمُ الخامُّ هُوَ المُوَقَّع — والبُرهانُ
    /// بِالتَحليلِ وإعادَةِ التَسلسُل.</b></para>
    ///
    /// <para><b>وهذا هُوَ الفَخُّ الأَوَّلُ في كِتابَةِ تَحَقُّقٍ
    /// بِاليَد</b>: جِسمٌ حُلِّلَ ثُمَّ أُعيدَ تَسلسُلُه يَحمِلُ
    /// **نَفسَ المَعنى** ويُنتِجُ **بَصمَةً أُخرى** — تَسقُط
    /// المَسافاتُ وتَتَبَدَّل الصيغَة. فَتُرفَضُ كُلُّ رِسالَةٍ
    /// صَحيحَة، ويَبدو العَطَبُ «سِرٌّ خاطِئ».</para>
    /// </summary>
    [Fact]
    public void Gate_Refuses_WhenTheBodyWasReserializedInsteadOfPassedRaw()
    {
        var raw = CompletedBody();
        var ts  = NowOffset.ToUnixTimeSeconds();
        var header = $"ts={ts};h1={PaddleWebhookGuard.SignHex(Secret, ts, raw)}";

        using var doc = System.Text.Json.JsonDocument.Parse(raw);
        var reserialized = System.Text.Json.JsonSerializer.Serialize(doc.RootElement);

        Assert.NotEqual(raw, reserialized);
        Assert.Equal(PaddleWebhookGate.Accepted,
            PaddleWebhookGuard.Gate(Ready(), header, raw, NowOffset));
        Assert.Equal(PaddleWebhookGate.SignatureInvalid,
            PaddleWebhookGuard.Gate(Ready(), header, reserialized, NowOffset));
    }

    /// <summary>بَصمَةٌ سِتَّ عَشَرِيَّةٌ مُشَوَّهَةٌ <b>رَفضٌ لا
    /// انفِجار</b> — والانفِجارُ هُنا ‏500 تُعيدُها Paddle مِراراً.</summary>
    [Theory]
    [InlineData("zz")]
    [InlineData("abc")]     // طولٌ فَرديّ
    [InlineData("")]
    public void Matches_Refuses_AMalformedHexDigest(string hex)
        => Assert.False(PaddleWebhookGuard.Matches(
            PaddleWebhookGuard.Sign(Secret, 1, "{}"), hex));

    /// <summary>وتَرتيبُ الزَوجَينِ في الرَأسِ لا يُغَيِّرُ شَيئاً —
    /// <b>تَرتيبٌ عِندَ طَرَفٍ ثالِثٍ لَيسَ عَقداً</b>.</summary>
    [Fact]
    public void Signature_Parses_RegardlessOfPairOrder()
    {
        var a = PaddleSignature.Parse("ts=17;h1=beef");
        var b = PaddleSignature.Parse("h1=beef;ts=17");

        Assert.Equal(a, b);
        Assert.Equal(17, a!.Timestamp);
        Assert.Equal("beef", a.Hash);
    }

    // ═══ ٢. المَبلَغُ يُقارَنُ ولا يُفتَرَض ═══════════════════════════

    /// <summary><b>مَبلَغٌ أَقَلُّ لا يُمَدِّد.</b></summary>
    [Fact]
    public void ALowerAmount_DoesNotExtend()
    {
        var e = PaddleBillingPolicy.Parse(CompletedBody(grandTotal: "100"))!;
        var d = PaddleBillingPolicy.Decide(e, Record(), Plan(), alreadySeen: false, Now);

        Assert.Equal(PaddleAction.AmountMismatch, d.Action);
        Assert.False(d.Writes);
    }

    /// <summary><b>عُملَةٌ أُخرى لا تُمَدِّد</b> — ولَو تَطابَقَ
    /// الرَقَم.</summary>
    [Fact]
    public void ADifferentCurrency_DoesNotExtend()
    {
        var e = PaddleBillingPolicy.Parse(CompletedBody(currency: "EUR"))!;
        var d = PaddleBillingPolicy.Decide(e, Record(), Plan(), alreadySeen: false, Now);

        Assert.Equal(PaddleAction.AmountMismatch, d.Action);
        Assert.False(d.Writes);
    }

    /// <summary>ومَبلَغٌ <b>أَكبَر</b> لا يُمَدِّد كَذلك: المُقارَنَةُ
    /// تَطابُقٌ لا «يَكفي» — ودَفعَةٌ بِمَبلَغٍ آخَرَ قَد تَكون
    /// مُعامَلَةً لَيسَت لَنا.</summary>
    [Fact]
    public void AHigherAmount_DoesNotExtendEither()
    {
        var e = PaddleBillingPolicy.Parse(CompletedBody(grandTotal: "9800"))!;
        var d = PaddleBillingPolicy.Decide(e, Record(), Plan(), alreadySeen: false, Now);

        Assert.Equal(PaddleAction.AmountMismatch, d.Action);
    }

    /// <summary>والمُقارَنَةُ عَدَدِيَّةٌ لا نَصِّيَّة —
    /// <c>"04900"</c> هُوَ <c>4900</c>.</summary>
    [Fact]
    public void TheAmountComparison_IsNumericNotTextual()
    {
        var e = PaddleBillingPolicy.Parse(CompletedBody(grandTotal: "04900"))!;
        var d = PaddleBillingPolicy.Decide(e, Record(), Plan(), alreadySeen: false, Now);

        Assert.Equal(PaddleAction.Extend, d.Action);
    }

    // ═══ ٣. اسمُ الحَدَثِ دَعوى، والحَقلُ واقِعَة ══════════════════════

    [Fact]
    public void AnEventNamedCompleted_WithAnotherStatus_DoesNotExtend()
    {
        var e = PaddleBillingPolicy.Parse(CompletedBody(status: "billed"))!;
        var d = PaddleBillingPolicy.Decide(e, Record(), Plan(), alreadySeen: false, Now);

        Assert.Equal(PaddleAction.StatusNotCompleted, d.Action);
        Assert.False(d.Writes);
    }

    /// <summary>وحَدَثٌ خارِجَ المَعجَمِ لا يُمَدِّد ولا يَكتُب —
    /// <b>«أُنشِئَت المُعامَلَة» لَيسَت مالاً</b>.</summary>
    [Theory]
    [InlineData("transaction.created")]
    [InlineData("transaction.ready")]
    [InlineData("transaction.billed")]
    [InlineData("transaction.payment_failed")]
    [InlineData("customer.created")]
    public void AnEventOutsideTheVocabulary_WritesNothing(string type)
    {
        var body = $$$"""
            {
              "event_id": "evt_x",
              "event_type": "{{{type}}}",
              "data": {
                "id": "txn_01j",
                "status": "completed",
                "currency_code": "USD",
                "custom_data": { "wasayel_ref": "{{{Reference}}}" },
                "details": { "totals": { "grand_total": "4900" } }
              }
            }
            """;
        var e = PaddleBillingPolicy.Parse(body)!;
        var d = PaddleBillingPolicy.Decide(e, Record(), Plan(), alreadySeen: false, Now);

        Assert.Equal(PaddleAction.Ignored, d.Action);
        Assert.False(d.Writes);
    }

    // ═══ ٤. التَكرارُ لا يُمَدِّد — بِمِفتاحَينِ لا واحِد ═════════════

    [Fact]
    public void AReplayedEventId_DoesNotExtend()
    {
        var e = PaddleBillingPolicy.Parse(CompletedBody())!;
        var d = PaddleBillingPolicy.Decide(e, Record(), Plan(), alreadySeen: true, Now);

        Assert.Equal(PaddleAction.Replay, d.Action);
        Assert.False(d.Writes);
    }

    /// <summary><b>ومُعامَلَةٌ بَلَغَت «وَصَلَ المال» لا تُمَدِّدُ
    /// ثانِيَةً ولَو تَبَدَّلَ <c>event_id</c></b> — وهذا هُوَ
    /// المِفتاحُ الثاني، والأَوَّلُ وَحدَه لا يَكفي.</summary>
    [Fact]
    public void ASecondCompletedEvent_OnACompletedTransaction_DoesNotExtendAgain()
    {
        var e = PaddleBillingPolicy.Parse(CompletedBody(eventId: "evt_2"))!;
        var d = PaddleBillingPolicy.Decide(
            e, Record(PaddleTransactionStatuses.Completed), Plan(), alreadySeen: false, Now);

        Assert.Equal(PaddleAction.Replay, d.Action);
        Assert.False(d.Writes);
    }

    // ═══ ٥. المَرجِعُ والمَتجَر ════════════════════════════════════════

    [Fact]
    public void AnUnknownReference_WritesNothing()
    {
        var e = PaddleBillingPolicy.Parse(CompletedBody(reference: "wsl-pd-nobody-000"))!;
        var d = PaddleBillingPolicy.Decide(e, record: null, Plan(), alreadySeen: false, Now);

        Assert.Equal(PaddleAction.UnknownReference, d.Action);
        Assert.False(d.Writes);
    }

    [Fact]
    public void ATransactionWhoseTenantHasNoPlanDocument_WritesNothing()
    {
        var e = PaddleBillingPolicy.Parse(CompletedBody())!;
        var d = PaddleBillingPolicy.Decide(e, Record(), plan: null, alreadySeen: false, Now);

        Assert.Equal(PaddleAction.UnknownTenant, d.Action);
        Assert.False(d.Writes);
    }

    // ═══ ٦. التَمديدُ ومِرساتُه ════════════════════════════════════════

    /// <summary><b>مَن جَدَّدَ مُبَكِّراً لا يُصادَر ما تَبَقّى
    /// لَه</b> — المِرساةُ تاريخُ الانتِهاءِ لا اليَوم.</summary>
    [Fact]
    public void AnEarlyRenewal_AddsToTheRemainingTerm()
    {
        var e = PaddleBillingPolicy.Parse(CompletedBody())!;
        var plan = Plan();
        var d = PaddleBillingPolicy.Decide(e, Record(), plan, alreadySeen: false, Now);

        Assert.Equal(PaddleAction.Extend, d.Action);
        Assert.Equal(plan.ExpiresAt.AddDays(30), d.NewExpiresAt);
        Assert.True(d.TouchesPlan);
        Assert.True(d.TouchesTransaction);
    }

    /// <summary><b>ومَن عادَ بَعدَ انقِطاعٍ لا يُشتَرى لَه ماضٍ
    /// مَضى</b> — المِرساةُ اليَومُ لا التاريخُ المُنقَضي.</summary>
    [Fact]
    public void ALapsedTenant_StartsFromToday_NotFromThePast()
    {
        var e = PaddleBillingPolicy.Parse(CompletedBody())!;
        var plan = Plan();
        plan.ExpiresAt = Now.AddDays(-60);

        var d = PaddleBillingPolicy.Decide(e, Record(), plan, alreadySeen: false, Now);

        Assert.Equal(PaddleAction.Extend, d.Action);
        Assert.Equal(Now.AddDays(30), d.NewExpiresAt);
    }

    // ═══ ٧. الاسترداد — يُسحَبُ ما مُنِح، ولا يُسحَبُ ما لَم يُمنَح ════

    private static string AdjustmentBody(
        string eventId = "evt_adj", string type = "adjustment.updated",
        string action = "refund", string status = "approved", string txn = "txn_01j")
        => $$$"""
        {
          "event_id": "{{{eventId}}}",
          "event_type": "{{{type}}}",
          "data": {
            "id": "adj_01j",
            "transaction_id": "{{{txn}}}",
            "action": "{{{action}}}",
            "status": "{{{status}}}",
            "currency_code": "USD"
          }
        }
        """;

    [Fact]
    public void AnApprovedRefund_WithdrawsExactlyTheDaysItGranted()
    {
        var e = PaddleBillingPolicy.Parse(AdjustmentBody())!;
        var plan = Plan();
        var d = PaddleBillingPolicy.Decide(
            e, Record(PaddleTransactionStatuses.Completed), plan, alreadySeen: false, Now);

        Assert.Equal(PaddleAction.Withdraw, d.Action);
        Assert.Equal(plan.ExpiresAt.AddDays(-30), d.NewExpiresAt);
        Assert.Equal(PaddleTransactionStatuses.Refunded, d.TransactionStatus);
    }

    /// <summary><b>ولا يُسحَبُ قَبلَ الاعتِماد</b>: تَسوِيَةٌ
    /// «بِانتِظارِ المُوافَقَة» لا تَعني عَودَةَ مال.</summary>
    [Fact]
    public void APendingRefund_WithdrawsNothing()
    {
        var e = PaddleBillingPolicy.Parse(AdjustmentBody(status: "pending_approval"))!;
        var d = PaddleBillingPolicy.Decide(
            e, Record(PaddleTransactionStatuses.Completed), Plan(), alreadySeen: false, Now);

        Assert.Equal(PaddleAction.Ignored, d.Action);
        Assert.False(d.Writes);
    }

    /// <summary>وفِعلٌ لا يُعيدُ مالاً (رَصيدٌ مَثَلاً) لا
    /// يَسحَب.</summary>
    [Fact]
    public void ACreditAdjustment_WithdrawsNothing()
    {
        var e = PaddleBillingPolicy.Parse(AdjustmentBody(action: "credit"))!;
        var d = PaddleBillingPolicy.Decide(
            e, Record(PaddleTransactionStatuses.Completed), Plan(), alreadySeen: false, Now);

        Assert.Equal(PaddleAction.Ignored, d.Action);
        Assert.False(d.Writes);
    }

    /// <summary><b>ولا يُسحَبُ ما لَم يُمنَح</b>: مُعامَلَةٌ لَم
    /// تَكتَمِل لَم تُحَرِّك تاريخاً، فَسَحبُها يُصادِر مُدَّةً
    /// اشتُرِيَت بِمُعامَلَةٍ أُخرى.</summary>
    [Fact]
    public void ARefund_OnATransactionThatNeverCompleted_TouchesNoPlan()
    {
        var e = PaddleBillingPolicy.Parse(AdjustmentBody())!;
        var d = PaddleBillingPolicy.Decide(e, Record(), Plan(), alreadySeen: false, Now);

        Assert.Equal(PaddleAction.MarkTransaction, d.Action);
        Assert.False(d.TouchesPlan);
    }

    /// <summary>ورَدٌّ قَضائيٌّ مُعتَمَدٌ يَسحَبُ كَما يَسحَبُ
    /// الاستِرداد — <b>المالُ عادَ في الحالَتَين</b>.</summary>
    [Fact]
    public void AnApprovedChargeback_WithdrawsToo()
    {
        var e = PaddleBillingPolicy.Parse(AdjustmentBody(action: "chargeback"))!;
        var d = PaddleBillingPolicy.Decide(
            e, Record(PaddleTransactionStatuses.Completed), Plan(), alreadySeen: false, Now);

        Assert.Equal(PaddleAction.Withdraw, d.Action);
    }

    /// <summary><b>وحَدَثُ «اكتَمَلَت» بَعدَ الاستِردادِ لا يُمَدِّد</b>
    /// — واقِعَةٌ مُمكِنَةٌ لا نادِرَة، فَكُلُّ رَدٍّ غَيرِ ‏2xx
    /// يَجعَل Paddle تُعيد الإرسال.</summary>
    [Fact]
    public void ACompletedEvent_ArrivingAfterARefund_DoesNotGrantTheDaysBack()
    {
        var e = PaddleBillingPolicy.Parse(CompletedBody(eventId: "evt_late"))!;
        var d = PaddleBillingPolicy.Decide(
            e, Record(PaddleTransactionStatuses.Refunded), Plan(), alreadySeen: false, Now);

        Assert.Equal(PaddleAction.MarkTransaction, d.Action);
        Assert.False(d.TouchesPlan);
    }

    // ═══ ٧-ب. جِسرُ الاسترداد يَتبَعُ المُعامَلَةَ الَّتي دَفَعَت ═══════
    //
    // **السيناريو المَقيس، بِأَربَعِ خُطُوات**: المُشرِفُ يَنقُر
    // «أَنشِئ الرابِط» مَرَّتَينِ بِنَفسِ المَبلَغِ والمُدَّةِ والوَصف.
    // المَرجِعُ **حَتميٌّ مِن المُدخَلات** فَهُوَ واحِد، والوَثيقَةُ
    // ما زالَت `created` فَهي **قابِلَةٌ لِلكِتابَة**
    // (`IsOverwritable`) — فَتُنشَأُ **مُعامَلَتانِ عِندَ Paddle**
    // (لا رَأسَ مَرَّة-واحِدَةٍ في نِداءِ الإنشاء) وتَبقى **وَثيقَةٌ
    // واحِدَةٌ تَحمِلُ الأَحدَثَ وَحدَه**. ثُمَّ يَدفَعُ الدافِعُ
    // بِالرابِطِ **الأَوَّل**.
    //
    // وحَدَثُ الدَفعِ يَحمِلُ `custom_data` فَيُمَدِّدُ صَحيحاً. أَمّا
    // **حَدَثُ التَسوِيَةِ فَبِلا `custom_data` إطلاقاً**، وجِسرُه
    // الوَحيدُ `data.transaction_id` — يُبحَثُ بِه في
    // `PaddleFlow.FindTransactionAsync` عَن وَثيقَةٍ
    // `r.TransactionId == txn`. فَإن بَقِيَ في الوَثيقَةِ مُعَرِّفُ
    // **النَقرَةِ الثانِيَة** لَم تَجِد الاستِعلامَةُ شَيئاً ⇒
    // `UnknownReference` ⇒ ‏503 بِلا نِهايَة: **المالُ يَعودُ
    // والأَيّامُ تَبقى**.

    /// <summary>
    /// <para><b>المالُ يَعودُ فَتُسحَبُ أَيّامُه — لِأَنّ التَمديدَ
    /// يُثَبِّتُ مُعَرِّفَ المُعامَلَةِ الَّتي دَفَعَت.</b></para>
    ///
    /// <para><b>وهذا الاختِبارُ يَقيسُ الحَقلَ الَّذي تُرَشِّحُ عَلَيه
    /// الاستِعلامَة</b> (<c>TransactionId</c>)، لا الاستِعلامَةَ
    /// نَفسَها: جَلسَةُ الحُرّاسِ السُلوكِيَّةِ لا تُنَفِّذ
    /// <c>Query</c> — دَينٌ مُعلَنٌ في رَأسِ
    /// <c>PaddleEndpointBehaviourTests</c> ويُسَدَّدُ يَومَ يوجَد
    /// حِسابُ Paddle حَقيقيّ.</para>
    /// </summary>
    [Fact]
    public void TwoLinksOnePayment_TheRefundStillFindsItsDocument_SoTheDaysAreWithdrawn()
    {
        // النَقرَةُ الثانِيَةُ دَهَسَت الوَثيقَةَ بِمُعامَلَتِها هي.
        var record = Record();
        record.TransactionId = "txn_second_click";

        // والدافِعُ فَتَحَ الرابِطَ الأَوَّل.
        var paid = PaddleBillingPolicy.Parse(CompletedBody(txn: "txn_first_link"))!;
        var extend = PaddleBillingPolicy.Decide(paid, record, Plan(), alreadySeen: false, Now);
        Assert.Equal(PaddleAction.Extend, extend.Action);

        PaddleBillingPolicy.Apply(record, paid, extend, Now);

        // ← الجِسر: الوَثيقَةُ تَحمِلُ الآنَ المُعامَلَةَ الَّتي دَفَعَت.
        Assert.Equal("txn_first_link", record.TransactionId);

        // ثُمَّ يَصِلُ الاستِردادُ بِلا `custom_data`، ومِفتاحُه هذا.
        var refund = PaddleBillingPolicy.Parse(
            AdjustmentBody(txn: "txn_first_link"))!;
        Assert.Null(refund.Reference);
        Assert.Equal(record.TransactionId, refund.TransactionId);   // شَرطُ `FindTransactionAsync`

        var plan = Plan();
        var withdraw = PaddleBillingPolicy.Decide(refund, record, plan, alreadySeen: false, Now);

        Assert.Equal(PaddleAction.Withdraw, withdraw.Action);
        Assert.Equal(plan.ExpiresAt.AddDays(-30), withdraw.NewExpiresAt);
    }

    /// <summary><b>ومُعَرِّفُ الاشتِراكِ يَتبَعُ الدافِعَ كَذلك</b> —
    /// قاعِدَةٌ واحِدَةٌ لا اثنَتان: مَن دَفَعَ هُوَ مَن يُعَرِّف
    /// المُعامَلَة.</summary>
    [Fact]
    public void Extend_PinsTheSubscriptionIdOfThePayingTransaction_Too()
    {
        var record = Record();
        record.SubscriptionId = "sub_from_the_second_click";

        var body = CompletedBody(txn: "txn_first_link")
            .Replace("\"status\": \"completed\"",
                     "\"status\": \"completed\",\n    \"subscription_id\": \"sub_paid\"",
                     StringComparison.Ordinal);

        var e = PaddleBillingPolicy.Parse(body)!;
        var d = PaddleBillingPolicy.Decide(e, record, Plan(), alreadySeen: false, Now);
        PaddleBillingPolicy.Apply(record, e, d, Now);

        Assert.Equal("sub_paid", record.SubscriptionId);
    }

    /// <summary><b>وما ليسَ تَمديداً لا يَدهَسُ شَيئاً</b>: حَدَثٌ
    /// مُتَأَخِّرٌ على مُعامَلَةٍ حُسِمَت يُعَلِّمُ ولا يُبَدِّلُ
    /// المُعَرِّفَ الَّذي يَربِطُ الاستِرداد.</summary>
    [Fact]
    public void AMarkingEvent_NeverRewritesThePayingTransactionId()
    {
        var record = Record(PaddleTransactionStatuses.Refunded);
        record.TransactionId = "txn_that_paid";

        var e = PaddleBillingPolicy.Parse(CompletedBody(eventId: "evt_late", txn: "txn_other"))!;
        var d = PaddleBillingPolicy.Decide(e, record, Plan(), alreadySeen: false, Now);
        Assert.Equal(PaddleAction.MarkTransaction, d.Action);

        PaddleBillingPolicy.Apply(record, e, d, Now);

        Assert.Equal("txn_that_paid", record.TransactionId);
    }

    /// <summary>
    /// <para><b>ودَفعَتانِ لِمُعامَلَةٍ واحِدَةٍ لَيسَتا «إعادَةَ
    /// إرسال» — تُسَمّى بِاسمِها.</b> ‏«‏Replay» تَقول «هذِه الرِسالَةُ
    /// وَصَلَت مَرَّتَين»، والواقِعُ هُنا <b>مالٌ قُبِضَ مَرَّتَينِ
    /// ومُدِّدَ مَرَّة</b>. وسَطرُ لوغٍ يَقولُ الأولى عَن الثانِيَةِ
    /// <b>سَطرٌ يَكذِب</b>، وهُوَ أَسوَأُ مِن سَطرٍ غائِب.</para>
    ///
    /// <para><b>ولا يُشفى بِالإعادَة</b>: لا كِتابَةَ تُصلِحُ قَبضاً
    /// ثانِياً عِندَ Paddle — يُرَدُّ يَدَوِيّاً مِن لَوحَتِها.
    /// فَالرَدُّ ‏200 وسَطرُ خَطَإٍ يَصرُخ، ودَينٌ مُعلَنٌ في
    /// <c>docs/DEPLOY.md</c> §٢·هـ.</para>
    /// </summary>
    [Fact]
    public void ASecondPaidTransaction_OnTheSameReference_IsNamedNotDisguisedAsAReplay()
    {
        var record = Record(PaddleTransactionStatuses.Completed);
        record.TransactionId = "txn_first_link";

        var e = PaddleBillingPolicy.Parse(
            CompletedBody(eventId: "evt_2", txn: "txn_second_click"))!;
        var d = PaddleBillingPolicy.Decide(e, record, Plan(), alreadySeen: false, Now);

        Assert.Equal(PaddleAction.DuplicatePayment, d.Action);
        Assert.False(d.Writes);
        Assert.Contains("txn_second_click", d.ReasonAr, StringComparison.Ordinal);
    }

    /// <summary>وإعادَةُ إرسالِ <b>نَفسِ</b> المُعامَلَةِ تَبقى
    /// «‏Replay» — الفَرقُ مُعَرِّفُ المُعامَلَةِ لا عَدَدُ
    /// الرَسائِل.</summary>
    [Fact]
    public void TheSameTransactionArrivingTwice_IsStillAReplay()
    {
        var record = Record(PaddleTransactionStatuses.Completed);
        record.TransactionId = "txn_01j";

        var e = PaddleBillingPolicy.Parse(CompletedBody(eventId: "evt_2"))!;
        var d = PaddleBillingPolicy.Decide(e, record, Plan(), alreadySeen: false, Now);

        Assert.Equal(PaddleAction.Replay, d.Action);
        Assert.False(d.Writes);
    }

    // ═══ ٨. الثابِتُ الأَخير — لا يُغادَرُ «اكتَمَلَت» إلّا بِسَحب ════

    [Theory]
    [InlineData(PaddleTransactionStatuses.Completed, PaddleTransactionStatuses.Refunded, PaddleAction.Withdraw,        true)]
    [InlineData(PaddleTransactionStatuses.Completed, PaddleTransactionStatuses.Refunded, PaddleAction.MarkTransaction, false)]
    [InlineData(PaddleTransactionStatuses.Created,   PaddleTransactionStatuses.Completed, PaddleAction.Extend,         true)]
    [InlineData(PaddleTransactionStatuses.Refunded,  PaddleTransactionStatuses.Completed, PaddleAction.Extend,         false)]
    [InlineData(PaddleTransactionStatuses.Canceled,  PaddleTransactionStatuses.Completed, PaddleAction.Extend,         false)]
    public void MayWriteStatus_LetsNothingLeaveCompleted_ExceptAWithdrawal(
        string from, string to, PaddleAction action, bool expected)
        => Assert.Equal(expected, PaddleBillingPolicy.MayWriteStatus(from, to, action));

    /// <summary>والأَثَرُ يَمُرُّ بِنَفسِ الحارِس: حَدَثٌ مُتَأَخِّرٌ
    /// لا يَهبِط بِالحالَةِ مِن «وَصَلَ المال».</summary>
    [Fact]
    public void Apply_NeverDowngradesACompletedTransaction()
    {
        var record = Record(PaddleTransactionStatuses.Completed);
        var e = PaddleBillingPolicy.Parse(CompletedBody(status: "billed"))!;

        PaddleBillingPolicy.Apply(
            record, e,
            new PaddleDecision(PaddleAction.MarkTransaction, default,
                PaddleTransactionStatuses.Created, ""),
            Now);

        Assert.Equal(PaddleTransactionStatuses.Completed, record.Status);
    }

    // ═══ ٩. الاشتِراكُ حالَةٌ لا مال ═══════════════════════════════════

    private static string SubscriptionBody(string type, string eventId = "evt_sub")
        => $$$"""
        {
          "event_id": "{{{eventId}}}",
          "event_type": "{{{type}}}",
          "data": {
            "id": "sub_01j",
            "transaction_id": "txn_01j",
            "status": "active",
            "custom_data": { "wasayel_ref": "{{{Reference}}}" }
          }
        }
        """;

    [Theory]
    [InlineData("subscription.created")]
    [InlineData("subscription.updated")]
    public void ASubscriptionEvent_NeverMovesAPlanDate(string type)
    {
        var e = PaddleBillingPolicy.Parse(SubscriptionBody(type))!;
        var d = PaddleBillingPolicy.Decide(e, Record(), Plan(), alreadySeen: false, Now);

        Assert.Equal(PaddleAction.MarkTransaction, d.Action);
        Assert.False(d.TouchesPlan);
    }

    /// <summary><b>والإلغاءُ يُوقِفُ التَجديدَ ولا يُطفِئُ مَتجَراً
    /// سارِياً</b> — مَن دَفَعَ شَهراً يَأخُذُ شَهرَه كامِلاً.</summary>
    [Fact]
    public void ACanceledSubscription_StopsRenewal_AndKeepsThePaidTerm()
    {
        var e = PaddleBillingPolicy.Parse(SubscriptionBody("subscription.canceled"))!;
        var plan = Plan();
        var d = PaddleBillingPolicy.Decide(e, Record(), plan, alreadySeen: false, Now);

        Assert.Equal(PaddleAction.StopRenewal, d.Action);
        Assert.Equal(plan.ExpiresAt, d.NewExpiresAt);
    }

    /// <summary>ومُعَرِّفُ الاشتِراكِ يُسَجَّلُ على الوَثيقَة —
    /// <c>data.id</c> في أَحداثِ الاشتِراك، لا
    /// <c>data.subscription_id</c>.</summary>
    [Fact]
    public void ASubscriptionEvent_RecordsItsIdOnTheTransaction()
    {
        var record = Record();
        var e = PaddleBillingPolicy.Parse(SubscriptionBody("subscription.created"))!;
        var d = PaddleBillingPolicy.Decide(e, record, Plan(), alreadySeen: false, Now);

        PaddleBillingPolicy.Apply(record, e, d, Now);

        Assert.Equal("sub_01j", record.SubscriptionId);
        Assert.Equal(PaddleTransactionStatuses.Created, record.Status);
    }

    // ═══ ١٠. القِراءَة — جِسمٌ مُشَوَّهٌ يُعطي null ولا يَرمي ══════════

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"event_type\":\"transaction.completed\"}")]   // بِلا event_id
    [InlineData("{\"event_id\":\"evt_1\"}")]                     // بِلا event_type
    public void Parse_ReturnsNull_ForAnUnreadableBody(string body)
        => Assert.Null(PaddleBillingPolicy.Parse(body));

    /// <summary>وجِسمٌ بِلا <c>data</c> يُقرَأُ ولا يَرمي — ويَنتَهي
    /// «مَرجِعاً مَجهولاً» لا انفِجاراً.</summary>
    [Fact]
    public void Parse_ReadsAnEnvelopeWithNoData()
    {
        var e = PaddleBillingPolicy.Parse(
            "{\"event_id\":\"evt_1\",\"event_type\":\"transaction.completed\"}");

        Assert.NotNull(e);
        Assert.Null(e!.Reference);
    }

    // ═══ ١١. المُسَوَّدَةُ والمَرجِعُ والمَبلَغُ المُرسَل ══════════════

    private static PaddleTransactionDraft Draft(
        decimal amount = 49m, string currency = "USD", int days = 30,
        string description = "باقَةُ شَهر", string cycle = "2026-09-08")
        => new("ejar", "manual", amount, currency, days, description, cycle);

    /// <summary><b>المَبلَغُ يُرسَل بِأَصغَرِ وَحدَة</b> — و«‏49.00
    /// دولاراً» تُرسَل <c>"4900"</c>.</summary>
    [Theory]
    [InlineData(49, "USD", "4900")]
    [InlineData(9.99, "EUR", "999")]
    [InlineData(1, "SAR", "100")]
    [InlineData(5000, "JPY", "5000")]     // بِلا كُسور
    [InlineData(1200, "KRW", "1200")]
    public void TheAmount_IsSentInTheSmallestUnit(decimal amount, string currency, string expected)
        => Assert.Equal(expected, Draft(amount, currency).MinorAmount);

    /// <summary><b>ونَقرَتانِ على نَفسِ النَموذَجِ تُعطيانِ مَرجِعاً
    /// واحِداً</b> — فَوَثيقَةٌ واحِدَةٌ لا وَثيقَتان.</summary>
    [Fact]
    public void TwoIdenticalDrafts_ProduceOneReference()
        => Assert.Equal(
            PaddleTransactionPolicy.Reference(Draft()),
            PaddleTransactionPolicy.Reference(Draft()));

    /// <summary><b>ومُمَيِّزُ الدَورَةِ يَجعَل تَجديدَ الشَهرِ التالي
    /// مَرجِعاً جَديداً</b> — ولَولاه لَدَهَسَ وَثيقَةَ الشَهرِ
    /// السابِقِ ومَحا مُعَرِّفَ المُعامَلَةِ الَّذي يَربِط أَيَّ
    /// استِردادٍ لاحِق.</summary>
    [Fact]
    public void TheCycleDiscriminator_MakesTheNextRenewalANewReference()
        => Assert.NotEqual(
            PaddleTransactionPolicy.Reference(Draft(cycle: "2026-09-08")),
            PaddleTransactionPolicy.Reference(Draft(cycle: "2026-10-08")));

    /// <summary>وتَغييرُ المَبلَغِ أَو المُدَّةِ يُعطي مَرجِعاً آخَرَ
    /// — طَلَبٌ جَديدٌ حينَ يُرادُ فِعلاً.</summary>
    [Fact]
    public void ChangingTheInputs_ChangesTheReference()
    {
        var baseRef = PaddleTransactionPolicy.Reference(Draft());
        Assert.NotEqual(baseRef, PaddleTransactionPolicy.Reference(Draft(amount: 59m)));
        Assert.NotEqual(baseRef, PaddleTransactionPolicy.Reference(Draft(days: 60)));
        Assert.NotEqual(baseRef, PaddleTransactionPolicy.Reference(Draft(currency: "EUR")));
    }

    /// <summary>والمَرجِعُ يَحمِلُ السلاجَ ظاهِراً — مَن يَفتَح
    /// تَقريرَ Paddle يَقرَأُ المَتجَرَ لا بَصمَةً صَمّاء.</summary>
    [Fact]
    public void TheReference_CarriesTheSlugInTheClear()
        => Assert.StartsWith("wsl-pd-ejar-", PaddleTransactionPolicy.Reference(Draft()));

    // ═══ ١٢. المُصادِق — رَمزُ خَرقٍ ثابِتٌ لِكُلِّ شَرط ═══════════════

    [Fact]
    public void AValidDraft_HasNoViolations()
        => Assert.True(PaddleTransactionPolicy.IsValid(Draft()));

    [Theory]
    [InlineData(0,    PaddleTransactionPolicy.AmountNotPositive)]
    [InlineData(-1,   PaddleTransactionPolicy.AmountNotPositive)]
    public void ANonPositiveAmount_IsRefused(decimal amount, string code)
        => Assert.Contains(PaddleTransactionPolicy.Validate(Draft(amount: amount)),
            v => v.Code == code);

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(731)]
    public void ADurationOutsideTheRange_IsRefused(int days)
        => Assert.Contains(PaddleTransactionPolicy.Validate(Draft(days: days)),
            v => v.Code == PaddleTransactionPolicy.DaysOutOfRange);

    /// <summary><b>والعُملَةُ حَقلٌ لا مَعجَمٌ مُغلَق</b> — يُفحَصُ
    /// شَكلُها وَحدَه، وتَرُدُّ Paddle ما لا تَقبَل. فَـ<c>SAR</c>
    /// تَمُرُّ هُنا وقَد كانَت تُرَدُّ في مَسارِ PayPal.</summary>
    [Theory]
    [InlineData("SAR")]
    [InlineData("USD")]
    [InlineData("aed")]
    public void AWellFormedCurrency_Passes(string currency)
        => Assert.DoesNotContain(PaddleTransactionPolicy.Validate(Draft(currency: currency)),
            v => v.Code == PaddleTransactionPolicy.CurrencyMalformed);

    [Theory]
    [InlineData("US")]
    [InlineData("DOLLAR")]
    [InlineData("1SD")]
    public void AMalformedCurrency_IsRefused(string currency)
        => Assert.Contains(PaddleTransactionPolicy.Validate(Draft(currency: currency)),
            v => v.Code == PaddleTransactionPolicy.CurrencyMalformed);

    [Fact]
    public void ADraftWithoutAPlanDocument_IsRefused()
        => Assert.Contains(
            PaddleTransactionPolicy.Validate(
                new PaddleTransactionDraft("ejar", "", 49m, "USD", 30, "", "")),
            v => v.Code == PaddleTransactionPolicy.PlanMissing);

    /// <summary>وكُلُّ رَمزِ خَرقٍ لَه نَصٌّ عَرَبيٌّ غَيرُ فارِغ —
    /// فَاللوغُ يُقرَأ.</summary>
    [Fact]
    public void EveryViolation_CarriesAnArabicMessage()
    {
        var v = PaddleTransactionPolicy.Validate(
            new PaddleTransactionDraft("", "", 0m, "X", 0, new string('x', 500), ""));

        Assert.NotEmpty(v);
        Assert.All(v, x => Assert.False(string.IsNullOrWhiteSpace(x.MessageAr)));
        Assert.All(v, x => Assert.StartsWith("paddle_tx_", x.Code));
    }

    // ═══ ١٣. الكِتابَةُ فَوقَ وَثيقَةٍ قائِمَة ════════════════════════

    /// <summary><b>وَثيقَةٌ بَلَغَت «وَصَلَ المال» لا تُدهَس</b> —
    /// فيها مُعَرِّفُ المُعامَلَةِ الَّذي يَربِط أَيَّ استِردادٍ
    /// لاحِق.</summary>
    [Theory]
    [InlineData(PaddleTransactionStatuses.Created,   true)]
    [InlineData(PaddleTransactionStatuses.Completed, false)]
    [InlineData(PaddleTransactionStatuses.Refunded,  false)]
    [InlineData(PaddleTransactionStatuses.Canceled,  false)]
    public void OnlyATransactionStillAwaitingPayment_MayBeOverwritten(string status, bool expected)
        => Assert.Equal(expected, PaddleTransactionPolicy.IsOverwritable(Record(status)));

    [Fact]
    public void NoDocumentAtAll_IsOverwritable()
        => Assert.True(PaddleTransactionPolicy.IsOverwritable(null));

    // ═══ ١٤. رابِطُ الدَفع ═════════════════════════════════════════════

    /// <summary>ما تُعيدُه Paddle يَفوز — وهي أَعلَمُ بِرابِطِها
    /// المُسَجَّل.</summary>
    [Fact]
    public void ThePaddleReturnedCheckoutUrl_Wins()
    {
        var url = PaddleTransactionPolicy.CheckoutUrl(
            "https://ours.example/pay.html", "https://paddle.example/x?_ptxn=txn_9", "txn_9", Reference);

        Assert.StartsWith("https://paddle.example/x?_ptxn=txn_9&", url);
        Assert.Contains("ref=" + Reference, url);
    }

    /// <summary>وبِلا رابِطٍ مِن Paddle يُركَّبُ مِن صَفحَتِنا
    /// ومُعَرِّفِ المُعامَلَة.</summary>
    [Fact]
    public void WithoutAPaddleUrl_TheLinkIsComposedFromOurPage()
    {
        var url = PaddleTransactionPolicy.CheckoutUrl(
            "https://ours.example/pay.html", null, "txn_9", Reference);

        Assert.Equal($"https://ours.example/pay.html?_ptxn=txn_9&ref={Reference}", url);
    }

    /// <summary><b>وبِلا صَفحَةٍ ولا رابِطٍ لا يُبنى نِصفُ رابِط</b>
    /// — <c>null</c> تَعني «لا زِرّ»، ومَدخَلٌ يَضُرّ أَسوَأُ مِن
    /// غِيابِ مَدخَل.</summary>
    [Fact]
    public void WithNeither_NoLinkIsInvented()
        => Assert.Null(PaddleTransactionPolicy.CheckoutUrl("", null, "txn_9", Reference));

    /// <summary>والفاصِلُ يُحسَبُ ولا يُفتَرَض — عُنوانٌ يَحمِل
    /// مُعامِلاً سَلَفاً يَأخُذ <c>&amp;</c>.</summary>
    [Fact]
    public void TheQuerySeparator_IsComputedNotAssumed()
        => Assert.Equal(
            "https://ours.example/pay.html?v=2&_ptxn=txn_9",
            PaddleTransactionPolicy.CheckoutUrl(
                "https://ours.example/pay.html?v=2", null, "txn_9", reference: null));

    // ═══ ١٥. جِسمُ النِداء — يُقاسُ بِلا شَبَكَة ═══════════════════════

    /// <summary><b>مَرجِعُنا يُرسَل في <c>custom_data</c></b> — وهُوَ
    /// الطَريقُ الوَحيدُ الَّذي يَعودُ بِه الحَدَث.</summary>
    [Fact]
    public void TheCreateBody_CarriesOurReferenceInCustomData()
    {
        var body = PaddleTransactionPolicy.CreateBody(Draft(), Reference);
        var custom = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object>>(
            body["custom_data"]);

        Assert.Equal(Reference, custom[PaddleTransactionPolicy.ReferenceKey]);
    }

    /// <summary>والمَبلَغُ المُرسَلُ هُوَ <b>المَصوغُ بِأَصغَرِ
    /// وَحدَة</b> نَصّاً — لا عَدَداً عَشرِيّاً.</summary>
    [Fact]
    public void TheCreateBody_SendsTheMinorUnitAmountAsAString()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            PaddleTransactionPolicy.CreateBody(Draft(), Reference));

        Assert.Contains("\"amount\":\"4900\"", json);
        Assert.Contains("\"currency_code\":\"USD\"", json);
        Assert.Contains("\"collection_mode\":\"automatic\"", json);
    }

    /// <summary><b>والكَمِّيَّةُ واحِدَةٌ مَحبوسَةٌ بِحَدَّين</b>:
    /// باقَةُ مَنَصَّةٍ لا تُشتَرى «‏3 مَرّات»، وسَقفٌ مَفتوحٌ يَجعَل
    /// الدافِعَ يَدفَع ثَلاثَةَ أَضعافٍ <b>ولا يُمَدَّدُ شَيء</b>.</summary>
    [Fact]
    public void TheCreateBody_LocksTheQuantityToOne()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            PaddleTransactionPolicy.CreateBody(Draft(), Reference));

        Assert.Contains("\"minimum\":1", json);
        Assert.Contains("\"maximum\":1", json);
    }

    /// <summary>ووَصفٌ فارِغٌ لا يُرسَل فارِغاً — <c>name</c> مَطلوبٌ
    /// لِلسِعرِ المُرتَجَل، وصَفحَةُ دَفعٍ بِلا اسمِ ما يُشتَرى
    /// مَدخَلٌ يَضُرّ.</summary>
    [Fact]
    public void AnEmptyDescription_StillProducesALabel()
        => Assert.False(string.IsNullOrWhiteSpace(
            PaddleTransactionPolicy.Label(Draft(description: ""))));

    // ═══ ١٦. قِراءَةُ رَدِّ الإنشاء ════════════════════════════════════

    [Fact]
    public void ReadTransaction_ReadsTheIdStatusAndCheckoutUrl()
    {
        var (id, status, url) = PaddlePaymentProvider.ReadTransaction(
            """
            {"data":{"id":"txn_01j","status":"ready",
                     "checkout":{"url":"https://pay.example/x?_ptxn=txn_01j"}}}
            """);

        Assert.Equal("txn_01j", id);
        Assert.Equal("ready", status);
        Assert.Equal("https://pay.example/x?_ptxn=txn_01j", url);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"data\":null}")]
    [InlineData("[]")]
    public void ReadTransaction_ReturnsNothing_ForAnUnreadableBody(string body)
        => Assert.Null(PaddlePaymentProvider.ReadTransaction(body).Id);

    // ═══ ١٧. دَرَجاتُ التَهيئَة — ثَلاثٌ لا واحِدَة ════════════════════

    [Fact]
    public void ConfiguredCanVerifyAndCanSell_AreThreeDifferentQuestions()
    {
        Assert.True(PaddleEnvironment.IsConfigured(Ready()));
        Assert.True(PaddleEnvironment.CanVerifyWebhooks(Ready()));
        Assert.True(PaddleEnvironment.CanSell(Ready()));

        // مِفتاحٌ وبيئَةٌ بِلا سِرِّ وِجهَة: نُنادي ولا نَستَقبِل.
        Assert.True(PaddleEnvironment.IsConfigured(Ready(secret: "")));
        Assert.False(PaddleEnvironment.CanVerifyWebhooks(Ready(secret: "")));
        Assert.False(PaddleEnvironment.CanSell(Ready(secret: "")));

        // كُلُّ شَيءٍ إلّا صَفحَةَ الدَفع: نَستَقبِل ولا نَبيع —
        // **رابِطٌ يُرسَل ولا يُفتَح**.
        Assert.True(PaddleEnvironment.CanVerifyWebhooks(Ready(link: "")));
        Assert.False(PaddleEnvironment.CanSell(Ready(link: "")));

        // وبِلا رَمزِ عَميلٍ لا تُهَيَّأُ paddle.js فَلا تُعرَض نافِذَة.
        Assert.False(PaddleEnvironment.CanSell(Ready(token: "")));
    }

    /// <summary><b>وبيئَةٌ خارِجَ المَعجَمِ نِيَّةٌ لَم تَتَحَقَّق لا
    /// غِياب</b> — تُفَرَّق عَن الفَراغ، وهذا هُوَ ما يُفشِلُ
    /// الإقلاع.</summary>
    [Theory]
    [InlineData("", false)]
    [InlineData("sandbox", false)]
    [InlineData("live", false)]
    [InlineData("sanbdox", true)]
    [InlineData("production", true)]
    public void AnEnvironmentOutsideTheVocabulary_IsToldApartFromAnAbsentOne(
        string env, bool misconfigured)
        => Assert.Equal(misconfigured, PaddleEnvironment.IsMisconfiguredEnvironment(env));

    [Theory]
    [InlineData("sandbox", PaddleEnvironment.SandboxBaseUrl)]
    [InlineData("live", PaddleEnvironment.LiveBaseUrl)]
    [InlineData("LIVE", PaddleEnvironment.LiveBaseUrl)]
    [InlineData("staging", null)]
    [InlineData("", null)]
    public void TheHost_IsChosenByVocabulary_NeverGuessed(string env, string? expected)
        => Assert.Equal(expected, PaddleEnvironment.BaseUrlFor(env));

    /// <summary>ومُتَغَيِّرُ البيئَةِ يُشتَقُّ مِن مِفتاحِ التَهيئَة —
    /// فَلا يُكتَب اسمانِ في وَثيقَةٍ وكود.</summary>
    [Fact]
    public void TheEnvironmentVariableNames_AreDerivedFromTheConfigKeys()
    {
        Assert.Equal("Payments__Paddle__ApiKey",
            PaddleEnvironment.EnvVarName(PaddleEnvironment.ApiKeyKey));
        Assert.Equal("Payments__Paddle__WebhookSecret",
            PaddleEnvironment.EnvVarName(PaddleEnvironment.WebhookSecretKey));
        Assert.Equal("Payments__Paddle__DefaultPaymentLink",
            PaddleEnvironment.EnvVarName(PaddleEnvironment.DefaultPaymentLinkKey));
    }

    // ═══ ١٨. التَقريبُ لِصالِحِ البائِعِ دائِماً ═══════════════════════

    /// <summary><b>نِصفُ قِرشٍ يُقَرَّبُ صُعوداً دائِماً</b> — لا
    /// مَرَّةً صُعوداً ومَرَّةً نُزولاً بِحَسَبِ زَوجِيَّةِ الرَقَم
    /// (التَقريبُ المَصرِفيُّ الافتِراضيُّ في .NET)، وإلّا أُرسِلَ
    /// مَبلَغانِ مُتَساوِيانِ مُختَلِفَين.</summary>
    [Theory]
    [InlineData(0.005, "USD", "1")]
    [InlineData(0.015, "USD", "2")]
    [InlineData(0.025, "USD", "3")]
    public void HalfUnits_AlwaysRoundUp(decimal amount, string currency, string expected)
        => Assert.Equal(expected, PaddleCurrencies.Minor(amount, currency));

    /// <summary>ولا يُقرَأُ مَبلَغٌ بِفاصِلَةٍ أوروبِّيَّةٍ عَلى
    /// أَنَّه أَلف — <c>"49,99"</c> لَيسَت <c>4999</c>.</summary>
    [Fact]
    public void AThousandsSeparator_IsNotReadAsANumber()
    {
        var draft = PaddleTransactionPolicy.ReadDraft(
            "ejar", "manual", "49,99", "USD", "30", "", "2026-09-08");

        Assert.Equal(0m, draft.Amount);
        Assert.Contains(PaddleTransactionPolicy.Validate(draft),
            v => v.Code == PaddleTransactionPolicy.AmountNotPositive);
    }

    /// <summary>ومَبلَغٌ سالِبٌ يَسقُط إلى صِفرٍ فَيَرتَدُّ بِخَرقٍ
    /// مُسَمّى — لا يُرسَل ولا يُقبَل.</summary>
    [Fact]
    public void ANegativeAmount_FallsToZero_AndIsRefusedByName()
    {
        var draft = PaddleTransactionPolicy.ReadDraft(
            "ejar", "manual", "-49", "USD", "30", "", "2026-09-08");

        Assert.Contains(PaddleTransactionPolicy.Validate(draft),
            v => v.Code == PaddleTransactionPolicy.AmountNotPositive);
    }

    /// <summary>ومُمَيِّزُ الدَورَةِ يُشتَقُّ مِن تاريخِ الانتِهاءِ
    /// القائِم — دالَّةٌ نَقِيَّةٌ بِلا قِراءَةٍ ثانِيَة.</summary>
    [Fact]
    public void TheCycleDiscriminator_IsTheStandingExpiryDate()
    {
        Assert.Equal("", PaddleTransactionPolicy.CycleOf(null));
        Assert.Equal(
            Plan().ExpiresAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PaddleTransactionPolicy.CycleOf(Plan()));
    }
}
