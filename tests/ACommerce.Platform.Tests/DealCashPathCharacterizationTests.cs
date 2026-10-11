using System.Reflection;
using System.Text.RegularExpressions;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ مَسارِ صَفقَةِ النَقدِ قَبلَ الخُطوَة 3 — ‏ADR-033 §٢‑هـ الخُطوَتان 2 و3 ═══
//
// ‏ADR-033 §٢‑ج‑أ: «ومَن يحرِّكُ Paid → Shipping لصفقةِ cash … يُقالُ في كوميتِ
// الخطوة 3 بسببِه **بعدَ أن يُثبَّتَ الفرقُ توصيفًا (الخطوة 2)**». و‏CLAUDE.md
// القاعِدَة ٣: التَوصيفُ يَخضَرُّ قَبلَ التَبديل، في كوميتٍ مُستَقِلّ.
//
// **ما يُثَبِّتُه هذا المِلَفُّ هُوَ ما سَيُقلَبُ في كوميتِ الخُطوَة 3 (شَطرُ
// الصَفَقات) بِقَرارٍ مَرئِيّ** — فَكُتِبَ كَما كانَ يَومَه (‏2026-10-11، ‏52eaaa2؛
// كوميتُ التَوصيف ‏5554975) لِيَكونَ الفَرقُ بَينَ «قَبل» و«بَعد» مَقيساً لا مَوصوفاً.
//
// **ثُمَّ قُلِبَت صُفوفُ البَندَين ١ و٢ وشَطرِ الدَورِ مِن ٤ في الكوميتِ التالي بِقَرارٍ
// مَرئِيّ** (‏ADR-033 §٦): كُلُّ صَفٍّ مَقلوبٍ يَحمِلُ «قُلِبَ» في عُنوانِه أَو شَرحِه،
// ويُثبِّتُ النَقيضَ بِاسمِه — لا يُحذَفُ ولا يُسكَت. وما بَقِيَ مِن ٣ و٤ (الإشعارُ،
// والعُنوانُ، و`err`، وأُنبوبُ المُحادَثَة) يَبقى حَرفاً حَتّى كوميتاتِ P1-4 وP1-5.
// وما كانَ يَومَ التَوصيف:
//   ١) السِياسَةُ عَمياءُ عَن طَريقَةِ الدَفع: صَفقَةُ `cash` تَمشي
//      Confirmed → Paid → Shipping كَصَفقَةِ البِطاقَة، و`Paid` يُحَرِّكُها
//      المُشتَري وَحدَه — فَيَرى «دَورُكَ الآن: تَأكيد: شَحن» وهُوَ لا يَشحَن.
//   ٢) `/accept` يُحَرِّكُ `Offered → Booked` بِفاعِلٍ هُوَ **المُبادِرُ** بِاسمِ
//      القابِل، ويُرجِعُ `Accepted` بِلا قِراءَةِ نَتيجَةِ التَقَدُّم؛ ومَرجِعُ
//      المالِكِ يُقارَنُ **نَصّاً** فَصيغَةٌ أُخرى لِنَفسِ الـGuid لَيسَت المالِك؛
//      وصَفقَةٌ وُلِدَت بِطَرَفٍ مَربوطٍ لا تُقبَل.
//   ٣) `/checkout/submit` يُلحِقُ المَرجِعَ ولا يَربُطُ طَرَفاً ولا يُشعِرُ أَحَداً؛
//      و`/accept` لا يُشعِرُ المُشتَري؛ وتَعليقُ `Deal.cs` يَدَّعي إشعاراً تِلقائِيّاً.
//   ٤) صَفحَةُ الصَفقَةِ تَشتَقُّ الدَورَ والمَراحِلَ مِن (النَمَط، المَرحَلَة)
//      وَحدَهُما، ولا تَرسُمُ عُنوانَ التَوصيلِ لِأَحَد، ولا تَقرَأُ `?err=`،
//      وأُنبوبُ المُحادَثَةِ يُسقِطُ كُلَّ فَشَلٍ عَلى `/drivers`.
//
// **وما لا يُكَرَّرُ هُنا** (القاعِدَة ٨): فاعِلُ كُلِّ مَرحَلَةٍ لِذاتِه في
// `DealsPolicyCharacterizationTests`؛ وسَلسَلَةُ `AcceptAsync` بِحَرفِها و«مَسارانِ
// لا ثالِث» في `DealCounterpartyCharacterizationTests`؛ وأَنّ النَقدَ لا يَبلُغُ
// القَبضَ في `DealPaidStageCharacterizationTests` — تُستَشهَدُ ولا تُنسَخ.
//
// **الأَداةُ مَقيسَة** (القاعِدَة ١٠): كُلُّ مَقطَعٍ يُقرَأُ يَفشَلُ إن لَم يوجَد
// طَرَفاه، وكُلُّ مِلَفٍّ يُقرَأُ يَفشَلُ إن كانَ أَقصَرَ مِن أَن يَكونَ قَد قُرِئ.

public class DealCashPathCharacterizationTests
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static string Source(params string[] parts)
    {
        var path = Path.Combine(RepoRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياءُ بِلا طَرَفٍ مَقروء.");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 500,
            $"أَداةٌ عَمياء: {path} طولُه {text.Length} مِحرَفاً — لَم يُقرَأ.");
        return text;
    }

    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    private static string DealsServiceSource() => Source(Template, "Services", "Deals", "DealsService.cs");
    private static string DealSource()         => Source(Template, "Services", "Deals", "Deal.cs");
    private static string PolicySource()       => Source(Template, "Services", "Deals", "DealsPolicy.cs");
    private static string CatalogSource()      => Source(Template, "Services", "Deals", "DealPatternCatalog.cs");
    private static string TurnViewSource()     => Source(Template, "Services", "Ux", "DealTurnView.cs");
    private static string EndpointsSource()    => Source(Template, "MarketplaceTemplateExtensions.cs");
    private static string DealPageSource()     => Source(Template, "Components", "Pages", "MyDealDetail.razor");
    private static string FlowExplainerSource()=> Source(Template, "Components", "FlowExplainer.razor");

    /// <summary>مَقطَعٌ مِن المَصدَرِ مِن <paramref name="from"/> إلى ما قَبلَ
    /// <paramref name="to"/> — <b>بِلا تَعليقات</b>. يَفشَلُ إن لَم يُوجَد الطَرَفان.</summary>
    private static string Segment(string source, string from, string to)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start > 0, $"‏`{from}` غَيرُ مَوجود — الأَداةُ عَمياء.");
        var end = source.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"‏`{to}` لا يَتلو `{from}` — الأَداةُ عَمياء.");
        return WriteEndpointGuardTests.StripComments(source.Substring(start, end - start));
    }

    private static string AcceptBody()  => Segment(DealsServiceSource(), "public async Task<DealAcceptResult> AcceptAsync(",  "public async Task<DealAdvanceResult> AdvanceAsync(");
    private static string AdvanceBody() => Segment(DealsServiceSource(), "public async Task<DealAdvanceResult> AdvanceAsync(", "public async Task<DealCancelResult> CancelAsync(");

    /// <summary>جِسمُ نُقطَةٍ مِن حَرفِ مَسارِها إلى خاتِمَتِها.</summary>
    private static string Endpoint(string route) => Segment(EndpointsSource(), "\"" + route + "\"", ".RequireStoreWritable();");

    private static int Count(string text, string needle)
    {
        var n = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            n++;
        return n;
    }

    private static string Squash(string s) => Regex.Replace(s, @"\s+", " ");

    private static readonly MethodInfo ActorGate = typeof(DealsService)
        .GetMethod("IsActorAllowed", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("‏`DealsService.IsActorAllowed` غَيرُ مَوجودَة — الأَداةُ عَمياء.");

    private static bool Allowed(Deal deal, Guid actor, string required)
        => (bool)ActorGate.Invoke(null, new object[] { deal, actor, required })!;

    private static readonly Guid Buyer  = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Seller = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    /// <summary>صَفقَةُ سَلَّةٍ نَقدِيَّةٌ كَما تَخرُجُ مِن <c>/checkout/submit</c>
    /// اليَوم: المُشتَري مُبادِر، ومالِكُ الإعلانِ في المَرجِعِ وَحدَه، وطَرَفُها
    /// مَن يُعَيِّنُه المُنادي.</summary>
    private static Deal CashDeal(Guid? counterparty = null, DealStage stage = DealStage.Offered)
    {
        var d = new Deal
        {
            Id = Guid.NewGuid(), Pattern = "marketplace", Stage = stage, AmountSar = 100,
            InitiatorId = Buyer, CounterpartyId = counterparty,
        };
        d.Refs[DealsService.ListingOwnerRef] = Seller.ToString();
        d.Attributes["pay_method"] = CheckoutPayMethods.CashOnDelivery;
        return d;
    }

    // ═══ ١) السِياسَةُ عَمياءُ عَن طَريقَةِ الدَفع ═══════════════════════════

    /// <summary><b>قُلِبَ</b> (‏ADR-033 §٦): كانَت <c>DealsPolicy</c> و
    /// <c>DealPatternCatalog</c> و<c>DealTurnView</c> لا تَعرِفُ كَلِمَةَ <c>pay_method</c>
    /// ولا مَعجَمَ <c>CheckoutPayMethods</c>، و<c>Next</c> و<c>StagesFor</c> و<c>For</c>
    /// بِحِملٍ واحِدٍ لِكُلٍّ. وصارَ لِكُلٍّ ثَلاثَةُ أَحمال — والحِملُ القَديمُ بِحَرفِه
    /// وجَوابِه. <b>والكاتالوجُ وَحدَه بَقِيَ أَعمى عَن الدَفع</b>: النَمَطُ بَياناتٌ
    /// والإسقاطُ كودٌ في السِياسَة (القاعِدَة ٥).</summary>
    [Fact]
    public void The_policy_now_knows_the_pay_method_while_the_catalog_stays_blind_to_it()
    {
        var catalog = WriteEndpointGuardTests.StripComments(CatalogSource());
        Assert.DoesNotContain("pay_method", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain("CheckoutPayMethods", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain("CashOnDelivery", catalog, StringComparison.Ordinal);

        var policy = WriteEndpointGuardTests.StripComments(PolicySource());
        Assert.Contains("CheckoutPayMethods.AttributeKey", policy, StringComparison.Ordinal);
        Assert.Contains("CheckoutPayMethods.CashOnDelivery", policy, StringComparison.Ordinal);
        Assert.Contains("stage == DealStage.Paid && IsCashOnDelivery(payMethod)", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("pay_method", policy, StringComparison.Ordinal);   // الحَرفُ في المَعجَمِ وَحدَه
        Assert.DoesNotContain("pay_method", WriteEndpointGuardTests.StripComments(TurnViewSource()), StringComparison.Ordinal);

        Assert.Equal(3, typeof(DealsPolicy).GetMethods().Count(m => m.Name == nameof(DealsPolicy.Next)));
        Assert.Equal(3, typeof(DealsPolicy).GetMethods().Count(m => m.Name == nameof(DealsPolicy.StagesFor)));
        Assert.Equal(3, typeof(DealTurnView).GetMethods().Count(m => m.Name == nameof(DealTurnView.For)));
    }

    /// <summary><b>قُلِبَ شَطرُه</b> (‏ADR-033 §٦): كانَت صَفقَةُ النَقدِ تَمشي
    /// <c>Confirmed → Paid → Shipping</c> كَصَفقَةِ البِطاقَةِ حَرفاً، و<c>Paid</c> لا
    /// يُحَرِّكُها إلّا المُشتَري — فَالبائِعُ لا يَستَطيعُ نَقلَ صَفقَةِ نَقدٍ مُؤَكَّدَةٍ
    /// إلى الشَحنِ بِنَفسِه (P1-3). وصارَت <c>Confirmed → Shipping</c> لِلنَقدِ وَحدَه؛
    /// <b>وما لَم يُقلَب يُثَبَّتُ حَرفاً</b>: الجَدوَلُ بِلا طَريقَةِ دَفعٍ كَما كان، وفاعِلُ
    /// كُلِّ مَرحَلَةٍ كَما كان، وصَفقَةٌ قَديمَةٌ عِندَ <c>Paid</c> تَمضي.</summary>
    [Fact]
    public void A_cash_deal_walks_Confirmed_then_Shipping_now_while_the_pattern_table_and_the_actors_are_unchanged()
    {
        var confirmed = CashDeal(counterparty: Seller, stage: DealStage.Confirmed);
        Assert.Equal(DealStage.Paid, DealsPolicy.Next(confirmed.Pattern, confirmed.Stage));        // الجَدوَلُ كَما كان
        Assert.Equal(DealStage.Shipping, DealsPolicy.Next(confirmed));                                // والصَفقَةُ تَتَخَطّى
        Assert.Equal("either", DealsPolicy.Actor(confirmed.Stage));
        Assert.True(Allowed(confirmed, Buyer, "either"));
        Assert.True(Allowed(confirmed, Seller, "either"));

        var paid = CashDeal(counterparty: Seller, stage: DealStage.Paid);
        Assert.Equal(DealStage.Shipping, DealsPolicy.Next(paid.Pattern, paid.Stage));
        Assert.Equal(DealStage.Shipping, DealsPolicy.Next(paid));                                     // القَديمَةُ لا تُحبَس
        Assert.Equal("initiator", DealsPolicy.Actor(paid.Stage));
        Assert.True(Allowed(paid, Buyer, "initiator"));
        Assert.False(Allowed(paid, Seller, "initiator"));

        var shipping = CashDeal(counterparty: Seller, stage: DealStage.Shipping);
        Assert.Equal(DealStage.Delivered, DealsPolicy.Next(shipping.Pattern, shipping.Stage));
        Assert.Equal(DealStage.Delivered, DealsPolicy.Next(shipping));
        Assert.Equal("counterparty", DealsPolicy.Actor(shipping.Stage));
        Assert.True(Allowed(shipping, Seller, "counterparty"));
        Assert.False(Allowed(shipping, Buyer, "counterparty"));

        // والجَدوَلُ بِلا طَريقَةِ دَفعٍ كَما كان — والنَقدُ وَحدَه يُسقِطُ «دَفع».
        Assert.Equal(
            new[] { DealStage.Offered, DealStage.Booked, DealStage.Confirmed, DealStage.Paid, DealStage.Shipping, DealStage.Delivered, DealStage.Reviewed },
            DealsPolicy.StagesFor("marketplace").ToArray());
        Assert.Equal(
            new[] { DealStage.Offered, DealStage.Booked, DealStage.Confirmed, DealStage.Shipping, DealStage.Delivered, DealStage.Reviewed },
            DealsPolicy.StagesFor(confirmed).ToArray());
    }

    /// <summary><b>قُلِبَ</b> (‏ADR-033 §٦): كانَت <c>AdvanceAsync</c> تَقرَأُ التالِيَ مِن
    /// (النَمَط، المَرحَلَة) وَحدَهُما ولا تَذكُرُ النَقدَ ولا <c>Delivered</c>. وصارَت
    /// تَقرَأُ <c>Next(deal)</c>، و<c>MoveAsync</c> تَكتُبُ سَطرَ القَبضِ النَقديِّ عِندَ
    /// <c>Delivered</c> — والحَرفُ <c>pay_method</c> لا يَظهَرُ في الخِدمَة: المِفتاحُ مِن
    /// المَعجَمِ وَحدَه.</summary>
    [Fact]
    public void AdvanceAsync_now_reads_the_deal_and_MoveAsync_writes_the_cash_on_delivery_line()
    {
        var advance = AdvanceBody();
        Assert.Contains("var next = DealsPolicy.Next(deal);", advance, StringComparison.Ordinal);
        Assert.DoesNotContain("DealsPolicy.Next(deal.Pattern, deal.Stage)", advance, StringComparison.Ordinal);
        Assert.Contains("if (next == DealStage.Delivered && DealsPolicy.IsCashOnDelivery(deal))", advance, StringComparison.Ordinal);
        Assert.Contains("DealEvent.CashOnDelivery,", advance, StringComparison.Ordinal);
        Assert.DoesNotContain("pay_method", advance, StringComparison.Ordinal);
        Assert.DoesNotContain("CheckoutPayMethods", advance, StringComparison.Ordinal);
    }

    // ═══ ٢) القَبولُ اليَوم: فاعِلٌ مُستَعار، ونَتيجَةٌ مُهمَلَة، ومُقارَنَةٌ نَصِّيَّة ═══

    /// <summary><b>قُلِبَ</b> (‏ADR-033 §٦): كانَت <c>AcceptAsync</c> تُحَرِّكُ بِـ
    /// <c>deal.InitiatorId</c> بِاسمِ القابِل ولا تَقرَأُ <c>advanced.Ok</c> — رَفضٌ
    /// يُبتلَع. وصارَت تَنتَقِلُ بِـ<c>by.ActorId</c> عَبرَ <c>MoveAsync</c>، ولا نَتيجَةَ
    /// تُهمَل لِأَنّ لا نَتيجَةَ: الرَفضُ كُلُّه رُموزُ <c>Validate</c> قَبلَ الكِتابَة،
    /// و<c>Accepted</c> بَعدَ الحَفظ.</summary>
    [Fact]
    public void AcceptAsync_now_moves_as_the_acceptor_and_has_no_advance_result_to_ignore()
    {
        var accept = AcceptBody();
        // (والمُلاحَظَةُ `null` لا «قُبِلَ العَرض» — مُراجَعَةُ المَوجَةِ الثانِيَة، ‏2026-10-11.)
        Assert.Contains("MoveAsync(s, deal, next, by.ActorId, by.ActorName, null, ct)", accept, StringComparison.Ordinal);
        Assert.DoesNotContain("deal.InitiatorId", accept, StringComparison.Ordinal);
        Assert.DoesNotContain("advanced.Deal ?? deal", accept, StringComparison.Ordinal);
        Assert.Equal(0, Count(accept, "AdvanceAsync("));
    }

    /// <summary><b>قُلِبَ</b> (‏ADR-033 §٦): كانَ مَرجِعُ المالِكِ يُقارَنُ <b>نَصّاً</b>
    /// بِـ<c>ActorId.ToString()</c> في <c>Validate</c> و<c>IsIncomingOfferFor</c> وفي
    /// الشاشَة — فَنَفسُ الـGuid بِصيغَةٍ أُخرى (أَحرُفٌ كَبيرَة، أَو <c>N</c> بِلا
    /// شَرطات) «لَيسَ المالِك». وصارَ يُقارَنُ بِالقيمَةِ عَبرَ
    /// <c>DealAcceptAuthorization.OwnerIdOf</c> — دالَّةٌ واحِدَةٌ لِلمَواضِعِ الثَلاثَة.</summary>
    [Fact]
    public void The_owner_ref_is_now_compared_by_guid_value_in_the_gate_the_list_and_the_screen()
    {
        var upper = CashDeal(); upper.Refs[DealsService.ListingOwnerRef] = Seller.ToString("D").ToUpperInvariant();
        var n     = CashDeal(); n.Refs[DealsService.ListingOwnerRef]     = Seller.ToString("N");

        Assert.Null(DealAcceptAuthorization.Validate(upper, new DealAcceptor(Seller, "بائِع")));
        Assert.Null(DealAcceptAuthorization.Validate(n,     new DealAcceptor(Seller, "بائِع")));
        Assert.True(DealsService.IsIncomingOfferFor(upper, Seller));
        Assert.True(DealsService.IsIncomingOfferFor(n, Seller));
        // والصيغَةُ الَّتي يَكتُبُها الكاتِبُ (`ToString()` = D صَغيرَة) تَمُرُّ كَما كانَت.
        Assert.Null(DealAcceptAuthorization.Validate(CashDeal(), new DealAcceptor(Seller, "بائِع")));
        Assert.True(DealsService.IsIncomingOfferFor(CashDeal(), Seller));

        var validate = WriteEndpointGuardTests.StripComments(Source(Template, "Services", "Deals", "DealAcceptAuthorization.cs"));
        Assert.DoesNotContain("owner != by.ActorId.ToString()", validate, StringComparison.Ordinal);
        Assert.Contains("if (ownerId != by.ActorId)", validate, StringComparison.Ordinal);
        Assert.Contains("isListingOwner = DealAcceptAuthorization.OwnerIdOf(deal) == uid || deal.CounterpartyId == uid;", DealPageSource(), StringComparison.Ordinal);
    }

    /// <summary><b>قُلِبَ</b> (‏ADR-033 §٦): كانَت صَفقَةٌ لَها طَرَفٌ مُقابِلٌ — وإن كانَ
    /// هُوَ مالِكَ الإعلانِ نَفسَه — تُجابُ <c>deal_already_accepted</c> في <c>Offered</c>.
    /// وصارَ البائِعُ يُربَطُ عِندَ الوِلادَة (الطَريقُ الأَوَّلُ في §٢‑ج‑أ) فَيَقبَلُ هُوَ،
    /// ولا يَقبَلُ غَيرُه، ولا تُقبَلُ ثانِيَةً بَعدَ <c>Offered</c>.</summary>
    [Fact]
    public void A_deal_whose_counterparty_is_already_the_listing_owner_is_now_accepted_by_them_and_by_nobody_else()
    {
        var bound = CashDeal(counterparty: Seller);
        Assert.Equal(DealStage.Offered, bound.Stage);
        Assert.Null(DealAcceptAuthorization.Validate(bound, new DealAcceptor(Seller, "بائِع")));
        Assert.Equal(DealAcceptAuthorization.ActorNotListingOwner,
            DealAcceptAuthorization.Validate(bound, new DealAcceptor(Guid.NewGuid(), "غَريب"))!.Code);
        Assert.Equal(DealAcceptAuthorization.DealAlreadyAccepted,
            DealAcceptAuthorization.Validate(CashDeal(counterparty: Seller, stage: DealStage.Booked), new DealAcceptor(Seller, "بائِع"))!.Code);
    }

    // ═══ ٣) لا طَرَفَ عِندَ الوِلادَة، ولا إشعارَ في المَسارَين ═══════════════

    /// <summary><c>/checkout/submit</c> يُلحِقُ <c>listing_owner</c> <b>ويَربُطُ البائِعَ
    /// طَرَفاً مِن الوِلادَة</b> (قُلِبَ شَطرُ الرَبطِ في ‏ADR-033 §٦ — كانَ صِفراً)،
    /// <b>ويُشعِرُه بَعدَ الحَفظ</b> (قُلِبَ شَطرُ الإشعارِ في P1-5 — كانَ صِفرَ
    /// <c>Notification</c>/<c>NudgeAsync</c>/<c>push.</c> فَالبائِعُ لا يَعلَمُ بِالطَلَبِ
    /// إلّا إن فَتَحَ «صَفقات» بِنَفسِه). والتَركيبُ في <c>DealNotices</c> لا في الجِسم
    /// (<c>DealNoticesTests</c>).</summary>
    [Fact]
    public void Checkout_submit_attaches_the_owner_ref_binds_him_and_now_notifies_him_through_the_service()
    {
        var submit = Endpoint("/{slug}/checkout/submit");
        Assert.Equal(1, Count(submit, "await deals.AttachRefAsync(slug, deal.Id, \"listing_owner\", oid);"));
        Assert.Equal(1, Count(submit, "await deals.BindListingOwnerAsync(slug, deal.Id, oid);"));
        Assert.Equal(0, Count(submit, "AssignCounterpartyAsync("));   // الرَبطُ في الخِدمَةِ لا في الجِسم
        Assert.Equal(1, Count(submit, "await notices.NotifyNewDealsAsync(slug, born, l);"));
        Assert.Equal(0, Count(submit, "new ACommerce.Kit.Notifications.Notification"));   // التَركيبُ في الخِدمَةِ لا في الجِسم
        Assert.Equal(0, Count(submit, "NudgeAsync("));
        Assert.Equal(0, Count(submit, "push."));
    }

    /// <summary><b>قُلِبَ</b> (P1-5): كانَ <c>/accept</c> لا يُشعِرُ المُشتَري بِأَنّ طَلَبَه
    /// قُبِل — بَينَما نُقطَةُ قَبولِ العَرضِ في عِدَّةِ العُروض تَفعَل (<c>offer_accepted</c>).
    /// وصارَ يُشعِرُه عَلى النَسَقِ نَفسِه عَبرَ <c>DealNotices</c> بَعدَ قَبولٍ ناجِحٍ وَحدَه.</summary>
    [Fact]
    public void The_accept_endpoint_now_notifies_the_buyer_through_the_service_on_success()
    {
        var accept = Endpoint("/{slug}/deals/{id:guid}/accept");
        Assert.Contains("deals.AcceptAsync(", accept, StringComparison.Ordinal);
        Assert.Equal(1, Count(accept, "if (res.Ok) await notices.NotifyAcceptedAsync(slug, res.Deal!, userName, l);"));
        Assert.Equal(0, Count(accept, "new ACommerce.Kit.Notifications.Notification"));
        Assert.Equal(0, Count(accept, "NudgeAsync("));
        Assert.Equal(0, Count(accept, "push."));

        // والمُقارَنُ القائِمُ في المِلَفِّ نَفسِه يَكتُبُ ويُنادي — فَالأُنبوبُ مَوجود.
        var offers = EndpointsSource();
        Assert.Contains("Type = \"offer_accepted\",", offers, StringComparison.Ordinal);
        Assert.True(Count(offers, "await NudgeAsync(hub, slug,") >= 3);
    }

    /// <summary><b>قُلِبَ شَطرُه</b> (P1-5): كانَ تَعليقُ <c>Deal.cs</c> يَقول «إِشعار الأَطراف
    /// يَخرُج تِلقائيّاً» بِصِفرِ مُنتِج — وصارَ يُسَمّي المُنتِجَ <c>DealNotices</c> ومَوضِعَي
    /// نِدائِه. <b>وما بَقِيَ حَرفاً</b>: <c>DealsService</c> نَفسُها لا تَذكُرُ
    /// <c>Notification</c> — الإشعارُ عِندَ النُقطَتَينِ لا في مَكينَةِ الحالَة.</summary>
    [Fact]
    public void Deal_cs_now_names_the_producer_while_the_service_itself_still_writes_none()
    {
        Assert.DoesNotContain("إِشعار الأَطراف يَخرُج تِلقائيّاً", DealSource(), StringComparison.Ordinal);
        Assert.Contains("DealNotices", DealSource(), StringComparison.Ordinal);
        Assert.Equal(0, Count(DealsServiceSource(), "Notification"));
    }

    // ═══ ٤) صَفحَةُ الصَفقَةِ وأُنبوبُ المُحادَثَة ═══════════════════════════

    /// <summary><b>قُلِبَ شَطرُه</b> (‏ADR-033 §٦): كانَت «دَورُكَ الآن» والـstepper
    /// والمَرحَلَةُ التالِيَةُ تُشتَقُّ مِن <c>(deal.Pattern, deal.Stage)</c> وَحدَهُما —
    /// فَالمُشتَري في صَفقَةِ نَقدٍ عِندَ <c>Paid</c> يُقالُ لَه «دَورُكَ الآن … تَأكيد:
    /// شَحن». وصارَت تُشتَقُّ مِن الصَفقَةِ نَفسِها (بِطَريقَةِ دَفعِها)، وشَرحُ التَدَفُّقِ
    /// يَأخُذُ <c>PayMethod</c>. <b>وما لَم يُقلَب يُثَبَّتُ حَرفاً</b>: حالَةُ القَبولِ
    /// الخاصَّة، وبَوّابَةُ <c>canAdvance</c>، والحِملُ القَديمُ لِـ<c>DealTurnView.For</c>
    /// بِجَوابِه.</summary>
    [Fact]
    public void The_deal_page_now_derives_turn_and_stepper_from_the_deal_itself_and_the_accept_case_is_unchanged()
    {
        var page = DealPageSource();
        Assert.Contains("Services.Ux.DealTurnView.For(deal, mySide)", page, StringComparison.Ordinal);
        Assert.Contains("stages = DealsPolicy.StagesFor(deal).ToList();", page, StringComparison.Ordinal);
        Assert.Contains("nextStage = DealsPolicy.Next(deal);", page, StringComparison.Ordinal);
        Assert.DoesNotContain("For(deal.Pattern, deal.Stage, mySide)", page, StringComparison.Ordinal);
        Assert.Contains("var acceptCase = deal.Stage == DealStage.Offered && isListingOwner && !isInitiator;", page, StringComparison.Ordinal);
        Assert.Contains("var mine = acceptCase || turn.IsMine;", page, StringComparison.Ordinal);
        var gate = Squash(page);
        Assert.Contains("\"initiator\" => isInitiator, \"counterparty\" => deal.CounterpartyId == uid, \"either\" => isInitiator || deal.CounterpartyId == uid,", gate, StringComparison.Ordinal);

        // الحِملُ القَديمُ بِجَوابِه القَديم — فَلا مُستَهلِكٌ قَديمٌ تَغَيَّرَ تَحتَه.
        // **قُلِبَ شَطرُ الدَور** (‏ADR-033 §٦‑ط، ‏2026-10-11): كانَ الدَورُ عِندَ `Paid` لِلمُشتَري (فاعِلُ
        // `Paid`) والتالي «شَحن» — يُعلِنُ الشَحنَ عَن البائِع. وصارَ الخُروجُ مِن `Paid` دُخولَ `Shipping`
        // وفاعِلُها الطَرَف: الدَورُ لِلبائِعِ والمُشتَري يَنتَظِر؛ والتالي بِحَرفِه.
        var buyerAtPaid  = DealTurnView.For("marketplace", DealStage.Paid, DealSide.Initiator);
        var sellerAtPaid = DealTurnView.For("marketplace", DealStage.Paid, DealSide.Counterparty);
        Assert.False(buyerAtPaid.IsMine);
        Assert.Equal(DealStage.Shipping, buyerAtPaid.NextStage);
        Assert.True(sellerAtPaid.IsMine);
        // والصَفقَةُ النَقديَّةُ لا تَبلُغُ Paid أَصلاً — فَلا بِطاقَةَ تَقولُ ذلك لِمُشتَريها.
        var cashAtConfirmed = DealTurnView.For(CashDeal(counterparty: Seller, stage: DealStage.Confirmed), DealSide.Counterparty);
        Assert.Equal(DealStage.Shipping, cashAtConfirmed.NextStage);

        Assert.Contains("var stages = DealsPolicy.StagesFor(Pattern, PayMethod).ToList();", FlowExplainerSource(), StringComparison.Ordinal);
        Assert.Contains("PayMethod=\"@DealsPolicy.PayMethodOf(deal)\"", page, StringComparison.Ordinal);
    }

    /// <summary><b>قُلِبَ شَطرُه</b> (P1-4): كانَ لا سَطرَ يَرسُمُ <c>Attributes</c>
    /// (عُنوانُ التَوصيلِ وهاتِفُ المُشتَري مَكتوبانِ في الصَفقَةِ ولا يَراهُما
    /// البائِع) وحَقنُ <c>IDocumentStore</c> مَيِّتٌ يُحقَنُ ولا يُقرَأ — فَصارا يُرسَمانِ
    /// لِلبائِعِ وَحدَه خَلفَ حارِسٍ (<c>DealDetailDeliveryDisclosureTests</c>) وزالَ
    /// الحَقن. <b>ثُمَّ قُلِبَ شَطرُ <c>err</c></b> (C2): كانَ رَفضُ <c>/accept</c>
    /// و<c>/cancel</c> يَرتَدُّ بِرَمزٍ لا تَقرَؤُه الصَفحَة — فَصارَ يُقرَأُ ويُرسَمُ مِن
    /// القامُوسِ عَبرَ <c>DealRefusalTexts</c> (<c>DealRefusalTextsTests</c>).</summary>
    [Fact]
    public void The_deal_page_now_renders_delivery_attributes_behind_the_vendor_guard_and_reads_err()
    {
        var page = DealPageSource();
        var code = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(page));
        Assert.Equal(2, Count(code, "deal.Attributes.TryGetValue(Deal.Delivery"));
        Assert.Contains("deliveryVisible = !isInitiator && isListingOwner;", code, StringComparison.Ordinal);
        Assert.Contains("[SupplyParameterFromQuery(Name = \"err\")]", page, StringComparison.Ordinal);
        Assert.Contains("DealRefusalTexts.KeyFor(Err)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@inject IDocumentStore Store", page, StringComparison.Ordinal);
        Assert.Equal(0, Regex.Matches(code, @"\bStore\s*\.").Count);

        // ورَفضُ النُقطَتَينِ يَصِلُ بِـ`?err=` — إلى صَفحَةٍ لا تَقرَؤُه.
        Assert.Contains("$\"?err={res.Violation!.Code}\"", Endpoint("/{slug}/deals/{id:guid}/accept"), StringComparison.Ordinal);
        Assert.Contains("$\"?err={res.Violation!.Code}\"", Endpoint("/{slug}/deals/{id:guid}/cancel"), StringComparison.Ordinal);
    }

    /// <summary><b>قُلِبَ</b> (C2): كانَ أُنبوبُ <c>POST /{slug}/users/{userId}/chat</c> —
    /// مَولودُ صَفحَةِ السائِقين، ومُستَهلِكُه الثاني زِرُّ «تَواصَل مَع البائِع» — يُسقِطُ
    /// كُلَّ فَشَلٍ عَلى <c>drivers</c> ولا يَقرَأُ <c>returnUrl</c>، والصَفحَةُ لا تُرسِلُه.
    /// وصارَ يَقرَأُ مَنشَأَه ويَعودُ إلَيه بِرَمزِه عِندَ الطَرَفِ المَحذوف
    /// (<c>ChatStartReturnTests</c>)؛ ومَسقَطُ الرَأسِ بِلا مَنشَأٍ كَما كان.</summary>
    [Fact]
    public void The_user_chat_pipe_now_reads_its_origin_and_sends_a_deleted_partner_back_with_a_code()
    {
        var pipe = Endpoint("/{slug}/users/{userId:guid}/chat");
        Assert.Contains("var origin = Services.LocalRedirect.Resolve(req.Form[\"returnUrl\"].ToString(), Link(req, slug, $\"drivers\"));", pipe, StringComparison.Ordinal);
        Assert.Contains("Services.ChatStartReturn.Failure(origin, Services.ChatStartReturn.PartnerMissing)", pipe, StringComparison.Ordinal);
        Assert.Equal(0, Count(pipe, "return Results.Redirect(Link(req, slug, $\"drivers\"));"));
        Assert.Equal(1, Count(pipe, "Link(req, slug, $\"drivers\")"));   // مَسقَطُ الرَأسِ باقٍ سُقوطاً لِلمَنشَأ

        var page = DealPageSource();
        Assert.Contains("action=\"@($\"/{Slug}/users/{vid}/chat\")\"", page, StringComparison.Ordinal);
        Assert.Equal(1, Count(page, "name=\"returnUrl\""));
    }
}
