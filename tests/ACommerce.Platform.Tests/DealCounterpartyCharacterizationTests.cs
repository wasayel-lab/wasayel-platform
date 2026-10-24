using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ACommerce.Kit.Payments;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ الطَرَفِ المُقابِلِ وفاعِلَي Paid وShipping — ‏ADR-033 §٢‑هـ الخُطوَة 2 ═══
//
// ‏ADR-033 §٢‑هـ الخُطوَة 2: «كوميتُ توصيفٍ مستقلّ … ويُضافُ قبلَ أيِّ تبديلٍ
// توصيفُ فاعلي Paid وShipping وأنّ CounterpartyId ≠ Refs["listing_owner"] في
// المسارَين». وهذا هُوَ الكوميت — **مُتَأَخِّراً عَن مَوضِعِه** (‏2026-10-11):
// كوميتُ التَخويل ‏2d8cf5f نَقَلَ ما بَعدَ التَخويلِ مِن جِسمِ نُقطَةِ `/accept`
// إلى `DealsService.AcceptAsync` وقَلَبَ شَطرَ «يُخرِجُها /accept بِأَيِّ مُسَجَّل»
// قَبلَ أَن يُثَبَّت. فَذاكَ الشَطرُ لا يُثَبَّتُ كَما كان (زال)، ويُثَبَّتُ هُنا
// **ما بَقِيَ حَقّاً اليَوم** حَرفاً، ويُقالُ الانحِرافُ في التَرتيبِ في ‏ADR-033
// §٢‑ج‑أ بِتاريخِه وفي سِجِلِّ المالِك (S1·٩) — لا يُبتلَع.
//
// **القياسُ الَّذي كَتَبَ هذا المِلَفّ** (‏2026-10-11، قَبلَ هذا الكوميت): في
// `tests/` **صِفرُ** مِلَفٍّ يَحمِلُ `deal.InitiatorId, by.ActorName` أَو «قُبِلَ
// العَرض» أَو يُقارِنُ `CounterpartyId` بِـ`listing_owner`؛ وصِفرُ مِلَفٍّ يَذكُرُ
// «مالِك التَّطبيق» (تَعيينُ المَسارِ الإداريّ)؛ و`IsActorAllowed` تُذكَرُ في
// تَعليقٍ واحِد (`FlowDefinitionValidatorTests.cs:310`) ولا تُقاس؛ و
// `AssignCounterpartyAsync` تُنادى في بُرهانٍ حَيٍّ واحِد تَمهيداً لا مَوصوفاً
// (`DealPaidStageCharacterizationTests.cs:350`). فَلَو انجَرَفَ فاعِلُ
// `AdvanceAsync` داخِلَ `AcceptAsync` مِن المُبادِرِ إلى القابِل لَرُفِضَ
// `Offered → Booked` (المَرحَلَةُ تَطلُبُ `initiator`) **بِلا اختِبارٍ أَحمَر**.
//
// **ولِماذا شَطرانِ لا شَطر** (نَسَقُ `DealPaidStageCharacterizationTests`):
//   • **تَوصيفٌ مِن المَصدَرِ والانعِكاس** يَعمَلُ في كُلِّ تَشغيل — يَقرَأُ أَجسامَ
//     الدَوالِّ بِلا تَعليقاتٍ ويَستَدعي `IsActorAllowed` الخاصَّةَ بِالانعِكاس،
//     فَيُقاسُ القَرارُ لا الكَلامُ عَنه. وهُوَ الحَكَمُ في CI.
//   • **بُرهانٌ حَيٌّ** عَلى Postgres بِنَفسِ عَقدِ `Live*ProofTests` (يَتَخَطّى
//     نَفسَه بِلا `WASAYEL_LIVE_PROOF=1` و`ConnectionStrings__Postgres`) يَمشي
//     القَبولَ فِعلاً ويَقرَأُ السِجِلَّ الزَمَنيَّ المَكتوب. **ولَم يُشَغَّل في
//     الجَلسَةِ الَّتي كَتَبَته** (لا اعتِمادَ قاعِدَةٍ مُتاحاً لَها، والمِجَسُّ
//     الوَحيدُ نَحوَها رُفِض)، فَيُقالُ ذلك ولا يُدَّعى: الحَكَمُ المَقيسُ هُنا
//     شَطرُ المَصدَرِ والانعِكاس.
//
// **وما لا يُكَرَّرُ هُنا** (القاعِدَة ٨): فاعِلُ كُلِّ مَرحَلَةٍ لِذاتِه مُثَبَّتٌ
// في `DealsPolicyCharacterizationTests` حَرفاً فَيُستَشهَدُ بِه ولا يُنسَخ؛
// وتَجاوُزُ `TenantProviderSet.FromDocuments` لِوَثيقَةٍ مُعَرِّفُها ≠ قُدرَتِها
// وعَودَةُ `Platform` بِنَفسِ المَرجِعِ عِندَ الصِفرِ مُثَبَّتانِ سُلوكاً في
// `TenantProviderZeroEquivalenceTests` (`A_binding_whose_provider_serves_another_capability_is_ignored`
// و`No_binding_returns_the_platform_snapshot_by_reference`) — فَيُثَبَّتُ مِنهُما
// هُنا شَطرُ «بِتَحذير» وَحدَه؛ وتَعليقُ «يَدخُل Deals بِمَرحَلَة Booked» القَديمُ
// صُحِّحَ في ‏2d8cf5f وحارِسُه في `DealAcceptAuthorizationTests`.

public class DealCounterpartyCharacterizationTests
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

    private static string DealsServiceSource() => Source(
        "libs", "templates", "ACommerce.Templates.Customer.Marketplace",
        "Services", "Deals", "DealsService.cs");

    private static string EndpointsSource() => Source(
        "libs", "templates", "ACommerce.Templates.Customer.Marketplace",
        "MarketplaceTemplateExtensions.cs");

    private static string ProviderBindingSource() => Source(
        "libs", "core", "ACommerce.Platform.Providers", "TenantProviderBinding.cs");

    /// <summary>مَقطَعٌ مِن المَصدَرِ مِن <paramref name="from"/> إلى ما قَبلَ
    /// <paramref name="to"/> — <b>بِلا تَعليقات</b>، فَلا يُقاسُ كَلامٌ عَن الكودِ
    /// بَدَلَ الكود. يَفشَلُ إن لَم يُوجَد الطَرَفان، فَلا يُقاسُ فَراغ.</summary>
    private static string Segment(string source, string from, string to)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start > 0, $"‏`{from}` غَيرُ مَوجود — الأَداةُ عَمياء.");
        var end = source.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"‏`{to}` لا يَتلو `{from}` — الأَداةُ عَمياء.");
        return WriteEndpointGuardTests.StripComments(source.Substring(start, end - start));
    }

    // أَجسامُ دَوالِّ الخِدمَة — كُلٌّ مِن تَوقيعِه إلى تَوقيعِ التالي.
    private static string StartBody()   => Segment(DealsServiceSource(), "public async Task<Deal> StartAsync(",               "public async Task<Deal?> AssignCounterpartyAsync(");
    // (كانَ طَرَفُها `AcceptAsync`؛ وصارَ `BindListingOwnerAsync` حينَ وُلِدَت بَينَهُما — ‏ADR-033 §٦.)
    private static string AssignBody()  => Segment(DealsServiceSource(), "public async Task<Deal?> AssignCounterpartyAsync(", "public async Task<Deal?> BindListingOwnerAsync(");
    private static string AcceptBody()  => Segment(DealsServiceSource(), "public async Task<DealAcceptResult> AcceptAsync(",  "public async Task<DealAdvanceResult> AdvanceAsync(");
    private static string AdvanceBody() => Segment(DealsServiceSource(), "public async Task<DealAdvanceResult> AdvanceAsync(", "public async Task<DealCancelResult> CancelAsync(");
    private static string AttachBody()  => Segment(DealsServiceSource(), "public async Task<Deal?> AttachRefAsync(",          "public async Task<Deal?> LoadAsync(");
    private static string ActorGateBody() => Segment(DealsServiceSource(), "static bool IsActorAllowed(",                    "public sealed record DealAdvanceResult");

    /// <summary>جِسمُ نُقطَةٍ مِن حَرفِ مَسارِها (بِعَلامَتَي تَنصيصِه) إلى خاتِمَتِها.</summary>
    private static string Endpoint(string route, string terminator)
        => Segment(EndpointsSource(), "\"" + route + "\"", terminator);

    /// <summary>فَراغاتٌ مُتَتالِيَةٌ إلى واحِدَة — لِمُطابَقَةِ أَذرُعِ <c>switch</c>
    /// المُحاذاةِ بِأَعمِدَةٍ بِلا تَثبيتِ عَدَدِ الفَراغات.</summary>
    private static string Squash(string s) => Regex.Replace(s, @"\s+", " ");

    private static int Count(string text, string needle)
    {
        var n = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            n++;
        return n;
    }

    /// <summary>مِن أَوَّلِ <c>{</c> بَعدَ المَوضِعِ إلى قَوسِه المُقابِل.</summary>
    private static string BlockFrom(string text, int at)
    {
        var open = text.IndexOf('{', at);
        Assert.True(open > at, "لا كُتلَةَ بَعدَ الشَرط.");
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0)
                return text.Substring(open, i - open + 1);
        }
        Assert.Fail("كُتلَةٌ بِلا قَوسٍ مُقابِل.");
        return "";
    }

    /// <summary><c>DealsService.IsActorAllowed</c> الخاصَّةُ الساكِنَة — تُستَدعى
    /// بِالانعِكاسِ لِأَنّ القَرارَ فيها وَحدَها، و<c>AdvanceAsync</c> لا تُنادى
    /// بِلا قاعِدَة. تَفشَلُ إن غُيِّرَ اسمُها أَو شَكلُها، فَلا تُقاسُ دالَّةٌ أُخرى.</summary>
    private static readonly MethodInfo ActorGate = typeof(DealsService)
        .GetMethod("IsActorAllowed", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("‏`DealsService.IsActorAllowed` غَيرُ مَوجودَة — الأَداةُ عَمياء.");

    private static bool Allowed(Deal deal, Guid actor, string required)
        => (bool)ActorGate.Invoke(null, new object[] { deal, actor, required })!;

    private static readonly Guid Buyer    = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");   // المُبادِر
    private static readonly Guid Seller   = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");   // مالِكُ الإعلان
    private static readonly Guid AppOwner = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");   // مالِكُ التَطبيق (المَسارُ الإداريّ)
    private static readonly Guid Stranger = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");

    /// <summary>صَفقَةُ سَلَّةٍ كَما تَخرُجُ مِن <c>/checkout/submit</c>: المُشتَري
    /// مُبادِر، ومالِكُ الإعلانِ في المَرجِعِ وَحدَه، ونَقدٌ عِندَ الاستِلام، وبِلا
    /// طَرَفٍ مُقابِلٍ ما لَم يُعَيَّن.</summary>
    private static Deal CartDeal(Guid? counterparty = null, DealStage stage = DealStage.Offered)
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

    // ═══ ١) الفاعِلُ يُقاسُ بِالهُوِيَّةِ المَكتوبَةِ عَلى الصَفقَة، لا بِالمَرجِع ═══

    /// <summary><b>مَن يَشحَنُ هُوَ مَن يَحمِلُ <c>CounterpartyId</c></b> — ولا يُسأَلُ
    /// <c>Refs["listing_owner"]</c>. فَصَفقَةٌ عَيَّنَ المَسارُ الإداريُّ مالِكَ
    /// التَطبيقِ طَرَفاً يَشحَنُها هُوَ لا مالِكُ إعلانِها؛ وصَفقَةٌ بِلا طَرَفٍ لا
    /// يَشحَنُها أَحَد. <b>وهذا بِعَينِه ما تَحسُمُه الخُطوَة 3 بِقَرارٍ مَرئِيّ</b>
    /// (‏ADR-033 §٢‑ج‑أ: أَحَدُ طَريقَين) — فَيُثَبَّتُ قَبلَها كَما هُو.</summary>
    [Fact]
    public void Shipping_is_moved_by_whoever_holds_CounterpartyId_and_the_listing_owner_ref_is_not_consulted()
    {
        Assert.Equal("counterparty", DealsPolicy.Actor(DealStage.Shipping));

        var admin = CartDeal(counterparty: AppOwner, stage: DealStage.Shipping);
        Assert.True(Allowed(admin, AppOwner, "counterparty"));
        Assert.False(Allowed(admin, Seller, "counterparty"));
        Assert.False(Allowed(admin, Buyer, "counterparty"));

        var unassigned = CartDeal(stage: DealStage.Shipping);
        Assert.False(Allowed(unassigned, Seller, "counterparty"));
        Assert.False(Allowed(unassigned, Buyer, "counterparty"));

        // وحينَ يَقبَلُ مالِكُ الإعلانِ عَبرَ /accept يَصيرُ هُوَ الطَرَفَ — فَيَشحَن.
        var accepted = CartDeal(counterparty: Seller, stage: DealStage.Shipping);
        Assert.True(Allowed(accepted, Seller, "counterparty"));
    }

    /// <summary><b><c>Paid</c> يُحَرِّكُها المُبادِرُ وَحدَه</b> — حَتّى لِصَفقَةِ نَقدٍ،
    /// حَيثُ «مَدفوعَة» قَبلَ التَسليمِ وَصفٌ لا يَصدُق. مَن يُحَرِّكُ
    /// <c>Paid → Shipping</c> لِلنَقدِ قَرارُ الخُطوَة 3 — يُثَبَّتُ القائِمُ قَبلَه.</summary>
    [Fact]
    public void Paid_is_moved_by_the_initiator_alone_even_for_a_cash_deal()
    {
        Assert.Equal("initiator", DealsPolicy.Actor(DealStage.Paid));
        var cash = CartDeal(counterparty: Seller, stage: DealStage.Paid);
        Assert.Equal(CheckoutPayMethods.CashOnDelivery, cash.Attributes["pay_method"]);
        Assert.True(Allowed(cash, Buyer, "initiator"));
        Assert.False(Allowed(cash, Seller, "initiator"));
        Assert.False(Allowed(cash, AppOwner, "initiator"));
    }

    /// <summary>الكَلِماتُ أَربَعٌ (‏META-MODEL §4.2)، وكَلِمَةٌ خارِجَها لا تُمَرِّرُ
    /// أَحَداً — حَتّى المُبادِر. و<c>platform</c> تُمَرِّرُ أَيَّ هُوِيَّة (كودُ
    /// مَنصَّة — «افتَرِض السَّماح» بِحَرفِ تَعليقِ المَصدَر).</summary>
    [Fact]
    public void The_actor_words_are_four_and_an_unknown_word_lets_nobody_through()
    {
        Assert.Equal(new[] { "initiator", "counterparty", "either", "platform" }, DealPatternCatalog.Actors);

        var d = CartDeal(counterparty: Seller);
        Assert.True(Allowed(d, Buyer, "either"));
        Assert.True(Allowed(d, Seller, "either"));
        Assert.False(Allowed(d, Stranger, "either"));
        Assert.True(Allowed(d, Stranger, "platform"));
        Assert.False(Allowed(d, Buyer, "owner"));
        Assert.False(Allowed(d, Seller, "listing_owner"));
    }

    /// <summary><c>AdvanceAsync</c> تَفحَصُ فاعِلَ <b>المَرحَلَةِ الحالِيَّة</b> لا
    /// التالِيَة، وتَقيسُ الطَرَفَ بِـ<c>deal.CounterpartyId == actorId</c> في ذِراعِ
    /// <c>switch</c> لا تَقرَأُ <c>Refs</c> إطلاقاً.
    /// <para><b>قُلِبَ شَطرُه الأَوَّل</b> (‏ADR-033 §٦‑ط، ‏2026-10-11): كانَ
    /// <c>AdvanceAsync_checks_the_actor_of_the_current_stage_…</c> — <c>Actor(deal.Stage)</c>، إزاحَةُ مَرحَلَةٍ في
    /// قِراءَةِ عَمودِ الفاعِل. وصارَت تَفحَصُ فاعِلَ المَرحَلَةِ المَدخولَة بِالدالَّةِ الواحِدَة
    /// <c>DealsPolicy.RequiredActorToLeave</c>؛ والقِياسُ بِالهُوِيَّةِ بِلا <c>Refs</c> بِحَرفِه.</para></summary>
    [Fact]
    public void AdvanceAsync_checks_the_actor_of_the_entered_stage_and_measures_the_counterparty_by_identity()
    {
        var advance = AdvanceBody();
        Assert.Contains("var required = DealsPolicy.RequiredActorToLeave(deal)!;", advance, StringComparison.Ordinal);
        Assert.Contains("if (!IsActorAllowed(deal, actorId, required))", advance, StringComparison.Ordinal);
        Assert.DoesNotContain("DealsPolicy.Actor(deal.Stage)", advance, StringComparison.Ordinal);

        var gate = Squash(ActorGateBody());
        Assert.Contains("\"initiator\" => deal.InitiatorId == actorId,", gate, StringComparison.Ordinal);
        Assert.Contains("\"counterparty\" => deal.CounterpartyId == actorId,", gate, StringComparison.Ordinal);
        Assert.Contains("\"either\" => deal.InitiatorId == actorId || deal.CounterpartyId == actorId,", gate, StringComparison.Ordinal);
        Assert.Contains("\"platform\" => true,", gate, StringComparison.Ordinal);
        Assert.Contains("_ => false", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("listing_owner", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("ListingOwnerRef", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("Refs", gate, StringComparison.Ordinal);
    }

    // ═══ ٢) الطَرَفُ المُقابِل — مَن يُعَيِّنُه، في المَسارَين ═══════════════

    /// <summary>صَفقَةُ السَلَّةِ تُولَدُ <c>Offered</c> فَعّالَةً <b>بِلا طَرَفٍ
    /// مُقابِل</b> — بِافتِراضِ النَوعِ وبِجِسمِ <c>StartAsync</c> مَعاً؛ والتَعليقُ
    /// القَديمُ «Booked مُباشَرَةً» كانَ يُخالِفُ الكود.</summary>
    [Fact]
    public void A_deal_is_born_in_Offered_and_Active_without_a_counterparty()
    {
        var fresh = new Deal();
        Assert.Equal(DealStage.Offered, fresh.Stage);
        Assert.Equal(DealStatus.Active, fresh.Status);
        Assert.Null(fresh.CounterpartyId);

        var start = StartBody();
        Assert.Contains("Stage = DealStage.Offered,", start, StringComparison.Ordinal);
        Assert.Contains("Status = DealStatus.Active,", start, StringComparison.Ordinal);
        Assert.Contains("new(DealStage.Offered, DealStage.Offered, \"created\",", start, StringComparison.Ordinal);
        Assert.DoesNotContain("CounterpartyId", start, StringComparison.Ordinal);
    }

    /// <summary><c>AssignCounterpartyAsync</c> تُرجِعُ الصَفقَةَ <b>بِلا تَغيير</b> إن
    /// كانَ لَها طَرَفٌ أَو خَرَجَت مِن <c>Active</c> — الحارِسانِ قَبلَ الكِتابَة —
    /// وتُلحِقُ سَطراً مَنسوباً إلى الطَرَفِ المُعَيَّن. ولا تَسأَلُ
    /// المَرجِعَ: <b>مَن يُنادِيها هُوَ مَن يُقَرِّرُ مَن الطَرَف</b> — ولِذلِك
    /// اختَلَفَت المَساراتُ أَدناه.
    ///
    /// <para><b>قُلِبَ حَرفٌ واحِدٌ هُنا بِقَرارٍ مَرئِيّ</b> (‏ADR-033 §٦، ‏2026-10-11): اسمُ
    /// السَطرِ كانَ <c>"assigned"</c> حَرفِيّاً وصارَ وَسيطاً <c>action</c> افتِراضِيُّه
    /// <c>"assigned"</c> — لِأَنّ رَبطَ البائِعِ عِندَ وِلادَةِ صَفقَةِ السَلَّةِ يَمُرُّ مِن
    /// هذِه الدالَّةِ نَفسِها (لا حَولَها) ويَكتُبُ <c>bound</c>، والشاشَةُ تُسَمّي
    /// <c>assigned</c> «قُبِلَ العَرض» وهُوَ لَم يُقبَل. وبَعدَ <c>ct</c> عَمداً كَي تَبقى
    /// نِداءاتُ القَبولِ والمَسارِ الإداريِّ المُثَبَّتَةُ أَدناه بِحَرفِها.</para></summary>
    [Fact]
    public void AssignCounterpartyAsync_is_a_no_op_once_a_counterparty_is_set_or_the_deal_left_Active()
    {
        var assign = AssignBody();
        var inactive = assign.IndexOf("if (deal is null || deal.Status != DealStatus.Active) return deal;", StringComparison.Ordinal);
        var taken    = assign.IndexOf("if (deal.CounterpartyId is not null) return deal;", StringComparison.Ordinal);
        var write    = assign.IndexOf("deal.CounterpartyId = counterpartyId;", StringComparison.Ordinal);
        Assert.True(inactive > 0 && taken > inactive && write > taken,
            $"حارِسا التَعيينِ لا يَسبِقانِ الكِتابَة: inactive={inactive} taken={taken} write={write}");
        Assert.Contains("CancellationToken ct = default, string action = \"assigned\")", assign, StringComparison.Ordinal);
        Assert.Contains("deal.Timeline.Add(new(deal.Stage, deal.Stage, action,", assign, StringComparison.Ordinal);
        Assert.Contains("counterpartyId, counterpartyName, null, DateTime.UtcNow));", assign, StringComparison.Ordinal);
        Assert.DoesNotContain("listing_owner", assign, StringComparison.Ordinal);
        Assert.DoesNotContain("ListingOwnerRef", assign, StringComparison.Ordinal);
    }

    /// <summary><b>ثَلاثَةُ مَساراتٍ لا رابِع</b> تُعَيِّنُ طَرَفاً في وَقتِ التَشغيل —
    /// بِالعَدِّ عَلى كُلِّ <c>.cs</c>/<c>.razor</c> تَحتَ <c>libs/</c> و<c>apps/</c>
    /// خارِجَ التَعليقات، والعَدّادُ يُطبَعُ فَلا يُقالُ «ثَلاثَة» بِلا عَدّ (القاعِدَة
    /// ١٠): نِداءٌ في <c>AcceptAsync</c>، ونِداءٌ في <c>BindListingOwnerAsync</c>
    /// (رَبطُ مالِكِ الإعلانِ عِندَ وِلادَةِ صَفقَةِ السَلَّة)، ونِداءٌ في المَسارِ
    /// الإداريّ، والتَعريفُ نَفسُه.
    ///
    /// <para><b>أَحمَرَّ هذا الصَفُّ كَما وَعَد، وقُلِبَ بِقَرارٍ مَرئِيّ</b> (‏ADR-033 §٦،
    /// ‏2026-10-11): كانَ «مَسارانِ لا ثالِث» ونَصَّ عَلى أَنّ الثالِثَ المُتَوَقَّعَ هُوَ
    /// «تَعيينُ مالِكِ الإعلانِ عِندَ إنشاءِ صَفقَةِ السَلَّة — الطَريقُ الأَوَّلُ في
    /// الخُطوَة 3» — وهُوَ ما اختيرَ. فَصارَ ثَلاثَةً، والمَسارُ الثالِثُ في الخِدمَةِ
    /// لا في جِسمِ النُقطَة.</para></summary>
    [Fact]
    public void Exactly_three_runtime_paths_assign_a_counterparty_and_all_are_named_here()
    {
        var scanned = 0;
        var hits = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var root in new[] { "libs", "apps" })
        foreach (var f in Directory.EnumerateFiles(Path.Combine(RepoRoot, root), "*.*", SearchOption.AllDirectories))
        {
            if (!f.EndsWith(".cs", StringComparison.Ordinal) && !f.EndsWith(".razor", StringComparison.Ordinal)) continue;
            if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
            scanned++;
            var code = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(f)));
            var n = Count(code, "AssignCounterpartyAsync(");
            if (n > 0) hits[Path.GetRelativePath(RepoRoot, f).Replace('\\', '/')] = n;
        }

        Assert.True(scanned > 200, $"أَداةٌ عَمياء: فُحِصَ {scanned} مِلَفّاً.");
        const string service   = "libs/templates/ACommerce.Templates.Customer.Marketplace/Services/Deals/DealsService.cs";
        const string endpoints = "libs/templates/ACommerce.Templates.Customer.Marketplace/MarketplaceTemplateExtensions.cs";
        Assert.Equal(new[] { endpoints, service }, hits.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Equal(3, hits[service]);     // التَعريفُ + نِداءُ AcceptAsync + نِداءُ BindListingOwnerAsync (رَبطُ البائِعِ عِندَ الوِلادَة)
        Assert.Equal(1, hits[endpoints]);   // المَسارُ الإداريُّ وَحدَه — نُقطَتا /accept و/checkout/submit لا تُعَيِّنانِ بِنَفسَيهِما
        Assert.Equal(4, hits.Values.Sum());
    }

    /// <summary><b>مَسارُ <c>/accept</c>: الطَرَفُ هُوَ القابِل، والقابِلُ لا يَكونُ
    /// إلّا مالِكَ الإعلان</b> — فَعَلى هذا المَسارِ <c>CounterpartyId ==
    /// Refs["listing_owner"]</c> بِالبِناء. <b>وهذا نَقيضُ ما كانَ يَومَ كُتِبَ
    /// ‏ADR-033</b> («يُعيِّنُ الناقرَ نفسَه طرفًا مقابلًا أيًّا كان» ⇒ ≠): قُلِبَ في
    /// ‏2d8cf5f قَبلَ أَن يُثَبَّت، فَيُثَبَّتُ الآنَ كَما صار، والانحِرافُ مَكتوبٌ
    /// في ‏ADR-033 §٢‑ج‑أ.</summary>
    [Fact]
    public void The_accept_path_makes_the_acceptor_the_counterparty_and_only_the_listing_owner_passes()
    {
        var accept = AcceptBody();
        Assert.Contains("await AssignCounterpartyAsync(tenantSlug, dealId, by.ActorId, by.ActorName, ct);", accept, StringComparison.Ordinal);

        Assert.Null(DealAcceptAuthorization.Validate(CartDeal(), new DealAcceptor(Seller, "بائِع")));
        Assert.Equal(DealAcceptAuthorization.ActorNotListingOwner,
            DealAcceptAuthorization.Validate(CartDeal(), new DealAcceptor(Stranger, "غَريب"))!.Code);
        Assert.Equal(DealAcceptAuthorization.ActorNotListingOwner,
            DealAcceptAuthorization.Validate(CartDeal(), new DealAcceptor(AppOwner, "مالِك التَّطبيق"))!.Code);
    }

    /// <summary><b>المَسارُ الإداريُّ: الطَرَفُ مالِكُ التَطبيق</b> حَيثُ لا طَرَفَ
    /// بَعد — ولا يُقرَأُ <c>listing_owner</c> فيه حَرفاً، فَـ<c>CounterpartyId ≠
    /// listing_owner</c> عَلى هذا المَسارِ ما لَم يَتَّفِقا صُدفَةً. والفاعِلُ الَّذي
    /// يُحَرِّكُ بِه: المُبادِرُ عِندَ <c>Offered</c> وإلّا الطَرَفُ المُقابِلُ أَو
    /// مالِكُ التَطبيق. وهُوَ المَسارُ الوَحيدُ لِلصَفقَةِ اليَتيمَة (‏db64c7b).
    /// <para><b>قُلِبَ شَطرُ الفاعِل</b> (‏ADR-033 §٦‑ط، ‏2026-10-11): كانَ
    /// <c>The_admin_path_makes_the_app_owner_the_counterparty_and_never_reads_…</c> — يَستَعيرُ المُبادِرَ عِندَ
    /// <c>Offered</c> لِأَنّ الخِدمَةَ كانَت تَسأَلُ فاعِلَ المَرحَلَةِ الحالِيَّة. وصارَت تَسأَلُ فاعِلَ المَدخولَة
    /// (<c>Booked</c> = الطَرَف)، فَيُحَرِّكُ المَسارُ بِجانِبِ المَتجَرِ وَحدَه (الطَرَفُ أَو مالِكُ التَطبيق) ولا
    /// يَستَعيرُ المُشتَري — ويَقِفُ عِندَ خُطوَتِه (يَدفَع، يَستَلِم): مَقيسٌ في <c>DealActorToLeaveTests</c>.</para></summary>
    [Fact]
    public void The_admin_path_makes_the_app_owner_the_counterparty_moves_as_the_store_side_and_never_reads_the_listing_owner_ref()
    {
        var admin = Endpoint("/studio/apps/{slug}/deals/{id:guid}/advance", ".DisableAntiforgery();");
        Assert.Contains("if (!await StudioOwnsAsync(store, auth, slug)) return Results.Redirect(\"/studio\");", admin, StringComparison.Ordinal);

        var guard  = admin.IndexOf("if (deal.CounterpartyId is null)", StringComparison.Ordinal);
        var assign = admin.IndexOf("await deals.AssignCounterpartyAsync(slug, id,", StringComparison.Ordinal);
        Assert.True(guard > 0 && assign > guard, $"التَعيينُ الإداريُّ بِلا حارِسِ «لا طَرَفَ بَعد»: guard={guard} assign={assign}");
        Assert.Contains("auth.UserId!.Value, \"مالِك التَّطبيق\");", admin, StringComparison.Ordinal);

        Assert.Contains("var actorId = deal.CounterpartyId ?? auth.UserId!.Value;", admin, StringComparison.Ordinal);
        Assert.DoesNotContain("deal.InitiatorId", admin, StringComparison.Ordinal);
        Assert.DoesNotContain("DealStage.Offered", admin, StringComparison.Ordinal);
        // (‏2026-10-24، تَدقيقُ الإطلاق brief-P0-6؛ ‏ADR-036 §١٣ — ولا يُعادُ حَرفٌ فَوقَه: صارَ بَعدَ `note` وَسيطُ `from:` — المَرحَلَةُ الَّتي رُسِمَ عَلَيها الزِرّ —
        // فَسَقَطَ القَوسُ مِن الحَرفِ المَفحوص؛ والمَقيسُ هُنا هُوِيَّةُ الفاعِلِ كَما كان، و`from:` يَقيسُه `DealAdvanceExpectedStageTests`.)
        Assert.Contains("deals.AdvanceAsync(slug, id, actorId, \"مالِك التَّطبيق (إداريّ)\", note", admin, StringComparison.Ordinal);

        Assert.DoesNotContain("listing_owner", admin, StringComparison.Ordinal);
        Assert.DoesNotContain("ListingOwnerRef", admin, StringComparison.Ordinal);
    }

    // ═══ ٣) ما بَعدَ التَخويلِ في `AcceptAsync` — السَلسَلَةُ بِحَرفِها ═══════

    /// <summary><b>السَلسَلَةُ — قُلِبَ شَطرُها الأَخيرُ بِقَرارٍ مَرئِيّ</b> (‏ADR-033 §٦،
    /// ‏2026-10-11): كانَت كَما نُقِلَت مِن جِسمِ النُقطَةِ في ‏2d8cf5f — تَحميلٌ، ثُمَّ
    /// <c>Validate</c>، ثُمَّ رَفضٌ بِرَمز، ثُمَّ تَعيينُ القابِلِ طَرَفاً، ثُمَّ
    /// <c>AdvanceAsync</c> <b>بِفاعِلٍ هُوَ المُبادِر</b> بِاسمِ القابِل، ثُمَّ
    /// <c>Accepted</c> بِلا قِراءَةِ النَتيجَة. وصارَ ما بَعدَ التَعيين: تَحميلٌ في
    /// جَلسَةِ كِتابَة، ثُمَّ <c>Next(deal)</c>، ثُمَّ <c>MoveAsync</c> <b>بِفاعِلٍ هُوَ
    /// القابِل</b> (<c>by.ActorId</c>) ومُلاحَظَةِ «قُبِلَ العَرض»، و<c>Accepted</c> بَعدَ
    /// الحَفظ. ولا نِداءَ لِـ<c>AdvanceAsync</c> ولا لِـ<c>deal.InitiatorId</c> بَعدَ اليَوم.
    ///
    /// <para><b>وقُلِبَ ثانِيَةً بِاسمِه</b> (مُراجَعَةُ ‏2026-10-11 عَلى <c>8769fa4</c>؛ ‏ADR-033 §٦‑ب‑٢
    /// مُلحَقاً): كانَ ما بَعدَ تَحميلِ جَلسَةِ الكِتابَةِ <c>Next</c> ثُمَّ <c>MoveAsync</c> بِلا فَحص —
    /// <c>Validate</c> واحِدَةٌ عَلى لَقطَةِ جَلسَةِ قِراءَةٍ قَد تَتَقادَم، فَكانَ <c>advanced</c> يُكتَبُ
    /// فَوقَ صَفقَةٍ أُلغِيَت بَينَهُما (‏<c>DealAcceptStaleSnapshotCharacterizationTests</c>). وصارَ
    /// بَينَهُما إعادَةُ <c>Validate</c> عَلى تِلكَ الوَثيقَةِ ورَفضٌ بِرَمزِها — <c>Validate</c> مَرَّتان.</para></summary>
    [Fact]
    public void AcceptAsync_validates_then_assigns_then_revalidates_the_write_session_document_then_moves_as_the_acceptor_through_MoveAsync()
    {
        var accept = AcceptBody();
        var load     = accept.IndexOf("var loaded = await LoadAsync(tenantSlug, dealId, ct);", StringComparison.Ordinal);
        var validate = accept.IndexOf("var violation = DealAcceptAuthorization.Validate(loaded, by);", StringComparison.Ordinal);
        var refuse   = accept.IndexOf("if (violation is not null) return DealAcceptResult.Refused(violation, loaded);", StringComparison.Ordinal);
        var assign   = accept.IndexOf("await AssignCounterpartyAsync(tenantSlug, dealId, by.ActorId, by.ActorName, ct);", StringComparison.Ordinal);
        var fresh    = accept.IndexOf("var fresh = await s.LoadAsync<Deal>(dealId, ct);", StringComparison.Ordinal);
        var recheck  = accept.IndexOf("var stale = DealAcceptAuthorization.Validate(fresh, by);", StringComparison.Ordinal);
        var refuse2  = accept.IndexOf("if (stale is not null) return DealAcceptResult.Refused(stale, fresh);", StringComparison.Ordinal);
        var next     = accept.IndexOf("var next = DealsPolicy.Next(deal)", StringComparison.Ordinal);
        // (والمُلاحَظَةُ `null` لا «قُبِلَ العَرض» — مُراجَعَةُ المَوجَةِ الثانِيَة، ‏2026-10-11.)
        var move     = accept.IndexOf("return DealAcceptResult.Accepted(await MoveAsync(s, deal, next, by.ActorId, by.ActorName, null, ct));", StringComparison.Ordinal);
        Assert.True(load > 0 && validate > load && refuse > validate && assign > refuse
                    && fresh > assign && recheck > fresh && refuse2 > recheck && next > refuse2 && move > next,
            $"سَلسَلَةُ القَبولِ تَغَيَّرَت: load={load} validate={validate} refuse={refuse} assign={assign} " +
            $"fresh={fresh} recheck={recheck} refuse2={refuse2} next={next} move={move}");
        Assert.Equal(2, Count(accept, "DealAcceptAuthorization.Validate("));

        Assert.Equal(1, Count(accept, "AssignCounterpartyAsync("));
        Assert.Equal(0, Count(accept, "AdvanceAsync("));
        Assert.DoesNotContain("deal.InitiatorId", accept, StringComparison.Ordinal);
    }

    /// <summary><b>ولِماذا لَم يَعُد المُبادِرُ فاعِلَ القَبول — مَقيسٌ لا مَشروح</b>:
    /// صَفُّ <c>Offered</c> ما زالَ يَطلُبُ <c>initiator</c>، فَالقابِلُ مَردودٌ عَلى
    /// مَسارِ <c>AdvanceAsync</c> العامّ — كانَ هذا سَبَبَ الاستِعارَة. وصارَ القَبولُ
    /// انتِقالاً لَه بَوّابَتُه (<c>Validate</c>) ولا يَمُرُّ بِهذِه البَوّابَة، فَبَقِيَ
    /// الصَفُّ بِحَرفِه (‏ADR-033 §٦): «مَن يُحَرِّكُ Offered بِلا قَبول» جَوابُه
    /// المُبادِرُ كَما كان.
    /// <para><b>قُلِبَ</b> (‏ADR-033 §٦‑ط، ‏2026-10-11): كانَ
    /// <c>Offered_still_asks_the_initiator_on_the_general_advance_path_so_acceptance_has_its_own_gate</c> — يَقرَأُ
    /// صَفَّ <c>Offered</c> سُؤالَ الخُروجِ مِنها. وصَفُّه بِحَرفِه (<c>initiator</c> — مَن يُنشِئُ العَرض)، لَكِنَّ
    /// الخُروجَ مِنها دُخولُ <c>Booked</c> وفاعِلُها الطَرَفُ المُقابِل: فَالمُشتَري مَردودٌ عَلى المَسارِ العامّ
    /// (لا يَحجِزُ طَلَبَه بِنَفسِه)، والبائِعُ المَربوطُ يَمُرّ — والقَبولُ يَسأَلُ البَوّابَةَ نَفسَها.</para></summary>
    [Fact]
    public void Offered_is_left_by_the_counterparty_on_the_general_advance_path_and_acceptance_asks_the_same_gate()
    {
        Assert.Equal("initiator", DealsPolicy.Actor(DealStage.Offered));
        Assert.Equal(DealStage.Booked, DealsPolicy.Next("marketplace", DealStage.Offered));

        var justAssigned = CartDeal(counterparty: Seller);
        var required = DealsPolicy.RequiredActorToLeave(justAssigned);
        Assert.Equal("counterparty", required);
        Assert.False(Allowed(justAssigned, Buyer, required!));
        Assert.True(Allowed(justAssigned, Seller, required!));

        Assert.Contains("if (!IsActorAllowed(deal, by.ActorId, DealsPolicy.RequiredActorToLeave(deal)!))", AcceptBody(), StringComparison.Ordinal);
    }

    // ═══ ٤) صَفقَةُ النَقدِ تَحمِلُ `pay_method` و`AmountSar` ═══════════════

    /// <summary><c>/checkout/submit</c> يُنشِئُ صَفقَةً لِكُلِّ بَندٍ بِمَبلَغِ
    /// السَطر (<c>UnitPriceSar × Quantity</c>) وبِـ<c>pay_method</c>
    /// <b>المُطَبَّعَةِ</b> مِن <c>CheckoutPaymentPolicy.Decide</c>، ويُلحِقُ
    /// <c>listing_owner</c> مِن <c>owner_id</c> الإعلان. والنَقدُ لا يُرَدُّ أَبَداً
    /// أَيّاً كانَ عَلَما القَبض — فَهُوَ ما يُغَذّي <c>cod_amount</c> في الخُطوَة 3.</summary>
    [Fact]
    public void A_cart_deal_carries_the_normalised_pay_method_and_the_line_amount_and_cash_is_never_refused()
    {
        var submit = Endpoint("/{slug}/checkout/submit", ".RequireStoreWritable();");
        Assert.Equal(1, Count(submit, "deals.StartAsync("));
        Assert.Contains("var (payMethod, payRefusal) = Services.Deals.CheckoutPaymentPolicy.Decide(pay,", submit, StringComparison.Ordinal);
        Assert.Contains("var amount = item.UnitPriceSar * item.Quantity;", submit, StringComparison.Ordinal);
        Assert.Contains("amountSar: amount,", submit, StringComparison.Ordinal);
        // بِالثابِتِ لا بِالحَرفِيَّة (مُراجَعَةُ المَوجَةِ الثانِيَة، ‏2026-10-11 — `DealCashPolicyTests`).
        Assert.Contains("[Services.Deals.CheckoutPayMethods.AttributeKey] = payMethod", submit, StringComparison.Ordinal);
        Assert.Contains("await deals.AttachRefAsync(slug, deal.Id, \"listing_owner\", oid);", submit, StringComparison.Ordinal);

        Assert.Equal("cash", CheckoutPayMethods.CashOnDelivery);
        Assert.Equal((CheckoutPayMethods.CashOnDelivery, (string?)null), CheckoutPaymentPolicy.Decide("cash", false, false));
        Assert.Equal((CheckoutPayMethods.CashOnDelivery, (string?)null), CheckoutPaymentPolicy.Decide("cash", true, true));
        Assert.Equal(typeof(decimal), typeof(Deal).GetProperty(nameof(Deal.AmountSar))!.PropertyType);
    }

    // ═══ ٥) `Deal.Refs` يَقبَلُ مِفتاحاً جَديداً بِلا تَغييرِ شَكل ═══════════

    /// <summary><c>Refs</c> قامُوسُ نَصٍّ إلى نَصٍّ مَفتوح: مِفتاحٌ لَم يُكتَب بَعد
    /// (رابِطُ بوليصَةِ الخُطوَة 3) يُحفَظُ ويَعودُ مِن JSON بِلا تَغييرِ نَوع؛ و
    /// <c>AttachRefAsync</c> تَكتُبُ أَيَّ مِفتاحٍ بِلا قائِمَةٍ بَيضاء. (الشَكلُ
    /// يُقاسُ هُنا لا مُسَلسِلُ المَخزَن.)</summary>
    [Fact]
    public void Deal_Refs_accepts_a_new_key_without_a_shape_change_and_AttachRefAsync_whitelists_nothing()
    {
        Assert.Equal(typeof(Dictionary<string, string>), typeof(Deal).GetProperty(nameof(Deal.Refs))!.PropertyType);

        var d = CartDeal();
        d.Refs["payment_id"] = "auth_1";
        d.Refs["shipment_awb"] = "OTO-0001";
        var back = JsonSerializer.Deserialize<Deal>(JsonSerializer.Serialize(d))!;
        Assert.Equal("OTO-0001", back.Refs["shipment_awb"]);
        Assert.Equal(Seller.ToString(), back.Refs[DealsService.ListingOwnerRef]);
        Assert.Equal(3, back.Refs.Count);

        var attach = AttachBody();
        Assert.Contains("deal.Refs[key] = value;", attach, StringComparison.Ordinal);
        Assert.DoesNotContain("switch (key)", attach, StringComparison.Ordinal);
        Assert.DoesNotContain("Require(", attach, StringComparison.Ordinal);
        Assert.DoesNotContain("throw", attach, StringComparison.Ordinal);
    }

    // ═══ ٦) `TenantProviderSet.FromDocuments` — شَطرُ «بِتَحذير» وَحدَه ═══════

    /// <summary>السُلوكُ (تُجوهَل، و<c>Platform</c> بِنَفسِ المَرجِع) في
    /// <c>TenantProviderZeroEquivalenceTests</c>؛ وهُنا أَنّ التَجاوُزَ <b>يَكتُبُ
    /// تَحذيراً</b> عَلى <c>stderr</c> لا يَصمُت — فَمَدخَلُ البائِعِ في الخُطوَة 7
    /// حينَ يُتَجاوَزُ بِقاعِدَةٍ يُرى.</summary>
    [Fact]
    public void FromDocuments_skips_a_binding_whose_id_is_not_its_capability_with_a_warning_and_returns_Platform_at_zero()
    {
        var src = WriteEndpointGuardTests.StripComments(ProviderBindingSource());
        var at = src.IndexOf("if (def.Capability != doc.Id)", StringComparison.Ordinal);
        Assert.True(at > 0, "لا شَرطَ `def.Capability != doc.Id` في `FromDocuments` — الأَداةُ عَمياء.");
        var block = BlockFrom(src, at);
        Assert.Contains("Console.Error.WriteLine(", block, StringComparison.Ordinal);
        Assert.Contains("continue;", block, StringComparison.Ordinal);
        Assert.Contains("return accepted.Count == 0 ? Platform : new TenantProviderSet(tenantSlug, accepted);", src, StringComparison.Ordinal);
    }
}

// ─── البُرهانُ الحَيّ — سَلسَلَةُ القَبولِ عَلى Postgres ──────────────────────
//
// يَتَخَطّى نَفسَه ما لَم تُضبَط WASAYEL_LIVE_PROOF=1 و ConnectionStrings__Postgres،
// بِنَفسِ عَقدِ `LiveDealPaidStageProofTests` حَرفاً — ومِنه نُسِخَ تَركيبُ
// المَخزَن. يُنشِئُ صَفقَةَ سَلَّةٍ نَقديَّةً بِمَرجِعِ مالِكِ إعلان، ويَقيسُ أَنّ
// مالِكَ الإعلانِ لا يُحَرِّكُ `Offered` بِنَفسِه عَبرَ `AdvanceAsync`، وأَنّ الغَريبَ
// يُرَدُّ بِرَمزِه بِلا كِتابَة، وأَنّ قَبولَ المالِكِ يُعَيِّنُه طَرَفاً ويَمضي
// `Offered → Booked` بِسَطرٍ فاعِلُه **القابِلُ نَفسُه** ومُلاحَظَةِ «قُبِلَ العَرض»
// (كانَ فاعِلُه المُبادِرَ بِاسمِ القابِل — قُلِبَ بِقَرارٍ مَرئِيّ، ‏ADR-033 §٦)،
// وأَنّ القَبولَ الثاني يُرَدُّ — ومُزَوِّدُ الدَفعِ يَرمي عِندَ أَيِّ نِداء، فَإن
// مَسَّ القَبولُ المالَ أَحمَرّ. **شُغِّلَ عَلى Postgres المَحَلِّيِّ قَبلَ القَلبِ وبَعدَه.**

[Collection(LiveProofCollection.Name)]
public class LiveDealAcceptSequenceProofTests(ITestOutputHelper output)
{
    private static bool Enabled =>
        Environment.GetEnvironmentVariable("WASAYEL_LIVE_PROOF") == "1";

    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");

    private static string ResilientConnection =>
        ConnectionString!.TrimEnd(';') + ";Timeout=60;Command Timeout=120";

    private static DocumentStore BuildStore() => DocumentStore.For(o =>
    {
        o.Connection(ResilientConnection);
        o.DatabaseSchemaName = "platform";
        o.Policies.AllDocumentsAreMultiTenanted();
        o.Events.TenancyStyle = global::JasperFx.MultiTenancy.TenancyStyle.Conjoined;
        o.Schema.For<ACommerce.Kit.Tenants.Tenant>().SingleTenanted().Identity(x => x.Id);
        o.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
    });

    /// <summary>مُزَوِّدٌ يَرمي عِندَ أَيِّ نِداء — القَبولُ لا يَمَسُّ المال، وإن
    /// مَسَّه أَحمَرَّ البُرهانُ بَدَلَ أَن يُحاكى.</summary>
    private sealed class ForbiddenPaymentProvider : IPaymentProvider
    {
        private static InvalidOperationException Touched()
            => new("القَبولُ لا يَمَسُّ المال — نودِيَ مُزَوِّدُ الدَفع.");

        public string ProviderName => "forbidden";

        public Task<PaymentResult> AuthorizeAsync(PaymentRequest req, string idempotencyKey, CancellationToken ct = default)
            => throw Touched();

        public Task<PaymentResult> CaptureAsync(string paymentId, decimal? amount = null, CancellationToken ct = default)
            => throw Touched();

        public Task<PaymentResult> RefundAsync(string paymentId, decimal amount, string reason, CancellationToken ct = default)
            => throw Touched();

        public Task<SubscriptionResult> CreateSubscriptionAsync(SubscriptionRequest req, string idempotencyKey, CancellationToken ct = default)
            => throw Touched();

        public Task<bool> CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
            => throw Touched();

        public Task<Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default)
            => throw Touched();
    }

    [Fact]
    public async Task Accepting_assigns_the_listing_owner_and_moves_Offered_to_Booked_as_the_owner_themselves()
    {
        if (!Enabled || string.IsNullOrEmpty(ConnectionString)) return;

        var evidence = new StringBuilder();
        void Log(string line) { evidence.AppendLine(line); output.WriteLine(line); }

        await using var store = BuildStore();
        var deals = new DealsService(store, new ForbiddenPaymentProvider());

        // مُستَأجِرٌ عَشوائِيٌّ يَخُصُّ هذا التَشغيلَ وَحدَه — لا `ashare` ولا مَتجَرَ لَقطَة.
        var slug = "zz-proof-" + Guid.NewGuid().ToString("N")[..8];
        var buyer = Guid.NewGuid();
        var seller = Guid.NewGuid();
        var stranger = Guid.NewGuid();

        try
        {
            var deal = await deals.StartAsync(slug, "marketplace", buyer, "مُشتَرٍ", null, "سِلعَة", 100m,
                new() { ["pay_method"] = CheckoutPayMethods.CashOnDelivery });
            await deals.AttachRefAsync(slug, deal.Id, DealsService.ListingOwnerRef, seller.ToString());

            // ١) مالِكُ الإعلانِ لا يُحَرِّكُ Offered بِنَفسِه قَبلَ أَن يَصيرَ الطَرَف — ولا المُشتَري.
            //    **قُلِبَ** (‏ADR-033 §٦‑ط، ‏2026-10-11): كانَ رَدُّه «initiator» — المَرحَلَةُ لِلمُبادِر،
            //    والمُشتَري يَمُرّ. وصارَ الخُروجُ مِن Offered دُخولَ Booked وفاعِلُها الطَرَف: لا طَرَفَ بَعد،
            //    فَيُرَدّانِ كِلاهُما بِـ«counterparty» — والقَبولُ هُوَ ما يَجعَلُ المالِكَ الطَرَف.
            var ownerMoves = await deals.AdvanceAsync(slug, deal.Id, seller, "بائِع");
            Assert.False(ownerMoves.Ok);
            Assert.Contains("counterparty", ownerMoves.Reason ?? "");
            var buyerMoves = await deals.AdvanceAsync(slug, deal.Id, buyer, "مُشتَرٍ");
            Assert.False(buyerMoves.Ok);
            Assert.Contains("counterparty", buyerMoves.Reason ?? "");
            Log($"مالِكُ الإعلانِ يُحَرِّكُ Offered بِنَفسِه: Ok={ownerMoves.Ok} — {ownerMoves.Reason} · والمُشتَري: Ok={buyerMoves.Ok}");

            // ٢) غَريبٌ يَقبَل — يُرَدُّ بِرَمزِه ولا يُكتَبُ شَيء.
            var refused = await deals.AcceptAsync(slug, deal.Id, new DealAcceptor(stranger, "غَريب"));
            Assert.False(refused.Ok);
            Assert.Equal(DealAcceptAuthorization.ActorNotListingOwner, refused.Violation!.Code);
            var untouched = (await deals.LoadAsync(slug, deal.Id))!;
            Assert.Null(untouched.CounterpartyId);
            Assert.Equal(DealStage.Offered, untouched.Stage);
            Log($"غَريبٌ يَقبَل: {refused.Violation.Code} · Stage={untouched.Stage} · counterparty=null");

            // ٣) مالِكُ الإعلانِ يَقبَل — يَصيرُ الطَرَفَ، وتَمضي Offered → Booked
            //    بِفاعِلٍ هُوَ القابِلُ نَفسُه (كانَ المُبادِرَ بِاسمِه — ‏ADR-033 §٦).
            var accepted = await deals.AcceptAsync(slug, deal.Id, new DealAcceptor(seller, "بائِع"));
            Assert.True(accepted.Ok, accepted.Violation?.MessageAr);
            var after = (await deals.LoadAsync(slug, deal.Id))!;
            Assert.Equal(DealStage.Booked, after.Stage);
            Assert.NotNull(after.CounterpartyId);
            var counterparty = after.CounterpartyId!.Value;
            Assert.Equal(seller, counterparty);
            Assert.Equal("بائِع", after.CounterpartyName);
            // CounterpartyId == listing_owner عَلى هذا المَسار — نَقيضُ ما كانَ يَومَ كُتِبَ ADR-033.
            Assert.Equal(counterparty.ToString(), after.Refs[DealsService.ListingOwnerRef]);

            var assigned = after.Timeline[^2];
            var advanced = after.Timeline[^1];
            Assert.Equal("assigned", assigned.Action);
            Assert.Equal(seller, assigned.ActorId!.Value);
            Assert.Equal("advanced", advanced.Action);
            Assert.Equal(DealStage.Offered, advanced.StageBefore);
            Assert.Equal(DealStage.Booked, advanced.StageAfter);
            // **قُلِبَ بِقَرارٍ مَرئِيّ** (‏ADR-033 §٦): كانَ `buyer` — المُبادِرُ بِاسمِ القابِل.
            Assert.Equal(seller, advanced.ActorId!.Value);
            Assert.Equal("بائِع", advanced.ActorName);
            // **قُلِبَ** (مُراجَعَةُ المَوجَةِ الثانِيَة، ‏2026-10-11): كانَت «قُبِلَ العَرض» حَرفِيَّةً في المَخزَن.
            Assert.Null(advanced.Note);
            Log($"قَبولُ المالِك: Stage={after.Stage} · counterparty={after.CounterpartyName} · " +
                $"advanced actor={(advanced.ActorId == seller ? "acceptor" : advanced.ActorId == buyer ? "initiator" : "other")} name={advanced.ActorName} note={advanced.Note}");

            // ٤) قَبولٌ ثانٍ — رَمزٌ لا صَمت.
            var twice = await deals.AcceptAsync(slug, deal.Id, new DealAcceptor(seller, "بائِع"));
            Assert.False(twice.Ok);
            Assert.Equal(DealAcceptAuthorization.DealAlreadyAccepted, twice.Violation!.Code);
            Log($"قَبولٌ ثانٍ: {twice.Violation.Code}");
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            foreach (var d in await s.Query<Deal>().ToListAsync()) s.Delete(d);
            await s.SaveChangesAsync();
        }
    }
}
