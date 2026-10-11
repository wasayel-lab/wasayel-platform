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
// الصَفَقات) بِقَرارٍ مَرئِيّ** — فَيُكتَبُ كَما هُوَ اليَوم (‏2026-10-11، ‏52eaaa2)
// لِيَكونَ الفَرقُ بَينَ «قَبل» و«بَعد» مَقيساً لا مَوصوفاً:
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

    /// <summary><c>DealsPolicy</c> و<c>DealPatternCatalog</c> و<c>DealTurnView</c> لا
    /// تَعرِفُ كَلِمَةَ <c>pay_method</c> ولا مَعجَمَ <c>CheckoutPayMethods</c>؛ و
    /// <c>Next</c> و<c>StagesFor</c> بِحِملٍ واحِدٍ لِكُلٍّ، لا مُعامِلَ فيه لِطَريقَةِ
    /// الدَفع — فَلا سَبيلَ لِأَن تُجيبَ سُؤالاً عَن النَقد.</summary>
    [Fact]
    public void The_policy_has_no_pay_method_in_its_vocabulary_or_its_signatures_today()
    {
        foreach (var src in new[] { PolicySource(), CatalogSource(), TurnViewSource() })
        {
            var code = WriteEndpointGuardTests.StripComments(src);
            Assert.DoesNotContain("pay_method", code, StringComparison.Ordinal);
            Assert.DoesNotContain("CheckoutPayMethods", code, StringComparison.Ordinal);
            Assert.DoesNotContain("CashOnDelivery", code, StringComparison.Ordinal);
        }

        Assert.Single(typeof(DealsPolicy).GetMethods().Where(m => m.Name == nameof(DealsPolicy.Next)));
        Assert.Single(typeof(DealsPolicy).GetMethods().Where(m => m.Name == nameof(DealsPolicy.StagesFor)));
        Assert.Single(typeof(DealTurnView).GetMethods().Where(m => m.Name == nameof(DealTurnView.For)));
    }

    /// <summary>صَفقَةُ النَقدِ تَمشي <c>Confirmed → Paid → Shipping</c> كَصَفقَةِ
    /// البِطاقَةِ حَرفاً، و<c>Paid</c> لا يُحَرِّكُها إلّا المُشتَري — أَي أَنّ
    /// البائِعَ لا يَستَطيعُ نَقلَ صَفقَةِ نَقدٍ مُؤَكَّدَةٍ إلى الشَحنِ بِنَفسِه
    /// (موجَزُ الفَجَوات P1-3).</summary>
    [Fact]
    public void A_cash_deal_walks_Confirmed_then_Paid_then_Shipping_today_and_Paid_waits_for_the_buyer()
    {
        var confirmed = CashDeal(counterparty: Seller, stage: DealStage.Confirmed);
        Assert.Equal(DealStage.Paid, DealsPolicy.Next(confirmed.Pattern, confirmed.Stage));
        Assert.Equal("either", DealsPolicy.Actor(confirmed.Stage));
        Assert.True(Allowed(confirmed, Buyer, "either"));
        Assert.True(Allowed(confirmed, Seller, "either"));

        var paid = CashDeal(counterparty: Seller, stage: DealStage.Paid);
        Assert.Equal(DealStage.Shipping, DealsPolicy.Next(paid.Pattern, paid.Stage));
        Assert.Equal("initiator", DealsPolicy.Actor(paid.Stage));
        Assert.True(Allowed(paid, Buyer, "initiator"));
        Assert.False(Allowed(paid, Seller, "initiator"));

        var shipping = CashDeal(counterparty: Seller, stage: DealStage.Shipping);
        Assert.Equal(DealStage.Delivered, DealsPolicy.Next(shipping.Pattern, shipping.Stage));
        Assert.Equal("counterparty", DealsPolicy.Actor(shipping.Stage));
        Assert.True(Allowed(shipping, Seller, "counterparty"));
        Assert.False(Allowed(shipping, Buyer, "counterparty"));

        // والمَسارُ نَفسُه لِلبِطاقَة — لا فَرقَ تَقرَؤُه السِياسَة.
        Assert.Equal(
            new[] { DealStage.Offered, DealStage.Booked, DealStage.Confirmed, DealStage.Paid, DealStage.Shipping, DealStage.Delivered, DealStage.Reviewed },
            DealsPolicy.StagesFor("marketplace").ToArray());
    }

    /// <summary><c>AdvanceAsync</c> تَقرَأُ التالِيَ مِن (النَمَط، المَرحَلَة) وَحدَهُما،
    /// ولا تَذكُرُ النَقدَ ولا <c>Delivered</c> — فَلا سَطرَ في السِجِلِّ يَقولُ
    /// «يُقبَضُ عِندَ التَسليم» لِأَحَد.</summary>
    [Fact]
    public void AdvanceAsync_reads_pattern_and_stage_only_and_writes_no_cash_on_delivery_line_today()
    {
        var advance = AdvanceBody();
        Assert.Contains("var next = DealsPolicy.Next(deal.Pattern, deal.Stage);", advance, StringComparison.Ordinal);
        Assert.DoesNotContain("pay_method", advance, StringComparison.Ordinal);
        Assert.DoesNotContain("CheckoutPayMethods", advance, StringComparison.Ordinal);
        Assert.DoesNotContain("DealStage.Delivered", advance, StringComparison.Ordinal);
    }

    // ═══ ٢) القَبولُ اليَوم: فاعِلٌ مُستَعار، ونَتيجَةٌ مُهمَلَة، ومُقارَنَةٌ نَصِّيَّة ═══

    /// <summary><c>AcceptAsync</c> تُحَرِّكُ بِـ<c>deal.InitiatorId</c> بِاسمِ القابِل
    /// (السَلسَلَةُ بِحَرفِها في <c>DealCounterpartyCharacterizationTests</c>) <b>ولا
    /// تَقرَأُ <c>advanced.Ok</c></b>: تُرجِعُ <c>Accepted</c> أَيّاً كانَت نَتيجَةُ
    /// التَقَدُّم — رَفضٌ يُبتلَع.</summary>
    [Fact]
    public void AcceptAsync_returns_Accepted_without_reading_the_advance_result_today()
    {
        var accept = AcceptBody();
        Assert.Contains("deal.InitiatorId, by.ActorName, \"قُبِلَ العَرض\"", accept, StringComparison.Ordinal);
        Assert.Contains("return DealAcceptResult.Accepted(advanced.Deal ?? deal);", accept, StringComparison.Ordinal);
        Assert.DoesNotContain("advanced.Ok", accept, StringComparison.Ordinal);
    }

    /// <summary>مَرجِعُ المالِكِ يُقارَنُ <b>نَصّاً</b> بِـ<c>ActorId.ToString()</c> في
    /// <c>Validate</c> و<c>IsIncomingOfferFor</c> وفي الشاشَة — فَنَفسُ الـGuid
    /// بِصيغَةٍ أُخرى (أَحرُفٌ كَبيرَة، أَو <c>N</c> بِلا شَرطات) <b>لَيسَ
    /// المالِك</b> اليَوم، وتُجابُ «لَستَ المالِك».</summary>
    [Fact]
    public void The_owner_ref_is_compared_as_a_raw_string_so_another_format_of_the_same_guid_is_not_the_owner_today()
    {
        var upper = CashDeal(); upper.Refs[DealsService.ListingOwnerRef] = Seller.ToString("D").ToUpperInvariant();
        var n     = CashDeal(); n.Refs[DealsService.ListingOwnerRef]     = Seller.ToString("N");

        Assert.Equal(DealAcceptAuthorization.ActorNotListingOwner, DealAcceptAuthorization.Validate(upper, new DealAcceptor(Seller, "بائِع"))!.Code);
        Assert.Equal(DealAcceptAuthorization.ActorNotListingOwner, DealAcceptAuthorization.Validate(n,     new DealAcceptor(Seller, "بائِع"))!.Code);
        Assert.False(DealsService.IsIncomingOfferFor(upper, Seller));
        Assert.False(DealsService.IsIncomingOfferFor(n, Seller));
        // والصيغَةُ الَّتي يَكتُبُها الكاتِبُ (`ToString()` = D صَغيرَة) تَمُرّ.
        Assert.Null(DealAcceptAuthorization.Validate(CashDeal(), new DealAcceptor(Seller, "بائِع")));
        Assert.True(DealsService.IsIncomingOfferFor(CashDeal(), Seller));

        var validate = WriteEndpointGuardTests.StripComments(Source(Template, "Services", "Deals", "DealAcceptAuthorization.cs"));
        Assert.Contains("if (owner != by.ActorId.ToString())", validate, StringComparison.Ordinal);
        Assert.Contains("isListingOwner = ownerRef == uid.ToString() || deal.CounterpartyId == uid;", DealPageSource(), StringComparison.Ordinal);
    }

    /// <summary>صَفقَةٌ لَها طَرَفٌ مُقابِلٌ — وإن كانَ هُوَ مالِكَ الإعلانِ نَفسَه —
    /// تُجابُ <c>deal_already_accepted</c> في <c>Offered</c>: فَرَبطُ البائِعِ عِندَ
    /// الوِلادَةِ (الطَريقُ الأَوَّلُ في ‏ADR-033 §٢‑ج‑أ) يَستَلزِمُ قَلبَ هذا الصَفّ.</summary>
    [Fact]
    public void A_deal_whose_counterparty_is_already_the_listing_owner_cannot_be_accepted_by_them_today()
    {
        var bound = CashDeal(counterparty: Seller);
        Assert.Equal(DealStage.Offered, bound.Stage);
        Assert.Equal(DealAcceptAuthorization.DealAlreadyAccepted,
            DealAcceptAuthorization.Validate(bound, new DealAcceptor(Seller, "بائِع"))!.Code);
    }

    // ═══ ٣) لا طَرَفَ عِندَ الوِلادَة، ولا إشعارَ في المَسارَين ═══════════════

    /// <summary><c>/checkout/submit</c> يُلحِقُ <c>listing_owner</c> ولا يَربُطُ طَرَفاً
    /// مُقابِلاً ولا يَكتُبُ إشعاراً ولا يُنادي الجَرَسَ ولا الدَفعَ الفَوريّ —
    /// فَالبائِعُ لا يَعلَمُ أَنّ طَلَباً وَصَلَ إلّا إن فَتَحَ «صَفقات» بِنَفسِه
    /// (موجَزُ الفَجَوات P1-5).</summary>
    [Fact]
    public void Checkout_submit_attaches_the_owner_ref_but_binds_no_counterparty_and_notifies_nobody_today()
    {
        var submit = Endpoint("/{slug}/checkout/submit");
        Assert.Equal(1, Count(submit, "await deals.AttachRefAsync(slug, deal.Id, \"listing_owner\", oid);"));
        Assert.Equal(0, Count(submit, "AssignCounterpartyAsync("));
        Assert.Equal(0, Count(submit, "Notification"));
        Assert.Equal(0, Count(submit, "NudgeAsync("));
        Assert.Equal(0, Count(submit, "push."));
    }

    /// <summary>و<c>/accept</c> لا يُشعِرُ المُشتَري بِأَنّ طَلَبَه قُبِل — بَينَما
    /// نُقطَةُ قَبولِ العَرضِ في عِدَّةِ العُروض تَفعَل (<c>offer_accepted</c>).</summary>
    [Fact]
    public void The_accept_endpoint_notifies_nobody_today()
    {
        var accept = Endpoint("/{slug}/deals/{id:guid}/accept");
        Assert.Contains("deals.AcceptAsync(", accept, StringComparison.Ordinal);
        Assert.Equal(0, Count(accept, "Notification"));
        Assert.Equal(0, Count(accept, "NudgeAsync("));
        Assert.Equal(0, Count(accept, "push."));

        // والمُقارَنُ القائِمُ في المِلَفِّ نَفسِه يَكتُبُ ويُنادي — فَالأُنبوبُ مَوجود.
        var offers = EndpointsSource();
        Assert.Contains("Type = \"offer_accepted\",", offers, StringComparison.Ordinal);
        Assert.True(Count(offers, "await NudgeAsync(hub, slug,") >= 3);
    }

    /// <summary>تَعليقُ <c>Deal.cs</c> يَقول «إِشعار الأَطراف يَخرُج تِلقائيّاً» —
    /// و<c>DealsService</c> لا تَذكُرُ كَلِمَةَ <c>Notification</c> حَرفاً. دَعوى بِلا كود.</summary>
    [Fact]
    public void Deal_cs_claims_automatic_notification_while_the_service_writes_none_today()
    {
        Assert.Contains("إِشعار الأَطراف يَخرُج تِلقائيّاً", DealSource(), StringComparison.Ordinal);
        Assert.Equal(0, Count(DealsServiceSource(), "Notification"));
    }

    // ═══ ٤) صَفحَةُ الصَفقَةِ وأُنبوبُ المُحادَثَة ═══════════════════════════

    /// <summary>«دَورُكَ الآن» والـstepper والمَرحَلَةُ التالِيَةُ تُشتَقُّ مِن
    /// <c>(deal.Pattern, deal.Stage)</c> وَحدَهُما — فَالمُشتَري في صَفقَةِ نَقدٍ عِندَ
    /// <c>Paid</c> يُقالُ لَه «دَورُكَ الآن … تَأكيد: شَحن» والبائِعُ «بانتِظار».</summary>
    [Fact]
    public void The_deal_page_derives_turn_and_stepper_from_pattern_and_stage_alone_today()
    {
        var page = DealPageSource();
        Assert.Contains("Services.Ux.DealTurnView.For(deal.Pattern, deal.Stage, mySide)", page, StringComparison.Ordinal);
        Assert.Contains("stages = DealsPolicy.StagesFor(deal.Pattern).ToList();", page, StringComparison.Ordinal);
        Assert.Contains("nextStage = DealsPolicy.Next(deal.Pattern, deal.Stage);", page, StringComparison.Ordinal);
        Assert.Contains("var acceptCase = deal.Stage == DealStage.Offered && isListingOwner && !isInitiator;", page, StringComparison.Ordinal);
        Assert.Contains("var mine = acceptCase || turn.IsMine;", page, StringComparison.Ordinal);
        var gate = Squash(page);
        Assert.Contains("\"initiator\" => isInitiator, \"counterparty\" => deal.CounterpartyId == uid, \"either\" => isInitiator || deal.CounterpartyId == uid,", gate, StringComparison.Ordinal);

        var buyerAtPaid  = DealTurnView.For("marketplace", DealStage.Paid, DealSide.Initiator);
        var sellerAtPaid = DealTurnView.For("marketplace", DealStage.Paid, DealSide.Counterparty);
        Assert.True(buyerAtPaid.IsMine);
        Assert.Equal(DealStage.Shipping, buyerAtPaid.NextStage);
        Assert.False(sellerAtPaid.IsMine);

        Assert.Contains("var stages = DealsPolicy.StagesFor(Pattern).ToList();", FlowExplainerSource(), StringComparison.Ordinal);
        Assert.DoesNotContain("PayMethod", FlowExplainerSource(), StringComparison.Ordinal);
    }

    /// <summary>لا سَطرَ يَرسُمُ <c>Attributes</c> (عُنوانُ التَوصيلِ وهاتِفُ المُشتَري
    /// مَكتوبانِ في الصَفقَةِ ولا يَراهُما البائِع — P1-4)، ولا مُعامِلَ <c>err</c>
    /// (رَفضُ <c>/accept</c> و<c>/cancel</c> يَرتَدُّ بِرَمزٍ لا تَقرَؤُه الصَفحَة)،
    /// وحَقنُ <c>IDocumentStore</c> مَيِّتٌ: يُحقَنُ ولا يُقرَأ.</summary>
    [Fact]
    public void The_deal_page_renders_no_delivery_attributes_reads_no_err_and_carries_a_dead_store_injection_today()
    {
        var page = DealPageSource();
        Assert.Equal(0, Count(page, "Attributes["));
        Assert.DoesNotContain("SupplyParameterFromQuery", page, StringComparison.Ordinal);
        Assert.Contains("@inject IDocumentStore Store", page, StringComparison.Ordinal);
        Assert.Equal(0, Regex.Matches(WriteEndpointGuardTests.StripMarkupComments(page), @"\bStore\s*\.").Count);

        // ورَفضُ النُقطَتَينِ يَصِلُ بِـ`?err=` — إلى صَفحَةٍ لا تَقرَؤُه.
        Assert.Contains("$\"?err={res.Violation!.Code}\"", Endpoint("/{slug}/deals/{id:guid}/accept"), StringComparison.Ordinal);
        Assert.Contains("$\"?err={res.Violation!.Code}\"", Endpoint("/{slug}/deals/{id:guid}/cancel"), StringComparison.Ordinal);
    }

    /// <summary>أُنبوبُ <c>POST /{slug}/users/{userId}/chat</c> — مَولودُ صَفحَةِ
    /// السائِقين، وصارَ لَه مُستَهلِكٌ ثانٍ (زِرُّ «تَواصَل مَع البائِع» عَلى صَفحَةِ
    /// الصَفقَة) — يُسقِطُ كُلَّ فَشَلٍ عَلى <c>drivers</c>: الدُخولُ، والنَفسُ، والطَرَفُ
    /// المَحذوف؛ ولا يَقرَأُ <c>returnUrl</c>، والصَفحَةُ لا تُرسِلُه.</summary>
    [Fact]
    public void The_user_chat_pipe_sends_every_failure_to_drivers_and_knows_no_origin_today()
    {
        var pipe = Endpoint("/{slug}/users/{userId:guid}/chat");
        Assert.Contains("login?returnUrl=/{slug}/drivers", pipe, StringComparison.Ordinal);
        Assert.Equal(2, Count(pipe, "Link(req, slug, $\"drivers\")"));
        Assert.Contains("if (partner is null) return Results.Redirect(Link(req, slug, $\"drivers\"));", pipe, StringComparison.Ordinal);
        Assert.DoesNotContain("req.Form[\"returnUrl\"]", pipe, StringComparison.Ordinal);

        var page = DealPageSource();
        Assert.Contains("action=\"@($\"/{Slug}/users/{vid}/chat\")\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"returnUrl\"", page, StringComparison.Ordinal);
    }
}
