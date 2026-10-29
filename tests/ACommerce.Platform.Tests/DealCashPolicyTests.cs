using System.Reflection;
using System.Text.RegularExpressions;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ صَفقَةُ النَقدِ لا تَزورُ Paid، والبائِعُ طَرَفٌ مِن الوِلادَة — ‏ADR-033 §٦ ═══
//
// شَطرُ الصَفَقاتِ مِن ‏ADR-033 §٢‑هـ الخُطوَة 3 (‏2026-10-11)، بَعدَ تَوصيفِ
// `DealCashPathCharacterizationTests` (‏5554975). ثَلاثَةُ قَراراتٍ، لِكُلٍّ مُوجِبٌ
// وسالِب (القاعِدَة ٤):
//   ١) **النَمَطُ بَياناتٌ والإسقاطُ كود** (القاعِدَة ٥): `Next(pattern, stage)`
//      كَما كانَت حَرفاً، و`Next(pattern, stage, payMethod)` تَتَخَطّى `Paid` لِلنَقدِ
//      وَحدَه. البِطاقَةُ والغِيابُ يَمشِيانِ الطَريقَ كامِلاً.
//   ٢) **البائِعُ طَرَفٌ مِن الوِلادَة**: `/checkout/submit` يَربُطُه بَعدَ المَرجِع،
//      و`/accept` فِعلُه هُوَ بِهُوِيَّتِه — لا بِاستِعارَةِ المُبادِر — عَبرَ
//      `MoveAsync` نَفسِها الَّتي تَكتُبُ لِـ`AdvanceAsync`.
//   ٣) **هُوِيَّةُ المالِكِ بِالقيمَة** في البَوّابَةِ والقائِمَةِ والشاشَة — دالَّةٌ واحِدَة.
//
// **وما يُحرَسُ مِن غَيرِ تَغيير**: الجَدوَلُ الذَهَبِيُّ (`DealsPolicyCharacterizationTests`
// لَم يُمَسَّ حَرفاً)، وفاعِلُ كُلِّ مَرحَلَة، والقَبضُ عِندَ `Paid` لِلبِطاقَة
// (`DealPaidStageCharacterizationTests`). **وما يُقالُ ولا يُقَرَّرُ هُنا**: صَفُّ
// `Confirmed = either` بَقِيَ بَياناتٍ كَما هُو، فَالمُشتَري أَيضاً يَستَطيعُ نَقلَ
// صَفقَةِ نَقدٍ مُؤَكَّدَةٍ إلى `Shipping` — سُؤالٌ لِلمالِك (سِجِلُّ «ب»).
// **وأُغلِقَ ‏2026-10-11 (‏ADR-033 §٦‑ط) — ولا يُعادُ حَرفٌ فَوقَه**: الخُروجُ مِن `Confirmed` يَسأَلُ
// اليَومَ فاعِلَ المَدخولَة (`Shipping` = الطَرَف) فَلا يَنقُلُها المُشتَري، والصَفُّ `either` بِحَرفِه.

public class DealCashPolicyTests
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    private static string Source(params string[] parts)
    {
        var path = Path.Combine(RepoRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياءُ بِلا طَرَفٍ مَقروء.");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 500, $"أَداةٌ عَمياء: {path} طولُه {text.Length} مِحرَفاً — لَم يُقرَأ.");
        return text;
    }

    private static string Segment(string source, string from, string to)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start > 0, $"‏`{from}` غَيرُ مَوجود — الأَداةُ عَمياء.");
        var end = source.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"‏`{to}` لا يَتلو `{from}` — الأَداةُ عَمياء.");
        return WriteEndpointGuardTests.StripComments(source.Substring(start, end - start));
    }

    private static string DealsServiceSource() => Source(Template, "Services", "Deals", "DealsService.cs");
    private static string DealPageSource()     => Source(Template, "Components", "Pages", "MyDealDetail.razor");
    private static string StudioPageSource()   => Source(Template, "Components", "Pages", "StudioAppDealDetail.razor");
    private static string EndpointsSource()    => Source(Template, "MarketplaceTemplateExtensions.cs");

    private static string AssignBody()  => Segment(DealsServiceSource(), "public async Task<Deal?> AssignCounterpartyAsync(", "public async Task<Deal?> BindListingOwnerAsync(");
    private static string BindBody()    => Segment(DealsServiceSource(), "public async Task<Deal?> BindListingOwnerAsync(",   "public async Task<DealAcceptResult> AcceptAsync(");
    private static string AcceptBody()  => Segment(DealsServiceSource(), "public async Task<DealAcceptResult> AcceptAsync(",  "public async Task<DealAdvanceResult> AdvanceAsync(");
    private static string AdvanceBody() => Segment(DealsServiceSource(), "public async Task<DealAdvanceResult> AdvanceAsync(", "private async Task<Deal> MoveAsync(");
    private static string MoveBody()    => Segment(DealsServiceSource(), "private async Task<Deal> MoveAsync(",               "public async Task<DealCancelResult> CancelAsync(");
    private static string Endpoint(string route) => Segment(EndpointsSource(), "\"" + route + "\"", ".RequireStoreWritable();");

    private static int Count(string text, string needle)
    {
        var n = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            n++;
        return n;
    }

    private static readonly MethodInfo ActorGate = typeof(DealsService)
        .GetMethod("IsActorAllowed", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("‏`DealsService.IsActorAllowed` غَيرُ مَوجودَة — الأَداةُ عَمياء.");

    private static bool Allowed(Deal deal, Guid actor, string required)
        => (bool)ActorGate.Invoke(null, new object[] { deal, actor, required })!;

    private static readonly Guid Buyer    = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Seller   = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly Guid AppOwner = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
    private static readonly Guid Stranger = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");

    /// <summary>صَفقَةُ سَلَّةٍ كَما تَخرُجُ مِن <c>/checkout/submit</c> بَعدَ هذا
    /// القَرار: المُشتَري مُبادِر، ومالِكُ الإعلانِ في المَرجِعِ <b>وطَرَفٌ مُقابِلٌ مِن
    /// الوِلادَة</b>، وطَريقَةُ الدَفعِ المُطَبَّعَة.</summary>
    private static Deal CartDeal(string? payMethod, DealStage stage = DealStage.Offered, Guid? counterparty = null, bool bound = true)
    {
        var d = new Deal
        {
            Id = Guid.NewGuid(), Pattern = "marketplace", Stage = stage, AmountSar = 100,
            InitiatorId = Buyer, CounterpartyId = counterparty ?? (bound ? Seller : null),
        };
        d.Refs[DealsService.ListingOwnerRef] = Seller.ToString();
        if (payMethod is not null) d.Attributes[CheckoutPayMethods.AttributeKey] = payMethod;
        return d;
    }

    private static Deal Cash(DealStage stage = DealStage.Offered) => CartDeal(CheckoutPayMethods.CashOnDelivery, stage);
    private static Deal Card(DealStage stage = DealStage.Offered) => CartDeal(CheckoutPayMethods.Card, stage);
    private static Deal Bare(DealStage stage = DealStage.Offered) => CartDeal(null, stage);

    // ═══ ١) المِفتاحُ والقاعِدَة ═══════════════════════════════════════════

    /// <summary>الكاتِبُ (<c>/checkout/submit</c>) والقارِئُ (<c>DealsPolicy</c>) يَقرَآنِ
    /// نَفسَ الحَرف — والحَرفُ مُثَبَّتٌ في النُقطَةِ كَما كانَ.
    /// <para><b>قُلِبَ بِاسمِه</b> (مُراجَعَةُ المَوجَةِ الثانِيَة، ‏2026-10-11): كانَ
    /// <c>The_attribute_key_is_the_letter_the_checkout_writes</c> — الكاتِبُ بِحَرفِيَّةٍ <c>"pay_method"</c> تُطابِقُ
    /// الثابِتَ صُدفَةَ نَسخ، والقارِئُ بِالثابِت. وصارَ الكاتِبُ الثابِتَ نَفسَه (السَطرُ هُوَ هُوَ عَدّاً)، فَتَغييرُ
    /// الحَرفِ في مَوضِعٍ واحِدٍ يَبلُغُ الاثنَين.</para></summary>
    [Fact]
    public void The_checkout_writes_the_attribute_key_constant_the_policy_reads()
    {
        Assert.Equal("pay_method", CheckoutPayMethods.AttributeKey);
        var submit = Endpoint("/{slug}/checkout/submit");
        Assert.Contains("[Services.Deals.CheckoutPayMethods.AttributeKey] = payMethod", submit, StringComparison.Ordinal);
        Assert.DoesNotContain("\"pay_method\"", submit, StringComparison.Ordinal);
    }

    /// <summary><b>القاعِدَةُ الواحِدَة</b>: <c>Paid</c> وَحدَها، لِلنَقدِ وَحدَه.</summary>
    [Fact]
    public void Only_Paid_is_skipped_and_only_for_cash()
    {
        Assert.True(DealsPolicy.Skips(DealStage.Paid, CheckoutPayMethods.CashOnDelivery));
        Assert.False(DealsPolicy.Skips(DealStage.Paid, CheckoutPayMethods.Card));
        Assert.False(DealsPolicy.Skips(DealStage.Paid, null));
        Assert.False(DealsPolicy.Skips(DealStage.Paid, "CASH"));   // بِحَرفِ المَعجَم لا بِمُرادِفِه
        foreach (var stage in Enum.GetValues<DealStage>().Where(s => s != DealStage.Paid))
            Assert.False(DealsPolicy.Skips(stage, CheckoutPayMethods.CashOnDelivery), $"‏{stage} أُسقِطَت ولَيسَت Paid.");
    }

    /// <summary>الغِيابُ لَيسَ نَقداً هُنا — بِخِلافِ <c>Normalize</c> عِندَ الباب:
    /// عَرضٌ عَلى إعلانٍ بِلا <c>pay_method</c> يَمشي الطَريقَ كامِلاً.</summary>
    [Fact]
    public void PayMethodOf_reads_the_attribute_and_does_not_normalise_absence_to_cash()
    {
        Assert.Equal("cash", DealsPolicy.PayMethodOf(Cash()));
        Assert.Equal("card", DealsPolicy.PayMethodOf(Card()));
        Assert.Null(DealsPolicy.PayMethodOf(Bare()));
        Assert.True(DealsPolicy.IsCashOnDelivery(Cash()));
        Assert.False(DealsPolicy.IsCashOnDelivery(Card()));
        Assert.False(DealsPolicy.IsCashOnDelivery(Bare()));
        Assert.Equal(CheckoutPayMethods.CashOnDelivery, CheckoutPaymentPolicy.Normalize(null));   // البابُ يُطَبِّع، والسِياسَةُ لا
    }

    // ═══ ٢) المَراحِلُ والتالي ═══════════════════════════════════════════

    /// <summary><b>المُوجِب</b>: صَفقَةُ نَقدٍ في <c>marketplace</c> تَمشي
    /// <c>Confirmed → Shipping</c>، ولا تَبلُغُ <c>Paid</c> مِن أَيِّ مَرحَلَة.</summary>
    [Fact]
    public void A_cash_marketplace_deal_goes_Confirmed_to_Shipping_and_never_visits_Paid()
    {
        Assert.Equal(
            new[] { DealStage.Offered, DealStage.Booked, DealStage.Confirmed, DealStage.Shipping, DealStage.Delivered, DealStage.Reviewed },
            DealsPolicy.StagesFor(Cash()).ToArray());
        Assert.Equal(DealStage.Shipping, DealsPolicy.Next(Cash(DealStage.Confirmed)));
        foreach (var stage in Enum.GetValues<DealStage>())
            Assert.NotEqual(DealStage.Paid, DealsPolicy.Next("marketplace", stage, CheckoutPayMethods.CashOnDelivery));
    }

    /// <summary><b>السالِب</b>: البِطاقَةُ، والصَفقَةُ بِلا طَريقَةِ دَفع، لا يَتَغَيَّرُ
    /// فيهِما حَرف — والحِملُ الثُلاثِيُّ بِـ<c>null</c> يُساوي الثُنائِيَّ عَلى كُلِّ
    /// نَمَطٍ وكُلِّ مَرحَلَة (الجَدوَلُ الذَهَبِيُّ مَحروس).</summary>
    [Fact]
    public void A_card_deal_and_a_deal_without_pay_method_keep_the_full_path()
    {
        Assert.Equal(DealStage.Paid, DealsPolicy.Next(Card(DealStage.Confirmed)));
        Assert.Equal(DealStage.Paid, DealsPolicy.Next(Bare(DealStage.Confirmed)));
        Assert.Equal(DealsPolicy.StagesFor("marketplace"), DealsPolicy.StagesFor(Card()));
        Assert.Equal(DealsPolicy.StagesFor("marketplace"), DealsPolicy.StagesFor(Bare()));

        var checked_ = 0;
        foreach (var pattern in new[] { "trip", "rental", "marketplace", "classifieds", "service", "pattern_from_the_future" })
        foreach (var stage in Enum.GetValues<DealStage>())
        {
            Assert.Equal(DealsPolicy.Next(pattern, stage), DealsPolicy.Next(pattern, stage, null));
            Assert.Equal(DealsPolicy.Next(pattern, stage), DealsPolicy.Next(pattern, stage, CheckoutPayMethods.Card));
            checked_++;
        }
        Assert.Equal(48, checked_);
    }

    /// <summary>الإسقاطُ يَعمَلُ في كُلِّ نَمَطٍ فيه <c>Paid</c>، ولا يَمَسُّ نَمَطاً
    /// بِلا <c>Paid</c>.</summary>
    [Theory]
    [InlineData("rental",      DealStage.Confirmed, DealStage.Delivered)]
    [InlineData("service",     DealStage.Confirmed, DealStage.Delivered)]
    [InlineData("marketplace", DealStage.Confirmed, DealStage.Shipping)]
    [InlineData("trip",        DealStage.Confirmed, DealStage.Delivered)]
    [InlineData("classifieds", DealStage.Booked,    DealStage.Confirmed)]
    public void Cash_skips_Paid_in_every_pattern_that_has_it(string pattern, DealStage from, DealStage expected)
        => Assert.Equal(expected, DealsPolicy.Next(pattern, from, CheckoutPayMethods.CashOnDelivery));

    /// <summary>صَفقَةُ نَقدٍ قَديمَةٌ بَلَغَت <c>Paid</c> قَبلَ هذا القَرار لا تُحبَس:
    /// تَمضي مِنها إلى <c>Shipping</c> كَالمُعتاد.</summary>
    [Fact]
    public void A_legacy_cash_deal_already_at_Paid_is_not_trapped()
    {
        Assert.Equal(DealStage.Shipping, DealsPolicy.Next("marketplace", DealStage.Paid, CheckoutPayMethods.CashOnDelivery));
        Assert.Equal(DealStage.Shipping, DealsPolicy.Next(Cash(DealStage.Paid)));
    }

    /// <summary><b>المُوجِب</b> (مُراجَعَةُ المَوجَةِ الثانِيَة، ‏2026-10-11): صَفقَةُ النَقدِ القَديمَةُ الواقِفَةُ عِندَ
    /// <c>Paid</c> تَأخُذُ قائِمَةً فيها <c>Paid</c> في مَوضِعِها — كانَت بِلاها فَلا يَجِدُ الـstepper مَرحَلَتَها
    /// (<c>IndexOf</c> = ‏‎-1) ولا يُعَلِّمُ مُنجَزاً ولا حالِيّاً.</summary>
    [Fact]
    public void A_legacy_cash_deal_standing_at_Paid_keeps_Paid_in_its_stage_list_so_the_stepper_finds_it()
    {
        var legacy = Cash(DealStage.Paid);
        var stages = DealsPolicy.StagesFor(legacy);
        Assert.Equal(DealsPolicy.StagesFor("marketplace"), stages);
        Assert.Equal(3, stages.ToList().IndexOf(DealStage.Paid));
        Assert.Equal(new[] { DealStage.Offered, DealStage.Booked, DealStage.Confirmed },
            stages.Where(st => stages.ToList().IndexOf(st) < stages.ToList().IndexOf(legacy.Stage)).ToArray());
    }

    /// <summary><b>السالِب</b>: صَفقَةُ نَقدٍ في أَيِّ مَرحَلَةٍ سِوى <c>Paid</c> لا تَرى <c>Paid</c> في قائِمَتِها —
    /// الإبقاءُ لِمَرحَلَةِ الصَفقَةِ وَحدَها، والبِطاقَةُ كَما كانَت.</summary>
    [Fact]
    public void A_cash_deal_at_any_other_stage_never_lists_Paid_and_a_card_deal_always_does()
    {
        var checkedStages = 0;
        foreach (var stage in DealsPolicy.StagesFor("marketplace").Where(st => st != DealStage.Paid))
        {
            Assert.DoesNotContain(DealStage.Paid, DealsPolicy.StagesFor(Cash(stage)));
            Assert.Contains(DealStage.Paid, DealsPolicy.StagesFor(Card(stage)));
            checkedStages++;
        }
        Assert.Equal(6, checkedStages);
    }

    // ═══ ٣) الفاعِلُ — الخِدمَةُ والشاشَةُ مِن دالَّةٍ واحِدَة ═══════════════════

    /// <summary><b>المُوجِب</b>: عِندَ <c>Confirmed</c> يَنقُلُ البائِعُ (الطَرَفُ
    /// المُقابِل) صَفقَةَ النَقدِ إلى <c>Shipping</c> مُباشَرَةً — وهُوَ ما لَم يَكُن
    /// يَستَطيعُه (P1-3). والصَفُّ <c>Confirmed = either</c> بَياناتٌ لَم تُمَسّ،
    /// فَالمُشتَري أَيضاً يَمُرُّ مِن البَوّابَة — يُقالُ ولا يُبتلَع.
    /// <para><b>وأُلحِقَ ‏2026-10-11 (‏ADR-033 §٦‑ط) — ولا يُعادُ حَرفٌ فَوقَه</b>: «المُشتَري أَيضاً يَمُرُّ» صَدَقَ
    /// يَومَ كانَت الخِدمَةُ تَسأَلُ فاعِلَ المَرحَلَةِ الحالِيَّة؛ وتَسأَلُ اليَومَ فاعِلَ المَدخولَة
    /// (<c>Shipping</c> = الطَرَف) فَالمُشتَري مَردود — مَقيسٌ في الصَفِّ التالي. وما يُثَبِّتُه هذا الصَفُّ
    /// باقٍ بِحَرفِه: الصَفُّ <c>either</c> في الكاتالوج، ودَلالَةُ <c>either</c> في البَوّابَة، ودَورُ البائِع.</para></summary>
    [Fact]
    public void At_Confirmed_the_counterparty_moves_a_cash_deal_straight_to_Shipping_and_the_catalog_row_still_says_either()
    {
        var deal = Cash(DealStage.Confirmed);
        Assert.Equal("either", DealsPolicy.Actor(deal.Stage));
        Assert.True(Allowed(deal, Seller, "either"));
        Assert.True(Allowed(deal, Buyer, "either"));
        Assert.False(Allowed(deal, Stranger, "either"));
        Assert.Equal(DealStage.Shipping, DealsPolicy.Next(deal));

        var seller = DealTurnView.For(deal, DealSide.Counterparty);
        Assert.True(seller.IsMine);
        Assert.Equal(DealStage.Shipping, seller.NextStage);
    }

    /// <summary><b>السالِب</b>: لا مَوضِعَ تُقالُ فيه لِمُشتَري النَقدِ «الخُطوَةُ
    /// التالِيَة: دَفع» — وبِطاقَةُ البِطاقَةِ كَما كانَت.</summary>
    [Fact]
    public void The_turn_view_never_promises_a_cash_buyer_the_Paid_stage_and_leaves_the_card_buyer_as_before()
    {
        foreach (var stage in DealsPolicy.StagesFor(Cash()))
        {
            var cash = Cash(stage);
            Assert.NotEqual(DealStage.Paid, DealTurnView.For(cash, DealSide.Initiator).NextStage);
            Assert.NotEqual(DealStage.Paid, DealTurnView.For(cash, DealSide.Counterparty).NextStage);
        }
        var card = DealTurnView.For(Card(DealStage.Confirmed), DealSide.Initiator);
        Assert.Equal(DealStage.Paid, card.NextStage);
        Assert.Equal(DealTurnView.For("marketplace", DealStage.Confirmed, DealSide.Initiator), card);
    }

    /// <summary><b>ما بَقِيَ مِن عَرَضِ P1-3 — يُقالُ ويُثَبَّتُ بِاسمِه</b> (مُراجَعَةُ ‏2026-10-11 عَلى
    /// <c>8769fa4</c>؛ ‏ADR-033 §٦‑ج مُلحَقاً): إسقاطُ <c>Paid</c> نَقَلَ «دَورُكَ الآن … تَأكيد: شَحن»
    /// مِن <c>Paid</c> إلى <c>Confirmed</c> ولَم يُزِله. صَفُّ <c>Confirmed = either</c> يَجعَلُ الدَورَ
    /// لِلمُشتَري (المُبادِر) والتالي <c>Shipping</c>، فَتَقولُ لَه البِطاقَةُ «دَورُكَ الآن» ويُرسَمُ لَه
    /// زِرُّ «تَأكيد: شَحن» وهُوَ لا يَشحَن — والمُحَرِّكُ يَقبَلُ نَقرَتَه، فَالواجِهَةُ لا تَكذِبُ عَلى
    /// البَوّابَة. <b>والسالِبُ الَّذي يَقيسُه الصَفُّ السابِقُ</b> («لا دَفعَ» لِمُشتَري النَقد) لا يَنفي
    /// هذا. يَبقى حَتّى يُجيبَ المالِكُ «ب» S4·٤: فَإن اختارَ (٢) — فاعِلُ <c>Confirmed</c> لِلنَقدِ
    /// <c>counterparty</c> وَحدَه — نُفِّذَ قاعِدَةَ كودٍ خَلفَ <c>CheckoutPayMethods.CashOnDelivery</c>
    /// كَـ<c>Skips</c> بِمُوجِبٍ وسالِب، لا تَبديلاً لِصَفِّ الكاتالوجِ المُشتَرَك، وقُلِبَ هذا الصَفُّ
    /// بِاسمِه.
    /// <para><b>قُلِبَ بِاسمِه — بِلا قاعِدَةِ نَقدٍ ثانِيَة</b> (‏ADR-033 §٦‑ط، ‏2026-10-11؛ يُغلِقُ «ب» S4·٤): كانَ
    /// <c>Today_the_cash_buyer_at_Confirmed_is_still_told_it_is_his_turn_and_offered_confirm_Shipping</c>. والجُملَةُ لَم
    /// تَكُن سُؤالَ نَقدٍ بَل عَرَضَ إزاحَةٍ في قِراءَةِ عَمودِ الفاعِل: الخِدمَةُ والشاشَةُ تَسأَلانِ فاعِلَ المَرحَلَةِ
    /// الحالِيَّة (<c>Confirmed = either</c>). وصارَتا تَسأَلانِ فاعِلَ المَدخولَة (<c>Shipping</c> = الطَرَف) — فَالمُشتَري
    /// «بانتِظار الطَّرَف الآخَر لِـ: شَحن» بِلا زِرّ، والبائِعُ وَحدَه يُعلِنُ الشَحن، والمُحَرِّكُ يَرُدُّ نَقرَةَ المُشتَري.
    /// والصَفُّ <c>either</c> في الكاتالوجِ لَم يُمَسّ — هُوَ اليَومَ مَن يُؤَدّي «تَأكيد» لا مَن يُخرِجُ مِنها.</para></summary>
    [Fact]
    public void The_cash_buyer_at_Confirmed_is_no_longer_told_it_is_his_turn_and_the_seller_alone_confirms_Shipping()
    {
        var deal = Cash(DealStage.Confirmed);

        // البِطاقَة: الدَورُ لِلبائِع، والمُشتَري يَنتَظِرُ «الطَرَفَ الآخَر»، والتالي شَحن.
        var buyer = DealTurnView.For(deal, DealSide.Initiator);
        var seller = DealTurnView.For(deal, DealSide.Counterparty);
        Assert.False(buyer.IsMine);
        Assert.True(seller.IsMine);
        Assert.Equal("counterparty", buyer.ActorKey);
        Assert.Equal("deals.actor.counterparty", DealTurnView.ActorLabelKey(buyer.ActorKey, DealSide.Initiator));
        Assert.Equal(DealStage.Shipping, buyer.NextStage);

        // الشاشَة: `canAdvance` مِن الدالَّةِ نَفسِها، فَالمُشتَري يَرى سَطرَ الانتِظارِ لا الزِرّ.
        var page = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(DealPageSource()));
        Assert.Contains("var mine = acceptCase || turn.IsMine;", page, StringComparison.Ordinal);
        Assert.Contains("var required = DealsPolicy.RequiredActorToLeave(deal);", page, StringComparison.Ordinal);
        Assert.Contains("\"counterparty\" => deal.CounterpartyId == uid,", page, StringComparison.Ordinal);
        Assert.Contains("else if (nextStage is { } waiting)", page, StringComparison.Ordinal);
        Assert.Equal("بانتِظار الطَّرَف الآخَر لِـ: شَحن",
            $"{LocaleCatalog.Text(LocaleCatalog.Arabic, "deals.detail.awaiting_other")} {DealsPolicy.LabelAr(buyer.NextStage!.Value)}");

        // والمُحَرِّك: نَقرَةُ المُشتَري تُرَدُّ، ونَقرَةُ البائِعِ تَمُرّ — مِن الدالَّةِ نَفسِها.
        var required = DealsPolicy.RequiredActorToLeave(deal)!;
        Assert.False(Allowed(deal, Buyer, required));
        Assert.True(Allowed(deal, Seller, required));
    }

    /// <summary>الخِدمَةُ والشاشَتانِ والـAPI وشَرحُ التَدَفُّقِ يَقرَؤونَ الدالَّةَ
    /// نَفسَها مِن الصَفقَة، <b>ولا مَوضِعَ باقٍ</b> يَشتَقُّ التالي مِن
    /// (النَمَط، المَرحَلَة) وَحدَهُما — بِالعَدِّ عَلى <c>libs/</c> و<c>apps/</c>.</summary>
    [Fact]
    public void The_service_the_screens_and_the_api_read_the_same_pay_method_aware_function()
    {
        Assert.Contains("var next = DealsPolicy.Next(deal);", AdvanceBody(), StringComparison.Ordinal);

        var page = DealPageSource();
        Assert.Contains("Services.Ux.DealTurnView.For(deal, mySide)", page, StringComparison.Ordinal);
        Assert.Contains("stages = DealsPolicy.StagesFor(deal).ToList();", page, StringComparison.Ordinal);
        Assert.Contains("nextStage = DealsPolicy.Next(deal);", page, StringComparison.Ordinal);
        Assert.Contains("PayMethod=\"@DealsPolicy.PayMethodOf(deal)\"", page, StringComparison.Ordinal);

        var studio = StudioPageSource();
        Assert.Contains("DealsPolicy.Next(deal) is { } next", studio, StringComparison.Ordinal);
        Assert.Contains("stages = DealsPolicy.StagesFor(deal).ToList();", studio, StringComparison.Ordinal);

        var api = WriteEndpointGuardTests.StripComments(Source(Template, "Api", "DealApi.cs"));
        Assert.Contains("var next = DealsPolicy.Next(d);", api, StringComparison.Ordinal);
        Assert.Contains("if (DealsPolicy.Next(deal) is null) return ApiErrorCatalog.DealFinalStage;", api, StringComparison.Ordinal);

        var explainer = Source(Template, "Components", "FlowExplainer.razor");
        Assert.Contains("var stages = DealsPolicy.StagesFor(Pattern, PayMethod).ToList();", explainer, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public string? PayMethod { get; set; }", explainer, StringComparison.Ordinal);

        // **ووُسِّعَ الفاحِصُ ‏2026-10-11 (مُراجَعَةُ الموجَةِ الثانِيَةِ الثانِيَة)**: كانَ يَبحَثُ عَن حَرفَينِ
        // اثنَين — `Next(deal.Pattern, deal.Stage)` و`Next(d.Pattern, d.Stage)` — فَيَعمى عَن اسمِ مُتَغَيِّرٍ ثالِث
        // وعَن الحِملَينِ الأَعمَيَينِ الآخَرَين. صارَ يَعُدُّ بِنَمَطٍ كُلَّ حِملٍ أَعمى عَن طَريقَةِ الدَفعِ بِأَيِّ اسم —
        // خارِجَ `DealsPolicy` و`DealTurnView` (تَعريفُ الحِملِ وتَنفيذُه) — بِقائِمَةِ سَماحٍ مُسَمّاةٍ يُعَدُّ مَدخَلُها
        // فَلا تَبلى؛ والنَمَطُ نَفسُه مَقيسٌ بِعَيِّناتٍ مَحقونَةٍ في `The_pay_blind_scanner_catches_any_name_…` أَدناه.
        var scanned = 0;
        var matched = 0;
        var allowedSeen = 0;
        var strays = new List<string>();
        foreach (var root in new[] { "libs", "apps" })
        foreach (var f in Directory.EnumerateFiles(Path.Combine(RepoRoot, root), "*.*", SearchOption.AllDirectories))
        {
            if (!f.EndsWith(".cs", StringComparison.Ordinal) && !f.EndsWith(".razor", StringComparison.Ordinal)) continue;
            if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
            scanned++;
            var rel = Path.GetRelativePath(RepoRoot, f).Replace('\\', '/');
            if (PayBlindHomes.Contains(rel)) continue;
            var code = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(f)));
            foreach (Match m in PayBlind.Matches(code))
            {
                matched++;
                if (PayBlindAllowed.Any(a => a.File == rel && a.Call == m.Value)) allowedSeen++;
                else strays.Add($"{rel}: {m.Value}");
            }
        }
        Assert.True(scanned > 200, $"أَداةٌ عَمياء: فُحِصَ {scanned} مِلَفّاً.");
        Assert.True(strays.Count == 0 && allowedSeen == PayBlindAllowed.Length,
            $"فُحِصَ {scanned} مِلَفّاً وطابَقَ {matched} نِداءً أَعمى، المَسموحُ مِنها {allowedSeen} مِن {PayBlindAllowed.Length} — " +
            "مَوضِعٌ يَشتَقُّ التالي بِلا طَريقَةِ الدَفع: " + string.Join("، ", strays));

        // والمَسموحُ الوَحيدُ في مَوضِعِه المُسَمّى: خِتامُ `MoveAsync` — آخِرُ مَرحَلَةٍ لا تَتَعَلَّقُ بِطَريقَةِ الدَفع
        // (`Skips` تُسقِطُ `Paid` وَحدَها، فَـ`Last()` هِيَ هِيَ لِلنَقدِ والبِطاقَة).
        Assert.Contains("DealsPolicy.StagesFor(deal.Pattern).Last() == DealStage.Reviewed", MoveBody(), StringComparison.Ordinal);
    }

    /// <summary>الحِملُ الأَعمى عَن طَريقَةِ الدَفع — بِأَيِّ اسمِ مُتَغَيِّر: <c>Next(x.Pattern, x.Stage)</c>
    /// بِلا ثالِث، و<c>StagesFor(x.Pattern)</c> بِلا ثانٍ، و<c>DealTurnView.For(x.Pattern, x.Stage, side)</c>
    /// بِلا رابِع.</summary>
    /// <remarks><b>ووُسِّعَ ‏2026-10-13 — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق</b> (مُراجَعَةُ المَوجَة 3a — G4 (xiii)): المُستَقبِلُ <c>\w+</c> كانَ يَعمى عَن
    /// المُستَقبِلِ المُتَعَدِّدِ المَقاطِع (<c>result.Deal.Pattern</c>) وإسقاطِ العَدَمِ وشَرطِيِّه (<c>deal!.Pattern</c>، <c>res.Deal!.Stage</c>) والوَسيطِ
    /// المُسَمّى (<c>Next(pattern: x.Pattern, stage: x.Stage)</c>)؛ صارَ <c>[\w.!?]+</c> بِبادِئَةِ اسمٍ اختِيارِيَّة — بِحَرفِ فاحِصِ فاعِلِ المَرحَلَةِ
    /// الحالِيَّةِ في <c>DealActorToLeaveTests</c> — والعَيِّناتُ المَحقونَةُ أَدناه تَقيسُه.</remarks>
    private static readonly Regex PayBlind = new(
        @"\bNext\(\s*(?:\w+\s*:\s*)?[\w.!?]+\.Pattern\s*,\s*(?:\w+\s*:\s*)?[\w.!?]+\.Stage\s*\)" +
        @"|\bStagesFor\(\s*(?:\w+\s*:\s*)?[\w.!?]+\.Pattern\s*\)" +
        @"|\bDealTurnView\.For\(\s*(?:\w+\s*:\s*)?[\w.!?]+\.Pattern\s*,\s*(?:\w+\s*:\s*)?[\w.!?]+\.Stage\s*,\s*[^,()]+\)",
        RegexOptions.CultureInvariant);

    /// <summary>بَيتا الحِملِ الأَعمى: تَعريفُه وتَنفيذُ الحِملِ المُبصِرِ فَوقَه — لا يُفحَصانِ بِه.</summary>
    private static readonly HashSet<string> PayBlindHomes = new(StringComparer.Ordinal)
    {
        $"{Template}/Services/Deals/DealsPolicy.cs",
        $"{Template}/Services/Ux/DealTurnView.cs",
    };

    /// <summary>قائِمَةُ السَماحِ المُسَمّاة — كُلُّ مَدخَلٍ يُعَدُّ، فَمَدخَلٌ لا يُطابِقُه شَيءٌ يُحمِرُّ الفاحِص.</summary>
    private static readonly (string File, string Call)[] PayBlindAllowed =
    {
        ($"{Template}/Services/Deals/DealsService.cs", "StagesFor(deal.Pattern)"),
    };

    /// <summary><b>الأَداةُ مَقيسَةٌ قَبلَ أَن يُوثَقَ بِها</b> (القاعِدَة ١٠): عَيِّناتٌ مَحقونَةٌ بِأَسماءٍ لَم
    /// يَعرِفها الفاحِصُ القَديم تُطابَق، والحِملُ المُبصِرُ لا يُطابَق.</summary>
    [Theory]
    [InlineData("var n = DealsPolicy.Next(x.Pattern, x.Stage);", true)]
    [InlineData("var n = DealsPolicy.Next( order.Pattern , order.Stage );", true)]
    [InlineData("var all = DealsPolicy.StagesFor(item.Pattern).ToList();", true)]
    [InlineData("var t = Services.Ux.DealTurnView.For(row.Pattern, row.Stage, mySide);", true)]
    [InlineData("var n = DealsPolicy.Next(x.Pattern, x.Stage, DealsPolicy.PayMethodOf(x));", false)]
    [InlineData("var all = DealsPolicy.StagesFor(x.Pattern, pay);", false)]
    [InlineData("var t = DealTurnView.For(x.Pattern, x.Stage, side, pay);", false)]
    [InlineData("var n = DealsPolicy.Next(deal);", false)]
    [InlineData("var all = DealsPolicy.StagesFor(deal);", false)]
    [InlineData("var t = DealTurnView.For(deal, side);", false)]
    [InlineData("var all = DealsPolicy.StagesFor(pattern);", false)]
    // (‏2026-10-13، G4 (xiii)): مُستَقبِلٌ مُتَعَدِّدُ المَقاطِع، وإسقاطُ العَدَمِ وشَرطِيُّه، والوَسيطُ المُسَمّى — كانَ الفاحِصُ يَعمى عَنها؛
    // والحِملُ المُبصِرُ بِالأَشكالِ نَفسِها لا يُطابَق.
    [InlineData("var all = DealsPolicy.StagesFor(result.Deal.Pattern);", true)]
    [InlineData("var all = DealsPolicy.StagesFor(deal!.Pattern).ToList();", true)]
    [InlineData("var n = DealsPolicy.Next(deal!.Pattern, deal!.Stage);", true)]
    [InlineData("var n = DealsPolicy.Next(res.Deal!.Pattern, res.Deal!.Stage);", true)]
    [InlineData("var n = DealsPolicy.Next(pattern: x.Pattern, stage: x.Stage);", true)]
    [InlineData("var t = DealTurnView.For(row?.Pattern, row?.Stage, side);", true)]
    [InlineData("var n = DealsPolicy.Next(deal!.Pattern, deal!.Stage, DealsPolicy.PayMethodOf(deal!));", false)]
    [InlineData("var all = DealsPolicy.StagesFor(result.Deal.Pattern, pay);", false)]
    [InlineData("var n = DealsPolicy.Next(res.Deal!);", false)]
    public void The_pay_blind_scanner_catches_any_name_and_spares_the_pay_aware_overloads(string line, bool blind)
        => Assert.Equal(blind, PayBlind.IsMatch(line));

    // ═══ ٤) سَطرُ القَبضِ عِندَ التَسليم ════════════════════════════════════

    /// <summary>بُلوغُ <c>Delivered</c> في صَفقَةِ نَقدٍ يَكتُبُ سَطراً بِاسمِه،
    /// والنَصُّ مِن القامُوسِ عِندَ العَرضِ في الشاشَتَين (القاعِدَة ١١) — والحارِسُ
    /// نَقدِيٌّ فَلا سَطرَ لِلبِطاقَة.</summary>
    [Fact]
    public void Delivered_records_a_cash_on_delivery_line_whose_text_comes_from_the_dictionary()
    {
        Assert.Equal("cash_on_delivery", DealEvent.CashOnDelivery);

        var move = MoveBody();
        Assert.Contains("if (next == DealStage.Delivered && DealsPolicy.IsCashOnDelivery(deal))", move, StringComparison.Ordinal);
        Assert.Contains("deal.Timeline.Add(new(next, next, DealEvent.CashOnDelivery,", move, StringComparison.Ordinal);
        Assert.Equal(1, Count(DealsServiceSource(), "DealEvent.CashOnDelivery"));

        Assert.Contains("DealEvent.CashOnDelivery => L[\"deals.detail.event_cash_on_delivery\"]", DealPageSource(), StringComparison.Ordinal);
        Assert.Contains("DealEvent.CashOnDelivery => L[\"studio.deal.event_cash_on_delivery\"]", StudioPageSource(), StringComparison.Ordinal);
        foreach (var key in new[] { "deals.detail.event_cash_on_delivery", "studio.deal.event_cash_on_delivery" })
        {
            var text = LocaleCatalog.Text(LocaleCatalog.Arabic, key);
            Assert.NotEqual(key, text);
            Assert.False(LocaleCatalog.IsPlaceholder(text), $"‏{key} نائِب.");
        }
    }

    // ═══ ٥) البائِعُ طَرَفٌ مِن الوِلادَة ════════════════════════════════════

    /// <summary><c>/checkout/submit</c> يَربُطُ المالِكَ <b>بَعدَ</b> إلحاقِ المَرجِعِ
    /// وفي كُتلَتِه نَفسِها، والخِدمَةُ تَمُرُّ بِـ<c>AssignCounterpartyAsync</c> لا حَولَها
    /// — بِسَطرِ <c>bound</c> لا <c>assigned</c>، فَلا تُسَمّي الشاشَةُ رَبطاً «قُبِلَ العَرض».</summary>
    [Fact]
    public void Checkout_binds_the_listing_owner_right_after_the_ref_through_AssignCounterpartyAsync_with_its_own_line()
    {
        var submit = Endpoint("/{slug}/checkout/submit");
        var attach = submit.IndexOf("await deals.AttachRefAsync(slug, deal.Id, \"listing_owner\", oid);", StringComparison.Ordinal);
        var bind   = submit.IndexOf("await deals.BindListingOwnerAsync(slug, deal.Id, oid);", StringComparison.Ordinal);
        Assert.True(attach > 0 && bind > attach, $"الرَبطُ لا يَتلو المَرجِع: attach={attach} bind={bind}");
        Assert.Equal(1, Count(submit, "BindListingOwnerAsync("));

        var body = BindBody();
        Assert.Contains("if (!Guid.TryParse(ownerRef, out var ownerId)) return null;", body, StringComparison.Ordinal);
        Assert.Contains("LoadAsync<ACommerce.Kit.Auth.User>(ownerId, ct)", body, StringComparison.Ordinal);
        // **قُلِبَ** (‏2026-10-11، مُراجَعَةٌ ثانِيَة): كانَ `owner?.FullName ?? ""` — فَراغٌ يُرسَمُ فَراغاً؛ وصارَ الاسمُ
        // غائِباً `null` حينَ تَغيبُ الوَثيقَة (الصَفُّ `An_owner_without_a_user_document_…` أَعلاه).
        // ‏(2026-10-29، قَرارُ المالِك د40 «ج») اسمُ النَشاطِ حَيثُ وُجِد — القاعِدَةُ الواحِدَةُ `ActorName.Of(User?)` (و`null` لِوَثيقَةٍ غائِبَةٍ كَما كان)؛ كانَ `owner?.FullName`.
        Assert.Contains("AssignCounterpartyAsync(tenantSlug, dealId, ownerId, Queries.ActorName.Of(owner), ct, DealEvent.BoundAtBirth)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("owner?.FullName ?? \"\"", body, StringComparison.Ordinal);

        var assign = AssignBody();
        Assert.Contains("CancellationToken ct = default, string action = \"assigned\")", assign, StringComparison.Ordinal);
        Assert.Contains("deal.Timeline.Add(new(deal.Stage, deal.Stage, action,", assign, StringComparison.Ordinal);

        Assert.Equal("bound", DealEvent.BoundAtBirth);
        Assert.Contains("DealEvent.BoundAtBirth => L[\"deals.detail.event_bound\"]", DealPageSource(), StringComparison.Ordinal);
        Assert.Contains("DealEvent.BoundAtBirth   => L[\"studio.deal.event_bound\"]", StudioPageSource(), StringComparison.Ordinal);
        Assert.NotEqual("deals.detail.event_bound", LocaleCatalog.Text(LocaleCatalog.Arabic, "deals.detail.event_bound"));
        Assert.NotEqual("studio.deal.event_bound", LocaleCatalog.Text(LocaleCatalog.Arabic, "studio.deal.event_bound"));
    }

    /// <summary>مُزَوِّدٌ يَرمي عِندَ أَيِّ نِداء — الرَبطُ عِندَ الوِلادَةِ لا يَمَسُّ المال.</summary>
    private sealed class NoPayments : ACommerce.Kit.Payments.IPaymentProvider
    {
        private static InvalidOperationException Touched() => new("الرَبطُ لا يَمَسُّ المال — نودِيَ مُزَوِّدُ الدَفع.");
        public string ProviderName => "forbidden";
        public Task<ACommerce.Kit.Payments.PaymentResult> AuthorizeAsync(ACommerce.Kit.Payments.PaymentRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<ACommerce.Kit.Payments.PaymentResult> CaptureAsync(string paymentId, decimal? amount = null, CancellationToken ct = default) => throw Touched();
        public Task<ACommerce.Kit.Payments.PaymentResult> RefundAsync(string paymentId, decimal amount, string reason, CancellationToken ct = default) => throw Touched();
        public Task<ACommerce.Kit.Payments.SubscriptionResult> CreateSubscriptionAsync(ACommerce.Kit.Payments.SubscriptionRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<bool> CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default) => throw Touched();
        public Task<ACommerce.Kit.Payments.Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default) => throw Touched();
    }

    /// <summary><b>وَثيقَةُ المالِكِ الغائِبَة — صَفُّها</b> (‏2026-10-11، مُراجَعَةُ الموجَةِ الثانِيَةِ الثانِيَة):
    /// الهُوِيَّةُ مِن الإعلانِ هي الحُكمُ فَتُربَط، <b>والاسمُ غائِبٌ لا فارِغ</b> — <c>null</c> هو ما يَقرَؤُه كُلُّ
    /// قارِئٍ غِياباً (<c>?? "—"</c> في الاستوديو والتَقييم، و<c>is not null</c> في قائِمَةِ الاستوديو، و«بانتِظار
    /// القَبول» لِلمُشتَري). كانَ <c>owner?.FullName ?? ""</c> فَيُكتَبُ فَراغاً يُرسَمُ فَراغاً وسَهماً مُعَلَّقاً
    /// («⇄ ») في قائِمَةِ الاستوديو. والمُوجِبُ بِجِوارِه: وَثيقَةٌ حاضِرَةٌ تُربَطُ بِاسمِها.</summary>
    [Fact]
    public void An_owner_without_a_user_document_is_bound_by_identity_with_no_name_and_one_with_it_by_his_name()
    {
        var world = new DocWorld();
        var deals = new DealsService(world.Store, new NoPayments());
        T Done<T>(Task<T> t)
        {
            Assert.True(t.IsCompleted, "نِداءٌ لَم يَكتَمِل في عالَمٍ مُتَزامِن — الأَداةُ تَكذِب.");
            return t.GetAwaiter().GetResult();
        }
        Deal Born(Guid owner)
        {
            var d = Done(deals.StartAsync("zz-bind-name", "marketplace", Buyer, "مُشتَرٍ", null, "سِلعَة", 100m,
                new() { [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery }));
            Done(deals.AttachRefAsync("zz-bind-name", d.Id, DealsService.ListingOwnerRef, owner.ToString()));
            Done(deals.BindListingOwnerAsync("zz-bind-name", d.Id, owner.ToString()));
            return world.Read<Deal>(d.Id.ToString())!;
        }

        // السالِب: لا وَثيقَةَ لِلمالِك — يُربَطُ بِهُوِيَّتِه، والاسمُ وسَطرُ `bound` بِلا اسم.
        var ghost = Born(Seller);
        Assert.Equal(Seller, ghost.CounterpartyId);
        Assert.Null(ghost.CounterpartyName);
        var line = Assert.Single(ghost.Timeline, e => e.Action == DealEvent.BoundAtBirth);
        Assert.Equal(Seller, line.ActorId);
        Assert.Null(line.ActorName);

        // المُوجِب: وَثيقَةٌ حاضِرَة — يُربَطُ بِاسمِه مِنها.
        var named = Guid.NewGuid();
        world.Put(new ACommerce.Kit.Auth.User { Id = named, FullName = "مَتجَرُ النَخلَة" });
        var known = Born(named);
        Assert.Equal(named, known.CounterpartyId);
        Assert.Equal("مَتجَرُ النَخلَة", known.CounterpartyName);
        Assert.Equal("مَتجَرُ النَخلَة", Assert.Single(known.Timeline, e => e.Action == DealEvent.BoundAtBirth).ActorName);
    }

    /// <summary><b>المُوجِب</b>: المالِكُ المَربوطُ عِندَ الوِلادَةِ يَقبَلُ في
    /// <c>Offered</c>؛ <b>والسالِب</b>: لا يَقبَلُها غَيرُه — الغَريبُ والمُشتَري «لَستَ
    /// المالِك» (فَحصُ المالِكِ يَسبِقُ فَحصَ المُبادِر)، ومُشتَرٍ أَضافَ إعلانَه هُوَ إلى
    /// سَلَّتِه «أَنتَ المُبادِر».</summary>
    [Fact]
    public void The_owner_bound_at_birth_may_accept_at_Offered_and_nobody_else_may()
    {
        var bound = Cash();
        Assert.Equal(Seller, bound.CounterpartyId);
        Assert.Null(DealAcceptAuthorization.Validate(bound, new DealAcceptor(Seller, "بائِع")));
        Assert.Equal(DealAcceptAuthorization.ActorNotListingOwner, DealAcceptAuthorization.Validate(bound, new DealAcceptor(Stranger, "غَريب"))!.Code);
        Assert.Equal(DealAcceptAuthorization.ActorNotListingOwner, DealAcceptAuthorization.Validate(bound, new DealAcceptor(Buyer, "مُشتَرٍ"))!.Code);

        var ownListing = Cash();
        ownListing.Refs[DealsService.ListingOwnerRef] = Buyer.ToString();
        ownListing.CounterpartyId = Buyer;
        Assert.Equal(DealAcceptAuthorization.ActorIsInitiator, DealAcceptAuthorization.Validate(ownListing, new DealAcceptor(Buyer, "مُشتَرٍ"))!.Code);
    }

    /// <summary>«قُبِلَت أَصلاً» بِمِعيارَين: تَجاوَزَت <c>Offered</c>، أَو طَرَفُها غَيرُ
    /// القابِل (المَسارُ الإداريُّ عَيَّنَ مالِكَ التَطبيق). وعَرضُ الإعلانِ بِلا طَرَفٍ
    /// يُقبَلُ كَما كان.</summary>
    [Fact]
    public void Already_accepted_means_the_deal_left_Offered_or_another_party_holds_it()
    {
        var by = new DealAcceptor(Seller, "بائِع");
        Assert.Equal(DealAcceptAuthorization.DealAlreadyAccepted, DealAcceptAuthorization.Validate(Cash(DealStage.Booked), by)!.Code);
        Assert.Equal(DealAcceptAuthorization.DealAlreadyAccepted, DealAcceptAuthorization.Validate(CartDeal(null, DealStage.Booked, bound: false), by)!.Code);
        Assert.Equal(DealAcceptAuthorization.DealAlreadyAccepted, DealAcceptAuthorization.Validate(CartDeal(null, counterparty: AppOwner), by)!.Code);
        Assert.Null(DealAcceptAuthorization.Validate(CartDeal(null, bound: false), by));
    }

    /// <summary><c>AcceptAsync</c>: تَخويلٌ، ثُمَّ تَعيينُ القابِلِ (بِلا أَثَرٍ حَيثُ
    /// رُبِط)، ثُمَّ الانتِقالُ بِـ<b>هُوِيَّةِ القابِل</b> عَبرَ <c>MoveAsync</c> نَفسِها
    /// الَّتي تَكتُبُ لِـ<c>AdvanceAsync</c> — ولا نِداءَ لِـ<c>AdvanceAsync</c> ولا
    /// <c>deal.InitiatorId</c>، ولا نَتيجَةَ تُهمَل.</summary>
    [Fact]
    public void AcceptAsync_moves_as_the_acceptor_through_MoveAsync_and_never_borrows_the_initiator()
    {
        var accept = AcceptBody();
        var validate = accept.IndexOf("var violation = DealAcceptAuthorization.Validate(loaded, by);", StringComparison.Ordinal);
        var assign   = accept.IndexOf("await AssignCounterpartyAsync(tenantSlug, dealId, by.ActorId, by.ActorName, ct);", StringComparison.Ordinal);
        var next     = accept.IndexOf("var next = DealsPolicy.Next(deal)", StringComparison.Ordinal);
        // (والمُلاحَظَةُ `null` لا «قُبِلَ العَرض» — مُراجَعَةُ المَوجَةِ الثانِيَة، ‏2026-10-11؛ الصَفُّ التالي.)
        var move     = accept.IndexOf("return DealAcceptResult.Accepted(await MoveAsync(s, deal, next, by.ActorId, by.ActorName, null, ct));", StringComparison.Ordinal);
        Assert.True(validate > 0 && assign > validate && next > assign && move > next,
            $"سَلسَلَةُ القَبولِ: validate={validate} assign={assign} next={next} move={move}");
        Assert.Equal(0, Count(accept, "AdvanceAsync("));
        Assert.DoesNotContain("deal.InitiatorId", accept, StringComparison.Ordinal);
        Assert.DoesNotContain("advanced.Deal ?? deal", accept, StringComparison.Ordinal);

        // (‏2026-10-12، ‏ADR-036 الخُطوَة 2 — ولا يُعادُ حَرفٌ فَوقَه: كانَ `return new(true, await MoveAsync(…), null);` — صارَ إسناداً لِأَنّ
        // القَبضَ بَعدَ المُطالَبَةِ يُنادى خارِجَ التِقاطِ التَعارُض. والفاعِلُ هُوَ هُوَ: المُحَرِّكُ بِهُوِيَّتِه.)
        Assert.Contains("moved = await MoveAsync(s, deal, next.Value, actorId, actorName, note, ct);", AdvanceBody(), StringComparison.Ordinal);
        Assert.Equal(3, Count(WriteEndpointGuardTests.StripComments(DealsServiceSource()), "MoveAsync("));   // التَعريفُ ونِداءان
        Assert.Null(typeof(DealsService).GetMethod("MoveAsync", BindingFlags.Public | BindingFlags.Instance));   // لا بابَ ثالِثاً مِن خارِجِ الخِدمَة
    }

    /// <summary><b>القَبولُ لا يَكتُبُ مُلاحَظَةً عَرَبِيَّةً في السِجِلّ</b> (مُراجَعَةُ المَوجَةِ الثانِيَة، ‏2026-10-11؛
    /// القاعِدَة ١١): كانَ «قُبِلَ العَرض» يُكتَبُ في <c>Note</c> سَطرِ <c>advanced</c> نَصّاً لا مِفتاحاً، ويُرسَمُ
    /// خاماً في سِجِلِّ الاستوديو. أُسقِطَ — وسَطرُ <c>advanced</c> نَفسُه يَقولُها مِن تَسمِيَتَي الكاتالوج:
    /// «عَرض/طَلَب ← حَجز». <b>المُوجِب</b>: الحَرفِيَّةُ صِفرٌ في كودِ الخِدمَة؛ <b>والسالِب</b>: الشاشَتانِ ما زالَتا
    /// تَرسُمانِ السَطرَ بِالتَسمِيَتَين.</summary>
    [Fact]
    public void The_accept_move_writes_no_literal_note_and_the_timeline_says_it_through_the_stage_labels()
    {
        var code = WriteEndpointGuardTests.StripComments(DealsServiceSource());
        Assert.DoesNotContain("قُبِلَ العَرض", code, StringComparison.Ordinal);
        Assert.Equal(1, Count(code, "MoveAsync(s, deal, next, by.ActorId, by.ActorName, null, ct)"));

        Assert.Equal("عَرض/طَلَب ← حَجز", $"{DealsPolicy.LabelAr(DealStage.Offered)} ← {DealsPolicy.LabelAr(DealStage.Booked)}");
        Assert.Contains("\"advanced\" => $\"{DealsPolicy.LabelAr(e.StageBefore)} ← {DealsPolicy.LabelAr(e.StageAfter)}\"", DealPageSource(), StringComparison.Ordinal);
        Assert.Contains("\"advanced\"  => $\"{DealsPolicy.LabelAr(e.StageBefore)} ← {DealsPolicy.LabelAr(e.StageAfter)}\"", StudioPageSource(), StringComparison.Ordinal);
    }

    // ═══ ٦) هُوِيَّةُ المالِكِ بِالقيمَة ══════════════════════════════════════

    /// <summary>صيغَةُ <c>N</c> والأَحرُفُ الكَبيرَةُ لِنَفسِ المُعَرِّفِ هي المالِكُ في
    /// البَوّابَةِ والقائِمَةِ والشاشَة؛ ومَرجِعٌ لَيسَ مُعَرِّفاً كَغِيابِه.</summary>
    [Fact]
    public void The_owner_ref_is_compared_by_guid_value_in_the_gate_the_list_and_the_screen()
    {
        foreach (var format in new[] { Seller.ToString("D").ToUpperInvariant(), Seller.ToString("N"), Seller.ToString("B") })
        {
            var d = CartDeal(null, bound: false); d.Refs[DealsService.ListingOwnerRef] = format;
            Assert.Equal(Seller, DealAcceptAuthorization.OwnerIdOf(d));
            Assert.Null(DealAcceptAuthorization.Validate(d, new DealAcceptor(Seller, "بائِع")));
            Assert.True(DealsService.IsIncomingOfferFor(d, Seller));
            Assert.False(DealsService.IsIncomingOfferFor(d, Stranger));
        }

        var garbage = CartDeal(null, bound: false); garbage.Refs[DealsService.ListingOwnerRef] = "not-a-guid";
        Assert.Null(DealAcceptAuthorization.OwnerIdOf(garbage));
        Assert.Equal(DealAcceptAuthorization.ListingOwnerMissing, DealAcceptAuthorization.Validate(garbage, new DealAcceptor(Seller, "بائِع"))!.Code);
        Assert.False(DealsService.IsIncomingOfferFor(garbage, Seller));

        var page = DealPageSource();
        Assert.Contains("isListingOwner = DealAcceptAuthorization.OwnerIdOf(deal) == uid || deal.CounterpartyId == uid;", page, StringComparison.Ordinal);
        // الحَرفُ القَديمُ يَبقى في تَعليقٍ يُؤَرِّخُه — فَيُقاسُ الكودُ بِلا تَعليقات.
        Assert.DoesNotContain("ownerRef == uid.ToString()",
            WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(page)), StringComparison.Ordinal);
        Assert.Contains("&& DealAcceptAuthorization.OwnerIdOf(deal) == ownerId;", WriteEndpointGuardTests.StripComments(DealsServiceSource()), StringComparison.Ordinal);
    }
}
