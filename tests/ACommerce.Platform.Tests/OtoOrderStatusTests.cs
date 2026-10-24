using System.Net;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using ACommerce.Kit.Delivery;
using ACommerce.Kit.Payments;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Shipping;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ وارِدُ حالِ الطَلَبِ مِن OTO — إشارَةٌ لا بُرهان (‏ADR-033 §٢‑هـ القاعِدَة ٦؛ ‏ADR-036 الخُطوَة 3) ═════════
//
// ‏`POST /{slug}/shipping/oto/order-status` يُناديه خادِمُ OTO بِإشعارِ `orderStatus` (وَثيقَةُ OTO «Webhook»). ويُقاسُ هُنا:
//   ١) البَوّابَةُ النَقِيَّةُ `OtoOrderStatusGate` — لِكُلِّ رَمزٍ مِن العَشَرَةِ مُوجَبٌ وسالِب، والتَرتيب، ورَدُّ HTTP لِكُلِّ رَمز؛
//      وعَدّادٌ يُطبَعُ بِما أَنتَجَته الدَوالُّ الحَقيقِيَّةُ مِن المَعجَم (القاعِدَة ١٠).
//   ٢) الخِدمَةُ عَلى عالَمِ الوَثائِقِ ومُعالِجٍ يُعيدُ رُدودَ الوَثيقَة: لا كِتابَةَ قَبلَ سُؤالِ OTO، والإشعارُ وَحدَه لا يُصَدَّق،
//      ولا يُكتَبُ إلّا `Shipping → Delivered` بِالطَرَفِ المُقابِل، والمُكَرَّرُ بِلا أَثَر.
//   ٣) **الرَدُّ عَلى تَعارُضِ النُسخَة: يُعادُ القَرارُ عَلى المَخزون** — كِتابَةٌ أُخرى تَبقى ويُكتَبُ التَسليم، وتَسليمٌ يَدَوِيٌّ سَبَقَ
//      يُجابُ «سَبَق» بِلا انتِقالٍ ثانٍ، وثَلاثُ خَسائِرَ تُجابُ ‏503 بِلا كِتابَة. وفي CI يُعيدُ الخُطّافُ المَخزَنَ إلى ما قَبلَ
//      المُحاوَلَةِ المَردودَة (العالَمُ يُعيدُ الكائِنَ نَفسَه، فَبِلا ذلك تَبقى طَفرَتُها) — والبُرهانُ الحَيُّ عَلى Postgres أَدناه.
//   ٤) النُقطَةُ في مُضيفٍ مُصَغَّر: ‏404 فارِغٌ بِلا ناقِلٍ ولا لَمسَ، و‏400 و‏200 و‏503 — ومَسارٌ حَرفِيٌّ بِحارِسٍ قَبلَ النِداء.

public class OtoOrderStatusTests(ITestOutputHelper output)
{
    private const string Slug = DealShipTests.Slug;
    private static readonly Guid Seller = DealShipTests.Seller;
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private const string OurOtoId = "540789";
    private const string Label = "https://app.tryoto.com/print/awb?enc=y";
    private const string TrackingUrl = "https://app.tryoto.com/otoflex-tracking?enc=x";

    /// <summary>خَتمُ مِثالِ الوَثيقَةِ بِحَرفِه — وهُوَ ساعَةُ العالَم (<see cref="DealShipTests.Now"/>).</summary>
    internal const string DocTimestamp = "1595941360328";

    /// <summary>
    /// <para>جِسمُ الإشعارِ بِحُقولِ وَثيقَةِ OTO («Webhook»): ‏<c>orderId</c> و<c>otoId</c> و<c>status</c> و<c>timestamp</c>.</para>
    /// <para><b>و<c>signature</c> مُنذُ ‏2026-10-12</b> (تَيّارُ S8، ‏ADR-033 الخُطوَة 7): ‏Base64 لِبَصمَةِ ‏HmacSHA256 بِسِرِّ بائِعِ العالَمِ
    /// عَلى <c>orderId:status:timestamp</c> — كَما تُوَقِّعُ OTO. و<paramref name="secret"/> <c>null</c> يُسقِطُ الحَقلَ كُلَّه (إشعارٌ بِلا تَوقيع).</para>
    /// </summary>
    internal static string NoticeJson(string orderId, string? otoId = OurOtoId, string status = "delivered",
        string? secret = DealShipTests.WebhookSecret, string timestamp = DocTimestamp)
    {
        var body = new Dictionary<string, object?>
        {
            ["orderId"] = orderId, ["otoId"] = otoId, ["status"] = status, ["timestamp"] = timestamp,
        };
        if (secret is not null) body["signature"] = OtoWebhookSignature.Compute(secret, orderId, status, timestamp);
        return JsonSerializer.Serialize(body);
    }

    internal static OtoOrderStatusNotice Notice(Guid dealId, string? otoId = OurOtoId, string status = "delivered",
        string? secret = DealShipTests.WebhookSecret, string timestamp = DocTimestamp)
        => OtoOrderStatusGate.Admit(true, NoticeJson(dealId.ToString("N"), otoId, status, secret, timestamp)).Notice!;

    // ─── صَفقَةٌ عِندَ الشَحنِ — كُلُّ صَفٍّ يُغَيِّرُ فيها شَيئاً واحِداً (البَوّابَةُ النَقِيَّة) ───────────────

    private static readonly Guid DealId = Guid.Parse("0f8fad5b-d9cb-469f-a165-705ed7eb6e2a");

    private static Deal Shipping() => new()
    {
        Id = DealId,
        TenantSlug = Slug,
        Pattern = "marketplace",
        InitiatorId = DealShipTests.Buyer,
        InitiatorName = "مُشتَرٍ",
        CounterpartyId = Seller,
        CounterpartyName = "بائِع",
        AmountSar = 150m,
        Stage = DealStage.Shipping,
        Status = DealStatus.Active,
        Refs = new()
        {
            [DealsService.ListingOwnerRef] = Seller.ToString(),
            [DealShipping.OrderRef] = DealId.ToString("N"),
            [DealShipping.OtoIdRef] = OurOtoId,
        },
        Attributes = new() { [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery },
    };

    private static Deal With(Action<Deal> change)
    {
        var d = Shipping();
        change(d);
        return d;
    }

    private static OtoOrderState State(string status = "delivered", string? orderId = null, string? otoId = OurOtoId)
        => new(orderId ?? DealId.ToString("N"), otoId, status, Label, TrackingUrl);

    private static string? Decided(Deal deal, OtoOrderState? state = null)
        => OtoOrderStatusGate.Decide(deal, state ?? State())?.Code;

    // ═══ ١. البَوّابَة ═══════════════════════════════════════════════════

    /// <summary><b>أَربَعَةَ عَشَرَ مُنذُ ‏2026-10-12</b> — كانَ <c>The_lexicon_is_ten_closed_codes_…</c>: رُموزُ التَوقيعِ الأَربَعَةُ أُضيفَت
    /// بَعدَ العَشَرَةِ بِلا مَساسٍ بِها (تَيّارُ S8، ‏ADR-033 الخُطوَة 7).</summary>
    [Fact]
    public void The_lexicon_is_fourteen_closed_codes_two_shared_by_value_and_the_success_is_outside_it()
    {
        Assert.Equal(14, OtoOrderStatusGate.All.Count);
        Assert.Equal(14, OtoOrderStatusGate.All.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(new[] { OtoOrderStatusGate.NoticeUnverifiable, OtoOrderStatusGate.SignatureMissing,
                             OtoOrderStatusGate.SignatureStale, OtoOrderStatusGate.SignatureMismatch },
            OtoOrderStatusGate.All.Skip(10));
        Assert.Equal(DealShipAuthorization.CarrierUnavailable, OtoOrderStatusGate.CarrierUnavailable);
        Assert.Equal(DealAcceptAuthorization.DealNotActive, OtoOrderStatusGate.DealNotActive);
        Assert.DoesNotContain(OtoOrderStatusGate.Delivered, OtoOrderStatusGate.All);
        Assert.DoesNotContain(DealsService.ChangedMeanwhile, OtoOrderStatusGate.All);
    }

    [Fact]
    public void Carrier_unavailable_positive_and_negative()
    {
        var valid = NoticeJson(DealId.ToString("N"));
        Assert.Equal(OtoOrderStatusGate.CarrierUnavailable, OtoOrderStatusGate.Admit(false, valid).Violation!.Code);
        Assert.Null(OtoOrderStatusGate.Admit(false, valid).Notice);
        Assert.Null(OtoOrderStatusGate.Admit(true, valid).Violation);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"orderId\":\"\"}")]
    [InlineData("{\"orderId\":null}")]
    [InlineData("{\"orderId\":{}}")]
    public void Notice_unreadable_positive(string? body)
        => Assert.Equal(OtoOrderStatusGate.NoticeUnreadable, OtoOrderStatusGate.Admit(true, body).Violation!.Code);

    [Theory]
    [InlineData("{\"orderId\":\"1234\",\"status\":\"delivered\"}", "1234")]
    [InlineData("{\"orderId\":1234}", "1234")]
    [InlineData("{\"orderId\":\" OID-23331-9749 \",\"otoId\":17234521}", "OID-23331-9749")]
    public void Notice_unreadable_negative_reads_the_order_id_as_text_or_number(string body, string orderId)
    {
        var admitted = OtoOrderStatusGate.Admit(true, body);
        Assert.Null(admitted.Violation);
        Assert.Equal(orderId, admitted.Notice!.OrderId);
    }

    [Fact]
    public void The_notice_points_at_a_deal_only_through_our_N_format_id()
    {
        Assert.Equal(DealId, Notice(DealId).DealId);
        Assert.Equal("17234521", OtoOrderStatusGate.Admit(true, "{\"orderId\":\"x\",\"otoId\":17234521}").Notice!.OtoId);
        Assert.Null(OtoOrderStatusGate.Admit(true, NoticeJson(DealId.ToString("D"))).Notice!.DealId);
        Assert.Null(OtoOrderStatusGate.Admit(true, NoticeJson("OID-23331-9749")).Notice!.DealId);
    }

    [Fact]
    public void Order_unknown_positive_and_negative()
    {
        var notice = Notice(DealId);
        Assert.Equal(OtoOrderStatusGate.OrderUnknown, OtoOrderStatusGate.Match(notice, null)!.Code);
        Assert.Equal(OtoOrderStatusGate.OrderUnknown, OtoOrderStatusGate.Match(notice, With(d => d.Refs.Remove(DealShipping.OrderRef)))!.Code);
        Assert.Equal(OtoOrderStatusGate.OrderUnknown,
            OtoOrderStatusGate.Match(notice, With(d => d.Refs[DealShipping.OrderRef] = Guid.NewGuid().ToString("N")))!.Code);
        Assert.Null(OtoOrderStatusGate.Match(notice, Shipping()));
    }

    [Fact]
    public void Order_mismatch_positive_and_negative()
    {
        Assert.Equal(OtoOrderStatusGate.OrderMismatch, OtoOrderStatusGate.Match(Notice(DealId, otoId: "999"), Shipping())!.Code);
        Assert.Equal(OtoOrderStatusGate.OrderMismatch, Decided(Shipping(), State(otoId: "30514281")));
        Assert.Equal(OtoOrderStatusGate.OrderMismatch, Decided(Shipping(), State(orderId: "OID-23331-9749")));
        // لا يُقارَنُ ما لا يَحمِلُه الطَرَفان.
        Assert.Null(OtoOrderStatusGate.Match(Notice(DealId, otoId: null), Shipping()));
        Assert.Null(OtoOrderStatusGate.Match(Notice(DealId, otoId: "999"), With(d => d.Refs.Remove(DealShipping.OtoIdRef))));
        Assert.Null(Decided(With(d => d.Refs.Remove(DealShipping.OtoIdRef)), State(otoId: "30514281")));
    }

    [Theory]
    [InlineData("outForDelivery", true)]
    [InlineData("searchingDriver", true)]
    [InlineData("Delivered", true)]        // كَلِمَةُ الوَثيقَةِ «delivered» بِحَرفِها (‏OtoClientTests)
    [InlineData("delivered", false)]
    public void Not_delivered_positive_and_negative(string status, bool refused)
        => Assert.Equal(refused ? OtoOrderStatusGate.NotDelivered : null, Decided(Shipping(), State(status)));

    [Fact]
    public void Already_delivered_positive_and_negative()
    {
        Assert.Equal(OtoOrderStatusGate.AlreadyDelivered, Decided(With(d => d.Stage = DealStage.Delivered)));
        Assert.Equal(OtoOrderStatusGate.AlreadyDelivered,
            Decided(With(d => { d.Stage = DealStage.Reviewed; d.Status = DealStatus.Completed; })));
        Assert.Null(Decided(Shipping()));
        Assert.True(OtoOrderStatusGate.Reached(With(d => d.Stage = DealStage.Reviewed), DealStage.Delivered));
        Assert.False(OtoOrderStatusGate.Reached(Shipping(), DealStage.Delivered));
    }

    [Fact]
    public void Deal_not_active_positive_and_negative()
    {
        Assert.Equal(OtoOrderStatusGate.DealNotActive, Decided(With(d => d.Status = DealStatus.Cancelled)));
        Assert.Equal(OtoOrderStatusGate.DealNotActive, Decided(With(d => d.Status = DealStatus.Disputed)));
        Assert.Null(Decided(With(d => d.Status = DealStatus.Active)));
    }

    [Fact]
    public void Deal_not_shipping_positive_and_negative()
    {
        Assert.Equal(OtoOrderStatusGate.DealNotShipping, Decided(With(d => d.Stage = DealStage.Confirmed)));
        Assert.Equal(OtoOrderStatusGate.DealNotShipping, Decided(With(d => d.Stage = DealStage.Paid)));
        Assert.Null(Decided(With(d => d.Stage = DealStage.Shipping)));
    }

    [Fact]
    public void Actor_cannot_deliver_positive_and_negative()
    {
        Assert.Equal(OtoOrderStatusGate.ActorCannotDeliver, Decided(With(d => d.CounterpartyId = null)));
        Assert.Null(Decided(With(d => d.CounterpartyId = Seller)));
    }

    /// <summary>التَرتيب: المُعَرِّفانِ أَوَّلاً (جَوابٌ لِطَلَبٍ آخَرَ لا يُقرَأُ حالُه)، ثُمَّ «سُلِّمَ؟» (إشعارُ حالٍ آخَرَ عَلى
    /// صَفقَةٍ مُلغاةٍ لَيسَ شَأنَ الإلغاء)، ثُمَّ «سَبَق؟» قَبلَ الحالَة (صَفقَةٌ أُكمِلَت بَعدَ التَسليمِ يُجابُ مُكَرَّرُها «سَبَق»).</summary>
    [Fact]
    public void The_decision_answers_identity_then_status_then_precedence_then_state()
    {
        var mixed = With(d => { d.Status = DealStatus.Cancelled; d.Stage = DealStage.Confirmed; d.CounterpartyId = null; });
        Assert.Equal(OtoOrderStatusGate.OrderMismatch, Decided(mixed, State("outForDelivery", otoId: "30514281")));
        Assert.Equal(OtoOrderStatusGate.NotDelivered, Decided(mixed, State("outForDelivery")));
        Assert.Equal(OtoOrderStatusGate.DealNotActive, Decided(mixed));
        Assert.Equal(OtoOrderStatusGate.AlreadyDelivered,
            Decided(With(d => { d.Stage = DealStage.Reviewed; d.Status = DealStatus.Completed; d.CounterpartyId = null; })));
    }

    [Theory]
    [InlineData(OtoOrderStatusGate.CarrierUnavailable, 404)]
    [InlineData(OtoOrderStatusGate.NoticeUnreadable, 400)]
    [InlineData(OtoOrderStatusGate.StateUnconfirmed, 503)]
    [InlineData(DealsService.ChangedMeanwhile, 503)]
    [InlineData(OtoOrderStatusGate.Delivered, 200)]
    [InlineData(OtoOrderStatusGate.OrderUnknown, 200)]
    [InlineData(OtoOrderStatusGate.OrderMismatch, 200)]
    [InlineData(OtoOrderStatusGate.NotDelivered, 200)]
    [InlineData(OtoOrderStatusGate.AlreadyDelivered, 200)]
    [InlineData(OtoOrderStatusGate.DealNotActive, 200)]
    [InlineData(OtoOrderStatusGate.DealNotShipping, 200)]
    [InlineData(OtoOrderStatusGate.ActorCannotDeliver, 200)]
    [InlineData(OtoOrderStatusGate.NoticeUnverifiable, 401)]
    [InlineData(OtoOrderStatusGate.SignatureMissing, 401)]
    [InlineData(OtoOrderStatusGate.SignatureStale, 401)]
    [InlineData(OtoOrderStatusGate.SignatureMismatch, 401)]
    public void Each_code_has_one_answer(string code, int status)
        => Assert.Equal(status, OtoOrderStatusGate.StatusFor(code));

    [Fact]
    public void Every_lexicon_code_has_an_answer_and_a_code_outside_it_is_loud()
    {
        Assert.All(OtoOrderStatusGate.All, c => Assert.InRange(OtoOrderStatusGate.StatusFor(c), 200, 503));
        Assert.Throws<ArgumentOutOfRangeException>(() => OtoOrderStatusGate.StatusFor("oto_refused"));
    }

    // ═══ ١‑أ. التَوقيع — بِسِرِّ بائِعِ الصَفقَة، قَبلَ سُؤالِ OTO (تَيّارُ S8، ‏2026-10-12؛ ‏ADR-033 الخُطوَة 7 و§٧‑م) ═══════════
    //
    // ‏`Verify` نَقِيَّة: السِرُّ والإشعارُ والساعَةُ مُدخَلاتُها. والتَرتيبُ تَرتيبُ `PaddleWebhookGuard.Gate`: لا سِرّ ← لا تَوقيع ←
    // خارِجَ النافِذَة ← لا يُطابِق. لِكُلِّ رَمزٍ مُوجَبٌ وسالِب (القاعِدَة ٤).

    private const string Secret = DealShipTests.WebhookSecret;
    private static DateTimeOffset Now => DealShipTests.Now;

    /// <summary>إشعارٌ بِجِسمٍ مَكتوبٍ يَدَوِيّاً — لِحُقولٍ يُسقِطُها أَو يُفسِدُها الصَفّ.</summary>
    private static OtoOrderStatusNotice Raw(Dictionary<string, object?> body)
        => OtoOrderStatusGate.Admit(true, JsonSerializer.Serialize(body)).Notice!;

    [Fact]
    public void Notice_unverifiable_positive_and_negative()
    {
        Assert.Equal(OtoOrderStatusGate.NoticeUnverifiable, OtoOrderStatusGate.Verify(Notice(DealId), null, Now)!.Code);
        Assert.Equal(OtoOrderStatusGate.NoticeUnverifiable, OtoOrderStatusGate.Verify(Notice(DealId), "", Now)!.Code);
        Assert.Null(OtoOrderStatusGate.Verify(Notice(DealId), Secret, Now));
    }

    [Fact]
    public void Signature_missing_positive_and_negative()
    {
        var id = DealId.ToString("N");
        Assert.Equal(OtoOrderStatusGate.SignatureMissing, OtoOrderStatusGate.Verify(Notice(DealId, secret: null), Secret, Now)!.Code);
        Assert.Equal(OtoOrderStatusGate.SignatureMissing, OtoOrderStatusGate.Verify(Raw(new()
        {
            ["orderId"] = id, ["status"] = "delivered", ["timestamp"] = DocTimestamp, ["signature"] = "  ",
        }), Secret, Now)!.Code);
        // خَتمٌ لا يُقرَأُ رَقَماً — ولَو وَقَّعَه المُرسِلُ بِحَرفِه.
        foreach (var stamp in new[] { "yesterday", "-1595941360328", "1595941360328.5" })
            Assert.Equal(OtoOrderStatusGate.SignatureMissing, OtoOrderStatusGate.Verify(Raw(new()
            {
                ["orderId"] = id, ["status"] = "delivered", ["timestamp"] = stamp,
                ["signature"] = OtoWebhookSignature.Compute(Secret, id, "delivered", stamp),
            }), Secret, Now)!.Code);
        Assert.Equal(OtoOrderStatusGate.SignatureMissing, OtoOrderStatusGate.Verify(Raw(new()
        {
            ["orderId"] = id, ["status"] = "delivered",
            ["signature"] = OtoWebhookSignature.Compute(Secret, id, "delivered", DocTimestamp),
        }), Secret, Now)!.Code);
        Assert.Equal(OtoOrderStatusGate.SignatureMissing, OtoOrderStatusGate.Verify(Raw(new()
        {
            ["orderId"] = id, ["timestamp"] = DocTimestamp, ["signature"] = OtoWebhookSignature.Compute(Secret, id, "", DocTimestamp),
        }), Secret, Now)!.Code);
        // والخَتمُ رَقَماً في الجِسمِ يُقرَأُ كَما يُقرَأُ نَصّاً.
        Assert.Null(OtoOrderStatusGate.Verify(Raw(new()
        {
            ["orderId"] = id, ["status"] = "delivered", ["timestamp"] = 1595941360328L,
            ["signature"] = OtoWebhookSignature.Compute(Secret, id, "delivered", DocTimestamp),
        }), Secret, Now));
    }

    /// <summary>النافِذَةُ خَمسُ ثَوانٍ بِطَرَفَيها، قَبلُ وبَعدُ: ‏5000 مِلّي ثانِيَةٍ داخِلَها و‏5001 خارِجَها — بِتَوقيعٍ صَحيحٍ في كُلِّ صَفّ.
    /// <para>(<b>قُلِبَ بِاسمِه ‏2026-10-12 — مُراجَعَةُ تَيّارِ S8، الجَولَة 1؛ ‏ADR-033 §٧‑ع</b> — كانَ <c>Signature_stale_positive_and_negative</c>:
    /// <c>offsetMs</c> = الخَتمُ ناقِصاً الساعَة. المُوجَبُ خَتمٌ يَسبِقُها بِأَكثَرَ مِن ‏5000 (‏5001، وعَشرُ دَقائِق)؛ والسالِبُ كُلُّ خَتمٍ مَضى
    /// مَهما طالَ عُمرُه — ‏−5001 و‏−600000 كانا مُوجَبَين وصارا سالِبَين، ومَعَهُما يَومٌ كامِل.)</para></summary>
    [Theory]
    [InlineData(5001, true)]
    [InlineData(600_000, true)]
    [InlineData(-5001, false)]
    [InlineData(-600_000, false)]
    [InlineData(-86_400_000, false)]
    [InlineData(-5000, false)]
    [InlineData(5000, false)]
    [InlineData(0, false)]
    public void Signature_stale_is_a_stamp_ahead_of_the_clock_positive_and_negative(long offsetMs, bool stale)
    {
        var stamp = (Now.ToUnixTimeMilliseconds() + offsetMs).ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(stale ? OtoOrderStatusGate.SignatureStale : null,
            OtoOrderStatusGate.Verify(Notice(DealId, timestamp: stamp), Secret, Now)?.Code);
    }

    [Fact]
    public void Signature_mismatch_positive_and_negative()
    {
        var id = DealId.ToString("N");
        Assert.Equal(OtoOrderStatusGate.SignatureMismatch, OtoOrderStatusGate.Verify(Notice(DealId), "another-secret", Now)!.Code);
        Assert.Equal(OtoOrderStatusGate.SignatureMismatch, OtoOrderStatusGate.Verify(Notice(DealId, secret: "another-secret"), Secret, Now)!.Code);
        // كُلُّ جُزءٍ مِن النَصِّ المُوَقَّعِ يُغَيِّرُ البَصمَة: الحالُ، والمُعَرِّفُ، والخَتم.
        var signed = OtoWebhookSignature.Compute(Secret, id, "outForDelivery", DocTimestamp);
        Assert.Equal(OtoOrderStatusGate.SignatureMismatch, OtoOrderStatusGate.Verify(Raw(new()
            { ["orderId"] = id, ["status"] = "delivered", ["timestamp"] = DocTimestamp, ["signature"] = signed }), Secret, Now)!.Code);
        Assert.Equal(OtoOrderStatusGate.SignatureMismatch, OtoOrderStatusGate.Verify(Raw(new()
        {
            ["orderId"] = Guid.NewGuid().ToString("N"), ["status"] = "delivered", ["timestamp"] = DocTimestamp,
            ["signature"] = OtoWebhookSignature.Compute(Secret, id, "delivered", DocTimestamp),
        }), Secret, Now)!.Code);
        Assert.Equal(OtoOrderStatusGate.SignatureMismatch, OtoOrderStatusGate.Verify(Raw(new()
        {
            ["orderId"] = id, ["status"] = "delivered", ["timestamp"] = "1595941360329",
            ["signature"] = OtoWebhookSignature.Compute(Secret, id, "delivered", DocTimestamp),
        }), Secret, Now)!.Code);
        Assert.Equal(OtoOrderStatusGate.SignatureMismatch, OtoOrderStatusGate.Verify(Raw(new()
            { ["orderId"] = id, ["status"] = "delivered", ["timestamp"] = DocTimestamp, ["signature"] = "not base64 !" }), Secret, Now)!.Code);
        Assert.Null(OtoOrderStatusGate.Verify(Notice(DealId), Secret, Now));
    }

    /// <summary>التَرتيب: السِرُّ أَوَّلاً (لا يُقرَأُ إشعارٌ لا يُتَحَقَّقُ مِنه)، ثُمَّ الحُضور، ثُمَّ النافِذَةُ قَبلَ المُطابَقَة — فَإعادَةُ لَعِبٍ
    /// قَديمَةٌ بِتَوقيعٍ صَحيحٍ تُجابُ «خارِجَ النافِذَة» لا «يُطابِق».
    /// <para>(<b>يُلحَقُ ‏2026-10-12 — مُراجَعَةُ تَيّارِ S8، الجَولَة 1؛ ‏ADR-033 §٧‑ع؛ ولا يُعادُ حَرفٌ فَوقَه</b>: «إعادَةُ لَعِبٍ قَديمَةٌ» لا تُرَدُّ بَعدَ
    /// اليَومِ عَلى عُمرِها — تُطابِقُ وتُسأَلُ OTO — فَخَتمُ هذا الصَفِّ صارَ يَسبِقُ الساعَةَ بِدَقيقَةٍ بَعدَ أَن كانَ قَبلَها بِدَقيقَة؛ والتَرتيبُ بِحَرفِه.)</para></summary>
    [Fact]
    public void The_verification_answers_secret_then_presence_then_window_then_match()
    {
        var stale = (Now.ToUnixTimeMilliseconds() + 60_000).ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(OtoOrderStatusGate.NoticeUnverifiable,
            OtoOrderStatusGate.Verify(Notice(DealId, secret: null, timestamp: stale), null, Now)!.Code);
        Assert.Equal(OtoOrderStatusGate.SignatureMissing,
            OtoOrderStatusGate.Verify(Notice(DealId, secret: null, timestamp: stale), Secret, Now)!.Code);
        Assert.Equal(OtoOrderStatusGate.SignatureStale,
            OtoOrderStatusGate.Verify(Notice(DealId, secret: "another-secret", timestamp: stale), Secret, Now)!.Code);
        Assert.Equal(OtoOrderStatusGate.SignatureStale,
            OtoOrderStatusGate.Verify(Notice(DealId, timestamp: stale), Secret, Now)!.Code);
    }

    // ═══ ٢. الخِدمَة — عالَمُ الوَثائِقِ ووَثيقَةُ OTO ═══════════════════════════════

    /// <summary>صَفقَةُ نَقدٍ شُحِنَت بِنُقطَةِ الشَحنِ نَفسِها — عِندَ <c>Shipping</c> بِمَراجِعِها الأَربَعَة.</summary>
    private static async Task<DealShipTests.World> ShippedCashDeal()
    {
        var w = await DealShipTests.ConfirmedCashDeal();
        var shipped = await w.Shipping.ShipAsync(Slug, w.Id, DealShipTests.Vendor, "Riyadh");
        Assert.True(shipped.Ok, shipped.Code);
        Assert.Equal(DealStage.Shipping, shipped.Deal!.Stage);
        return w;
    }

    private static void OtoSays(DealShipTests.World w, string status, string? otoId = OurOtoId, string trackingUrl = TrackingUrl)
        => w.Oto.Routes[OtoClient.OrderStatusPath] = body =>
            OtoRouteHandler.Tracking(OtoRouteHandler.OrderIdIn(body), otoId ?? "", status, trackingUrl);

    private static DealShipping ShippingIn(DealShipTests.World w, string environment)
    {
        var options = Options.Create(new OtoOptions { Environment = environment, RefreshToken = "RT-theme-demo" });
        return new DealShipping(w.Deals,
            new OtoClient(new HttpClient(w.Oto), options, new OtoTokenCache(), NullLogger<OtoClient>.Instance), options);
    }

    private static Deal Stored(DealShipTests.World w) => w.Docs.Read<Deal>(w.Id.ToString())!;

    [Fact]
    public async Task A_delivered_notice_is_confirmed_at_OTO_before_any_write_then_Shipping_becomes_Delivered_by_the_vendor()
    {
        var w = await ShippedCashDeal();
        OtoSays(w, "delivered");
        var saves = w.Docs.SaveCalls;
        int? savesWhenAsked = null;
        w.Oto.Before = path => { if (path == OtoClient.OrderStatusPath) savesWhenAsked ??= w.Docs.SaveCalls; };
        var asked = w.Oto.CallsTo(OtoClient.OrderStatusPath);

        var outcome = await w.Shipping.ReceiveAsync(Slug, Notice(w.Id));

        Assert.Equal(OtoOrderStatusGate.Delivered, outcome.Code);
        Assert.Equal(saves, savesWhenAsked);                                  // لا كِتابَةَ قَبلَ سُؤالِ OTO
        Assert.Equal(asked + 1, w.Oto.CallsTo(OtoClient.OrderStatusPath));
        Assert.Equal(w.Id.ToString("N"), OtoRouteHandler.OrderIdIn(w.Oto.Calls.Last(c => c.Path == OtoClient.OrderStatusPath).Body));
        var deal = Stored(w);
        Assert.Equal(DealStage.Delivered, deal.Stage);
        var delivered = Assert.Single(deal.Timeline, e => e.Action == "advanced" && e.StageAfter == DealStage.Delivered);
        Assert.Equal(Seller, delivered.ActorId);
        Assert.Equal(DealStage.Shipping, delivered.StageBefore);
        Assert.Single(deal.Timeline, e => e.Action == DealEvent.CashOnDelivery);
    }

    [Fact]
    public async Task The_same_delivered_notice_twice_writes_once()
    {
        var w = await ShippedCashDeal();
        OtoSays(w, "delivered");
        Assert.Equal(OtoOrderStatusGate.Delivered, (await w.Shipping.ReceiveAsync(Slug, Notice(w.Id))).Code);
        var saves = w.Docs.SaveCalls;

        var again = await w.Shipping.ReceiveAsync(Slug, Notice(w.Id));

        Assert.Equal(OtoOrderStatusGate.AlreadyDelivered, again.Code);
        Assert.Equal(saves, w.Docs.SaveCalls);
        Assert.Single(Stored(w).Timeline, e => e.Action == "advanced" && e.StageAfter == DealStage.Delivered);
    }

    /// <summary><b>الإشعارُ وَحدَه لا يُصَدَّق</b>: يَقولُ «delivered» وOTO تُجيبُ «searchingDriver» — لا انتِقال.</summary>
    [Fact]
    public async Task A_notice_saying_delivered_is_not_believed_when_OTO_answers_otherwise()
    {
        var w = await ShippedCashDeal();
        var saves = w.Docs.SaveCalls;

        var outcome = await w.Shipping.ReceiveAsync(Slug, Notice(w.Id, status: "delivered"));

        Assert.Equal(OtoOrderStatusGate.NotDelivered, outcome.Code);
        Assert.Equal(DealStage.Shipping, Stored(w).Stage);
        Assert.Equal(saves, w.Docs.SaveCalls);                               // الرابِطانِ لَم يَتَغَيَّرا
    }

    [Fact]
    public async Task A_status_that_is_not_delivered_moves_nothing_and_refreshes_a_changed_tracking_link()
    {
        var w = await ShippedCashDeal();
        OtoSays(w, "outForDelivery", trackingUrl: "https://app.tryoto.com/otoflex-tracking?enc=z");

        var outcome = await w.Shipping.ReceiveAsync(Slug, Notice(w.Id, status: "outForDelivery"));

        Assert.Equal(OtoOrderStatusGate.NotDelivered, outcome.Code);
        Assert.Equal(DealStage.Shipping, Stored(w).Stage);
        Assert.Equal("https://app.tryoto.com/otoflex-tracking?enc=z", Stored(w).Refs[DealShipping.TrackingRef]);
    }

    [Fact]
    public async Task When_OTO_does_not_answer_nothing_is_written_and_the_answer_asks_for_a_retry()
    {
        var w = await ShippedCashDeal();
        w.Oto.Routes[OtoClient.OrderStatusPath] = _ => OtoRouteHandler.Json(HttpStatusCode.InternalServerError, "{}");
        var saves = w.Docs.SaveCalls;

        var outcome = await w.Shipping.ReceiveAsync(Slug, Notice(w.Id));

        Assert.Equal(OtoOrderStatusGate.StateUnconfirmed, outcome.Code);
        Assert.Equal(OtoFailures.Unreachable, outcome.OtoFailure);
        Assert.Equal(503, OtoOrderStatusGate.StatusFor(outcome.Code));
        Assert.Equal(saves, w.Docs.SaveCalls);
        Assert.Equal(DealStage.Shipping, Stored(w).Stage);
    }

    /// <summary>حِسابُ البائِعِ عِندَ OTO يَحمِلُ طَلَباتِ قَنَواتِه الأُخرى (مِثالُ الوَثيقَة <c>OID-23331-9749</c>)، وصَفقَةٌ لا
    /// وُجودَ لَها — يُتَجاهَلانِ بِلا سُؤالِ OTO ولا كِتابَة.</summary>
    [Theory]
    [InlineData("OID-23331-9749")]
    [InlineData("0f8fad5bd9cb469fa165705ed7eb6e2a")]
    public async Task A_notice_for_an_order_that_is_not_one_of_this_stores_deals_is_ignored_without_asking_OTO(string orderId)
    {
        var w = await ShippedCashDeal();
        var calls = w.Oto.Calls.Count;
        var saves = w.Docs.SaveCalls;

        var outcome = await w.Shipping.ReceiveAsync(Slug, OtoOrderStatusGate.Admit(true, NoticeJson(orderId)).Notice!);

        Assert.Equal(OtoOrderStatusGate.OrderUnknown, outcome.Code);
        Assert.Equal(calls, w.Oto.Calls.Count);
        Assert.Equal(saves, w.Docs.SaveCalls);
    }

    [Fact]
    public async Task A_deal_with_no_OTO_order_is_unknown_to_the_notice()
    {
        var w = await DealShipTests.ConfirmedCashDeal();
        var outcome = await w.Shipping.ReceiveAsync(Slug, Notice(w.Id));
        Assert.Equal(OtoOrderStatusGate.OrderUnknown, outcome.Code);
        Assert.Empty(w.Oto.Calls);
    }

    [Fact]
    public async Task Ids_that_disagree_with_the_deal_write_nothing_whether_in_the_notice_or_in_OTOs_answer()
    {
        var w = await ShippedCashDeal();
        var calls = w.Oto.Calls.Count;
        var saves = w.Docs.SaveCalls;

        Assert.Equal(OtoOrderStatusGate.OrderMismatch, (await w.Shipping.ReceiveAsync(Slug, Notice(w.Id, otoId: "999"))).Code);
        Assert.Equal(calls, w.Oto.Calls.Count);

        OtoSays(w, "delivered", otoId: "30514281");
        Assert.Equal(OtoOrderStatusGate.OrderMismatch, (await w.Shipping.ReceiveAsync(Slug, Notice(w.Id))).Code);
        Assert.Equal(saves, w.Docs.SaveCalls);
        Assert.Equal(DealStage.Shipping, Stored(w).Stage);
    }

    [Fact]
    public async Task A_disputed_deal_is_not_moved_by_a_delivery()
    {
        var w = await ShippedCashDeal();
        OtoSays(w, "delivered");
        Stored(w).Status = DealStatus.Disputed;

        var outcome = await w.Shipping.ReceiveAsync(Slug, Notice(w.Id));

        Assert.Equal(OtoOrderStatusGate.DealNotActive, outcome.Code);
        Assert.Equal(DealStage.Shipping, Stored(w).Stage);
        Assert.DoesNotContain(Stored(w).Timeline, e => e.StageAfter == DealStage.Delivered);
    }

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (‏2026-10-24، تَدقيقُ الإطلاق lens-money-integrity-4؛ كانَ <c>Today_a_delivered_notice_on_a_cancelled_deal_is_answered_200_and_writes_nothing</c>،
    /// مُودَعٌ في <c>818ae9e</c>): صَفقَةٌ أُلغِيَت وطَلَبُها عِندَ OTO حَيّ ثُمَّ «delivered» — يُكتَبُ سَطرُ <c>oto_delivered_after_cancel</c> مَرَّةً بِأُنبوبِ الإلحاق
    /// (تَسمِيَتُه لِلطَرَفَينِ مِن القامُوس)، والجَوابُ ‏200 <c>deal_not_active</c> كَما كان فَلا تُعيدُه OTO؛ وإعادَتُه لا تَكتُبُ ثانِيَة، والصَفقَةُ مُلغاةٌ لا تَتَحَرَّك.</para>
    /// </summary>
    [Fact]
    public async Task A_delivered_notice_on_a_cancelled_deal_is_written_once_as_a_timeline_line_and_answered_200()
    {
        var w = await ShippedCashDeal();
        OtoSays(w, "delivered");
        Stored(w).Status = DealStatus.Cancelled;
        var saves = w.Docs.SaveCalls;
        var lines = Stored(w).Timeline.Count;

        var outcome = await w.Shipping.ReceiveAsync(Slug, Notice(w.Id));

        Assert.Equal(OtoOrderStatusGate.DealNotActive, outcome.Code);
        Assert.Equal(200, OtoOrderStatusGate.StatusFor(outcome.Code));
        Assert.Equal(saves + 1, w.Docs.SaveCalls);
        Assert.Equal(lines + 1, Stored(w).Timeline.Count);
        var line = Stored(w).Timeline.Last();
        Assert.Equal(DealEvent.RefNote, line.Action);
        Assert.Equal("deals.detail.ref_oto_delivered_after_cancel", DealRefLabels.LabelKeyFor(line.Note));
        Assert.Equal("delivered", Stored(w).Refs[DealShipping.DeliveredAfterCancelRef]);
        Assert.Equal(DealStatus.Cancelled, Stored(w).Status);
        Assert.Equal(DealStage.Shipping, Stored(w).Stage);

        var again = await w.Shipping.ReceiveAsync(Slug, Notice(w.Id));
        Assert.Equal(OtoOrderStatusGate.DealNotActive, again.Code);
        Assert.Equal(saves + 1, w.Docs.SaveCalls);
        output.WriteLine($"cancelled + delivered: {outcome.Code} → {OtoOrderStatusGate.StatusFor(outcome.Code)} · saves +1 · lines +1 · again +0");
    }

    /// <summary>الدالَّةُ النَقِيَّة — مُوجِبٌ وسالِب: مُلغاةٌ بِلا السَطرِ قَبلَه ورَدُّ <c>deal_not_active</c> ⇐ نَعَم؛ والنِزاعُ، والسَطرُ المَكتوبُ قَبلُ،
    /// ورَمزٌ آخَرُ ⇐ لا.</summary>
    [Fact]
    public void DeliveredAfterCancel_positive_and_negative()
    {
        var notActive = new OtoNoticeViolation(OtoOrderStatusGate.DealNotActive, "x");
        Assert.True(OtoOrderStatusGate.DeliveredAfterCancel(With(d => d.Status = DealStatus.Cancelled), notActive));
        Assert.False(OtoOrderStatusGate.DeliveredAfterCancel(With(d => d.Status = DealStatus.Disputed), notActive));
        Assert.False(OtoOrderStatusGate.DeliveredAfterCancel(With(d =>
        {
            d.Status = DealStatus.Cancelled;
            d.Refs[DealShipping.DeliveredAfterCancelRef] = "delivered";
        }), notActive));
        Assert.False(OtoOrderStatusGate.DeliveredAfterCancel(With(d => d.Status = DealStatus.Cancelled),
            new OtoNoticeViolation(OtoOrderStatusGate.AlreadyDelivered, "x")));
    }

    /// <summary>صَفقَةٌ لَها طَلَبٌ ولَم تَخرُج إلى الشَحن (رُدَّ انتِقالُها) — التَسليمُ لا يَقفِزُ فَوقَ الشَحن.
    /// <para><b>قُلِبَ بِاسمِه</b> — كانَ <c>A_delivery_for_a_deal_still_at_Confirmed_does_not_jump_over_Shipping</c> (‏<c>e96c482</c>): «delivered» بِبوليصَةٍ
    /// عَلى صَفقَةٍ عِندَ «تأكيد» بِطَلَبِها يُجابُ <c>deal_not_shipping</c> فَلا تُعيدُه OTO ويَضيعُ التَسليمُ الآلِيّ. <b>والآن</b> (مُراجَعَةُ المَوجَة 3a،
    /// تَيّارُ S8، ‏2026-10-12؛ ‏ADR-033 §٧‑ط): الجَوابُ يُثبِتُ الشُحنَةَ والبوليصَة فَتَدخُلُ «شَحن» ثُمَّ «تَسليم» — بِالبائِعِ في الاثنَين، ولا قَفزَ:
    /// سَطرا انتِقالٍ مُتَتالِيان.</para></summary>
    [Fact]
    public async Task A_delivery_for_a_deal_still_at_Confirmed_passes_through_Shipping_when_OTO_confirms_the_shipment()
    {
        var w = await DealShipTests.ConfirmedCashDeal();
        await w.Deals.AttachRefAsync(Slug, w.Id, DealShipping.OrderRef, w.Id.ToString("N"));
        await w.Deals.AttachRefAsync(Slug, w.Id, DealShipping.OtoIdRef, OurOtoId);
        OtoSays(w, "delivered");

        var outcome = await w.Shipping.ReceiveAsync(Slug, Notice(w.Id));

        Assert.Equal(OtoOrderStatusGate.Delivered, outcome.Code);
        var deal = Stored(w);
        Assert.Equal(DealStage.Delivered, deal.Stage);
        var moves = deal.Timeline.Where(e => e.Action == "advanced").Select(e => (e.StageBefore, e.StageAfter, e.ActorId)).ToArray();
        Assert.Contains((DealStage.Confirmed, DealStage.Shipping, Seller), moves);
        Assert.Contains((DealStage.Shipping, DealStage.Delivered, Seller), moves);
        Assert.Single(deal.Timeline, e => e.Action == DealEvent.CashOnDelivery);
    }

    /// <summary><b>والسالِب</b> (‏2026-10-12، ‏ADR-033 §٧‑ط): جَوابٌ لا يُثبِتُ الشُحنَة — <c>missingData</c> بِلا بوليصَة — لا يُدخِلُ «شَحن»: تَبقى
    /// الصَفقَةُ عِندَ «تأكيد» بِطَلَبِها، ويُجابُ <c>not_delivered</c> ‏200 كَما كان.</summary>
    [Fact]
    public async Task A_notice_whose_answer_does_not_confirm_the_shipment_leaves_the_Confirmed_deal_where_it_is()
    {
        var w = await DealShipTests.ConfirmedCashDeal();
        await w.Deals.AttachRefAsync(Slug, w.Id, DealShipping.OrderRef, w.Id.ToString("N"));
        await w.Deals.AttachRefAsync(Slug, w.Id, DealShipping.OtoIdRef, OurOtoId);
        w.Oto.Routes[OtoClient.OrderStatusPath] = body =>
            OtoRouteHandler.Tracking(OtoRouteHandler.OrderIdIn(body), status: "missingData", label: null);

        var outcome = await w.Shipping.ReceiveAsync(Slug, Notice(w.Id, status: "missingData"));

        Assert.Equal(OtoOrderStatusGate.NotDelivered, outcome.Code);
        Assert.Equal((DealStatus.Active, DealStage.Confirmed), (Stored(w).Status, Stored(w).Stage));
        Assert.DoesNotContain(Stored(w).Timeline, e => e.Action == "advanced" && e.StageAfter == DealStage.Shipping);
    }

    /// <summary>
    /// <para><b>إشعاراتٌ لِصَفقَةٍ رَدَّت عَلَيها <c>/ship</c> بِـ<c>shipment_unconfirmed</c></b> (مُراجَعَةُ تَيّارِ S7 الثانِيَة، ‏2026-10-12؛ ‏ADR-033 §٧‑ج‑٣
    /// و§٧‑ح): كانَ مَقيساً بِمِجَسٍّ لَم يُودَع (القاعِدَة ٢).</para>
    ///
    /// <para><b>قُلِبَ بِاسمِه</b> — كانَ <c>After_shipment_unconfirmed_notices_write_the_links_on_the_Confirmed_deal_and_a_delivery_answers_200_so_it_is_not_resent</c>
    /// (‏<c>dace4bd</c>، ‏<c>71fcd5e</c>): إشعارُ <c>searchingDriver</c> كانَ يُجابُ <c>not_delivered</c> ويَكتُبُ الرابِطَينِ عَلى صَفقَةٍ عِندَ «تأكيد»، ثُمَّ
    /// <c>delivered</c> يُجابُ ‏200 <c>deal_not_shipping</c> فَلا تُعيدُه OTO — ويَضيعُ التَسليمُ الآلِيّ. <b>والآن</b> (مُراجَعَةُ المَوجَة 3a، تَيّارُ S8،
    /// ‏2026-10-12؛ ‏ADR-033 §٧‑ط؛ سِجِلُّ المالِكِ «أ» S8): الجَوابُ المَجلوبُ الَّذي يُثبِتُ الشُحنَةَ والبوليصَةَ يُدخِلُها «شَحن» بِالبائِعِ — كَما تُدخِلُها
    /// «أَكمِل الشَّحن» — فَيُجابُ <c>shipped</c> ‏200، و<c>delivered</c> بَعدَه يُكتَبُ تَسليماً بِسَطرِ القَبضِ النَقديّ.</para>
    /// </summary>
    [Fact]
    public async Task After_shipment_unconfirmed_a_notice_whose_shipment_OTO_confirms_moves_the_deal_into_Shipping_and_the_later_delivery_is_written()
    {
        var w = await DealShipTests.ConfirmedCashDeal();
        w.Oto.Routes[OtoClient.OrderStatusPath] = body =>
            OtoRouteHandler.Tracking(OtoRouteHandler.OrderIdIn(body), status: "missingData", label: null);
        Assert.Equal(DealShipping.ShipmentUnconfirmed, (await w.Shipping.ShipAsync(Slug, w.Id, DealShipTests.Vendor, "Riyadh")).Code);

        OtoSays(w, "searchingDriver");
        var searching = await w.Shipping.ReceiveAsync(Slug, Notice(w.Id, status: "searchingDriver"));
        Assert.Equal(OtoOrderStatusGate.Shipped, searching.Code);
        Assert.Equal(200, OtoOrderStatusGate.StatusFor(searching.Code));
        Assert.Equal((DealStatus.Active, DealStage.Shipping), (Stored(w).Status, Stored(w).Stage));
        Assert.Equal((Label, TrackingUrl), DealShipping.LinksOf(Stored(w)));
        var entered = Assert.Single(Stored(w).Timeline, e => e.Action == "advanced" && e.StageAfter == DealStage.Shipping);
        Assert.Equal((DealStage.Confirmed, Seller), (entered.StageBefore, entered.ActorId));

        OtoSays(w, "delivered");
        var delivered = await w.Shipping.ReceiveAsync(Slug, Notice(w.Id));
        Assert.Equal(OtoOrderStatusGate.Delivered, delivered.Code);
        Assert.Equal(200, OtoOrderStatusGate.StatusFor(delivered.Code));
        Assert.Equal((DealStatus.Active, DealStage.Delivered), (Stored(w).Status, Stored(w).Stage));
        Assert.Single(Stored(w).Timeline, e => e.Action == DealEvent.CashOnDelivery);
    }

    /// <summary><b>البَوّابَةُ النَقِيَّة — مُوجَبٌ وسالِبٌ لِكُلِّ شَرطٍ في <see cref="OtoOrderStatusGate.ShipsFirst"/></b> (‏2026-10-12، ‏ADR-033 §٧‑ط):
    /// صَفقَةٌ عِندَ «تأكيد» بِطَلَبِها وجَوابٌ يُثبِتُ الشُحنَةَ والبوليصَةَ تَدخُلُ «شَحن»؛ وكُلُّ شَرطٍ يُكسَرُ وَحدَه يَرُدُّها.</summary>
    [Fact]
    public void Ships_first_positive_and_negative()
    {
        Deal Confirmed(Action<Deal>? change = null)
            => With(d => { d.Stage = DealStage.Confirmed; change?.Invoke(d); });

        Assert.True(OtoOrderStatusGate.ShipsFirst(Confirmed(), State("searchingDriver")));
        Assert.True(OtoOrderStatusGate.ShipsFirst(Confirmed(), State("delivered")));

        Assert.False(OtoOrderStatusGate.ShipsFirst(Shipping(), State("searchingDriver")));                        // عِندَ «شَحن» أَصلاً
        Assert.False(OtoOrderStatusGate.ShipsFirst(Confirmed(d => d.Refs.Remove(DealShipping.OrderRef)), State())); // لا طَلَبَ عِندَ OTO
        Assert.False(OtoOrderStatusGate.ShipsFirst(Confirmed(), State("missingData")));                           // لا شُحنَة
        Assert.False(OtoOrderStatusGate.ShipsFirst(Confirmed(), new OtoOrderState(DealId.ToString("N"), OurOtoId, "searchingDriver", null, TrackingUrl))); // لا بوليصَة
        Assert.False(OtoOrderStatusGate.ShipsFirst(Confirmed(), State("searchingDriver", otoId: "30514281")));     // مُعَرِّفُ OTO آخَر
        Assert.False(OtoOrderStatusGate.ShipsFirst(Confirmed(), State("searchingDriver", orderId: "OID-23331-9749"))); // طَلَبٌ آخَر
        Assert.False(OtoOrderStatusGate.ShipsFirst(Confirmed(d => d.Status = DealStatus.Cancelled), State()));    // غَيرُ فَعّالَة
        Assert.False(OtoOrderStatusGate.ShipsFirst(Confirmed(d => d.CounterpartyId = null), State()));            // لا بائِعَ يُحَرِّك
    }

    /// <summary>و<see cref="OtoOrderStatusGate.Shipped"/> نَتيجَةٌ تَكتُبُ لا خَرق: خارِجَ المَعجَم، وجَوابُها ‏200 فَلا تُعيدُه OTO.</summary>
    [Fact]
    public void Shipped_is_a_writing_outcome_outside_the_lexicon_answered_200()
    {
        Assert.DoesNotContain(OtoOrderStatusGate.Shipped, OtoOrderStatusGate.All);
        Assert.NotEqual(OtoOrderStatusGate.Delivered, OtoOrderStatusGate.Shipped);
        Assert.Equal(200, OtoOrderStatusGate.StatusFor(OtoOrderStatusGate.Shipped));
    }

    [Fact]
    public async Task A_deal_without_a_counterparty_is_not_moved_by_a_delivery()
    {
        var w = await ShippedCashDeal();
        OtoSays(w, "delivered");
        Stored(w).CounterpartyId = null;

        var outcome = await w.Shipping.ReceiveAsync(Slug, Notice(w.Id));

        Assert.Equal(OtoOrderStatusGate.ActorCannotDeliver, outcome.Code);
        Assert.Equal(DealStage.Shipping, Stored(w).Stage);
    }

    [Theory]
    [InlineData(OtoEnvironment.Production)]
    [InlineData("")]
    public async Task Without_an_open_environment_the_service_answers_carrier_unavailable_and_asks_nobody(string environment)
    {
        var w = await ShippedCashDeal();
        var calls = w.Oto.Calls.Count;

        var outcome = await ShippingIn(w, environment).ReceiveAsync(Slug, Notice(w.Id));

        Assert.Equal(OtoOrderStatusGate.CarrierUnavailable, outcome.Code);
        Assert.Equal(calls, w.Oto.Calls.Count);
    }

    // ─── ٢‑أ. التَوقيعُ في الخِدمَة — بَعدَ المُطابَقَة، قَبلَ سُؤالِ OTO وقَبلَ أَيِّ كِتابَة (تَيّارُ S8، ‏2026-10-12) ─────────────
    //
    // السِرُّ سِرُّ بائِعِ الصَفقَة (مَدخَلُه المَختوم)، فَيُعرَفُ بَعدَ أَن يُطابِقَ الإشعارُ صَفقَةً — وإشعارٌ لِطَلَبٍ لَيسَ مِن صَفَقاتِ
    // المَتجَرِ يُجابُ ‏200 «لا شَأنَ لَنا» كَما كان: حِسابُ البائِعِ عِندَ OTO قَد يَحمِلُ طَلَباتٍ مِن غَيرِ مَتجَرِنا.

    /// <summary>خِدمَةٌ عَلى العالَمِ نَفسِه — مَداخِلُه وخِزانَتُه وساعَتُه — بِلوغٍ يُلتَقَط.
    /// و<paramref name="now"/> (‏2026-10-12، مُراجَعَةُ تَيّارِ S8): ساعَةٌ غَيرُ ساعَةِ العالَم — لِإشعارٍ يَصِلُ بَعدَ حَدَثِه؛ بِلا شَيءٍ هي هي.</summary>
    private static DealShipping Logged(DealShipTests.World w, ShipCapturingLogger log, DateTimeOffset? now = null)
    {
        var options = Options.Create(new OtoOptions { Environment = OtoEnvironment.Staging, RefreshToken = DealShipTests.ConfigToken });
        return new DealShipping(w.Deals,
            new OtoClient(new HttpClient(w.Oto), options, new OtoTokenCache(), NullLogger<OtoClient>.Instance),
            options, log, w.Providers, w.Vault, new FixedClock(now ?? DealShipTests.Now));
    }

    /// <summary>إشعارٌ لا يَتَحَقَّقُ — بِلا تَوقيع، أَو خارِجَ النافِذَة، أَو بِسِرٍّ آخَر — يُجابُ بِرَمزِه ‏401: لا تُسأَلُ OTO، ولا يُكتَبُ
    /// حَرف، وسَطرُ اللوغِ يَحمِلُ الرَمزَ والانحِرافَ ولا يَحمِلُ السِرّ.
    /// <para>(<b>يُلحَقُ ‏2026-10-12 — مُراجَعَةُ تَيّارِ S8، الجَولَة 1؛ ‏ADR-033 §٧‑ع</b>: «خارِجَ النافِذَة» خَتمٌ يَسبِقُ الساعَةَ بِسِتِّ ثَوانٍ بَعدَ أَن كانَ
    /// قَبلَها بِسِتّ — فَذاكَ صارَ يُطابِقُ ويُسأَلُ OTO (صَفُّ «بَعدَ حَدَثِه» أَدناه)؛ و«الانحِرافُ» صارَ «العُمرَ» بِإشارَتِه: <c>age -6000 ms</c>.)</para></summary>
    [Theory]
    [InlineData("unsigned", OtoOrderStatusGate.SignatureMissing)]
    [InlineData("stale", OtoOrderStatusGate.SignatureStale)]
    [InlineData("forged", OtoOrderStatusGate.SignatureMismatch)]
    public async Task A_notice_that_does_not_verify_asks_OTO_nothing_and_writes_nothing(string kind, string code)
    {
        var w = await ShippedCashDeal();
        OtoSays(w, "delivered");
        var calls = w.Oto.Calls.Count;
        var stored = w.Docs.Stored.Count;
        var saves = w.Docs.SaveCalls;
        var log = new ShipCapturingLogger();
        var stale = (DealShipTests.Now.ToUnixTimeMilliseconds() + 6_000).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var notice = kind switch
        {
            "unsigned" => Notice(w.Id, secret: null),
            "stale"    => Notice(w.Id, timestamp: stale),
            _          => Notice(w.Id, secret: "another-secret"),
        };

        var outcome = await Logged(w, log).ReceiveAsync(Slug, notice);

        Assert.Equal(code, outcome.Code);
        Assert.Equal(401, OtoOrderStatusGate.StatusFor(outcome.Code));
        Assert.Equal(calls, w.Oto.Calls.Count);
        Assert.Equal(stored, w.Docs.Stored.Count);
        Assert.Equal(saves, w.Docs.SaveCalls);
        Assert.Equal(DealStage.Shipping, Stored(w).Stage);
        var line = Assert.Single(log.Lines);
        Assert.Contains(code, line, StringComparison.Ordinal);
        Assert.DoesNotContain(DealShipTests.WebhookSecret, line, StringComparison.Ordinal);
        Assert.Contains(kind == "stale" ? "age -6000 ms" : "age 0 ms", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> — كانَ <c>Today_a_correctly_signed_notice_that_arrives_after_its_event_is_refused_stale_before_OTO_is_asked</c>
    /// (‏<c>0549f2e</c>؛ مُراجَعَةُ تَيّارِ S8، الجَولَة 1؛ ‏ADR-033 §٧‑ع): إشعارٌ <b>مُوَقَّعٌ صَحيحاً</b> بِسِرِّ بائِعِ الصَفقَةِ يَصِلُ بَعدَ حَدَثِه بِسِتِّ
    /// ثَوانٍ أَو بِعَشرِ دَقائِقَ <b>يَبلُغُ فاحِصَ الحَقيقَة</b> (‏Order Tracking) — وكانَ يُرَدُّ ‏401 <c>signature_stale</c> قَبلَه — فَتُسأَلُ OTO مَرَّةً
    /// واحِدَة، وتَخرُجُ الصَفقَةُ إلى «تسليم» بِجَوابِها، وسَطرُ اللوغِ الواحِدُ يَحمِلُ عُمرَ الإشعارِ ولا يَحمِلُ السِرّ.</para>
    /// </summary>
    [Theory]
    [InlineData(6_000L)]
    [InlineData(600_000L)]
    public async Task A_correctly_signed_notice_minutes_after_its_event_still_reaches_the_Order_Tracking_fetch(long ageMs)
    {
        var w = await ShippedCashDeal();
        OtoSays(w, "delivered");
        var asked = w.Oto.CallsTo(OtoClient.OrderStatusPath);
        var log = new ShipCapturingLogger();
        var stamp = DealShipTests.Now.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

        // الحَدَثُ عِندَ ساعَةِ العالَم، والإشعارُ يَصِلُ بَعدَه بِـ`ageMs` — بِتَوقيعٍ صَحيحٍ عَلى خَتمِ الحَدَثِ نَفسِه.
        var outcome = await Logged(w, log, DealShipTests.Now.AddMilliseconds(ageMs)).ReceiveAsync(Slug, Notice(w.Id, timestamp: stamp));

        Assert.Equal(OtoOrderStatusGate.Delivered, outcome.Code);
        Assert.Equal(200, OtoOrderStatusGate.StatusFor(outcome.Code));
        Assert.Equal(asked + 1, w.Oto.CallsTo(OtoClient.OrderStatusPath));
        Assert.Equal(DealStage.Delivered, Stored(w).Stage);
        var line = Assert.Single(log.Lines);
        Assert.Contains($"← verified (age {ageMs} ms)", line, StringComparison.Ordinal);
        Assert.DoesNotContain(DealShipTests.WebhookSecret, line, StringComparison.Ordinal);
    }

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> — كانَ <c>Today_OTO_resending_a_notice_it_was_answered_503_for_is_refused_stale_ten_minutes_later</c> (‏<c>0549f2e</c>؛
    /// ‏ADR-033 §٧‑ع): ما رَدَدناه ‏503 لِتُعيدَه OTO يَمُرُّ إذا أَعادَتْه — بِخَتمِ الحَدَثِ نَفسِه بَعدَ عَشرِ دَقائِق — فَتُسأَلُ OTO ويُكتَبُ التَسليم.</para>
    /// </summary>
    [Fact]
    public async Task OTO_resending_a_notice_it_was_answered_503_for_is_verified_again_and_written_ten_minutes_later()
    {
        var w = await ShippedCashDeal();
        var notice = Notice(w.Id);
        w.Oto.Routes[OtoClient.OrderStatusPath] = _ => OtoRouteHandler.Json(HttpStatusCode.ServiceUnavailable, "{}");
        var first = await Logged(w, new ShipCapturingLogger()).ReceiveAsync(Slug, notice);
        Assert.Equal(OtoOrderStatusGate.StateUnconfirmed, first.Code);
        Assert.Equal(503, OtoOrderStatusGate.StatusFor(first.Code));

        OtoSays(w, "delivered");
        var asked = w.Oto.CallsTo(OtoClient.OrderStatusPath);
        var again = await Logged(w, new ShipCapturingLogger(), DealShipTests.Now.AddMinutes(10)).ReceiveAsync(Slug, notice);

        Assert.Equal(OtoOrderStatusGate.Delivered, again.Code);
        Assert.Equal(200, OtoOrderStatusGate.StatusFor(again.Code));
        Assert.Equal(asked + 1, w.Oto.CallsTo(OtoClient.OrderStatusPath));
        Assert.Equal(DealStage.Delivered, Stored(w).Stage);
    }

    /// <summary><b>والتَوقيعُ يَحرُسُ الإشعارَ المُتَأَخِّرَ كَما يَحرُسُ الحاضِر</b> (‏2026-10-12، ‏ADR-033 §٧‑ع): خَتمٌ مَضى عَلَيه عَشرُ دَقائِقَ لا يُرَدُّ عَلى
    /// عُمرِه، فَيُسأَلُ ما بَعدَ النافِذَة — بِلا تَوقيعٍ <c>signature_missing</c>، وبِسِرٍّ آخَرَ <c>signature_mismatch</c>، وبِسِرِّ البائِعِ لا خَرق.</summary>
    [Fact]
    public void A_late_notice_still_needs_the_vendors_signature()
    {
        var late = Now.AddMinutes(10);
        Assert.Equal(OtoOrderStatusGate.SignatureMissing, OtoOrderStatusGate.Verify(Notice(DealId, secret: null), Secret, late)!.Code);
        Assert.Equal(OtoOrderStatusGate.SignatureMismatch, OtoOrderStatusGate.Verify(Notice(DealId, secret: "another-secret"), Secret, late)!.Code);
        Assert.Equal(OtoOrderStatusGate.NoticeUnverifiable, OtoOrderStatusGate.Verify(Notice(DealId), null, late)!.Code);
        Assert.Null(OtoOrderStatusGate.Verify(Notice(DealId), Secret, late));
    }

    /// <summary><b>الاحتِياطُ بِلا سِرٍّ — فَشَلٌ مُغلَق</b>: بائِعٌ لا مَدخَلَ لَه في مَتجَرِ العَرضِ يَشحَنُ بِرَمزِ التَهيئَة، وإشعارُ صَفقَتِه — ولَو
    /// وُقِّعَ — يُجابُ <c>notice_unverifiable</c> ‏401: لا تُسأَلُ OTO ولا يُكتَب. والسالِبُ: عالَمُ المَدخَلِ يُكتَبُ فيه التَسليم (صُفوفُ هذا القِسم).</summary>
    [Fact]
    public async Task Without_a_vendor_entry_the_fallback_ships_but_its_notices_are_unverifiable_and_write_nothing()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        var shipped = await w.Shipping.ShipAsync(Slug, w.Id, DealShipTests.Vendor, "Riyadh");
        Assert.True(shipped.Ok, shipped.Code);
        Assert.Contains(DealShipTests.ConfigToken, w.Oto.Calls.First(c => c.Path == OtoClient.RefreshTokenPath).Body!, StringComparison.Ordinal);
        OtoSays(w, "delivered");
        var calls = w.Oto.Calls.Count;
        var saves = w.Docs.SaveCalls;

        var outcome = await w.Shipping.ReceiveAsync(Slug, Notice(w.Id));

        Assert.Equal(OtoOrderStatusGate.NoticeUnverifiable, outcome.Code);
        Assert.Equal(401, OtoOrderStatusGate.StatusFor(outcome.Code));
        Assert.Equal(calls, w.Oto.Calls.Count);
        Assert.Equal(saves, w.Docs.SaveCalls);
        Assert.Equal(DealStage.Shipping, Stored(w).Stage);
    }

    /// <summary><b>مَدخَلٌ لا يُفَكُّ لا يَسقُطُ إلى الاحتِياط</b>: المَدخَلُ نَفسُه مَختوماً بِمِفتاحٍ رَئيسٍ آخَرَ (تَبَدَّلَ المِفتاح) — فَلا رَمزَ ولا
    /// سِرّ، ويُجابُ <c>carrier_unavailable</c> بِلا سُؤالِ OTO ولا كِتابَة، ولَو كانَ رَمزُ التَهيئَةِ مَضبوطاً لِمَتجَرِ العَرض.</summary>
    [Fact]
    public async Task A_vendor_entry_that_does_not_open_answers_carrier_unavailable_and_never_falls_back()
    {
        var w = await ShippedCashDeal();
        w.Docs.Put(DealShipTests.VendorEntry(DealShipTests.NewVault(), Slug, Seller));
        OtoSays(w, "delivered");
        var calls = w.Oto.Calls.Count;

        var outcome = await w.Shipping.ReceiveAsync(Slug, Notice(w.Id));

        Assert.Equal(OtoOrderStatusGate.CarrierUnavailable, outcome.Code);
        Assert.Equal(calls, w.Oto.Calls.Count);
        Assert.Equal(DealStage.Shipping, Stored(w).Stage);
    }

    // ═══ ٣. تَعارُضُ النُسخَة — يُعادُ القَرارُ عَلى المَخزون (‏ADR-036 الخُطوَة 3) ═══════════════════════

    private static Deal Clone(Deal d) => JsonSerializer.Deserialize<Deal>(JsonSerializer.Serialize(d))!;

    /// <summary>
    /// <para><b>كاتِبٌ آخَرُ يَحفَظُ بَينَ القَرارِ والحِفظ، ثُمَّ يُرَدُّ الحِفظُ كَما يَرُدُّه Marten</b> — <paramref name="times"/> مَرَّة.
    /// والعالَمُ يُعيدُ الكائِنَ نَفسَه، فَطَفرَةُ المُحاوَلَةِ المَردودَةِ تَبقى فيه؛ فَيُعيدُ الخُطّافُ المَخزَنَ إلى ما كانَ
    /// قَبلَها (وَثيقَةٌ مُستَنسَخَةٌ عِندَ سُؤالِ OTO) ثُمَّ يُشَغِّلُ الكاتِبَ الآخَرَ عَلَيه — وذلك ما يَكونُ في Postgres: الحِفظُ
    /// المَردودُ لا يَقَع. ويُعيدُ عَدَدَ ما رَدّ.</para>
    ///
    /// <para><b>و<paramref name="armAt"/> — ‏2026-10-12، إلحاقُ المُنَسِّقِ (٢) عَلى P1-6</b>: مَسارُ OTO الَّذي يُستَنسَخُ عِندَه المَخزَن.
    /// افتِراضُه «Order Tracking» كَما كانَ لِصُفوفِ الوارِدِ هُنا بِحَرفِها؛ وصُفوفُ <c>DealShipTests</c> تُسَلِّحُه عِندَ «Create
    /// Order» فَيَقَعُ تَعارُضُها عَلى حِفظِ مَرجِعِ الطَلَب — أَداةٌ واحِدَةٌ لا نُسخَتانِ تَنجَرِفان، فَهي <c>internal</c>.</para>
    /// </summary>
    internal static Func<int> OthersWinTheSave(DealShipTests.World w, Func<Task> otherWrite, int times = 1,
        string armAt = OtoClient.OrderStatusPath)
    {
        Deal? stored = null;
        var conflicts = 0;
        var inOther = false;
        w.Oto.Before = path =>
        {
            if (path == armAt) stored = Clone(Stored(w));
        };
        w.Docs.BeforeMember = name =>
        {
            if (name != "SaveChangesAsync" || stored is null || inOther || conflicts == times) return;
            conflicts++;
            w.Docs.Put(Clone(stored));
            inOther = true;
            try { otherWrite().GetAwaiter().GetResult(); }
            finally { inOther = false; }
            stored = Clone(Stored(w));
            throw new JasperFx.ConcurrencyException(typeof(Deal), w.Id);
        };
        return () => conflicts;
    }

    /// <summary><b>المُوجَب — يُعادُ القَرارُ فَيُكتَب</b>: كِتابَةٌ لا تَمَسُّ التَسليمِ (مَرجِعٌ يُلحَق) تَسبِقُ حِفظَ الانتِقال —
    /// تَبقى، ويُقَرَّرُ التَسليمُ عَلى ما كُتِبَ فَيُكتَب: سَطرُ تَسليمٍ واحِدٌ وسَطرُ قَبضٍ نَقدِيٍّ واحِد.</summary>
    [Fact]
    public async Task A_write_that_wins_the_save_is_kept_and_the_delivery_is_re_decided_and_written()
    {
        var w = await ShippedCashDeal();
        OtoSays(w, "delivered");
        var conflicts = OthersWinTheSave(w, () => w.Deals.AttachRefAsync(Slug, w.Id, "probe_note", "1"));

        var outcome = await w.Shipping.ReceiveAsync(Slug, Notice(w.Id));

        Assert.Equal(1, conflicts());
        Assert.Equal(OtoOrderStatusGate.Delivered, outcome.Code);
        var deal = Stored(w);
        Assert.Equal(DealStage.Delivered, deal.Stage);
        Assert.Equal("1", deal.Refs["probe_note"]);
        Assert.Single(deal.Timeline, e => e.Action == "advanced" && e.StageAfter == DealStage.Delivered);
        Assert.Single(deal.Timeline, e => e.Action == DealEvent.CashOnDelivery);
    }

    /// <summary><b>السالِب — يُعادُ القَرارُ فَلا يُكتَب</b>: البائِعُ أَعلَنَ «تَسليم» بِزِرِّه بَينَ القَرارِ والحِفظ — يُجابُ
    /// «سَبَق»، ولا تَمضي الصَفقَةُ إلى «تَقييم» بِاسمِه (وهو ما كانَت تَفعَلُه إعادَةُ الكِتابَةِ بِلا قَرار).</summary>
    [Fact]
    public async Task A_manual_delivery_that_wins_the_save_is_answered_already_delivered_and_nothing_moves_twice()
    {
        var w = await ShippedCashDeal();
        OtoSays(w, "delivered");
        var conflicts = OthersWinTheSave(w, async () =>
            Assert.True((await w.Deals.AdvanceAsync(Slug, w.Id, Seller, "بائِع")).Ok));

        var outcome = await w.Shipping.ReceiveAsync(Slug, Notice(w.Id));

        Assert.Equal(1, conflicts());
        Assert.Equal(OtoOrderStatusGate.AlreadyDelivered, outcome.Code);
        var deal = Stored(w);
        Assert.Equal((DealStage.Delivered, DealStatus.Active), (deal.Stage, deal.Status));
        Assert.Single(deal.Timeline, e => e.Action == "advanced" && e.StageAfter == DealStage.Delivered);
        Assert.DoesNotContain(deal.Timeline, e => e.StageAfter == DealStage.Reviewed);
        Assert.Single(deal.Timeline, e => e.Action == DealEvent.CashOnDelivery);
    }

    /// <summary><b>وما بَينَ القَرارِ والتَحميل — يَحرُسُه <c>from</c></b>: البائِعُ يُعلِنُ «تَسليم» بَعدَ أَن قَرَّرَ الوارِدُ وقَبلَ أَن
    /// يَفتَحَ جَلسَةَ الانتِقال — فَيَقرَأُ الانتِقالُ صَفقَةً في <c>Delivered</c>. بِلا <c>from = Shipping</c> لَمَضى بِها إلى «تَقييم»
    /// بِاسمِ البائِع (فاعِلُ <c>Reviewed</c> = <c>either</c>)؛ وبِه يُرَدُّ، ويُعادُ القَرارُ فَيُجابُ «سَبَق».</summary>
    [Fact]
    public async Task A_manual_delivery_between_the_decision_and_the_transition_load_is_caught_by_from_and_never_reaches_Reviewed()
    {
        var w = await ShippedCashDeal();
        OtoSays(w, "delivered");
        var asked = false;
        var fired = false;
        w.Oto.Before = path => { if (path == OtoClient.OrderStatusPath) asked = true; };
        w.Docs.BeforeMember = name =>
        {
            if (name != "LightweightSession" || !asked || fired) return;
            fired = true;
            Assert.True(w.Deals.AdvanceAsync(Slug, w.Id, Seller, "بائِع").GetAwaiter().GetResult().Ok);
        };

        var outcome = await w.Shipping.ReceiveAsync(Slug, Notice(w.Id));

        Assert.True(fired, "الخُطّافُ لَم يَعمَل — الأَداةُ عَمياء.");
        Assert.Equal(OtoOrderStatusGate.AlreadyDelivered, outcome.Code);
        var deal = Stored(w);
        Assert.Equal(DealStage.Delivered, deal.Stage);
        Assert.DoesNotContain(deal.Timeline, e => e.StageAfter == DealStage.Reviewed);
        Assert.Single(deal.Timeline, e => e.Action == "advanced" && e.StageAfter == DealStage.Delivered);
    }

    /// <summary><b>والحَدّ</b>: ثَلاثُ خَسائِرَ ⇒ <c>deal_changed_meanwhile</c> و‏503 (تُعيدُ OTO الإرسال)، بِلا كِتابَةٍ ولا رابِعَة.</summary>
    [Fact]
    public async Task Three_lost_saves_give_up_with_deal_changed_meanwhile_and_a_retry_answer_without_writing()
    {
        var w = await ShippedCashDeal();
        OtoSays(w, "delivered");
        var conflicts = OthersWinTheSave(w, () => Task.CompletedTask, times: int.MaxValue);
        var saves = w.Docs.SaveCalls;

        var outcome = await w.Shipping.ReceiveAsync(Slug, Notice(w.Id));

        Assert.Equal(DealsService.ChangedMeanwhile, outcome.Code);
        Assert.Equal(503, OtoOrderStatusGate.StatusFor(outcome.Code));
        Assert.Equal(DealShipping.NoticeAttempts, conflicts());
        Assert.Equal(saves, w.Docs.SaveCalls);
        Assert.Equal(DealStage.Shipping, Stored(w).Stage);
    }

    // ═══ العَدّاد — ما تُنتِجُه الدَوالُّ الحَقيقِيَّةُ مِن المَعجَم (القاعِدَة ١٠) ═══════════════════════

    [Fact]
    public async Task Every_code_of_the_lexicon_is_produced_by_the_real_functions_and_counted()
    {
        var produced = new HashSet<string>(StringComparer.Ordinal);
        void Add(string? code) { if (code is not null) produced.Add(code); }

        Add(OtoOrderStatusGate.Admit(false, NoticeJson("x")).Violation?.Code);
        Add(OtoOrderStatusGate.Admit(true, "{}").Violation?.Code);
        Add(OtoOrderStatusGate.Match(Notice(DealId), null)?.Code);
        Add(OtoOrderStatusGate.Match(Notice(DealId, otoId: "999"), Shipping())?.Code);
        Add(Decided(Shipping(), State("outForDelivery")));
        Add(Decided(With(d => d.Stage = DealStage.Delivered)));
        Add(Decided(With(d => d.Status = DealStatus.Cancelled)));
        Add(Decided(With(d => d.Stage = DealStage.Confirmed)));
        Add(Decided(With(d => d.CounterpartyId = null)));
        // ورُموزُ التَوقيعِ الأَربَعَةُ مِن `Verify` نَفسِها (‏2026-10-12، تَيّارُ S8). و`signature_stale` بِساعَةٍ تَتَأَخَّرُ عَن الخَتمِ دَقيقَةً
        // (‏2026-10-12، الجَولَة 1، ‏ADR-033 §٧‑ع): كانَت تَسبِقُه دَقيقَة — وإشعارٌ مَضى عَلَيه دَقيقَةٌ لا يُرَدُّ بَعدَ اليَومِ عَلى عُمرِه.
        Add(OtoOrderStatusGate.Verify(Notice(DealId), null, DealShipTests.Now)?.Code);
        Add(OtoOrderStatusGate.Verify(Notice(DealId, secret: null), DealShipTests.WebhookSecret, DealShipTests.Now)?.Code);
        Add(OtoOrderStatusGate.Verify(Notice(DealId), DealShipTests.WebhookSecret, DealShipTests.Now.AddMinutes(-1))?.Code);
        Add(OtoOrderStatusGate.Verify(Notice(DealId), "another-secret", DealShipTests.Now)?.Code);

        var w = await ShippedCashDeal();
        w.Oto.Routes[OtoClient.OrderStatusPath] = _ => OtoRouteHandler.Json(HttpStatusCode.BadGateway, "{}");
        Add((await w.Shipping.ReceiveAsync(Slug, Notice(w.Id))).Code);
        OtoSays(w, "delivered");
        Add((await w.Shipping.ReceiveAsync(Slug, Notice(w.Id))).Code);

        var counted = produced.Intersect(OtoOrderStatusGate.All).Count();
        output.WriteLine($"رُموزُ الوارِدِ المُنتَجَةُ بِالدَوالِّ الحَقيقِيَّة: {counted} مِن {OtoOrderStatusGate.All.Count}، " +
                         $"ومَعَها «{OtoOrderStatusGate.Delivered}»: {string.Join(" · ", produced.OrderBy(c => c, StringComparer.Ordinal))}");
        Assert.True(counted > 0, "أَداةٌ عَمياء: صِفرُ رَمز.");
        Assert.Equal(OtoOrderStatusGate.All.OrderBy(c => c, StringComparer.Ordinal),
            produced.Intersect(OtoOrderStatusGate.All).OrderBy(c => c, StringComparer.Ordinal));
        Assert.Contains(OtoOrderStatusGate.Delivered, produced);
    }

    // ═══ ٤. النُقطَة ═══════════════════════════════════════════════════

    [Fact]
    public void The_inbound_route_is_literal_matches_the_documented_path_and_admits_before_it_receives()
    {
        var path = Path.Combine(RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace",
            "Shipping", "OtoShippingEndpoints.cs");
        var code = WriteEndpointGuardTests.StripComments(File.ReadAllText(path));
        var start = code.IndexOf($"app.MapPost(\"{OtoShippingEndpoints.OrderStatusPath}\"", StringComparison.Ordinal);
        Assert.True(start > 0, "نُقطَةُ الوارِدِ لا تُطابِقُ ثابِتَ مَسارِها — الوَثيقَةُ تُسَجِّلُ عِندَ OTO عُنواناً لا نُقطَةَ عَلَيه.");
        var body = code[start..code.IndexOf(".DisableAntiforgery();", start, StringComparison.Ordinal)];

        var admit = body.IndexOf("OtoOrderStatusGate.Admit(", StringComparison.Ordinal);
        var receive = body.IndexOf("shipping.ReceiveAsync(", StringComparison.Ordinal);
        Assert.True(admit > 0 && receive > admit, $"البَوّابَةُ لا تَسبِقُ النِداء: {admit}/{receive}");
        Assert.Contains("OtoOrderStatusGate.StatusFor(", body, StringComparison.Ordinal);
        foreach (var decision in new[] { "DealStage.", "Refs[", "AdvanceAsync", "SaveChangesAsync", "IDocumentSession" })
            Assert.DoesNotContain(decision, body, StringComparison.Ordinal);
    }

    [Fact]
    public void OTO_is_asked_before_the_first_write_in_the_service()
    {
        var path = Path.Combine(RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace",
            "Services", "Deals", "DealShipping.cs");
        var code = WriteEndpointGuardTests.StripComments(File.ReadAllText(path));
        var from = code.IndexOf("public async Task<OtoNoticeOutcome> ReceiveAsync(", StringComparison.Ordinal);
        var to = code.IndexOf("private async Task<OtoNoticeOutcome> DeliverAsync(", from, StringComparison.Ordinal);
        Assert.True(from > 0 && to > from, "‏ReceiveAsync غَيرُ مَوجودَة — الأَداةُ عَمياء.");
        var receive = code[from..to];

        var ask = receive.IndexOf("_oto.GetOrderStatusAsync(", StringComparison.Ordinal);
        var writes = new[] { "DeliverAsync(", "AttachLinksAsync(", "_deals.AttachRefAsync(", "_deals.AdvanceAsync(" }
            .Select(w => receive.IndexOf(w, StringComparison.Ordinal)).Where(i => i >= 0).ToArray();
        Assert.True(ask > 0 && writes.Length > 0, $"أَداةٌ عَمياء: سُؤال={ask} كِتابات={writes.Length}");
        Assert.True(writes.All(i => i > ask), "كِتابَةٌ تَسبِقُ سُؤالَ OTO في ReceiveAsync.");
    }

    /// <summary><b>التَوقيعُ قَبلَ سُؤالِ OTO وبَعدَ المُطابَقَة</b> في نَصِّ <c>ReceiveAsync</c> (تَيّارُ S8، ‏2026-10-12): المُطابَقَةُ تُعَرِّفُ البائِعَ،
    /// وسِرُّه يُفَكُّ بَعدَها، و<c>Verify</c> قَبلَ <c>GetOrderStatusAsync</c> — فَلا يُسأَلُ OTO بِإشعارٍ لَم يَتَحَقَّق.</summary>
    [Fact]
    public void The_signature_is_verified_after_the_match_and_before_OTO_is_asked_in_the_service()
    {
        var path = Path.Combine(RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace",
            "Services", "Deals", "DealShipping.cs");
        var code = WriteEndpointGuardTests.StripComments(File.ReadAllText(path));
        var from = code.IndexOf("public async Task<OtoNoticeOutcome> ReceiveAsync(", StringComparison.Ordinal);
        var to = code.IndexOf("private async Task<OtoNoticeOutcome> DeliverAsync(", from, StringComparison.Ordinal);
        Assert.True(from > 0 && to > from, "‏ReceiveAsync غَيرُ مَوجودَة — الأَداةُ عَمياء.");
        var receive = code[from..to];

        var match = receive.IndexOf("OtoOrderStatusGate.Match(", StringComparison.Ordinal);
        // (‏2026-10-13، G5 (viii) — ولا يُعادُ حَرفٌ فَوقَه: والناقِلُ خُطوَتان — يُحَلُّ بِلا فَكٍّ ويُفَكُّ سِرُّه قَبلَ التَوقيع، ورَمزُ تَحديثِه بَعدَه قُبَيلَ
        // السُؤال؛ كانَ `CarrierForAsync(` يَفُكُّهُما مَعاً قَبلَ التَوقيع. ولا يَعودُ إلى هذا الجِسم.)
        var carrier = receive.IndexOf("ResolveCarrierAsync(", StringComparison.Ordinal);
        var secret = receive.IndexOf("TryOpenSecret(", StringComparison.Ordinal);
        var verify = receive.IndexOf("OtoOrderStatusGate.Verify(", StringComparison.Ordinal);
        var token = receive.IndexOf("TryOpenToken(", StringComparison.Ordinal);
        var ask = receive.IndexOf("_oto.GetOrderStatusAsync(", StringComparison.Ordinal);
        output.WriteLine($"ReceiveAsync: مُطابَقَة={match} · ناقِل={carrier} · سِرّ={secret} · تَوقيع={verify} · رَمز={token} · سُؤال={ask}");
        Assert.True(match > 0 && carrier > match && secret > carrier && verify > secret && token > verify && ask > token,
            $"التَرتيبُ مَكسور: مُطابَقَة={match} ناقِل={carrier} سِرّ={secret} تَوقيع={verify} رَمز={token} سُؤال={ask}");
        Assert.DoesNotContain("CarrierForAsync(", receive, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(receive, @"_oto\.GetOrderStatusAsync\("));
    }

    [Fact]
    public async Task Without_a_carrier_the_endpoint_answers_an_empty_404_and_touches_nothing()
    {
        var w = await ShippedCashDeal();
        OtoSays(w, "delivered");
        var calls = w.Oto.Calls.Count;
        var touches = w.Docs.Touches.Count;
        await using var host = await OtoHost.StartAsync(ShippingIn(w, OtoEnvironment.Production));

        using var resp = await host.PostAsync(Slug, NoticeJson(w.Id.ToString("N")));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Equal("", await resp.Content.ReadAsStringAsync());
        Assert.Equal(calls, w.Oto.Calls.Count);
        Assert.Equal(touches, w.Docs.Touches.Count);
        Assert.Equal(DealStage.Shipping, Stored(w).Stage);
    }

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> — كانَ <c>Today_the_inbound_route_reads_the_whole_body_before_answering_404_without_a_carrier</c>
    /// (‏<c>ecb278a</c>): <c>ReadToEndAsync</c> كانَ يَسبِقُ البَوّابَةَ فَيُقرَأُ الجِسمُ كُلُّه ثُمَّ يُجابُ ‏404 (مُراجَعَةُ المَوجَة 3a، تَيّارُ S8،
    /// ‏2026-10-12). <b>والآن</b>: الناقِلُ يُسأَلُ أَوَّلاً ولا يُقرَأُ الجِسمُ بِدونِه — جِسمٌ يَرمي عِندَ أَوَّلِ قِراءَةٍ (فَقِراءَتُه ‏500) ويُجابُ
    /// ‏404 فارِغاً بِصِفرِ قِراءَة.</para>
    /// </summary>
    [Fact]
    public async Task Without_a_carrier_the_inbound_route_answers_404_before_reading_the_body()
    {
        var w = await ShippedCashDeal();
        var tripwire = new BodyTripwire(throws: true);
        await using var host = await OtoHost.StartAsync(ShippingIn(w, OtoEnvironment.Production), tripwire);

        using var resp = await host.PostAsync(Slug, NoticeJson(w.Id.ToString("N")));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Equal("", await resp.Content.ReadAsStringAsync());
        Assert.Equal(0, tripwire.Reads);
    }

    /// <summary><b>والسالِب</b>: بِالناقِلِ يُقرَأُ الجِسمُ — فَالجِسمُ الَّذي يَرمي يَصيرُ ‏500، والأَداةُ لا تَمُرُّ خَضراءَ عَلى مَسارٍ لا يَقرَأُ شَيئاً.</summary>
    [Fact]
    public async Task With_a_carrier_the_inbound_route_does_read_the_body()
    {
        var w = await ShippedCashDeal();
        var tripwire = new BodyTripwire(throws: true);
        await using var host = await OtoHost.StartAsync(w.Shipping, tripwire);

        using var resp = await host.PostAsync(Slug, NoticeJson(w.Id.ToString("N")));

        Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);
        Assert.True(tripwire.Reads > 0, $"قِراءاتُ الجِسم: {tripwire.Reads}");
        Assert.Equal(DealStage.Shipping, Stored(w).Stage);
    }

    /// <summary>ورَمزُ <c>theme-demo</c> لا يُحَلُّ لِمَتجَرٍ آخَر: الوارِدُ إلى <c>ashare</c> ‏404 ولا يُقرَأ.
    /// <b>(وصارَ «ولا يُقرَأ» مَقيساً ‏2026-10-12 — مُراجَعَةُ المَوجَة 3a: كانَ الاسمُ يَقولُه ولا يَفحَصُه، وكانَ الجِسمُ يُقرَأ؛ فَالجِسمُ
    /// الَّذي يَرمي مَلفوفٌ هُنا، وكانَ سَيُحمِرُّ هذا الصَفَّ قَبلَ الإصلاح.)</b></summary>
    [Fact]
    public async Task Another_store_on_the_same_staging_token_answers_404_without_reading()
    {
        var w = await ShippedCashDeal();
        var calls = w.Oto.Calls.Count;
        var tripwire = new BodyTripwire(throws: true);
        // خِدمَةُ رَمزِ التَهيئَةِ وَحدَه (تَيّارُ S8، ‏2026-10-12): سُؤالُ هذا الصَفِّ «أَيَتَسَرَّبُ رَمزُ التَهيئَةِ إلى مَتجَرٍ آخَر؟» — وشَطرُ
        // «لا مَدخَلَ بائِعٍ في ذلك المَتجَر» يُسأَلُ عَلى Postgres (‏`LiveVendorShippingProofTests`): عالَمُ الوَثائِقِ بِلا إيجارٍ ولا استِعلام.
        await using var host = await OtoHost.StartAsync(ShippingIn(w, OtoEnvironment.Staging), tripwire);

        using var resp = await host.PostAsync("ashare", NoticeJson(w.Id.ToString("N")));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Equal(calls, w.Oto.Calls.Count);
        Assert.Equal(0, tripwire.Reads);
    }

    /// <summary>
    /// <para><b>مَتجَرٌ لا يَفتَحُه إلّا مَدخَلُ بائِعٍ، بِلا خِزانَة: ‏404 فارِغٌ بِلا قِراءَةٍ ولا سُؤالٍ لِمَداخِلِه</b> (‏2026-10-12، مُراجَعَةُ تَيّارِ S8، الجولة 2)
    /// — خِدمَةُ الشَحنِ بِمَداخِلِ البائِعينَ وخِزانَةٍ لا مِفتاحَ لَها، والمَتجَرُ غَيرُ مَتجَرِ العَرضِ فَلا رَمزَ تَهيئَةٍ يَفتَحُه. وعالَمُ الوَثائِقِ يَرمي
    /// عِندَ الاستِعلامِ ويَبتَلِعُه <c>HasVendorEntriesAsync</c> «لا»، فَـ‏404 وَحدَه يَصدُقُ بِشَرطِ الخِزانَةِ وبِغَيرِه؛ <b>وعَدُّ الاستِعلامِ يَنفَرِدُ بِه</b>:
    /// صِفرٌ هُنا. والمُوجَبُ — بِخِزانَةٍ يُقرَأُ الجِسم — عَلى Postgres
    /// (<c>LiveOtoOrderStatusProofTests.A_store_with_only_vendor_entries_reads_the_inbound_body_with_a_vault_and_answers_404_unread_without_one_on_Postgres</c>).</para>
    /// </summary>
    [Fact]
    public async Task A_store_with_only_vendor_entries_and_no_vault_answers_404_without_reading_or_asking_its_entries()
    {
        var w = await ShippedCashDeal();
        var options = Options.Create(new OtoOptions { Environment = OtoEnvironment.Staging, RefreshToken = DealShipTests.ConfigToken });
        var shipping = new DealShipping(w.Deals,
            new OtoClient(new HttpClient(w.Oto), options, new OtoTokenCache(), NullLogger<OtoClient>.Instance), options,
            null, w.Providers, ProviderVault.FromKek(null), new FixedClock(DealShipTests.Now));
        var asked = w.Docs.Members.Count(m => m == "Query");
        var tripwire = new BodyTripwire(throws: true);
        await using var host = await OtoHost.StartAsync(shipping, tripwire);

        using var resp = await host.PostAsync("owner-test", NoticeJson(w.Id.ToString("N")));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Equal("", await resp.Content.ReadAsStringAsync());
        Assert.Equal(0, tripwire.Reads);
        Assert.Equal(asked, w.Docs.Members.Count(m => m == "Query"));
    }

    /// <summary><b>إشعارٌ بِلا تَوقيعٍ أَو بِسِرٍّ آخَرَ ‏401 فارِغ</b> — ولا تُسأَلُ OTO ولا تُكتَبُ الصَفقَة (تَيّارُ S8، ‏2026-10-12؛ ‏ADR-033 §٧‑م).
    /// والسالِب: المُوَقَّعُ بِسِرِّ البائِعِ يَمُرُّ إلى سُؤالِ OTO ويُكتَبُ التَسليم.</summary>
    [Fact]
    public async Task An_unsigned_or_forged_notice_is_answered_401_and_neither_asks_OTO_nor_writes()
    {
        var w = await ShippedCashDeal();
        OtoSays(w, "delivered");
        var calls = w.Oto.Calls.Count;
        var saves = w.Docs.SaveCalls;
        await using var host = await OtoHost.StartAsync(w.Shipping);

        using (var unsigned = await host.PostAsync(Slug, NoticeJson(w.Id.ToString("N"), secret: null)))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, unsigned.StatusCode);
            Assert.Equal("", await unsigned.Content.ReadAsStringAsync());
        }
        using (var forged = await host.PostAsync(Slug, NoticeJson(w.Id.ToString("N"), secret: "another-secret")))
            Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);

        Assert.Equal(calls, w.Oto.Calls.Count);
        Assert.Equal(saves, w.Docs.SaveCalls);
        Assert.Equal(DealStage.Shipping, Stored(w).Stage);

        using (var signed = await host.PostAsync(Slug, NoticeJson(w.Id.ToString("N"))))
            Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
        Assert.Equal(DealStage.Delivered, Stored(w).Stage);
    }

    /// <summary><b>عَبرَ HTTP كَما تَراه OTO</b> (‏2026-10-12، مُراجَعَةُ تَيّارِ S8، الجَولَة 1؛ ‏ADR-033 §٧‑ع): خادِمٌ ساعَتُه بَعدَ الحَدَثِ بِعَشرِ دَقائِق —
    /// الإشعارُ المُزَوَّرُ ‏401 فارِغٌ بِلا سُؤالٍ ولا كِتابَة، والمُوَقَّعُ بِسِرِّ البائِعِ ‏200 والصَفقَةُ «تسليم». وكانَ المُوَقَّعُ يُجابُ ‏401 فَتُعيدُه OTO
    /// بِالخَتمِ نَفسِه ولا يَمُرّ.</summary>
    [Fact]
    public async Task Ten_minutes_after_its_event_a_signed_notice_is_answered_200_and_delivers_and_a_forged_one_401()
    {
        var w = await ShippedCashDeal();
        OtoSays(w, "delivered");
        var calls = w.Oto.Calls.Count;
        var asked = w.Oto.CallsTo(OtoClient.OrderStatusPath);
        var saves = w.Docs.SaveCalls;
        await using var host = await OtoHost.StartAsync(Logged(w, new ShipCapturingLogger(), DealShipTests.Now.AddMinutes(10)));

        using (var forged = await host.PostAsync(Slug, NoticeJson(w.Id.ToString("N"), secret: "another-secret")))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
            Assert.Equal("", await forged.Content.ReadAsStringAsync());
        }
        Assert.Equal(calls, w.Oto.Calls.Count);
        Assert.Equal(saves, w.Docs.SaveCalls);

        using (var signed = await host.PostAsync(Slug, NoticeJson(w.Id.ToString("N"))))
            Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
        Assert.Equal(asked + 1, w.Oto.CallsTo(OtoClient.OrderStatusPath));
        Assert.Equal(DealStage.Delivered, Stored(w).Stage);
    }

    [Fact]
    public async Task The_endpoint_answers_400_200_and_503_as_the_service_decides()
    {
        var w = await ShippedCashDeal();
        await using var host = await OtoHost.StartAsync(w.Shipping);

        using (var unreadable = await host.PostAsync(Slug, "not json"))
            Assert.Equal(HttpStatusCode.BadRequest, unreadable.StatusCode);

        w.Oto.Routes[OtoClient.OrderStatusPath] = _ => OtoRouteHandler.Json(HttpStatusCode.ServiceUnavailable, "{}");
        using (var unconfirmed = await host.PostAsync(Slug, NoticeJson(w.Id.ToString("N"))))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unconfirmed.StatusCode);
        Assert.Equal(DealStage.Shipping, Stored(w).Stage);

        OtoSays(w, "delivered");
        using (var delivered = await host.PostAsync(Slug, NoticeJson(w.Id.ToString("N"))))
        {
            Assert.Equal(HttpStatusCode.OK, delivered.StatusCode);
            Assert.Equal("", await delivered.Content.ReadAsStringAsync());
        }
        Assert.Equal(DealStage.Delivered, Stored(w).Stage);

        using (var again = await host.PostAsync(Slug, NoticeJson(w.Id.ToString("N"))))
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Single(Stored(w).Timeline, e => e.Action == "advanced" && e.StageAfter == DealStage.Delivered);
    }
}

// ─── المُضيفُ المُصَغَّر — نِقاطُ الشَحنِ نَفسُها بِخِدمَةٍ مَبنِيَّة ─────────────────────────────

file sealed class OtoHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;

    private OtoHost(WebApplication app, HttpClient client) { _app = app; _client = client; }

    private static int FreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>و<paramref name="tripwire"/> — اختِيارِيٌّ (‏2026-10-12، مُراجَعَةُ المَوجَة 3a): يَلُفُّ جِسمَ كُلِّ طَلَبٍ في الخادِمِ
    /// فَتُعَدُّ قِراءاتُه أَو تُرمى — «لا يُقرَأُ الجِسم» شَرطٌ يُعَدّ لا يُستَنتَج. بِلا شَيءٍ يَبقى المُضيفُ كَما كان.</summary>
    public static async Task<OtoHost> StartAsync(DealShipping shipping, BodyTripwire? tripwire = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(shipping);

        var port = FreePort();
        var app = builder.Build();
        app.Urls.Add($"http://127.0.0.1:{port}");
        if (tripwire is not null)
            app.Use(async (ctx, next) => { ctx.Request.Body = tripwire.Wrap(ctx.Request.Body); await next(); });
        app.MapOtoShipping();
        await app.StartAsync();

        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
        };
        return new OtoHost(app, client);
    }

    public Task<HttpResponseMessage> PostAsync(string slug, string body)
        => _client.PostAsync(OtoShippingEndpoints.OrderStatusPath.Replace("{slug}", slug, StringComparison.Ordinal),
            new StringContent(body, Encoding.UTF8, "application/json"));

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

/// <summary>
/// <para><b>جِسمُ طَلَبٍ يَعُدُّ قِراءاتِه — أَو يَرمي عِندَ أَوَّلِها</b> (‏2026-10-12، مُراجَعَةُ المَوجَة 3a). يَلُفُّه <c>OtoHost</c>
/// حَولَ جِسمِ الطَلَبِ في الخادِم؛ فَـ<c>throws: true</c> يَجعَلُ أَيَّ قِراءَةٍ ‏500 — و‏404 فارِغٌ بَعدَه بُرهانُ «لَم يُقرَأ».</para>
/// </summary>
internal sealed class BodyTripwire(bool throws)
{
    private int _reads;

    public int Reads => Volatile.Read(ref _reads);

    public Stream Wrap(Stream inner) => new Counting(inner, this);

    private void Touched()
    {
        Interlocked.Increment(ref _reads);
        if (throws) throw new InvalidOperationException("قُرِئَ جِسمُ الطَلَب — والمَسارُ يَقولُ إنّه لا يُقرَأ.");
    }

    private sealed class Counting(Stream inner, BodyTripwire owner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) { owner.Touched(); return inner.Read(buffer, offset, count); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        { owner.Touched(); return await inner.ReadAsync(buffer, ct); }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

/// <summary>
/// <para><b>مَخزَنٌ يُسَمّي مُستَأجِراً بِاسمِ آخَر</b> — جَلَساتُ <c>from</c> تُفتَحُ عَلى <c>to</c>، وكُلُّ ما سِواها
/// يُمَرَّرُ كَما هُوَ. رَمزُ الناقِلِ لا يُحَلُّ إلّا لِـ<c>theme-demo</c> (‏ADR-033 §٢‑هـ القاعِدَة ٩)، والبُرهانُ الحَيُّ لا يَكتُبُ
/// في مُستَأجِرِ <c>theme-demo</c> نَفسِه — فَيُنادى الوارِدُ بِاسمِه وتَقَعُ كِتاباتُه في مُستَأجِرٍ مُؤَقَّتٍ يُمسَحُ بَعدَه.</para>
/// </summary>
internal class TenantAlias : DispatchProxy
{
    private IDocumentStore _target = null!;
    private string _from = "";
    private string _to = "";

    public static IDocumentStore Wrap(IDocumentStore store, string from, string to)
    {
        var proxy = Create<IDocumentStore, TenantAlias>();
        var self = (TenantAlias)(object)proxy;
        (self._target, self._from, self._to) = (store, from, to);
        return proxy;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.Name is "LightweightSession" or "QuerySession"
            && args is { Length: > 0 } && args[0] is string tenant && tenant == _from)
            args[0] = _to;
        try { return method.Invoke(_target, args); }
        catch (TargetInvocationException e) { ExceptionDispatchInfo.Capture(e.InnerException!).Throw(); throw; }
    }
}

// ═══ البُرهانُ الحَيّ — Postgres، والمَخزَنُ كَما يُرَكِّبُه المُضيف (‏ADR-036 الخُطوَة 3: «بِصَفٍّ حَيٍّ كَهذَين») ═══════════
//
// بَوّابَةُ الحِفظِ نَفسُها (`SaveGate`، ‏DealConcurrencyTests): يَقرَأُ الوارِدُ الصَفقَةَ ويَسأَلُ OTO (مُعالِجٌ يُعيدُ رَدَّ الوَثيقَة —
// لا شَبَكَة) ويُقَرِّرُ ويَقِفُ قَبلَ حِفظِ الانتِقال؛ ثُمَّ تُكتَبُ كِتابَةٌ أُخرى وتُحفَظ؛ ثُمَّ يُستَأنَف — فَيَرُدُّه Marten نَفسُه.
// مُبَوَّبٌ بِـWASAYEL_LIVE_PROOF=1 وConnectionStrings__Postgres، ومُستَأجِرٌ مُؤَقَّتٌ يُمسَحُ بَعدَه.

[Collection(LiveProofCollection.Name)]
public class LiveOtoOrderStatusProofTests(ITestOutputHelper output)
{
    private sealed class ForbiddenPaymentProvider : IPaymentProvider
    {
        private static InvalidOperationException Touched() => new("صَفقَةُ النَقدِ لا تَمَسُّ المال — نودِيَ مُزَوِّدُ الدَفع.");
        public string ProviderName => "forbidden";
        public Task<PaymentResult> AuthorizeAsync(PaymentRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> CaptureAsync(string paymentId, decimal? amount = null, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> RefundAsync(string paymentId, decimal amount, string reason, CancellationToken ct = default) => throw Touched();
        public Task<SubscriptionResult> CreateSubscriptionAsync(SubscriptionRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<bool> CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default) => throw Touched();
        public Task<Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default) => throw Touched();
    }

    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>صَفقَةُ نَقدٍ عِندَ <c>Shipping</c> بِمَرجِعَي طَلَبِها — بِالخِدمَةِ الحَقيقِيَّةِ عَلى المَخزَنِ الحَقيقِيّ.</summary>
    private static async Task<Deal> ShippedAsync(DealsService plain, string slug, Guid buyer, Guid seller)
    {
        var born = await plain.StartAsync(slug, "marketplace", buyer, "مُشتَرٍ", null, "سِلعَة", 150m, new()
        {
            [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery,
            [Deal.DeliveryAddressAttribute] = "حَيُّ المِثال",
            [Deal.DeliveryPhoneAttribute] = "0500000000",
        });
        await plain.AttachRefAsync(slug, born.Id, DealsService.ListingOwnerRef, seller.ToString());
        await plain.BindListingOwnerAsync(slug, born.Id, seller.ToString());
        Assert.True((await plain.AcceptAsync(slug, born.Id, new DealAcceptor(seller, "بائِع"))).Ok);
        Assert.True((await plain.AdvanceAsync(slug, born.Id, seller, "بائِع")).Ok);   // ← Confirmed
        Assert.True((await plain.AdvanceAsync(slug, born.Id, seller, "بائِع")).Ok);   // ← Shipping
        await plain.AttachRefAsync(slug, born.Id, DealShipping.OrderRef, born.Id.ToString("N"));
        var shipped = (await plain.AttachRefAsync(slug, born.Id, DealShipping.OtoIdRef, "540789"))!;
        Assert.Equal(DealStage.Shipping, shipped.Stage);
        return shipped;
    }

    /// <summary>
    /// <para>الوارِدُ عَلى المَخزَنِ الحَقيقِيّ. <b>ومُنذُ ‏2026-10-12</b> (تَيّارُ S8، ‏ADR-033 الخُطوَة 7): لِلبائِعِ مَدخَلٌ مَختومٌ في المُستَأجِرِ
    /// المُؤَقَّت — يَكتُبُه <c>TenantProviderService.BindVendorAsync</c> نَفسُه عَلى Postgres (<paramref name="providersStore"/>) — فَيُتَحَقَّقُ مِن
    /// تَوقيعِ الإشعارِ بِسِرِّه قَبلَ سُؤالِ OTO كَما في الإنتاج. و<paramref name="dealsStore"/> مَخزَنُ الصَفَقاتِ (مُبَوَّباً في السِباق).</para>
    /// </summary>
    private static async Task<DealShipping> ReceiverAsync(IDocumentStore dealsStore, IDocumentStore providersStore,
        OtoRouteHandler oto, Guid seller)
    {
        oto.Routes[OtoClient.OrderStatusPath] = body =>
            OtoRouteHandler.Tracking(OtoRouteHandler.OrderIdIn(body), status: "delivered");
        var vault = DealShipTests.NewVault();
        var providers = new TenantProviderService(providersStore);
        var entry = DealShipTests.VendorEntry(vault, DealShipping.DemoStore, seller);
        await providers.BindVendorAsync(DealShipping.DemoStore, ProviderCapabilities.Delivery, seller, entry.ProviderSlug,
            entry.Values, entry.WebhookId, null, seller.ToString("D"));
        var options = Options.Create(new OtoOptions { Environment = OtoEnvironment.Staging, RefreshToken = "RT-live-proof" });
        return new DealShipping(new DealsService(dealsStore, new ForbiddenPaymentProvider()),
            new OtoClient(new HttpClient(oto), options, new OtoTokenCache(), NullLogger<OtoClient>.Instance), options,
            null, providers, vault, new FixedClock(DealShipTests.Now));
    }

    /// <summary>يُمسَحُ المُستَأجِرُ المُؤَقَّت: صَفَقاتُه ومَداخِلُ بائِعيه.</summary>
    internal static async Task WipeAsync(IDocumentStore store, string slug)
    {
        await using var s = store.LightweightSession(slug);
        foreach (var d in await s.Query<Deal>().ToListAsync()) s.Delete(d);
        foreach (var b in await s.Query<TenantProviderBinding>().ToListAsync()) s.Delete(b);
        await s.SaveChangesAsync();
    }

    private async Task<(OtoNoticeOutcome Outcome, Deal Final)> RaceAsync(Func<DealsService, string, Guid, Guid, Task> otherWrite)
    {
        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = DealShipping.DemoStore + "-zz-" + Guid.NewGuid().ToString("N")[..8];
        var buyer = Guid.NewGuid();
        var seller = Guid.NewGuid();
        try
        {
            var plain = new DealsService(store, new ForbiddenPaymentProvider());
            var deal = await ShippedAsync(plain, slug, buyer, seller);

            // الرَمزُ لِـtheme-demo وَحدَه — فَالوارِدُ يُنادى بِاسمِه، والمُستَأجِرُ المُؤَقَّتُ يُمَرَّرُ إلى المَخزَنِ تَحتَه.
            var gate = new SaveGate.Gate();
            var receiver = await ReceiverAsync(SaveGate.Wrap(TenantAlias.Wrap(store, DealShipping.DemoStore, slug), gate),
                TenantAlias.Wrap(store, DealShipping.DemoStore, slug), new OtoRouteHandler(), seller);

            var receiving = Task.Run(async () =>
            {
                gate.Marked.Value = true;
                return await receiver.ReceiveAsync(DealShipping.DemoStore, OtoOrderStatusTests.Notice(deal.Id));
            });
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(60));
            await otherWrite(plain, slug, deal.Id, seller);
            gate.Release.TrySetResult();
            var outcome = await receiving.WaitAsync(TimeSpan.FromSeconds(60));

            var final = (await plain.LoadAsync(slug, deal.Id))!;
            output.WriteLine($"الوارِد: {outcome.Code} · النِهائِيَّة: {final.Status}/{final.Stage} · سِجِلّ: " +
                             string.Join(",", final.Timeline.Select(e => e.Action == "advanced" ? $"advanced→{e.StageAfter}" : e.Action)));
            return (outcome, final);
        }
        finally
        {
            await WipeAsync(store, slug);
        }
    }

    /// <summary><b>المُوجَب</b>: مَرجِعٌ يُلحَقُ ويُحفَظُ بَينَ قَرارِ الوارِدِ وحِفظِه — يَرُدُّ Marten الحِفظ، فَيُعادُ القَرارُ عَلى ما
    /// كُتِبَ ويُكتَبُ التَسليم: الكِتابَتانِ قائِمَتان، وسَطرُ تَسليمٍ واحِد.</summary>
    [Fact]
    public async Task A_write_saved_between_the_inbound_decision_and_its_save_stands_and_the_delivery_is_re_decided()
    {
        if (!Enabled) return;

        var (outcome, final) = await RaceAsync((plain, slug, id, _) => plain.AttachRefAsync(slug, id, "probe_note", "live"));

        Assert.Equal(OtoOrderStatusGate.Delivered, outcome.Code);
        Assert.Equal((DealStatus.Active, DealStage.Delivered), (final.Status, final.Stage));
        Assert.Equal("live", final.Refs["probe_note"]);
        Assert.Single(final.Timeline, e => e.Action == "advanced" && e.StageAfter == DealStage.Delivered);
        Assert.Single(final.Timeline, e => e.Action == DealEvent.CashOnDelivery);
    }

    /// <summary><b>دُخولُ «شَحن» مِن الوارِدِ عَلى Postgres</b> (‏2026-10-12، مُراجَعَةُ المَوجَة 3a؛ ‏ADR-033 §٧‑ط): صَفقَةٌ عِندَ «تأكيد» بِمَرجِعَي
    /// طَلَبِها، وOTO تُجيبُ <c>searchingDriver</c> بِبوليصَة ⇒ <c>shipped</c> والصَفقَةُ عِندَ «شَحن» بِالبائِع؛ ثُمَّ <c>delivered</c> ⇒ «تَسليم»
    /// بِسَطرِ القَبضِ النَقديّ — بِالمَخزَنِ الحَقيقيِّ وتَزامُنِه التَفاؤُليّ.</summary>
    [Fact]
    public async Task A_notice_whose_shipment_OTO_confirms_moves_a_Confirmed_deal_into_Shipping_then_Delivered_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = DealShipping.DemoStore + "-zz-" + Guid.NewGuid().ToString("N")[..8];
        var seller = Guid.NewGuid();
        try
        {
            var plain = new DealsService(store, new ForbiddenPaymentProvider());
            var born = await plain.StartAsync(slug, "marketplace", Guid.NewGuid(), "مُشتَرٍ", null, "سِلعَة", 150m, new()
            {
                [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery,
                [Deal.DeliveryAddressAttribute] = "حَيُّ المِثال",
                [Deal.DeliveryPhoneAttribute] = "0500000000",
            });
            await plain.AttachRefAsync(slug, born.Id, DealsService.ListingOwnerRef, seller.ToString());
            await plain.BindListingOwnerAsync(slug, born.Id, seller.ToString());
            Assert.True((await plain.AcceptAsync(slug, born.Id, new DealAcceptor(seller, "بائِع"))).Ok);
            Assert.True((await plain.AdvanceAsync(slug, born.Id, seller, "بائِع")).Ok);   // ← Confirmed
            await plain.AttachRefAsync(slug, born.Id, DealShipping.OrderRef, born.Id.ToString("N"));
            await plain.AttachRefAsync(slug, born.Id, DealShipping.OtoIdRef, "540789");

            var oto = new OtoRouteHandler();
            var aliased = TenantAlias.Wrap(store, DealShipping.DemoStore, slug);
            var receiver = await ReceiverAsync(aliased, aliased, oto, seller);
            oto.Routes[OtoClient.OrderStatusPath] = body =>
                OtoRouteHandler.Tracking(OtoRouteHandler.OrderIdIn(body), status: "searchingDriver");
            var first = await receiver.ReceiveAsync(DealShipping.DemoStore, OtoOrderStatusTests.Notice(born.Id, status: "searchingDriver"));
            var shipped = (await plain.LoadAsync(slug, born.Id))!;

            oto.Routes[OtoClient.OrderStatusPath] = body =>
                OtoRouteHandler.Tracking(OtoRouteHandler.OrderIdIn(body), status: "delivered");
            var second = await receiver.ReceiveAsync(DealShipping.DemoStore, OtoOrderStatusTests.Notice(born.Id));
            var final = (await plain.LoadAsync(slug, born.Id))!;

            output.WriteLine($"الأَوَّل: {first.Code} · {shipped.Status}/{shipped.Stage} · الثاني: {second.Code} · {final.Status}/{final.Stage} · سِجِلّ: " +
                             string.Join(",", final.Timeline.Select(e => e.Action == "advanced" ? $"advanced→{e.StageAfter}" : e.Action)));
            Assert.Equal(OtoOrderStatusGate.Shipped, first.Code);
            Assert.Equal((DealStatus.Active, DealStage.Shipping), (shipped.Status, shipped.Stage));
            Assert.Equal(OtoOrderStatusGate.Delivered, second.Code);
            Assert.Equal((DealStatus.Active, DealStage.Delivered), (final.Status, final.Stage));
            Assert.Single(final.Timeline, e => e.Action == "advanced" && e.StageAfter == DealStage.Shipping && e.ActorId == seller);
            Assert.Single(final.Timeline, e => e.Action == DealEvent.CashOnDelivery);
        }
        finally
        {
            await WipeAsync(store, slug);
        }
    }

    /// <summary><b>السالِب</b>: البائِعُ يُعلِنُ «تَسليم» بِزِرِّه ويُحفَظُ بَينَ قَرارِ الوارِدِ وحِفظِه — يَرُدُّ Marten الحِفظ، ويُعادُ
    /// القَرارُ فَيُجابُ «سَبَق»: لا انتِقالَ ثانٍ، ولا «تَقييم» بِاسمِ البائِع.</summary>
    [Fact]
    public async Task A_manual_delivery_saved_between_the_inbound_decision_and_its_save_is_answered_already_delivered()
    {
        if (!Enabled) return;

        var (outcome, final) = await RaceAsync(async (plain, slug, id, seller) =>
            Assert.True((await plain.AdvanceAsync(slug, id, seller, "بائِع")).Ok));

        Assert.Equal(OtoOrderStatusGate.AlreadyDelivered, outcome.Code);
        Assert.Equal((DealStatus.Active, DealStage.Delivered), (final.Status, final.Stage));
        Assert.Single(final.Timeline, e => e.Action == "advanced" && e.StageAfter == DealStage.Delivered);
        Assert.DoesNotContain(final.Timeline, e => e.StageAfter == DealStage.Reviewed);
    }

    /// <summary>
    /// <para><b>الوارِدُ عَبرَ HTTP عَلى Postgres لِمَتجَرٍ لا يَفتَحُه إلّا مَدخَلُ بائِع</b> (‏2026-10-12، مُراجَعَةُ تَيّارِ S8، الجولة 2): <b>بِخِزانَةٍ يُقرَأُ الجِسمُ</b>
    /// (المِجَسُّ يَرمي عِندَ أَوَّلِ قِراءَةٍ فَيَصيرُ ‏500 — بُرهانُ «قُرِئ»)، <b>وبِلاها ‏404 فارِغٌ بِصِفرِ قِراءَة</b>. والمَتجَرُ مُستَأجِرٌ مُؤَقَّتٌ لا رَمزَ
    /// تَهيئَةٍ لَه، فَلا يَفتَحُه إلّا مَدخَلُه — وجَوابُ «أَفيه مَدخَل؟» مِن Postgres، وهُوَ ما لا يُجيبُه عالَمُ الوَثائِق.</para>
    /// </summary>
    [Fact]
    public async Task A_store_with_only_vendor_entries_reads_the_inbound_body_with_a_vault_and_answers_404_unread_without_one_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "vs-zz-h-" + Guid.NewGuid().ToString("N")[..8];
        var seller = Guid.NewGuid();
        try
        {
            var providers = new TenantProviderService(store);
            var vault = DealShipTests.NewVault();
            var entry = DealShipTests.VendorEntry(vault, slug, seller);
            await providers.BindVendorAsync(slug, ProviderCapabilities.Delivery, seller, entry.ProviderSlug,
                entry.Values, entry.WebhookId, null, seller.ToString("D"));
            var options = Options.Create(new OtoOptions { Environment = OtoEnvironment.Staging, RefreshToken = "RT-live-proof" });
            DealShipping ShippingWith(ProviderVault keys) => new(new DealsService(store, new ForbiddenPaymentProvider()),
                new OtoClient(new HttpClient(new OtoRouteHandler()), options, new OtoTokenCache(), NullLogger<OtoClient>.Instance), options,
                null, providers, keys, new FixedClock(DealShipTests.Now));
            var notice = OtoOrderStatusTests.NoticeJson(Guid.NewGuid().ToString("N"));

            var opened = new BodyTripwire(throws: true);
            HttpStatusCode openedStatus;
            await using (var host = await OtoHost.StartAsync(ShippingWith(vault), opened))
            using (var resp = await host.PostAsync(slug, notice))
                openedStatus = resp.StatusCode;

            var closed = new BodyTripwire(throws: true);
            HttpStatusCode closedStatus;
            string closedBody;
            await using (var host = await OtoHost.StartAsync(ShippingWith(ProviderVault.FromKek(null)), closed))
            using (var resp = await host.PostAsync(slug, notice))
                (closedStatus, closedBody) = (resp.StatusCode, await resp.Content.ReadAsStringAsync());

            output.WriteLine($"بِخِزانَة: {(int)openedStatus} وقِراءات {opened.Reads} · بِلاها: {(int)closedStatus} وقِراءات {closed.Reads}");
            Assert.Equal(HttpStatusCode.InternalServerError, openedStatus);
            Assert.True(opened.Reads > 0, $"قِراءاتُ الجِسمِ بِخِزانَة: {opened.Reads}");
            Assert.Equal((HttpStatusCode.NotFound, "", 0), (closedStatus, closedBody, closed.Reads));
        }
        finally
        {
            await WipeAsync(store, slug);
        }
    }
}
